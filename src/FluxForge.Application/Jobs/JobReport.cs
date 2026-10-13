using System.Globalization;
using System.Text;
using FluxForge.Application.Gw;

namespace FluxForge.Application.Jobs;

/// <summary>Plain-text job report for sharing and support (PRD FR-13, §13 Supportability).</summary>
public static class JobReport
{
    public static string ToText(JobRecord r, bool redactPaths = false)
    {
        string P(string? path) => path is null ? "" : redactPaths ? Path.GetFileName(path) : path;
        var sb = new StringBuilder();
        void Line(string label, object? value)
        {
            if (value is null || value is string { Length: 0 }) return;
            sb.Append(CultureInfo.InvariantCulture, $"{label,-16}{value}").AppendLine();
        }

        sb.AppendLine("FluxForge job report");
        sb.AppendLine(new string('=', 60));
        if (r.Simulated) sb.AppendLine("*** SIMULATED RUN — produced by the development fake gw, not real hardware ***");
        Line("Job", r.Title);
        Line("Id", r.Id);
        Line("Status", $"{r.Status} — {r.StatusDetail}");
        Line("Started", r.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Line("Ended", r.EndedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        Line("Disk label", r.DiskLabel);
        Line("Notes", r.Notes);
        Line("Format", r.GwFormat);
        Line("Capture", r.CaptureMode);
        Line("FluxForge", r.AppVersion);
        Line("gw version", r.ToolsVersion);
        Line("gw path", P(r.ToolsPath));
        Line("Device", r.DeviceModel is null ? null : $"{r.DeviceModel} on {r.DevicePort}");
        Line("Firmware", r.Firmware);
        Line("Arguments", string.Join(' ', r.Arguments.Select(a => redactPaths && Path.IsPathFullyQualified(a) ? Path.GetFileName(a) : a)));
        Line("Exit code", r.ExitCode);
        if (r.Sectors is { } s) Line("Sectors", $"{s.Found:N0} of {s.Total:N0} decoded");

        if (r.Artifacts.Count > 0)
        {
            sb.AppendLine().AppendLine("Files");
            foreach (var a in r.Artifacts)
                sb.Append(CultureInfo.InvariantCulture, $"  {P(a.Path)}{(a.Complete ? "" : "  [INCOMPLETE]")}").AppendLine()
                  .Append(CultureInfo.InvariantCulture, $"    {a.Kind}, {a.Bytes:N0} bytes, SHA-256 {a.Sha256}").AppendLine();
        }

        var problems = r.Tracks.Where(t => t.Status is not (TrackStatus.DecodedOk or TrackStatus.FluxCapturedDecodeUnknown)).ToList();
        sb.AppendLine().Append(CultureInfo.InvariantCulture, $"Tracks observed: {r.Tracks.Count}, needing attention: {problems.Count}").AppendLine();
        foreach (var t in problems)
            sb.Append(CultureInfo.InvariantCulture,
                $"  T{t.Cylinder}.{t.Head}: {t.Status}{(t.SectorsTotal is { } tot ? $" ({t.SectorsFound}/{tot} sectors)" : "")}{(t.Retries > 0 ? $", {t.Retries} retries" : "")}").AppendLine();
        return sb.ToString();
    }
}
