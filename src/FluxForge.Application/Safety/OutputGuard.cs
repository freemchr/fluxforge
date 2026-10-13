using FluxForge.Application.Formats;
using FluxForge.Application.Gw;

namespace FluxForge.Application.Safety;

public sealed record SafetyIssue(bool Blocking, string Message);

/// <summary>Pre-flight checks that keep jobs from overwriting files or running out of disk space.</summary>
public static class OutputGuard
{
    public const long RawCaptureReserveBytes = 256L * 1024 * 1024;

    public static long EstimateBytes(CaptureMode mode, DiskProfile? profile) =>
        mode == CaptureMode.Preservation ? RawCaptureReserveBytes : (profile?.ExpectedSectorImageBytes ?? 4_000_000) * 2 + 1_000_000;

    public static IReadOnlyList<SafetyIssue> CheckReadOutput(string outputPath, CaptureMode mode, DiskProfile? profile, Func<string, long?> freeSpace)
    {
        var issues = new List<SafetyIssue>();
        if (!Path.IsPathFullyQualified(outputPath))
        {
            issues.Add(new(true, "Choose a full output path."));
            return issues;
        }
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is null || !Directory.Exists(dir))
            issues.Add(new(true, $"Output folder does not exist: {dir}"));
        if (File.Exists(outputPath))
            issues.Add(new(true, $"'{Path.GetFileName(outputPath)}' already exists. FluxForge never overwrites existing images — choose a new name."));
        if (dir is not null && freeSpace(dir) is { } free && free < EstimateBytes(mode, profile))
            issues.Add(new(true, $"Not enough free space in {dir} ({free / 1024 / 1024} MB free)."));
        return issues;
    }

    public static IReadOnlyList<SafetyIssue> CheckWriteImage(string imagePath, DiskProfile? profile)
    {
        var issues = new List<SafetyIssue>();
        if (!File.Exists(imagePath))
        {
            issues.Add(new(true, "Image file not found."));
            return issues;
        }
        var ext = Path.GetExtension(imagePath);
        var isRaw = GwCommands.RawFluxExtensions.Contains(ext);
        if (!isRaw && profile is null)
            issues.Add(new(true, $"Select the disk format for this {ext} image — the extension alone is ambiguous."));
        if (profile is { CanWrite: false })
            issues.Add(new(true, $"{profile.DisplayName} images cannot be written by FluxForge."));
        if (!isRaw && profile is not null)
        {
            if (!string.Equals(ext, profile.SectorExtension, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(true, $"{profile.DisplayName} expects a {profile.SectorExtension} image, not {ext}."));
            var size = new FileInfo(imagePath).Length;
            if (size != profile.ExpectedSectorImageBytes)
                issues.Add(new(false, $"Image is {size:N0} bytes; {profile.DisplayName} is normally {profile.ExpectedSectorImageBytes:N0} bytes. Check the format before writing."));
        }
        if (isRaw)
            issues.Add(new(false, "Raw flux images are not verified the same way as sector images. Use a separate readback comparison if you need assurance."));
        return issues;
    }

    /// <summary>Returns "name (INCOMPLETE).ext", "name (INCOMPLETE 2).ext", ... that does not yet exist.</summary>
    public static string IncompleteName(string finalPath)
    {
        var dir = Path.GetDirectoryName(finalPath)!;
        var stem = Path.GetFileNameWithoutExtension(finalPath);
        var ext = Path.GetExtension(finalPath);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, i == 1 ? $"{stem} (INCOMPLETE){ext}" : $"{stem} (INCOMPLETE {i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
