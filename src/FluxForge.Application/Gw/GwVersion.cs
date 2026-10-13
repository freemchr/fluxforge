using System.Globalization;
using System.Text.RegularExpressions;

namespace FluxForge.Application.Gw;

/// <summary>Greaseweazle host-tools version, e.g. "1.23" or "v1.23.1".</summary>
public readonly partial record struct GwVersion(int Major, int Minor, int Patch = 0) : IComparable<GwVersion>
{
    public static bool TryParse(string? text, out GwVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = VersionPattern().Match(text);
        if (!m.Success) return false;
        version = new GwVersion(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
        return true;
    }

    public int CompareTo(GwVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    public static bool operator <(GwVersion a, GwVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(GwVersion a, GwVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(GwVersion a, GwVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(GwVersion a, GwVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => Patch == 0 ? $"{Major}.{Minor}" : $"{Major}.{Minor}.{Patch}";

    [GeneratedRegex(@"v?(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionPattern();
}
