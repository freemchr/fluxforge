using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Tools;
using FluxForge.Application.Verify;
using FluxForge.Infrastructure.Storage;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class VerifyTests
{
    private static readonly DiskProfile Amiga = FormatRegistry.BuiltIn.Single(p => p.Id == "amiga-dd");
    private static readonly DiskProfile Pc720 = FormatRegistry.BuiltIn.Single(p => p.Id == "ibm-720");
    private static readonly DiskProfile AtariSt720 = FormatRegistry.BuiltIn.Single(p => p.Id == "atarist-720");
    private static readonly DiskProfile C64 = FormatRegistry.BuiltIn.Single(p => p.Id == "c64-1541");
    private static readonly HostToolsInstallation Tools = new(@"C:\gw\gw.exe", new GwVersion(1, 23), HostToolsSource.Managed, false);

    private static string FixturePath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "fixtures", "gw-1.23", .. parts]);

    private static string[] FixtureLines(string scenario, string capture) =>
        File.ReadAllLines(FixturePath(scenario, capture, "stderr.txt"));

    /// <summary>The 512 bytes gw 1.23 wrote for a sector it could not decode on the worn Amiga disk.</summary>
    private static byte[] RealBadSector => File.ReadAllBytes(FixturePath("amiga-worn", "bad-sector", "sector.bin"));

    private static byte[] Image(DiskProfile profile, int seed = 1)
    {
        var bytes = new byte[profile.ExpectedSectorImageBytes];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static int Offset(DiskProfile p, int cylinder, int head, int sectorIndex)
    {
        var sectors = 0;
        for (var c = 0; c < cylinder; c++) sectors += p.SectorsOnTrack(c) * p.Heads;
        sectors += head * p.SectorsOnTrack(cylinder) + sectorIndex;
        return sectors * p.BytesPerSector;
    }

    private static void Fill(byte[] image, DiskProfile p, int c, int h, int s, byte[] content) =>
        content.CopyTo(image, Offset(p, c, h, s));

    private static TrackProgressTracker Tracker(params string[] lines)
    {
        var t = new TrackProgressTracker();
        foreach (var l in lines) t.Feed(l);
        t.Finish();
        return t;
    }

    [Fact]
    public void Real_gw_bad_sector_fill_is_recognised()
    {
        Assert.True(SectorComparer.IsBadSectorFill(RealBadSector));
        Assert.False(SectorComparer.IsBadSectorFill(new byte[512]));
        Assert.False(SectorComparer.IsBadSectorFill(RealBadSector.AsSpan(0, 500)));
    }

    [Fact]
    public void Identical_images_match_every_sector()
    {
        var image = Image(Amiga);
        var result = SectorComparer.Compare(image, (byte[])image.Clone(), Amiga);

        Assert.Equal(1760, result.Total);
        Assert.Equal(1760, result.Matched);
        Assert.Equal(160, result.Tracks.Count);
        Assert.All(result.Tracks, t => Assert.Equal(TrackStatus.Match, t.Status));
        Assert.Equal((JobStatus.Succeeded, "All 1,760 sectors match Workbench.adf."),
            JobRunner.EvaluateVerify(result, Tracker(), Amiga, "Workbench.adf"));
    }

    [Fact]
    public void Changed_byte_is_a_mismatch_at_the_right_amiga_sector()
    {
        var image = Image(Amiga);
        var disk = (byte[])image.Clone();
        disk[Offset(Amiga, 41, 1, 6) + 100] ^= 0xFF;

        var result = SectorComparer.Compare(image, disk, Amiga);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(new SectorProblem(41, 1, 6, SectorVerdict.Differs), problem); // AmigaDOS numbers sectors from 0
        Assert.Equal(TrackStatus.Mismatch, result.Tracks.Single(t => t is { Cylinder: 41, Head: 1 }).Status);
        var (status, detail) = JobRunner.EvaluateVerify(result, Tracker(), Amiga, "a.adf");
        Assert.Equal(JobStatus.Failed, status);
        Assert.Equal("1 sector(s) on 1 track(s) differ from a.adf.", detail);
    }

    [Fact]
    public void Pc_sectors_are_numbered_from_one()
    {
        var image = Image(Pc720);
        var disk = (byte[])image.Clone();
        disk[Offset(Pc720, 0, 0, 0)] ^= 1;

        Assert.Equal(1, Assert.Single(SectorComparer.Compare(image, disk, Pc720).Problems).Sector);
    }

    [Fact]
    public void Sector_gw_could_not_read_is_unreadable_not_different()
    {
        var image = Image(Amiga);
        var disk = (byte[])image.Clone();
        Fill(disk, Amiga, 12, 0, 1, RealBadSector);

        var result = SectorComparer.Compare(image, disk, Amiga);

        Assert.Equal(new SectorProblem(12, 0, 1, SectorVerdict.UnreadableOnDisk), Assert.Single(result.Problems));
        Assert.Equal(TrackStatus.Unreadable, result.Tracks.Single(t => t is { Cylinder: 12, Head: 0 }).Status);
        var (status, detail) = JobRunner.EvaluateVerify(result, Tracker(), Amiga, "a.adf");
        Assert.Equal(JobStatus.Partial, status);
        Assert.StartsWith("1 sector(s) on 1 track(s) could not be read from the disk. The other 1,759 match a.adf.", detail);
    }

    [Fact]
    public void Image_without_data_for_a_sector_is_never_counted_as_a_match()
    {
        var image = Image(Amiga);
        Fill(image, Amiga, 60, 1, 4, RealBadSector);
        var disk = (byte[])image.Clone(); // the disk carries the same fill bytes back

        var result = SectorComparer.Compare(image, disk, Amiga);

        Assert.Equal(SectorVerdict.NotInImage, Assert.Single(result.Problems).Verdict);
        Assert.Equal(TrackStatus.NotInImage, result.Tracks.Single(t => t is { Cylinder: 60, Head: 1 }).Status);
        Assert.Equal(JobStatus.Partial, JobRunner.EvaluateVerify(result, Tracker(), Amiga, "a.adf").Status);
    }

    [Fact]
    public void Track_detail_lists_problem_sectors_by_kind()
    {
        var image = Image(Amiga);
        var disk = (byte[])image.Clone();
        disk[Offset(Amiga, 3, 0, 2)] ^= 1;
        disk[Offset(Amiga, 3, 0, 7)] ^= 1;
        Fill(disk, Amiga, 3, 0, 5, RealBadSector);

        var cell = SectorComparer.ToObservations(SectorComparer.Compare(image, disk, Amiga)).Single(o => o is { Cylinder: 3, Head: 0 });

        Assert.Equal(TrackStatus.Mismatch, cell.Status);
        Assert.Equal(8, cell.SectorsFound);
        Assert.Equal(11, cell.SectorsTotal);
        Assert.Equal("Differs: sectors 2, 7 · Unreadable on disk: sector 5", cell.Detail);
    }

    [Fact]
    public void C64_zoned_layout_covers_all_683_sectors()
    {
        Assert.Equal(683, Enumerable.Range(0, C64.Cylinders).Sum(C64.SectorsOnTrack));
        var image = Image(C64);
        var disk = (byte[])image.Clone();
        disk[^1] ^= 1; // last sector of the last track (17-sector zone)

        var result = SectorComparer.Compare(image, disk, C64);

        Assert.Equal(683, result.Total);
        Assert.Equal(new SectorProblem(34, 0, 16, SectorVerdict.Differs), Assert.Single(result.Problems));
    }

    [Fact]
    public void Wrong_sized_readback_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => SectorComparer.Compare(Image(Amiga), new byte[1000], Amiga));
        Assert.Contains("read back 1,000 bytes", ex.Message);
    }

    [Fact]
    public void Extra_sectors_on_the_disk_stop_a_full_match_being_success()
    {
        // Real gw output: an 800K Atari ST disk read as the 720K format.
        var tracker = Tracker(FixtureLines("atarist-struggling", "read-raw-format"));
        var image = Image(AtariSt720);

        var (status, detail) = JobRunner.EvaluateVerify(SectorComparer.Compare(image, image, AtariSt720), tracker, AtariSt720, "game.st");

        Assert.Equal(JobStatus.Partial, status);
        Assert.Contains("beyond what atarist.720 defines", detail);
    }

    private static FakeProcessRunner Gw(byte[] disk, string[] readOutput, byte[]? decoded = null, int convertExit = 0) =>
        new((req, output, _) =>
        {
            switch (req.Arguments[0])
            {
                case "read":
                    File.WriteAllBytes(req.Arguments[^1], disk);
                    return Task.FromResult(FakeProcessRunner.Emit(output, 0, readOutput));
                case "convert":
                    if (decoded is not null) File.WriteAllBytes(req.Arguments[^1], decoded);
                    return Task.FromResult(FakeProcessRunner.Emit(output, convertExit, convertExit == 0 ? "Converted" : "** FATAL ERROR: bad image"));
                default:
                    return Task.FromResult(FakeProcessRunner.Result(0));
            }
        });

    [Fact]
    public async Task Verify_job_reads_to_temp_compares_and_cleans_up()
    {
        using var dir = new TempDir();
        var image = Image(Amiga);
        var imagePath = dir.File("before.adf");
        File.WriteAllBytes(imagePath, image);
        // Real gw output from verifying the CF0 test disk against its backup image.
        var runner = Gw(image, FixtureLines("verify-cf0", "read"));
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        var record = await jobs.RunVerifyAsync(new VerifyJob(Tools, imagePath, Amiga, new DeviceTarget(Drive: "B"), null), null, default);

        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal(JobKind.Verify, record.Kind);
        Assert.Equal("All 1,760 sectors match before.adf.", record.StatusDetail);
        Assert.Equal(new SectorSummary(1760, 1760), record.Sectors);
        Assert.Equal(1760, record.Verify!.Matched);
        Assert.Equal(160, record.Tracks.Count);
        Assert.All(record.Tracks, t => Assert.Equal(TrackStatus.Match, t.Status));
        Assert.Equal(imagePath, Assert.Single(record.Artifacts).Path);

        var read = Assert.Single(runner.Requests).Arguments;
        Assert.Equal(["read", "--format=amiga.amigados", "--drive=B"], read.Take(3));
        Assert.False(Directory.Exists(Path.GetDirectoryName(read[^1])), "the temporary readback must be deleted");
        Assert.Equal(["before.adf"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
        Assert.True(File.Exists(record.LogPath));
    }

    [Fact]
    public async Task Raw_capture_reference_is_decoded_before_the_disk_is_read()
    {
        using var dir = new TempDir();
        var scp = dir.File("capture.scp");
        File.WriteAllBytes(scp, [0x53, 0x43, 0x50]);
        var decoded = Image(Amiga);
        var disk = (byte[])decoded.Clone();
        disk[Offset(Amiga, 79, 1, 10)] ^= 1;
        var runner = Gw(disk, FixtureLines("write-test", "read-after"), decoded);
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        var record = await jobs.RunVerifyAsync(new VerifyJob(Tools, scp, Amiga, new DeviceTarget(), null), null, default);

        Assert.Equal(["convert", "read"], runner.Requests.Select(r => r.Arguments[0]));
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Equal(new SectorProblem(79, 1, 10, SectorVerdict.Differs), Assert.Single(record.Verify!.Problems));
        Assert.Equal(ArtifactKind.RawFlux, Assert.Single(record.Artifacts).Kind);
    }

    [Fact]
    public async Task Failed_decode_never_touches_the_drive()
    {
        using var dir = new TempDir();
        var scp = dir.File("capture.scp");
        File.WriteAllBytes(scp, [1]);
        var runner = Gw(Image(Amiga), [], decoded: null, convertExit: 1);
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        var record = await jobs.RunVerifyAsync(new VerifyJob(Tools, scp, Amiga, new DeviceTarget(), null), null, default);

        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Contains("the disk was not read", record.StatusDetail);
        Assert.Equal(["convert"], runner.Requests.Select(r => r.Arguments[0]));
        Assert.Null(record.Verify);
    }

    [Fact]
    public async Task Image_of_the_wrong_size_is_refused_before_reading()
    {
        using var dir = new TempDir();
        var adf = dir.File("short.adf");
        File.WriteAllBytes(adf, new byte[1000]);
        var runner = Gw(Image(Amiga), []);
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobs.RunVerifyAsync(new VerifyJob(Tools, adf, Amiga, new DeviceTarget(), null), null, default));
        Assert.Empty(runner.Requests);
    }
}
