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
