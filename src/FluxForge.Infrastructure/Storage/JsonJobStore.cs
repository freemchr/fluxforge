using System.Globalization;
using System.Text;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;

namespace FluxForge.Infrastructure.Storage;

/// <summary>
/// One folder per job containing job.json and the verbatim gw output (gw.log). SQLite indexing of
/// history arrives with the Library in Phase 2; these files remain the source of truth.
/// </summary>
public sealed class JsonJobStore(AppPaths paths) : IJobStore
{
    public string JobsDirectory => paths.Jobs;

    public async Task<JobRecord> SaveAsync(JobRecord record, IReadOnlyList<OutputLine> log, CancellationToken ct)
    {
        var folder = Path.Combine(paths.Jobs,
            $"{record.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{record.Kind}-{record.Id.ToString("N")[..8]}");
        Directory.CreateDirectory(folder);

        var logPath = Path.Combine(folder, "gw.log");
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# FluxForge {record.AppVersion} · gw {record.ToolsVersion ?? "unknown"} · {record.ToolsPath}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# args: {string.Join(' ', record.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");
        if (record.Simulated) sb.AppendLine("# SIMULATED RUN (development fake gw) — not a real capture");
        foreach (var line in log)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{line.At:HH:mm:ss.fff} {(line.Stream == OutputStream.StdErr ? "ERR" : "OUT")} {line.Text}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# exit: {record.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"} · status: {record.Status}");
        await File.WriteAllTextAsync(logPath, sb.ToString(), ct).ConfigureAwait(false);

        var saved = record with { LogPath = logPath };
        Json.WriteAtomic(Path.Combine(folder, "job.json"), saved);
        return saved;
    }

    public IReadOnlyList<JobRecord> List(int max = 200)
    {
        if (!Directory.Exists(paths.Jobs)) return [];
        return [.. Directory.EnumerateDirectories(paths.Jobs)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .Take(max)
            .Select(d => Json.Read<JobRecord>(Path.Combine(d, "job.json")))
            .OfType<JobRecord>()];
    }
}
