using FluxForge.Application.Gw;

namespace FluxForge.Tests;

public class GwOutputParserTests
{
    [Fact]
    public void Parses_info_with_device()
    {
        var info = GwOutputParser.ParseInfo("""
            Host Tools: 1.23
            Device:
              Port:     COM3
              Model:    Greaseweazle V4.1
              MCU:      AT32F403A, 240MHz, 224kB SRAM
              Firmware: 1.6
              Serial:   GW1234
            """);
        Assert.Equal(new GwVersion(1, 23), info.HostToolsVersion);
        Assert.True(info.DeviceFound);
        Assert.Equal("COM3", info.Port);
        Assert.Equal("1.6", info.Firmware);
        Assert.False(info.IsSimulated);
    }

    [Fact]
    public void Parses_info_without_device()
    {
        var info = GwOutputParser.ParseInfo("Host Tools: 1.23\nDevice:\n  Port:     Not found\n** FATAL ERROR: Cannot find the Greaseweazle device");
        Assert.Equal(new GwVersion(1, 23), info.HostToolsVersion);
        Assert.False(info.DeviceFound);
        Assert.NotNull(info.ErrorMessage);
    }

    [Fact]
    public void Clean_track_is_decoded_ok()
    {
        var t = new TrackProgressTracker();
        var obs = t.Feed("T0.0: Amiga DOS (11/11 sectors) from Raw Flux (151318 flux in 400.04ms)");
        Assert.Equal(TrackStatus.DecodedOk, obs!.Status);
        Assert.Equal(ParseConfidence.High, obs.Confidence);
    }

    [Fact]
    public void Track_recovered_after_retry()
    {
        var t = new TrackProgressTracker();
        t.Feed("T12.0: Amiga DOS (10/11 sectors) from Raw Flux (150312 flux in 400.04ms)");
        t.Feed("T12.0: Retry #1.1");
        var obs = t.Feed("T12.0: Amiga DOS (11/11 sectors) from Raw Flux (150312 flux in 400.04ms)");
        Assert.Equal(TrackStatus.RecoveredAfterRetry, obs!.Status);
        Assert.Equal(1, obs.Retries);
    }

    [Fact]
    public void Track_that_gives_up_is_unreadable()
    {
        var t = new TrackProgressTracker();
        t.Feed("T40.1: Amiga DOS (9/11 sectors) from Raw Flux (150312 flux in 400.04ms)");
        t.Feed("T40.1: Retry #1.1");
        t.Feed("T40.1: Retry #2.1");
        t.Finish();
        var obs = t.Tracks.Single();
        Assert.Equal(TrackStatus.Unreadable, obs.Status);
        Assert.Equal(9, obs.SectorsFound);
        Assert.Equal(2, obs.Retries);
    }

    [Fact]
    public void Raw_flux_track_is_not_called_decoded()
    {
        var t = new TrackProgressTracker();
        var obs = t.Feed("T3.1: Raw Flux (150012 flux in 400.01ms)");
        Assert.Equal(TrackStatus.FluxCapturedDecodeUnknown, obs!.Status);
    }

    [Fact]
    public void Unknown_track_text_is_low_confidence_not_success()
    {
        var t = new TrackProgressTracker();
        var obs = t.Feed("T5.0: something new in a future gw release");
        Assert.Equal(TrackStatus.Reading, obs!.Status);
        Assert.Equal(ParseConfidence.Low, obs.Confidence);
        Assert.Equal(1, t.UnresolvedTracks);
    }

    [Fact]
    public void Summary_line_is_parsed()
    {
        var t = new TrackProgressTracker();
        t.Feed("Found 1758 sectors of 1760 (99.9%)");
        Assert.Equal(new SectorSummary(1758, 1760), t.Summary);
        Assert.False(t.Summary!.Complete);
    }

    [Theory]
    [InlineData("v1.23", 1, 23, 0)]
    [InlineData("Host Tools: 1.23.1", 1, 23, 1)]
    public void Parses_versions(string text, int major, int minor, int patch)
    {
        Assert.True(GwVersion.TryParse(text, out var v));
        Assert.Equal(new GwVersion(major, minor, patch), v);
    }
}
