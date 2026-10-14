using System.Globalization;
using System.Text.RegularExpressions;

namespace FluxForge.Application.Gw;

public sealed record RpmReading(double Rpm, double PeriodMs)
{
    /// <summary>Plain-language verdict. 3.5" DD/HD drives spin at 300 RPM, most 5.25" HD drives at 360.</summary>
    public string Verdict =>
        Math.Abs(Rpm - 300) <= 4.5 ? $"Healthy: within {Math.Abs(Rpm - 300) / 3:0.0}% of 300 RPM."
        : Math.Abs(Rpm - 360) <= 5.4 ? $"Healthy for a 360 RPM drive (within {Math.Abs(Rpm - 360) / 3.6:0.0}%)."
        : $"Unusual speed. Expected about 300 RPM (or 360 for some 5.25\" drives). The belt or motor may need attention.";

    public bool IsHealthy => Math.Abs(Rpm - 300) <= 4.5 || Math.Abs(Rpm - 360) <= 5.4;
}

public sealed record BandwidthReading(double WriteMeanMbps, double ReadMeanMbps, double ConsistentMinMbps)
{
    /// <summary>Floppy flux needs well under 2 Mbit/s; below that, reads may overflow.</summary>
    public bool IsHealthy => ConsistentMinMbps >= 2;
}

/// <summary>Drive and device utilities (gw rpm, bandwidth, seek, reset, clean, erase, delays). gw 1.23 syntax.</summary>
public static partial class GwTools
{
    public const int MaxSeekCylinder = 83;

    public static IReadOnlyList<string> Rpm(DeviceTarget target) => With("rpm", target, drive: true);
    public static IReadOnlyList<string> Bandwidth(DeviceTarget target) => With("bandwidth", target, drive: false);
    public static IReadOnlyList<string> Delays(DeviceTarget target) => With("delays", target, drive: false);
    public static IReadOnlyList<string> Reset(DeviceTarget target) => With("reset", target, drive: false);

    public static IReadOnlyList<string> Seek(DeviceTarget target, int cylinder)
    {
        if (cylinder is < 0 or > MaxSeekCylinder)
            throw new GwCommandException($"Cylinder must be between 0 and {MaxSeekCylinder}.");
        return [.. With("seek", target, drive: true), cylinder.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>Requires a cleaning disk in the drive.</summary>
    public static IReadOnlyList<string> Clean(DeviceTarget target, int passes)
    {
        if (passes is < 1 or > 10) throw new GwCommandException("Passes must be between 1 and 10.");
        return [.. With("clean", target, drive: true), "--passes=" + passes.ToString(CultureInfo.InvariantCulture)];
    }

    /// <summary>Destructive: erases every track of the disk in the drive.</summary>
    public static IReadOnlyList<string> Erase(DeviceTarget target) => With("erase", target, drive: true);

    private static IReadOnlyList<string> With(string action, DeviceTarget target, bool drive)
    {
        var args = GwCommands.Info(new DeviceTarget(target.Port)).ToList();
        args[0] = action;
        if (drive && !string.IsNullOrWhiteSpace(target.Drive))
            args.AddRange(GwCommands.Info(new DeviceTarget(null, target.Drive)).Skip(1));
        return args;
    }

    // gw 1.23: "Rate: 299.642 rpm ; Period: 200.239 ms"
    [GeneratedRegex(@"Rate:\s*(?<rpm>[\d.]+)\s*rpm\s*;\s*Period:\s*(?<ms>[\d.]+)\s*ms", RegexOptions.IgnoreCase)]
    private static partial Regex RpmLine();

    // gw 1.23: "Write Bandwidth:    8.935 /    8.946 /    9.001 Mbps"
    [GeneratedRegex(@"(?<dir>Write|Read) Bandwidth:\s*[\d.]+\s*/\s*(?<mean>[\d.]+)\s*/\s*[\d.]+\s*Mbps", RegexOptions.IgnoreCase)]
    private static partial Regex BandwidthLine();

    [GeneratedRegex(@"Consistent Min\. Bandwidth:\s*(?<min>[\d.]+)\s*Mbps", RegexOptions.IgnoreCase)]
    private static partial Regex ConsistentLine();

    // gw 1.23: "Step Delay:   10000us"
    [GeneratedRegex(@"^(?<name>[A-Za-z -]+):\s*(?<value>\d+)(?<unit>us|ms)\s*$")]
    private static partial Regex DelayLine();

    public static RpmReading? ParseRpm(string text) =>
        RpmLine().Match(text) is { Success: true } m ? new RpmReading(D(m.Groups["rpm"].Value), D(m.Groups["ms"].Value)) : null;

    public static BandwidthReading? ParseBandwidth(string text)
    {
        double? write = null, read = null, min = null;
        foreach (Match m in BandwidthLine().Matches(text))
            if (m.Groups["dir"].Value.Equals("Write", StringComparison.OrdinalIgnoreCase)) write = D(m.Groups["mean"].Value);
            else read = D(m.Groups["mean"].Value);
        if (ConsistentLine().Match(text) is { Success: true } c) min = D(c.Groups["min"].Value);
        return write is { } w && read is { } r && min is { } n ? new BandwidthReading(w, r, n) : null;
    }

    public static IReadOnlyList<(string Name, string Value)> ParseDelays(string text) =>
        [.. text.Split('\n').Select(l => DelayLine().Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => (m.Groups["name"].Value.Trim(), m.Groups["value"].Value + " " + (m.Groups["unit"].Value == "us" ? "µs" : "ms")))];

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
