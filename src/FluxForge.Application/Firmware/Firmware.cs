using System.Text.RegularExpressions;
using FluxForge.Application.Gw;
using FluxForge.Application.Tools;

namespace FluxForge.Application.Firmware;

/// <summary>Where the latest official firmware release is looked up (github.com/keirf/greaseweazle-firmware).</summary>
public interface IFirmwareReleaseSource
{
    Task<ReleaseInfo> GetLatestFirmwareAsync(CancellationToken ct);
}

public enum FirmwareAdvice { Unknown, UpToDate, UpdateAvailable, NewerThanRelease }

public enum FirmwareUpdateOutcome
{
    /// <summary>No recognisable result line: never treated as success.</summary>
    Unknown,
    /// <summary>gw printed "Done." after flashing the main firmware.</summary>
    Flashed,
    /// <summary>"** SKIPPING UPDATE": the board already runs this version or newer. Nothing was written.</summary>
    AlreadyCurrent,
    /// <summary>"** UPDATE FAILED", "Command Failed:" or "ERROR:".</summary>
    Failed,
}

public sealed record FirmwareUpdateResult(FirmwareUpdateOutcome Outcome, GwVersion? TargetVersion, string? Message, bool NeedsJumperRemoved);

public static partial class FirmwareAssistant
{
    /// <summary>The official firmware guide, including recovery with the update jumper if a flash goes wrong.</summary>
    public const string RecoveryGuide = "https://github.com/keirf/greaseweazle/wiki/Firmware-Update";

    public static FirmwareAdvice Advise(string? installedFirmware, ReleaseInfo? latest)
    {
        if (latest is null || !GwVersion.TryParse(installedFirmware, out var installed)) return FirmwareAdvice.Unknown;
        return installed < latest.Version ? FirmwareAdvice.UpdateAvailable
            : installed > latest.Version ? FirmwareAdvice.NewerThanRelease
            : FirmwareAdvice.UpToDate;
    }

    /// <summary>
    /// Reads gw update's output (gw 1.23, tools/update.py). gw exits 0 on every outcome, so the result
    /// lines are the only evidence.
    /// </summary>
    public static FirmwareUpdateResult ParseUpdate(IEnumerable<string> lines)
    {
        GwVersion? target = null;
        var outcome = FirmwareUpdateOutcome.Unknown;
        string? message = null;
        var jumper = false;
        var list = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var line = list[i];
            if (UpdatingLine().Match(line) is { Success: true } m && GwVersion.TryParse(m.Groups["v"].Value, out var v))
                target = v;
            else if (line.Equals("Done.", StringComparison.Ordinal) && outcome == FirmwareUpdateOutcome.Unknown)
                outcome = FirmwareUpdateOutcome.Flashed;
            else if (line.StartsWith("** SKIPPING UPDATE", StringComparison.Ordinal))
            {
                outcome = FirmwareUpdateOutcome.AlreadyCurrent;
                message = i + 1 < list.Count ? list[i + 1] : null;
            }
            else if (line.StartsWith("** UPDATE FAILED", StringComparison.Ordinal) || line.StartsWith("Command Failed:", StringComparison.Ordinal)
                     || line.StartsWith("ERROR:", StringComparison.Ordinal) || line.StartsWith("** FATAL ERROR", StringComparison.Ordinal))
            {
                outcome = FirmwareUpdateOutcome.Failed;
                message ??= line.TrimStart('*', ' ');
            }
            else if (line.Contains("remove the Update Jumper", StringComparison.OrdinalIgnoreCase))
                jumper = true;
        }
        return new FirmwareUpdateResult(outcome, target, message, jumper);
    }

    [GeneratedRegex(@"^Updating Main Firmware to version (?<v>\d+\.\d+)")]
    private static partial Regex UpdatingLine();
}
