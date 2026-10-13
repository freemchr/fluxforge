using System.Globalization;
using FluxForge.Application.Formats;

namespace FluxForge.Application.Gw;

public enum CaptureMode
{
    /// <summary>Genuine raw flux (SCP). With a profile, <c>--raw</c> is always added so flux is never regenerated.</summary>
    Preservation,

    /// <summary>Decoded sector image (ADF, ST, IMG, ...).</summary>
    SectorImage,
}

public sealed record DeviceTarget(string? Port = null, string? Drive = null);

public sealed record ReadRequest(
    string OutputPath,
    CaptureMode Mode,
    DiskProfile? Profile,
    DeviceTarget Target,
    int? Revolutions = null,
    int? Retries = null);

public sealed record WriteRequest(
    string ImagePath,
    DiskProfile? Profile,
    DeviceTarget Target,
    bool Verify = true);

public sealed class GwCommandException(string message) : Exception(message);

/// <summary>
/// Builds typed, validated argument lists for gw.exe. No free-text interpolation: every value is
/// checked against a whitelist pattern before it is emitted.
/// </summary>
public static class GwCommands
{
    public static readonly IReadOnlySet<string> RawFluxExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".scp" };

    public static IReadOnlyList<string> Info(DeviceTarget? target = null)
    {
        var args = new List<string> { "info" };
        AddDevice(args, target);
        return args;
    }

    public static IReadOnlyList<string> Read(ReadRequest request)
    {
        var path = RequireAbsolutePath(request.OutputPath);
        var ext = Path.GetExtension(path);
        var args = new List<string> { "read" };

        switch (request.Mode)
        {
            case CaptureMode.Preservation:
                if (!RawFluxExtensions.Contains(ext))
                    throw new GwCommandException($"Preservation captures must be written to a raw flux container (.scp), not '{ext}'.");
                if (request.Profile is not null)
                {
                    // Critical invariant (PRD §10): with --format, gw writes regenerated flux unless --raw is given.
                    args.Add("--format=" + RequireToken(request.Profile.GwFormat));
                    args.Add("--raw");
                }
                break;

            case CaptureMode.SectorImage:
                if (request.Profile is null)
                    throw new GwCommandException("A sector image needs a disk format profile. Use a Preservation capture for unknown disks.");
                if (!string.Equals(ext, request.Profile.SectorExtension, StringComparison.OrdinalIgnoreCase))
                    throw new GwCommandException($"Profile '{request.Profile.DisplayName}' produces {request.Profile.SectorExtension} images, not '{ext}'.");
                args.Add("--format=" + RequireToken(request.Profile.GwFormat));
                break;
        }

        AddDevice(args, request.Target);
        if (request.Revolutions is { } revs)
            args.Add("--revs=" + RequireRange(revs, 1, 20, "revolutions").ToString(CultureInfo.InvariantCulture));
        if (request.Retries is { } retries)
            args.Add("--retries=" + RequireRange(retries, 0, 30, "retries").ToString(CultureInfo.InvariantCulture));

        args.Add(path);
        return args;
    }

    public static IReadOnlyList<string> Write(WriteRequest request)
    {
        var path = RequireAbsolutePath(request.ImagePath);
        var args = new List<string> { "write" };
        if (request.Profile is not null)
            args.Add("--format=" + RequireToken(request.Profile.GwFormat));
        AddDevice(args, request.Target);
        if (!request.Verify)
            args.Add("--no-verify");
        args.Add(path);
        return args;
    }

    public static IReadOnlyList<string> Convert(string input, string output, DiskProfile? profile)
    {
        var args = new List<string> { "convert" };
        if (profile is not null)
            args.Add("--format=" + RequireToken(profile.GwFormat));
        args.Add(RequireAbsolutePath(input));
        args.Add(RequireAbsolutePath(output));
        return args;
    }

    /// <summary>Human-readable preview for the "command preview" panel. Never executed.</summary>
    public static string Preview(string exe, IReadOnlyList<string> args) =>
        string.Join(' ', new[] { Quote(Path.GetFileName(exe)) }.Concat(args.Select(Quote)));

    private static string Quote(string a) => a.Contains(' ') || a.Length == 0 ? $"\"{a}\"" : a;

    private static void AddDevice(List<string> args, DeviceTarget? target)
    {
        if (target is null) return;
        if (!string.IsNullOrWhiteSpace(target.Port))
            args.Add("--device=" + RequireToken(target.Port));
        if (!string.IsNullOrWhiteSpace(target.Drive))
        {
            var drive = target.Drive.Trim().ToUpperInvariant();
            if (drive is not ("A" or "B" or "0" or "1" or "2"))
                throw new GwCommandException($"Unsupported drive selection '{target.Drive}'. Use A, B, 0, 1 or 2.");
            args.Add("--drive=" + drive);
        }
    }

    private static string RequireAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new GwCommandException($"Path must be fully qualified: '{path}'.");
        if (path.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
            throw new GwCommandException("Path contains characters that cannot be passed safely.");
        return Path.GetFullPath(path);
    }

    private static string RequireToken(string value)
    {
        // Diskdef names and COM ports: letters, digits, '.', '_', '-', ':' and '\' (for \\.\COM10).
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') ||
            !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '\\'))
            throw new GwCommandException($"Invalid option value '{value}'.");
        return value;
    }

    private static int RequireRange(int value, int min, int max, string name) =>
        value < min || value > max
            ? throw new GwCommandException($"{name} must be between {min} and {max}.")
            : value;
}
