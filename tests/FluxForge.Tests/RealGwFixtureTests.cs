using System.Text.RegularExpressions;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;

namespace FluxForge.Tests;

/// <summary>
/// Pins FluxForge's assumptions to output captured from real Greaseweazle host tools
/// (scripts/spike/Capture-GwFixtures.ps1). Note gw writes everything to stderr.
/// </summary>
public partial class RealGwFixtureTests
{
    private static string Fixture(string scenario, string capture, string stream = "stderr") =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gw-1.23", scenario, capture, stream + ".txt"));

    [Fact]
    public void Info_from_real_v41_board_is_parsed()
    {
        var info = GwOutputParser.ParseInfo(Fixture("help-only", "info"));
        Assert.Equal(new GwVersion(1, 23), info.HostToolsVersion);
        Assert.True(info.DeviceFound);
        Assert.Equal("COM6", info.Port);
        Assert.Equal("Greaseweazle V4.1", info.Model);
        Assert.Equal("1.6", info.Firmware);
        Assert.False(info.IsSimulated);
    }

    [Fact]
    public void Gw_writes_its_output_to_stderr() =>
        Assert.Empty(Fixture("help-only", "info", "stdout"));

    [Theory]
    [InlineData("read-help", "--device", "--drive", "--format", "--raw", "--revs", "--retries")]
    [InlineData("write-help", "--device", "--drive", "--format", "--no-verify")]
    public void Every_option_FluxForge_emits_exists_in_gw(string capture, params string[] options)
    {
        var help = Fixture("help-only", capture);
        foreach (var option in options)
            Assert.Matches($@"\s{Regex.Escape(option)}\b", help);
    }

    [Fact]
    public void Raw_flag_keeps_raw_stream_and_only_verifies_with_format() =>
        Assert.Contains("--raw                 output raw stream (--format verifies only)", Fixture("help-only", "read-help"));

    [Fact]
    public void Every_builtin_profile_maps_to_a_real_diskdef()
    {
        var formats = DiskdefList().Matches(Fixture("help-only", "read-help").Split("FORMAT options:")[1].Split("Supported file suffixes:")[0])
            .Select(m => m.Value).ToHashSet();
        foreach (var profile in FormatRegistry.BuiltIn)
            Assert.Contains(profile.GwFormat, formats);
    }

    [Fact]
    public void Every_builtin_extension_is_a_supported_suffix()
    {
        var suffixes = Fixture("help-only", "read-help").Split("Supported file suffixes:")[1];
        foreach (var ext in FormatRegistry.BuiltIn.Select(p => p.SectorExtension).Append(".scp").Distinct())
            Assert.Matches($@"(^|\s){Regex.Escape(ext)}(\s|$)", suffixes);
    }

    [Fact]
    public void Reset_action_exists() =>
        Assert.Contains("reset       Reset the Greaseweazle device", Fixture("help-only", "help"));

    [GeneratedRegex(@"[a-z0-9]+(\.[a-z0-9_]+)+")]
    private static partial Regex DiskdefList();
}

/// <summary>Real reads of a worn Amiga disk on a V4.1 + 3.5" PC drive (drive B), gw 1.23.</summary>
public class RealAmigaReadTests
{
    private static readonly DiskProfile Amiga = FormatRegistry.BuiltIn.Single(p => p.Id == "amiga-dd");

