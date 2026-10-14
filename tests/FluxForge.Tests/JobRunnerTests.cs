using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Tools;
using FluxForge.Infrastructure.Storage;
using FluxForge.Tests.Support;

namespace FluxForge.Tests;

public class JobRunnerTests
{
    private static readonly DiskProfile Amiga = FormatRegistry.BuiltIn.Single(p => p.Id == "amiga-dd");
    private static readonly HostToolsInstallation Tools = new(@"C:\gw\gw.exe", new GwVersion(1, 23), HostToolsSource.Managed, false);

    private static ProcessResult Exit(int? code, bool cancelled = false) => new(code, [], cancelled, false, TimeSpan.Zero);

    private static TrackProgressTracker Tracker(params string[] lines)
    {
        var t = new TrackProgressTracker();
        foreach (var l in lines) t.Feed(l);
        t.Finish();
        return t;
    }

    [Fact]
    public void Complete_decode_is_success()
    {
        var (status, _) = JobRunner.EvaluateRead(Exit(0),
            Tracker("T0.0: Amiga DOS (11/11 sectors) from Raw Flux (1 flux in 1ms)", "Found 11 sectors of 11 (100%)"),
            CaptureMode.SectorImage, Amiga, outputExists: true);
        Assert.Equal(JobStatus.Succeeded, status);
    }

    [Fact]
    public void Clean_exit_without_statistics_is_never_success()
    {
        var (status, _) = JobRunner.EvaluateRead(Exit(0), Tracker(), CaptureMode.SectorImage, Amiga, outputExists: true);
        Assert.Equal(JobStatus.CompletedUnverified, status);
    }

    [Fact]
    public void Missing_sectors_are_partial()
    {
        var (status, detail) = JobRunner.EvaluateRead(Exit(0),
            Tracker("T40.1: Amiga DOS (9/11 sectors) from Raw Flux (1 flux in 1ms)", "Found 1758 sectors of 1760 (99.9%)"),
            CaptureMode.SectorImage, Amiga, outputExists: true);
        Assert.Equal(JobStatus.Partial, status);
        Assert.Contains("1 track", detail);
    }

    [Fact]
    public void Unresolved_track_blocks_success()
    {
        var (status, _) = JobRunner.EvaluateRead(Exit(0),
            Tracker("T0.0: unexpected text", "Found 11 sectors of 11 (100%)"),
            CaptureMode.SectorImage, Amiga, outputExists: true);
        Assert.Equal(JobStatus.CompletedUnverified, status);
    }

    [Fact]
    public void Raw_capture_without_profile_is_unverified()
    {
        var (status, detail) = JobRunner.EvaluateRead(Exit(0), Tracker("T0.0: Raw Flux (1 flux in 1ms)"), CaptureMode.Preservation, null, true);
        Assert.Equal(JobStatus.CompletedUnverified, status);
        Assert.StartsWith("Raw flux captured", detail);
    }

    [Fact]
    public void Cancel_message_says_where_and_whether_anything_was_kept()
    {
        var t = Tracker("T21.1: AmigaDOS (11/11 sectors) from Raw Flux (125452 flux in 497.31ms)");
        Assert.Equal("Cancelled at cylinder 21. Nothing was saved — gw only writes the image file when a read finishes.",
            JobRunner.EvaluateRead(Exit(null, cancelled: true), t, CaptureMode.SectorImage, Amiga, outputExists: false).Detail);
        Assert.EndsWith("(INCOMPLETE).", JobRunner.EvaluateRead(Exit(null, cancelled: true), t, CaptureMode.SectorImage, Amiga, outputExists: true).Detail);
    }

    [Theory]
    [InlineData(1, false, true, JobStatus.Failed)]
    [InlineData(null, true, true, JobStatus.Cancelled)]
    [InlineData(0, false, false, JobStatus.Failed)]
    public void Failures_are_reported(int? exit, bool cancelled, bool output, JobStatus expected)
    {
        var (status, _) = JobRunner.EvaluateRead(Exit(exit, cancelled), Tracker(), CaptureMode.SectorImage, Amiga, output);
        Assert.Equal(expected, status);
    }

    [Fact]
    public void Interrupted_write_is_unknown_state()
    {
        var (status, detail) = JobRunner.EvaluateWrite(Exit(null, cancelled: true), new WriteRequest(@"C:\a.adf", Amiga, new DeviceTarget()));
        Assert.Equal(JobStatus.Interrupted, status);
        Assert.Contains("unknown state", detail);
    }

    [Fact]
    public void Raw_write_is_not_called_verified()
    {
        var (status, _) = JobRunner.EvaluateWrite(Exit(0), new WriteRequest(@"C:\a.scp", null, new DeviceTarget()));
        Assert.Equal(JobStatus.CompletedUnverified, status);
    }

