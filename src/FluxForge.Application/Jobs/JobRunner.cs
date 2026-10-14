using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using FluxForge.Application.Devices;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Processes;
using FluxForge.Application.Safety;
using FluxForge.Application.Tools;

namespace FluxForge.Application.Jobs;

public sealed record ReadJob(HostToolsInstallation Tools, ReadRequest Request, GwInfo? Device, string? DiskLabel, string? Notes);

public sealed record WriteJob(HostToolsInstallation Tools, WriteRequest Request, GwInfo? Device);

/// <summary>Decode a raw flux capture into a sector image with <c>gw convert</c>. No hardware involved.</summary>
public sealed record DecodeJob(HostToolsInstallation Tools, string InputPath, string OutputPath, DiskProfile Profile);

public interface IJobActivity
{
    bool HasActiveJobs { get; }
    bool IsDeviceBusy(string? port);
}

/// <summary>
/// Owns every hardware job: one process per device at a time, truthful status evaluation,
/// partial-file handling and a durable audit record for every run.
/// </summary>
public sealed class JobRunner(IProcessRunner runner, IJobStore store, TimeProvider? clock = null) : IJobActivity
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new(StringComparer.OrdinalIgnoreCase);
    private int _active;

    public static readonly TimeSpan HardwareJobTimeout = TimeSpan.FromMinutes(30);

    public bool HasActiveJobs => Volatile.Read(ref _active) > 0;

    public bool IsDeviceBusy(string? port) => Lock(port).CurrentCount == 0;

    /// <summary>Runs <paramref name="work"/> while holding the device lock, failing fast if the board is busy.</summary>
    public async Task<T> WithDeviceAsync<T>(string? port, Func<Task<T>> work)
    {
        var gate = Lock(port);
        if (!await gate.WaitAsync(0).ConfigureAwait(false))
            throw new DeviceBusyException("The Greaseweazle is busy with another job.");
        Interlocked.Increment(ref _active);
        try { return await work().ConfigureAwait(false); }
        finally
        {
            Interlocked.Decrement(ref _active);
            gate.Release();
        }
    }

    public Task<JobRecord> RunReadAsync(ReadJob job, IProgress<JobProgress>? progress, CancellationToken ct) =>
        WithDeviceAsync(job.Request.Target.Port, () => ReadCoreAsync(job, progress, ct));

    public Task<JobRecord> RunWriteAsync(WriteJob job, IProgress<JobProgress>? progress, CancellationToken ct) =>
        WithDeviceAsync(job.Request.Target.Port, () => WriteCoreAsync(job, progress, ct));

    /// <summary>
    /// Offline decode. Doesn't take the device lock (no hardware), but counts as an active job so host
    /// tools can't be swapped underneath it.
    /// </summary>
    public async Task<JobRecord> RunDecodeAsync(DecodeJob job, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        Interlocked.Increment(ref _active);
        try { return await DecodeCoreAsync(job, progress, ct).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _active); }
    }

    private async Task<JobRecord> DecodeCoreAsync(DecodeJob job, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var input = Path.GetFullPath(job.InputPath);
        if (!File.Exists(input))
            throw new InvalidOperationException("The raw capture file was not found.");
        if (!GwCommands.RawFluxExtensions.Contains(Path.GetExtension(input)))
            throw new InvalidOperationException("Choose a raw flux capture (.scp) to decode.");

        var finalPath = Path.GetFullPath(job.OutputPath);
        var blocking = OutputGuard.CheckReadOutput(finalPath, CaptureMode.SectorImage, job.Profile, FreeSpace).Where(i => i.Blocking).ToList();
        if (blocking.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, blocking.Select(i => i.Message)));
        if (!string.Equals(Path.GetExtension(finalPath), job.Profile.SectorExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{job.Profile.DisplayName} images use {job.Profile.SectorExtension}.");

        var stagingDir = Path.Combine(Path.GetDirectoryName(finalPath)!, ".fluxforge-partial");
        Directory.CreateDirectory(stagingDir).Attributes |= FileAttributes.Hidden;
        var tempPath = Path.Combine(stagingDir, $"{Guid.NewGuid():N}{Path.GetExtension(finalPath)}");
        var args = GwCommands.Convert(input, tempPath, job.Profile);

        var source = await ArtifactAsync(input, ArtifactKind.RawFlux, true, ct).ConfigureAwait(false);
        var record = NewRecord(JobKind.Convert, $"Decode {Path.GetFileName(input)} as {job.Profile.System} {job.Profile.DisplayName}", job.Tools, null, args) with
        {
            ProfileId = job.Profile.Id,
            GwFormat = job.Profile.GwFormat,
            CaptureMode = CaptureMode.SectorImage,
        };

        var tracker = new TrackProgressTracker();
        var sw = Stopwatch.StartNew();
        progress?.Report(new JobProgress(JobStatus.Running, TimeSpan.Zero));
        var lines = new SyncProgress<OutputLine>(line =>
        {
            var track = tracker.Feed(line.Text); // always parse, even with no UI listening
            progress?.Report(new JobProgress(JobStatus.Running, sw.Elapsed, line, track));
        });
        var result = await runner.RunAsync(new ProcessRequest(job.Tools.ExecutablePath, args, Timeout: TimeSpan.FromMinutes(10)), lines, ct).ConfigureAwait(false);
        tracker.Finish();

        var outputExists = File.Exists(tempPath) && new FileInfo(tempPath).Length > 0;
        var (status, detail) = EvaluateRead(result, tracker, CaptureMode.SectorImage, job.Profile, outputExists, fromRawCapture: true);
        var artifacts = new List<JobArtifact> { source };
        if (outputExists)
        {
            var complete = status is JobStatus.Succeeded or JobStatus.Partial or JobStatus.CompletedUnverified;
            var destination = complete ? finalPath : OutputGuard.IncompleteName(finalPath);
            File.Move(tempPath, destination, overwrite: false);
            artifacts.Add(await ArtifactAsync(destination, ArtifactKind.SectorImage, complete, CancellationToken.None).ConfigureAwait(false));
        }
        TryDelete(tempPath);
        TryDeleteEmptyDir(stagingDir);
        return await FinishAsync(record, result, status, detail, tracker, artifacts, progress).ConfigureAwait(false);
    }

    /// <summary>
    /// Run a drive/device utility (rpm, seek, clean, erase…) under the device lock and record it in the
    /// audit trail like any other job. Success needs a clean exit and no gw fatal error.
    /// </summary>
    public Task<(JobRecord Record, ProcessResult Result)> RunToolAsync(HostToolsInstallation tools, string title, IReadOnlyList<string> args,
        DeviceTarget target, GwInfo? device, IProgress<JobProgress>? progress, CancellationToken ct, TimeSpan? timeout = null) =>
        WithDeviceAsync(target.Port, async () =>
        {
            var record = NewRecord(JobKind.DriveTool, title, tools, device, args) with { DevicePort = target.Port ?? device?.Port };
            var tracker = new TrackProgressTracker();
            var result = await RunTrackedAsync(tools, args, tracker, progress, ct, timeout ?? TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            tracker.Finish();
            var (status, detail) =
                result.Cancelled ? (JobStatus.Cancelled, "Stopped by the user.") :
                result.TimedOut ? (JobStatus.Failed, "The tool did not finish in time and was stopped.") :
                LostConnection(result) ? (JobStatus.Failed, "Lost connection to the Greaseweazle. Check the USB cable.") :
                result.ExitCode != 0 ? (JobStatus.Failed, $"gw exited with code {result.ExitCode}. {LastError(result)}".Trim()) :
                tracker.FatalError is { } fatal ? (JobStatus.Failed, $"gw reported: {Sentence(fatal)}{Hint(fatal)}") :
                (JobStatus.Succeeded, "Completed.");
            var final = await FinishAsync(record, result, status, detail, tracker, [], progress).ConfigureAwait(false);
            return (final, result);
        });

    private SemaphoreSlim Lock(string? port) => _deviceLocks.GetOrAdd(port ?? "auto", _ => new SemaphoreSlim(1, 1));

    private async Task<JobRecord> ReadCoreAsync(ReadJob job, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var req = job.Request;
        var finalPath = Path.GetFullPath(req.OutputPath);
        var blocking = OutputGuard.CheckReadOutput(finalPath, req.Mode, req.Profile, FreeSpace).Where(i => i.Blocking).ToList();
        if (blocking.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, blocking.Select(i => i.Message)));

        // Capture into a staging folder on the same volume, then move into place once status is known.
        var stagingDir = Path.Combine(Path.GetDirectoryName(finalPath)!, ".fluxforge-partial");
        var staging = Directory.CreateDirectory(stagingDir);
        staging.Attributes |= FileAttributes.Hidden;
        var tempPath = Path.Combine(stagingDir, $"{Guid.NewGuid():N}{Path.GetExtension(finalPath)}");
        var args = GwCommands.Read(req with { OutputPath = tempPath });

        var record = NewRecord(JobKind.Read, ReadTitle(job), job.Tools, job.Device, args) with
        {
            ProfileId = req.Profile?.Id,
            GwFormat = req.Profile?.GwFormat,
            CaptureMode = req.Mode,
            DiskLabel = job.DiskLabel,
            Notes = job.Notes,
            DevicePort = req.Target.Port ?? job.Device?.Port,
        };

        var tracker = new TrackProgressTracker();
        var result = await RunTrackedAsync(job.Tools, args, tracker, progress, ct).ConfigureAwait(false);
        tracker.Finish();
        var outputExists = File.Exists(tempPath) && new FileInfo(tempPath).Length > 0;
        var (status, detail) = EvaluateRead(result, tracker, req.Mode, req.Profile, outputExists);

        var artifacts = new List<JobArtifact>();
        if (outputExists)
        {
            var complete = status is JobStatus.Succeeded or JobStatus.Partial or JobStatus.CompletedUnverified;
            var destination = complete ? finalPath : OutputGuard.IncompleteName(finalPath);
            File.Move(tempPath, destination, overwrite: false);
            artifacts.Add(await ArtifactAsync(destination, req.Mode == CaptureMode.Preservation ? ArtifactKind.RawFlux : ArtifactKind.SectorImage, complete, CancellationToken.None).ConfigureAwait(false));
        }
        TryDelete(tempPath);
        TryDeleteEmptyDir(stagingDir);

        return await FinishAsync(record, result, status, detail, tracker, artifacts, progress).ConfigureAwait(false);
    }

    private async Task<JobRecord> WriteCoreAsync(WriteJob job, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var req = job.Request;
        var blocking = OutputGuard.CheckWriteImage(req.ImagePath, req.Profile).Where(i => i.Blocking).ToList();
        if (blocking.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, blocking.Select(i => i.Message)));

        var args = GwCommands.Write(req);
        var source = await ArtifactAsync(req.ImagePath,
            GwCommands.RawFluxExtensions.Contains(Path.GetExtension(req.ImagePath)) ? ArtifactKind.RawFlux : ArtifactKind.SectorImage,
            true, ct).ConfigureAwait(false);
        var record = NewRecord(JobKind.Write, $"Write {Path.GetFileName(req.ImagePath)}", job.Tools, job.Device, args) with
        {
            ProfileId = req.Profile?.Id,
            GwFormat = req.Profile?.GwFormat,
            DevicePort = req.Target.Port ?? job.Device?.Port,
        };

        var tracker = new TrackProgressTracker();
        var result = await RunTrackedAsync(job.Tools, args, tracker, progress, ct).ConfigureAwait(false);
        tracker.Finish();
        var (status, detail) = EvaluateWrite(result, req, tracker);
        return await FinishAsync(record, result, status, detail, tracker, [source], progress).ConfigureAwait(false);
    }

    private async Task<ProcessResult> RunTrackedAsync(HostToolsInstallation tools, IReadOnlyList<string> args,
        TrackProgressTracker tracker, IProgress<JobProgress>? progress, CancellationToken ct, TimeSpan? timeout = null)
    {
        var sw = Stopwatch.StartNew();
        progress?.Report(new JobProgress(JobStatus.Running, TimeSpan.Zero));
        var lineProgress = new SyncProgress<OutputLine>(line =>
        {
            var track = tracker.Feed(line.Text);
            progress?.Report(new JobProgress(JobStatus.Running, sw.Elapsed, line, track));
        });
        var result = await runner.RunAsync(new ProcessRequest(tools.ExecutablePath, args, Timeout: timeout ?? HardwareJobTimeout), lineProgress, ct).ConfigureAwait(false);
        if (!result.Cancelled && !result.TimedOut) return result;

        // Best-effort: stop the drive motor and release the board after killing gw mid-operation.
        var reset = await runner.RunAsync(new ProcessRequest(tools.ExecutablePath, ["reset"], Timeout: TimeSpan.FromSeconds(10)), null, CancellationToken.None).ConfigureAwait(false);
        var note = new OutputLine(OutputStream.StdErr,
            reset.ExitCode == 0 ? "[FluxForge] Job stopped; device reset issued." : "[FluxForge] Job stopped; device reset did not succeed — unplug the board if the drive keeps spinning.",
            DateTimeOffset.Now);
        return result with { Lines = [.. result.Lines, note, .. reset.Lines] };
    }

    private async Task<JobRecord> FinishAsync(JobRecord record, ProcessResult result, JobStatus status, string detail,
        TrackProgressTracker tracker, IReadOnlyList<JobArtifact> artifacts, IProgress<JobProgress>? progress)
    {
        var final = record with
        {
            EndedAt = _clock.GetUtcNow(),
            Status = status,
            StatusDetail = detail,
            ExitCode = result.ExitCode,
            Artifacts = artifacts,
            Tracks = [.. tracker.Tracks.OrderBy(t => t.Cylinder).ThenBy(t => t.Head)],
            Sectors = tracker.Summary,
            SuggestedProfileId = tracker.UnexpectedSectors > 0 && record.ProfileId is { } id && new FormatRegistry().Find(id) is { } chosen
                ? new FormatRegistry().SuggestForExtraSectors(chosen, tracker.HighestUnexpectedSector)?.Id
                : null,
        };
        // The audit record is saved even if the caller cancelled.
        final = await store.SaveAsync(final, result.Lines, CancellationToken.None).ConfigureAwait(false);
        progress?.Report(new JobProgress(status, result.Duration));
        return final;
    }

    public static (JobStatus Status, string Detail) EvaluateRead(ProcessResult result, TrackProgressTracker tracker,
        CaptureMode mode, DiskProfile? profile, bool outputExists, bool fromRawCapture = false)
    {
        if (result.Cancelled || result.TimedOut)
        {
            var where = Where(tracker);
            var kept = outputExists
                ? " The partial file was kept and labelled (INCOMPLETE)."
                : " Nothing was saved — gw only writes the image file when a read finishes.";
            return result.Cancelled
                ? (JobStatus.Cancelled, $"Cancelled{where}.{kept}")
                : (JobStatus.Failed, $"The read timed out{where} and was stopped.{kept}");
        }
        if (LostConnection(result))
            return (JobStatus.Failed, $"Lost connection to the Greaseweazle{Where(tracker)} — the USB cable may have been unplugged or the board reset. "
                + (outputExists ? "The partial file was kept and labelled (INCOMPLETE)." : "Nothing was saved."));
        if (result.ExitCode != 0)
            return (JobStatus.Failed, $"gw exited with code {result.ExitCode}. {LastError(result)}".Trim());
        if (tracker.FatalError is { } fatal)
            return (JobStatus.Failed, $"gw reported: {Sentence(fatal)}{Hint(fatal)}");
        if (!outputExists)
            return (JobStatus.Failed, "gw reported success but no image file was produced.");

        var unreadable = tracker.Tracks.Count(t => t.Status == TrackStatus.Unreadable);
        var recovered = tracker.Tracks.Count(t => t.Status == TrackStatus.RecoveredAfterRetry);
        var prefix = mode == CaptureMode.Preservation ? "Raw flux captured. " : "";

        if (profile is null)
            return (JobStatus.CompletedUnverified, prefix + "No disk format was selected, so sectors were not decoded.");

        if (tracker.UnexpectedSectors > 0)
        {
            // The disk holds sectors the chosen format doesn't define. Never report this as success:
            // a sector image silently lacks them, and the decode check doesn't describe the real disk.
            var tracks = tracker.Tracks.Count(t => t.UnexpectedSectors > 0);
            var suggestion = new FormatRegistry().SuggestForExtraSectors(profile, tracker.HighestUnexpectedSector) is { } better
                ? $" This looks like {better.System} {better.DisplayName} ({better.GwFormat})."
                : "";
            var what = $"The disk has {tracker.UnexpectedSectors:N0} sector(s) on {tracks} track(s) beyond what {profile.GwFormat} defines (up to sector {tracker.HighestUnexpectedSector}).{suggestion}";
            return mode == CaptureMode.Preservation
                ? (JobStatus.CompletedUnverified, $"{prefix}{what} The raw capture includes them — decode it with the right format instead of reading the disk again.")
                : (JobStatus.Partial, fromRawCapture
                    ? $"{what} They are NOT in this image. Decode the capture again with the matching format."
                    : $"{what} They are NOT in this image. Read again with the matching format, or make a raw capture.");
        }
        if (unreadable > 0 || tracker.Summary is { Complete: false })
        {
            var sectors = tracker.Summary is { } s ? $" {s.Found:N0} of {s.Total:N0} sectors decoded." : "";
            return (JobStatus.Partial, $"{prefix}{unreadable} track(s) have missing sectors.{sectors}");
        }
        if (tracker.Summary is { Complete: true } summary && tracker.UnresolvedTracks == 0)
        {
            var retried = recovered > 0 ? $" {recovered} track(s) needed retries." : "";
            return (JobStatus.Succeeded, $"{prefix}All {summary.Total:N0} sectors decoded.{retried}");
        }
        return (JobStatus.CompletedUnverified, prefix + "gw finished without errors, but FluxForge could not confirm sector statistics from its output.");
    }

    public static (JobStatus Status, string Detail) EvaluateWrite(ProcessResult result, WriteRequest request, TrackProgressTracker? tracker = null)
    {
        if (result.Cancelled || result.TimedOut)
            return (JobStatus.Interrupted, "The write was interrupted. The floppy's contents are now in an unknown state.");
        if (LostConnection(result))
            return (JobStatus.Interrupted, $"Lost connection to the Greaseweazle{Where(tracker)} — the USB cable may have been unplugged or the board reset. The floppy's contents are now in an unknown state.");
        if (result.ExitCode != 0)
            return (JobStatus.Failed, $"Write failed: gw exited with code {result.ExitCode}. {LastError(result)}".Trim());
        if (tracker is { WriteProtected: true })
            return (JobStatus.Failed, "Nothing was written — the disk is write-protected. To overwrite it, slide the write-protect tab to writable and try again.");
        if (tracker?.FatalError is { } fatal)
            return (JobStatus.Failed, $"Write failed — gw reported: {Sentence(fatal)}{Hint(fatal)} The disk may be partly written.");
        var raw = GwCommands.RawFluxExtensions.Contains(Path.GetExtension(request.ImagePath));
        if (!request.Verify)
            return (JobStatus.CompletedUnverified, "Write finished. Verification was turned off, so the disk was not checked.");
        if (raw)
            return (JobStatus.CompletedUnverified, "Write finished. Raw flux writes are not verified like sector images; use a readback comparison.");
        if (tracker is { AllTracksVerified: false })
            return (JobStatus.CompletedUnverified, "Write finished, but gw did not confirm that its verification passed. Read the disk back to check it.");
        var count = tracker?.Tracks.Count(t => t.Status == TrackStatus.Verified) ?? 0;
        return (JobStatus.Succeeded, count > 0
            ? $"Write finished and gw verified all {count} tracks."
            : "Write finished and gw's built-in verification reported no errors.");
    }

    // gw writes all of its output (progress included) to stderr, so stream alone doesn't mark an error.
    /// <summary>
    /// gw 1.23 on Windows, cable pulled mid-job: "WriteFile failed (PermissionError(13, 'The device does
    /// not recognize the command.' ...))". Serial-port read/open failures look similar.
    /// </summary>
    private static bool LostConnection(ProcessResult result) =>
        result.Lines.Any(l => l.Text.Contains("WriteFile failed", StringComparison.OrdinalIgnoreCase)
                              || l.Text.Contains("ReadFile failed", StringComparison.OrdinalIgnoreCase)
                              || l.Text.Contains("device does not recognize the command", StringComparison.OrdinalIgnoreCase)
                              || l.Text.Contains("could not open port", StringComparison.OrdinalIgnoreCase)
                              || l.Text.Contains("SerialException", StringComparison.Ordinal));

    private static string Where(TrackProgressTracker? tracker) =>
        tracker is { Tracks.Count: > 0 } t ? $" at cylinder {t.Tracks.Max(x => x.Cylinder)}" : "";

    private static string Sentence(string text) => text.TrimEnd().EndsWith('.') ? text.TrimEnd() : text.TrimEnd() + ".";

    /// <summary>Plain-language hints for the common hardware errors seen on real boards.</summary>
    private static string Hint(string error) =>
        error.Contains("Track 0 not found", StringComparison.OrdinalIgnoreCase) || error.Contains("No Index", StringComparison.OrdinalIgnoreCase)
            ? " The drive did not respond: check the drive selection (A/B for PC cables — a straight cable usually means B), drive power, and that a disk is inserted."
            : "";

    private static string LastError(ProcessResult result) =>
        result.Lines.LastOrDefault(l => l.Text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))?.Text.Trim().TrimStart('*', ' ')
        ?? result.Lines.LastOrDefault(l => l.Text.Trim().Length > 0)?.Text.Trim()
        ?? "";

    private JobRecord NewRecord(JobKind kind, string title, HostToolsInstallation tools, GwInfo? device, IReadOnlyList<string> args) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Title = title,
        StartedAt = _clock.GetUtcNow(),
        Status = JobStatus.Running,
        ToolsPath = tools.ExecutablePath,
        ToolsVersion = tools.Version?.ToString(),
        Simulated = tools.IsSimulated || device?.IsSimulated == true,
        DeviceModel = device?.Model,
        Firmware = device?.Firmware,
        Arguments = args,
    };

    private static string ReadTitle(ReadJob job)
    {
        var what = job.Request.Profile?.ToString() ?? "Unknown format";
        var how = job.Request.Mode == CaptureMode.Preservation ? "raw flux" : "sector image";
        return string.IsNullOrWhiteSpace(job.DiskLabel) ? $"Read {what} ({how})" : $"Read '{job.DiskLabel}' ({how})";
    }

    public static async Task<JobArtifact> ArtifactAsync(string path, ArtifactKind kind, bool complete, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return new JobArtifact(path, Convert.ToHexStringLower(hash), stream.Length, kind, complete);
    }

    private static long? FreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDir(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>IProgress that invokes synchronously, so track state is updated in output order.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