    private static TrackProgressTracker Replay(string scenario, string capture)
    {
        var tracker = new TrackProgressTracker();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "gw-1.23", scenario, capture, "stderr.txt")))
            tracker.Feed(line);
        tracker.Finish();
        return tracker;
    }

    private static Application.Processes.ProcessResult Exit0 => new(0, [], false, false, TimeSpan.Zero);

    [Fact]
    public void Sector_read_of_worn_disk_is_partial_with_exact_counts()
    {
        var t = Replay("amiga-worn", "read-sector");
        Assert.Equal(new SectorSummary(1726, 1760), t.Summary);
        Assert.Equal(160, t.Tracks.Count);
        Assert.Equal(0, t.UnresolvedTracks);
        Assert.Null(t.FatalError);

        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.SectorImage, Amiga, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.Partial, status);
        Assert.Contains("1,726 of 1,760", detail);
    }

    [Fact]
    public void Retries_giving_up_and_recovery_are_classified()
    {
        var tracks = Replay("amiga-worn", "read-sector").Tracks.ToDictionary(x => (x.Cylinder, x.Head));

        var gaveUp = tracks[(0, 0)];
        Assert.Equal(TrackStatus.Unreadable, gaveUp.Status);
        Assert.Equal((10, 11, 3), (gaveUp.SectorsFound, gaveUp.SectorsTotal, gaveUp.Retries));

        var recovered = tracks[(58, 1)];
        Assert.Equal(TrackStatus.RecoveredAfterRetry, recovered.Status);
        Assert.Equal(1, recovered.Retries);

        Assert.Equal(TrackStatus.DecodedOk, tracks[(40, 0)].Status);
    }

    [Fact]
    public void Raw_capture_with_format_reports_decode_but_stays_raw()
    {
        var t = Replay("amiga-worn", "read-raw-format");
        Assert.Equal(new SectorSummary(1723, 1760), t.Summary);
        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.Preservation, Amiga, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.Partial, status);
        Assert.StartsWith("Raw flux captured.", detail);
    }

    [Fact]
    public void Pure_raw_capture_covers_82_cylinders_with_no_decode_claims()
    {
        var t = Replay("amiga-worn", "read-raw");
        Assert.Equal(164, t.Tracks.Count);
        Assert.All(t.Tracks, x => Assert.Equal(TrackStatus.FluxCapturedDecodeUnknown, x.Status));
        var (status, _) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.Preservation, null, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.CompletedUnverified, status);
    }

    [Fact]
    public void Known_good_disk_is_a_full_success()
    {
        var t = Replay("amiga-known-good", "read-raw-format");
        Assert.Equal(new SectorSummary(1760, 1760), t.Summary);
        Assert.Equal(160, t.Tracks.Count);
        Assert.All(t.Tracks, x => Assert.Equal(TrackStatus.DecodedOk, x.Status));
        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.Preservation, Amiga, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.Succeeded, status);
        Assert.Equal("Raw flux captured. All 1,760 sectors decoded.", detail);
    }

    [Fact]
    public void Command_failed_with_exit_code_zero_is_a_failure()
    {
        var t = Replay("wrong-drive", "read-raw");
        Assert.Equal("Command Failed: Seek: Track 0 not found", t.FatalError);
        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.Preservation, null, outputExists: false);
        Assert.Equal(Application.Jobs.JobStatus.Failed, status);
        Assert.Contains("straight cable usually means B", detail);

        var (writeStatus, _) = Application.Jobs.JobRunner.EvaluateWrite(Exit0, new WriteRequest(@"C:\a.adf", Amiga, new DeviceTarget()), t);
        Assert.Equal(Application.Jobs.JobStatus.Failed, writeStatus);
    }
}

/// <summary>
/// Real Atari ST disk formatted with 10 sectors/track (800K), first read as standard 720K.
/// gw decodes all 1,440 "expected" sectors and silently ignores sector 10 on every track.
/// </summary>
public class RealAtariStTests
{
    private static readonly FormatRegistry Formats = new();
    private static readonly DiskProfile St720 = Formats.Find("atarist-720")!;
    private static readonly DiskProfile St800 = Formats.Find("atarist-800")!;
    private static Application.Processes.ProcessResult Exit0 => new(0, [], false, false, TimeSpan.Zero);

    private static TrackProgressTracker Replay(string capture)
    {
        var tracker = new TrackProgressTracker();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "gw-1.23", "atarist-struggling", capture, "stderr.txt")))
            tracker.Feed(line);
        tracker.Finish();
        return tracker;
    }

    [Fact]
    public void Extra_sectors_are_counted_once_per_track()
    {
        var t = Replay("read-raw-format");
        Assert.Equal(new SectorSummary(1440, 1440), t.Summary);
        Assert.Equal(160, t.UnexpectedSectors);
        Assert.Equal(10, t.HighestUnexpectedSector);
        Assert.All(t.Tracks, x => Assert.Equal(TrackStatus.DecodedOk, x.Status));
    }

    [Fact]
    public void Sector_image_with_extra_sectors_is_never_reported_as_success()
    {
        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, Replay("read-raw-format"), CaptureMode.SectorImage, St720, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.Partial, status);
        Assert.Contains("160 sector(s) on 160 track(s)", detail);
        Assert.Contains("atarist.800", detail);
        Assert.Contains("NOT in this image", detail);
    }

    [Fact]
    public void Raw_capture_with_extra_sectors_points_to_offline_decode()
    {
        var (status, detail) = Application.Jobs.JobRunner.EvaluateRead(Exit0, Replay("read-raw-format"), CaptureMode.Preservation, St720, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.CompletedUnverified, status);
        Assert.Contains("raw capture includes them", detail);
    }

    [Fact]
    public void Decoding_the_raw_capture_as_800k_recovers_every_sector()
    {
        var t = Replay("convert-800");
        Assert.Equal(new SectorSummary(1600, 1600), t.Summary);
        Assert.Equal(0, t.UnexpectedSectors);
        var (status, _) = Application.Jobs.JobRunner.EvaluateRead(Exit0, t, CaptureMode.SectorImage, St800, outputExists: true);
        Assert.Equal(Application.Jobs.JobStatus.Succeeded, status);
    }

    [Theory]
    [InlineData("atarist-720", 10, "atarist-800")]
    [InlineData("atarist-720", 11, "atarist-880")]
    [InlineData("atarist-800", 11, "atarist-880")]
    public void Suggests_sibling_format_for_extra_sectors(string selected, int highest, string expected) =>
        Assert.Equal(expected, Formats.SuggestForExtraSectors(Formats.Find(selected)!, highest)?.Id);
}