    [Fact]
    public async Task Successful_read_moves_image_into_place_and_records_hash()
    {
        using var dir = new TempDir();
        var runner = new FakeProcessRunner((req, output, _) =>
        {
            File.WriteAllBytes(req.Arguments[^1], new byte[901120]);
            return Task.FromResult(FakeProcessRunner.Emit(output, 0,
                "T0.0: Amiga DOS (11/11 sectors) from Raw Flux (1 flux in 1ms)", "Found 11 sectors of 11 (100%)"));
        });
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));
        var final = dir.File("Workbench 1.3.adf");

        var record = await jobs.RunReadAsync(new ReadJob(Tools, new ReadRequest(final, CaptureMode.SectorImage, Amiga, new DeviceTarget()), null, "Workbench", null), null, default);

        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.True(File.Exists(final));
        var artifact = Assert.Single(record.Artifacts);
        Assert.Equal(final, artifact.Path);
        Assert.Equal(64, artifact.Sha256.Length);
        Assert.True(File.Exists(record.LogPath));
        Assert.False(Directory.Exists(dir.File(".fluxforge-partial")));
    }

    [Fact]
    public async Task Cancelled_read_keeps_data_under_incomplete_name()
    {
        using var dir = new TempDir();
        var runner = new FakeProcessRunner((req, _, _) =>
        {
            if (req.Arguments[0] == "read") File.WriteAllBytes(req.Arguments[^1], new byte[1000]);
            return Task.FromResult(req.Arguments[0] == "read" ? Exit(null, cancelled: true) : Exit(0));
        });
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));
        var final = dir.File("disk.adf");

        var record = await jobs.RunReadAsync(new ReadJob(Tools, new ReadRequest(final, CaptureMode.SectorImage, Amiga, new DeviceTarget()), null, null, null), null, default);

        Assert.Equal(JobStatus.Cancelled, record.Status);
        Assert.False(File.Exists(final));
        var artifact = Assert.Single(record.Artifacts);
        Assert.EndsWith("disk (INCOMPLETE).adf", artifact.Path);
        Assert.False(artifact.Complete);
        Assert.Contains(runner.Requests, r => r.Arguments[0] == "reset");
    }

    [Fact]
    public async Task Existing_output_is_never_overwritten()
    {
        using var dir = new TempDir();
        var final = dir.File("disk.adf");
        File.WriteAllText(final, "original");
        var runner = new FakeProcessRunner((_, _, _) => throw new InvalidOperationException("should not run"));
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobs.RunReadAsync(new ReadJob(Tools, new ReadRequest(final, CaptureMode.SectorImage, Amiga, new DeviceTarget()), null, null, null), null, default));
        Assert.Equal("original", File.ReadAllText(final));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Second_job_on_same_device_is_refused()
    {
        using var dir = new TempDir();
        var gate = new TaskCompletionSource();
        var runner = new FakeProcessRunner(async (req, _, _) =>
        {
            await gate.Task;
            File.WriteAllBytes(req.Arguments[^1], [1]);
            return Exit(0);
        });
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));
        var first = jobs.RunReadAsync(new ReadJob(Tools, new ReadRequest(dir.File("a.adf"), CaptureMode.SectorImage, Amiga, new DeviceTarget()), null, null, null), null, default);

        Assert.True(jobs.HasActiveJobs);
        await Assert.ThrowsAsync<DeviceBusyException>(() =>
            jobs.RunReadAsync(new ReadJob(Tools, new ReadRequest(dir.File("b.adf"), CaptureMode.SectorImage, Amiga, new DeviceTarget()), null, null, null), null, default));

        gate.SetResult();
        await first;
        Assert.False(jobs.HasActiveJobs);
    }
}

public class DecodeJobTests
{
    private static readonly HostToolsInstallation Tools = new(@"C:\gw\gw.exe", new GwVersion(1, 23), HostToolsSource.Managed, false);

    [Fact]
    public async Task Decode_converts_capture_without_touching_device_lock()
    {
        using var dir = new TempDir();
        var scp = dir.File("disk.scp");
        File.WriteAllBytes(scp, [0x53, 0x43, 0x50]);
        var st800 = new FormatRegistry().Find("atarist-800")!;
        var runner = new FakeProcessRunner((req, output, _) =>
        {
            Assert.Equal(["convert", "--format=atarist.800", scp], req.Arguments.Take(3));
            File.WriteAllBytes(req.Arguments[^1], new byte[819200]);
            return Task.FromResult(FakeProcessRunner.Emit(output, 0,
                "T0.0: IBM MFM (10/10 sectors) from Raw Flux (86555 flux in 400.55ms)", "Found 10 sectors of 10 (100%)"));
        });
        var jobs = new JobRunner(runner, new JsonJobStore(new AppPaths(dir.File("home"))));

        var record = await jobs.RunDecodeAsync(new DecodeJob(Tools, scp, dir.File("disk.st"), st800), null, default);

        Assert.Equal(JobStatus.Succeeded, record.Status);
        Assert.Equal(JobKind.Convert, record.Kind);
        Assert.Equal(2, record.Artifacts.Count); // source capture + decoded image, both hashed
        Assert.True(File.Exists(dir.File("disk.st")));
        Assert.False(jobs.IsDeviceBusy(null));
    }

    [Fact]
    public async Task Decode_refuses_non_flux_input_and_existing_output()
    {
        using var dir = new TempDir();
        var adf = dir.File("disk.adf");
        File.WriteAllBytes(adf, new byte[10]);
        var amiga = FormatRegistry.BuiltIn.Single(p => p.Id == "amiga-dd");
        var jobs = new JobRunner(new FakeProcessRunner((_, _, _) => throw new InvalidOperationException("must not run")), new JsonJobStore(new AppPaths(dir.File("home"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.RunDecodeAsync(new DecodeJob(Tools, adf, dir.File("out.adf"), amiga), null, default));

        var scp = dir.File("disk.scp");
        File.WriteAllBytes(scp, [1]);
        File.WriteAllText(dir.File("taken.adf"), "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.RunDecodeAsync(new DecodeJob(Tools, scp, dir.File("taken.adf"), amiga), null, default));
    }
}
