using FluxForge.Application.Gw;

namespace FluxForge.Tests;

/// <summary>Drive Tools parsing and commands, pinned to real gw 1.23 output from a V4.1 board.</summary>
public class GwToolsTests
{
    private static string Real(string capture) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gw-1.23", "tools", capture, "stderr.txt")) +
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "gw-1.23", "tools", capture, "stdout.txt"));

    [Fact]
    public void Parses_real_rpm()
    {
        var r = GwTools.ParseRpm(Real("rpm"))!;
        Assert.Equal(299.642, r.Rpm, 3);
        Assert.Equal(200.239, r.PeriodMs, 3);
        Assert.True(r.IsHealthy);
        Assert.StartsWith("Healthy", r.Verdict);
    }

    [Theory]
    [InlineData(288.0, false)]
    [InlineData(359.0, true)]
    public void Rpm_verdicts(double rpm, bool healthy) => Assert.Equal(healthy, new RpmReading(rpm, 60000 / rpm).IsHealthy);

    [Fact]
    public void Parses_real_bandwidth()
    {
        var b = GwTools.ParseBandwidth(Real("bandwidth"))!;
        Assert.Equal((8.946, 8.691, 7.688), (b.WriteMeanMbps, b.ReadMeanMbps, b.ConsistentMinMbps));
        Assert.True(b.IsHealthy);
    }

    [Fact]
    public void Parses_real_delays()
    {
        var d = GwTools.ParseDelays(Real("delays"));
        Assert.Equal(8, d.Count);
        Assert.Contains(("Step Delay", "10000 µs"), d);
        Assert.Contains(("Motor Delay", "750 ms"), d);
    }

    [Fact]
    public void Commands_match_real_gw_options()
    {
        var t = new DeviceTarget("COM6", "B");
        Assert.Equal(["rpm", "--device=COM6", "--drive=B"], GwTools.Rpm(t));
        Assert.Equal(["bandwidth", "--device=COM6"], GwTools.Bandwidth(t));
        Assert.Equal(["seek", "--device=COM6", "--drive=B", "40"], GwTools.Seek(t, 40));
        Assert.Equal(["clean", "--device=COM6", "--drive=B", "--passes=3"], GwTools.Clean(t, 3));
        Assert.Equal(["erase", "--device=COM6", "--drive=B"], GwTools.Erase(t));
        foreach (var (cmd, opts) in new[] { ("seek", "--motor-on"), ("clean", "--passes"), ("erase", "--revs"), ("rpm", "--drive") })
            Assert.Contains(opts.Split(' ')[0].Split('=')[0], Real(cmd + "-help"));
        Assert.Throws<GwCommandException>(() => GwTools.Seek(t, 90));
        Assert.Throws<GwCommandException>(() => GwTools.Clean(t, 0));
    }
}
