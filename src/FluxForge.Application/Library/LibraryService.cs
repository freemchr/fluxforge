using System.Security.Cryptography;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;

namespace FluxForge.Application.Library;

public enum IntegrityResult { Unchanged, Changed, Missing }

/// <summary>
/// Keeps the catalogue in step with what FluxForge does: successful reads and decodes are added
/// automatically (a decode joins the disk its raw capture belongs to), and verify runs are recorded
/// against the image they compared with. Recording is idempotent per job.
/// </summary>
public sealed class LibraryService(ILibraryStore store, FormatRegistry formats, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ILibraryStore Store => store;

    /// <summary>Fires after the catalogue changes, so open views can refresh.</summary>
    public event EventHandler? Changed;

    public void Record(JobRecord job)
    {
        if (store.HasJob(job.Id)) return;
        var changed = job.Kind switch
        {
            JobKind.Read or JobKind.Convert when Usable(job.Status) => AddImagesFrom(job),
            JobKind.Verify => RecordVerify(job),
            _ => false,
        };
        store.MarkJob(job.Id);
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One-off import of the existing job history, oldest first so decodes find their captures.</summary>
    public int ImportHistory(IEnumerable<JobRecord> jobs)
    {
        var before = store.Search().Count;
        foreach (var job in jobs.OrderBy(j => j.StartedAt)) Record(job);
        return store.Search().Count - before;
    }

    private static bool Usable(JobStatus status) => status is JobStatus.Succeeded or JobStatus.Partial or JobStatus.CompletedUnverified;

    private bool AddImagesFrom(JobRecord job)
    {
        var profile = job.ProfileId is { } id ? formats.Find(id) : null;
        LibraryDisk? disk = null;
        var added = false;
        foreach (var artifact in job.Artifacts.Where(a => a.Complete))
        {
            // A decode lists its source capture first: an existing capture decides which disk this belongs to.
            var existing = store.FindImageByHash(artifact.Sha256);
            if (existing is not null)
            {
                disk ??= store.Get(existing.DiskId);
                continue;
            }
            var isSource = job.Kind == JobKind.Convert && artifact.Kind == ArtifactKind.RawFlux;
            if (disk is null)
            {
                var now = _clock.GetUtcNow();
                disk = new LibraryDisk
                {
                    Id = Guid.NewGuid(),
                    Title = string.IsNullOrWhiteSpace(job.DiskLabel) ? Path.GetFileNameWithoutExtension(artifact.Path) : job.DiskLabel.Trim(),
                    System = profile?.System,
                    ProfileId = profile?.Id,
                    Notes = job.Notes?.Trim() ?? "",
                    AddedAt = now,
                    UpdatedAt = now,
                };
                store.SaveDisk(disk);
            }
            else if (disk.System is null && profile is not null)
            {
                disk = disk with { System = profile.System, ProfileId = profile.Id, UpdatedAt = _clock.GetUtcNow() };
                store.SaveDisk(disk);
            }
            store.SaveImage(new LibraryImage
            {
                Id = Guid.NewGuid(),
                DiskId = disk.Id,
                Path = artifact.Path,
                Sha256 = artifact.Sha256,
                Bytes = artifact.Bytes,
                Kind = artifact.Kind,
                // The capture fed into a decode wasn't made by it, and isn't in the decode's format.
                GwFormat = isSource ? null : job.GwFormat,
                JobId = isSource ? null : job.Id,
                JobKind = isSource ? null : job.Kind,
                JobStatus = isSource ? null : job.Status,
                JobDetail = isSource ? null : job.StatusDetail,
                AddedAt = job.EndedAt ?? job.StartedAt,
            });
            added = true;
        }
        return added;
    }

    private bool RecordVerify(JobRecord job)
    {
        if (job.Verify is null || job.Artifacts.FirstOrDefault() is not { } reference) return false;
        var image = store.FindImageByPath(reference.Path) ?? store.FindImageByHash(reference.Sha256);
        if (image is null || image.LastVerifiedAt >= job.StartedAt) return false;
        store.SaveImage(image with { LastVerifiedAt = job.StartedAt, LastVerifyStatus = job.Status, LastVerifyDetail = job.StatusDetail });
        return true;
    }

    /// <summary>Adds image files chosen by the user. Files already catalogued (same SHA-256) are skipped.</summary>
    public async Task<int> AddFilesAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var added = 0;
        foreach (var path in paths.Select(Path.GetFullPath).Where(File.Exists))
        {
            var kind = GwCommands.RawFluxExtensions.Contains(Path.GetExtension(path)) ? ArtifactKind.RawFlux : ArtifactKind.SectorImage;
            var artifact = await JobRunner.ArtifactAsync(path, kind, true, ct).ConfigureAwait(false);
            if (store.FindImageByHash(artifact.Sha256) is not null) continue;
            var profile = GuessProfile(path, artifact.Bytes);
            var now = _clock.GetUtcNow();
            var disk = SameNameNeighbour(path) ?? new LibraryDisk
            {
                Id = Guid.NewGuid(),
                Title = Path.GetFileNameWithoutExtension(path),
                System = profile?.System,
                ProfileId = profile?.Id,
                AddedAt = now,
                UpdatedAt = now,
            };
            if (disk.System is null && profile is not null) disk = disk with { System = profile.System, ProfileId = profile.Id };
            store.SaveDisk(disk);
            store.SaveImage(new LibraryImage
            {
                Id = Guid.NewGuid(),
                DiskId = disk.Id,
                Path = path,
                Sha256 = artifact.Sha256,
                Bytes = artifact.Bytes,
                Kind = kind,
                GwFormat = profile?.GwFormat,
                AddedAt = now,
            });
            added++;
        }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>
    /// "Game.scp" and "Game.adf" side by side are the same disk (a capture and its decode), so a file
    /// joins the disk of an already-catalogued file with the same name in the same folder.
    /// </summary>
    private LibraryDisk? SameNameNeighbour(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        return store.Search(stem).FirstOrDefault(d => d.Images.Any(i =>
            string.Equals(Path.GetDirectoryName(i.Path), folder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileNameWithoutExtension(i.Path), stem, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Only when the extension and size fit exactly one format; ambiguous files stay unclassified.</summary>
    private DiskProfile? GuessProfile(string path, long bytes)
    {
        var fits = formats.CandidatesForExtension(Path.GetExtension(path)).Where(p => p.ExpectedSectorImageBytes == bytes).ToList();
        return fits.Count == 1 ? fits[0] : null;
    }

    public LibraryDisk Update(LibraryDisk disk)
    {
        var saved = disk with
        {
            Title = string.IsNullOrWhiteSpace(disk.Title) ? "Untitled disk" : disk.Title.Trim(),
            Notes = disk.Notes.Trim(),
            Tags = NormaliseTags(disk.Tags),
            UpdatedAt = _clock.GetUtcNow(),
        };
        store.SaveDisk(saved);
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public static IReadOnlyList<string> NormaliseTags(IEnumerable<string> tags) =>
        [.. tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static IReadOnlyList<string> ParseTags(string text) => NormaliseTags(text.Split([',', ';']));

    /// <summary>Removes a disk from the catalogue. Image files are never deleted.</summary>
    public void Remove(Guid diskId)
    {
        store.DeleteDisk(diskId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes one image from the catalogue (the file stays), and the disk too if it has no images left.</summary>
    public void RemoveImage(LibraryImage image)
    {
        store.DeleteImage(image.Id);
        if (store.Get(image.DiskId) is { Images.Count: 0 } empty) store.DeleteDisk(empty.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-hashes the file: the fingerprint must still match what was recorded when it was catalogued.</summary>
    public static async Task<IntegrityResult> CheckAsync(LibraryImage image, CancellationToken ct)
    {
        if (!File.Exists(image.Path)) return IntegrityResult.Missing;
        await using var stream = File.OpenRead(image.Path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return string.Equals(hash, image.Sha256, StringComparison.OrdinalIgnoreCase) ? IntegrityResult.Unchanged : IntegrityResult.Changed;
    }
}
