using FluxForge.Application.Formats;
using FluxForge.Application.Jobs;
using FluxForge.Application.Library;
using FluxForge.Application.Processes;
using FluxForge.Infrastructure.Storage;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class LibraryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static (SqliteLibraryStore Store, LibraryService Service) Library(TempDir dir)
    {
        var store = new SqliteLibraryStore(dir.File(Path.Combine("home", "Data", "library.db")));
        return (store, new LibraryService(store, new FormatRegistry()));
    }

    private static JobArtifact Artifact(string path, string sha, ArtifactKind kind = ArtifactKind.SectorImage, bool complete = true) =>
        new(path, sha, 901120, kind, complete);

    private static JobRecord Job(JobKind kind, JobStatus status, params JobArtifact[] artifacts) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Title = "job",
        StartedAt = T0,
        EndedAt = T0.AddMinutes(1),
        Status = status,
        StatusDetail = $"{status} detail",
        ToolsPath = @"C:\gw\gw.exe",
        Arguments = [],
        ProfileId = "amiga-dd",
        GwFormat = "amiga.amigados",
        Artifacts = artifacts,
    };

    private static string Sha(char c) => new(c, 64);

    [Fact]
    public void Store_round_trips_disks_images_and_tags()
    {
        using var dir = new TempDir();
        var (store, _) = Library(dir);
        Assert.True(store.IsNew);
        var disk = new LibraryDisk { Id = Guid.NewGuid(), Title = "Workbench 1.3", System = "Commodore Amiga", Condition = DiskCondition.Worn,
            Notes = "Blue label", Tags = ["os", "original"], AddedAt = T0, UpdatedAt = T0 };
        store.SaveDisk(disk);
        var image = new LibraryImage { Id = Guid.NewGuid(), DiskId = disk.Id, Path = dir.File("wb.adf"), Sha256 = Sha('A'), Bytes = 901120,
            Kind = ArtifactKind.SectorImage, GwFormat = "amiga.amigados", JobId = Guid.NewGuid(), JobKind = JobKind.Read,
            JobStatus = JobStatus.Partial, AddedAt = T0 };
        store.SaveImage(image);

        var loaded = store.Get(disk.Id)!;
        Assert.Equal(["os", "original"], loaded.Tags);
        Assert.Equal(DiskCondition.Worn, loaded.Condition);
        var loadedImage = Assert.Single(loaded.Images);
        Assert.Equal(Sha('a'), loadedImage.Sha256); // hashes are stored lower case
        Assert.Equal(JobStatus.Partial, loadedImage.JobStatus);
        Assert.False(new SqliteLibraryStore(dir.File(Path.Combine("home", "Data", "library.db"))).IsNew);
    }

    [Theory]
    [InlineData("workbench", 1)]
    [InlineData("amiga original", 1)] // every word must match somewhere
    [InlineData("amiga missing", 0)]
    [InlineData("aaaaaaaa", 1)]       // part of the SHA-256
    [InlineData("wb.adf", 1)]         // image file name
    [InlineData("100%", 0)]           // LIKE wildcards are literal
    [InlineData("", 1)]
    public void Search_matches_words_across_disk_and_image_fields(string query, int expected)
    {
        using var dir = new TempDir();
        var (store, _) = Library(dir);
        var disk = new LibraryDisk { Id = Guid.NewGuid(), Title = "Workbench 1.3", System = "Commodore Amiga", Tags = ["original"], AddedAt = T0 };
        store.SaveDisk(disk);
        store.SaveImage(new LibraryImage { Id = Guid.NewGuid(), DiskId = disk.Id, Path = dir.File("wb.adf"), Sha256 = Sha('a'), AddedAt = T0 });

        Assert.Equal(expected, store.Search(query).Count);
    }

    [Fact]
    public void Deleting_a_disk_removes_its_images_from_the_catalogue_only()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var file = dir.File("keep.adf");
        File.WriteAllBytes(file, [1, 2, 3]);
        service.Record(Job(JobKind.Read, JobStatus.Succeeded, Artifact(file, Sha('b'))));
        var disk = Assert.Single(store.Search());

        service.Remove(disk.Id);

        Assert.Empty(store.Search());
        Assert.Null(store.FindImageByHash(Sha('b')));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Successful_read_is_added_with_label_system_and_provenance()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var job = Job(JobKind.Read, JobStatus.Partial, Artifact(dir.File("wb.adf"), Sha('c'))) with { DiskLabel = " Workbench ", Notes = "From the attic" };

        service.Record(job);
        service.Record(job); // recording is idempotent

        var disk = Assert.Single(store.Search());
        Assert.Equal("Workbench", disk.Title);
        Assert.Equal("Commodore Amiga", disk.System);
        Assert.Equal("From the attic", disk.Notes);
        var image = Assert.Single(disk.Images);
        Assert.Equal(job.Id, image.JobId);
        Assert.Equal(JobStatus.Partial, image.JobStatus);
        Assert.Equal("amiga.amigados", image.GwFormat);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void Failed_or_cancelled_reads_are_not_catalogued(JobStatus status)
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        service.Record(Job(JobKind.Read, status, Artifact(dir.File("x (INCOMPLETE).adf"), Sha('d'), complete: false)));
        Assert.Empty(store.Search());
    }

    [Fact]
    public void Decode_joins_the_disk_its_raw_capture_belongs_to()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var capture = Artifact(dir.File("st.scp"), Sha('e'), ArtifactKind.RawFlux);
        service.Record(Job(JobKind.Read, JobStatus.CompletedUnverified, capture) with { DiskLabel = "Game disk", ProfileId = null, GwFormat = null });

        var decode = Job(JobKind.Convert, JobStatus.Succeeded, capture, Artifact(dir.File("st (atarist.800).st"), Sha('f'))) with
        {
            ProfileId = "atarist-800",
            GwFormat = "atarist.800",
        };
        service.Record(decode);

        var disk = Assert.Single(store.Search());
        Assert.Equal("Game disk", disk.Title);
        Assert.Equal("Atari ST", disk.System); // learned from the decode
        Assert.Equal(2, disk.Images.Count);
        Assert.Equal(decode.Id, disk.Images.Single(i => i.Kind == ArtifactKind.SectorImage).JobId);
    }

    [Fact]
    public void Decode_of_an_uncatalogued_capture_adds_both_but_only_credits_the_output_to_the_job()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var decode = Job(JobKind.Convert, JobStatus.Succeeded,
            Artifact(dir.File("old.scp"), Sha('1'), ArtifactKind.RawFlux), Artifact(dir.File("old (amiga.amigados).adf"), Sha('2')));

        service.Record(decode);

        var disk = Assert.Single(store.Search());
        Assert.Equal("old", disk.Title);
        var source = disk.Images.Single(i => i.Kind == ArtifactKind.RawFlux);
        Assert.Null(source.JobId);
        Assert.Null(source.GwFormat);
        Assert.Equal(decode.Id, disk.Images.Single(i => i.Kind == ArtifactKind.SectorImage).JobId);
    }

    [Fact]
    public void Verify_run_is_recorded_on_the_image_it_compared_with()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var image = Artifact(dir.File("wb.adf"), Sha('3'));
        service.Record(Job(JobKind.Read, JobStatus.Succeeded, image));

        var verify = Job(JobKind.Verify, JobStatus.Failed, image) with
        {
            StartedAt = T0.AddHours(1),
            StatusDetail = "3 sector(s) on 2 track(s) differ from wb.adf.",
            Verify = new VerifySummary(1760, 1757, 3, 0, 0, []),
        };
        service.Record(verify);

        var saved = store.FindImageByHash(Sha('3'))!;
        Assert.Equal(T0.AddHours(1), saved.LastVerifiedAt);
        Assert.Equal(JobStatus.Failed, saved.LastVerifyStatus);
        Assert.Equal(verify.StatusDetail, saved.LastVerifyDetail);
    }

    [Fact]
    public void History_import_runs_oldest_first_so_decodes_find_their_capture()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var capture = Artifact(dir.File("a.scp"), Sha('4'), ArtifactKind.RawFlux);
        var read = Job(JobKind.Read, JobStatus.CompletedUnverified, capture) with { DiskLabel = "First" };
        var decode = Job(JobKind.Convert, JobStatus.Succeeded, capture, Artifact(dir.File("a.adf"), Sha('5'))) with { StartedAt = T0.AddDays(1) };

        Assert.Equal(1, service.ImportHistory([decode, read])); // newest first, as Activity lists them

        Assert.Equal("First", Assert.Single(store.Search()).Title);
    }

    [Fact]
    public async Task Added_files_are_hashed_classified_and_never_duplicated()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var adf = dir.File("Lemmings.adf");
        File.WriteAllBytes(adf, new byte[901120]);

        Assert.Equal(1, await service.AddFilesAsync([adf], default));
        Assert.Equal(0, await service.AddFilesAsync([adf], default));

        var disk = Assert.Single(store.Search());
        Assert.Equal("Lemmings", disk.Title);
        Assert.Equal("Commodore Amiga", disk.System); // .adf of exactly 880 KB fits one format
        Assert.Equal(64, Assert.Single(disk.Images).Sha256.Length);
    }

    [Fact]
    public async Task Capture_and_image_with_the_same_name_are_one_disk()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        File.WriteAllBytes(dir.File("Lemmings.scp"), [1, 2, 3]);
        File.WriteAllBytes(dir.File("Lemmings.adf"), new byte[901120]);
        Directory.CreateDirectory(dir.File("other"));
        File.WriteAllBytes(dir.File(Path.Combine("other", "Lemmings.adf")), new byte[901119]);

        await service.AddFilesAsync([dir.File("Lemmings.scp")], default);
        await service.AddFilesAsync([dir.File("Lemmings.adf"), dir.File(Path.Combine("other", "Lemmings.adf"))], default);

        var disks = store.Search();
        Assert.Equal(2, disks.Count); // a same-named file in another folder is a different disk
        var both = disks.Single(d => d.Images.Count == 2);
        Assert.Equal("Commodore Amiga", both.System); // learned from the .adf
    }

    [Fact]
    public async Task Integrity_check_detects_changed_and_missing_files()
    {
        using var dir = new TempDir();
        var (store, service) = Library(dir);
        var adf = dir.File("disk.adf");
        File.WriteAllBytes(adf, [1, 2, 3]);
        await service.AddFilesAsync([adf], default);
        var image = Assert.Single(Assert.Single(store.Search()).Images);

        Assert.Equal(IntegrityResult.Unchanged, await LibraryService.CheckAsync(image, default));
        File.WriteAllBytes(adf, [1, 2, 4]);
        Assert.Equal(IntegrityResult.Changed, await LibraryService.CheckAsync(image, default));
        File.Delete(adf);
        Assert.Equal(IntegrityResult.Missing, await LibraryService.CheckAsync(image, default));
    }

    [Fact]
    public void Tags_are_trimmed_and_deduplicated()
    {
        Assert.Equal(["game", "Psygnosis"], LibraryService.ParseTags(" game, Psygnosis ;GAME,, "));
    }

    [Fact]
    public async Task A_catalogue_failure_never_fails_the_job()
    {
        using var dir = new TempDir();
        var jobs = new CataloguingJobStore(new JsonJobStore(new AppPaths(dir.File("home"))), new LibraryService(new BrokenStore(), new FormatRegistry()));

        var saved = await jobs.SaveAsync(Job(JobKind.Read, JobStatus.Succeeded, Artifact(dir.File("a.adf"), Sha('6'))), [], default);

        Assert.True(File.Exists(saved.LogPath));
    }

    private sealed class BrokenStore : ILibraryStore
    {
        public bool IsNew => false;
        public IReadOnlyList<LibraryDisk> Search(string? text = null, int max = 500) => throw new IOException("disk full");
        public LibraryDisk? Get(Guid diskId) => throw new IOException("disk full");
        public LibraryImage? FindImageByHash(string sha256) => throw new IOException("disk full");
        public LibraryImage? FindImageByPath(string path) => throw new IOException("disk full");
        public bool HasJob(Guid jobId) => throw new IOException("disk full");
        public void MarkJob(Guid jobId) => throw new IOException("disk full");
        public void SaveDisk(LibraryDisk disk) => throw new IOException("disk full");
        public void SaveImage(LibraryImage image) => throw new IOException("disk full");
        public void DeleteDisk(Guid diskId) => throw new IOException("disk full");
        public void DeleteImage(Guid imageId) => throw new IOException("disk full");
    }
}
