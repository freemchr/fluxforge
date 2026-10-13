using System.Globalization;
using System.Text.RegularExpressions;

namespace FluxForge.Application.Gw;

public sealed record GwInfo(
    GwVersion? HostToolsVersion,
    bool DeviceFound,
    string? Port,
    string? Model,
    string? Firmware,
    string? Serial,
    string? ErrorMessage,
    bool IsSimulated);

/// <summary>
/// Parses gw.exe console output. The output is presentation-oriented, not a stable API (PRD §9), so
/// this parser is written for the 1.2x series, records its confidence, and falls back to "unknown"
/// rather than guessing success. Fixtures captured in the Phase 0 spike pin its behaviour.
/// </summary>
public static partial class GwOutputParser
{
    public const string SimulatedMarker = "FLUXFORGE-SIMULATED";

    public static GwInfo ParseInfo(string text)
    {
        GwVersion? version = null;
        string? port = null, model = null, firmware = null, serial = null, error = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("Host Tools:", StringComparison.OrdinalIgnoreCase) &&
                GwVersion.TryParse(line["Host Tools:".Length..], out var v))
                version = v;
            else if (TryField(line, "Port:", out var p)) port = p;
            else if (TryField(line, "Model:", out var m)) model = m;
            else if (TryField(line, "Firmware:", out var f)) firmware = f;
            else if (TryField(line, "Serial:", out var s)) serial = s;
            else if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("** ", StringComparison.Ordinal))
                error ??= line.TrimStart('*', ' ');
        }

        var deviceFound = model is not null && error is null &&
                          !string.Equals(port, "Not found", StringComparison.OrdinalIgnoreCase);
        return new GwInfo(version, deviceFound, port, model, firmware, serial, error,
            text.Contains(SimulatedMarker, StringComparison.Ordinal));
    }

    private static bool TryField(string line, string key, out string value)
    {
        value = "";
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;
        value = line[key.Length..].Trim();
        return value.Length > 0;
    }

    [GeneratedRegex(@"^T(?<c>\d+)\.(?<h>\d+)(?:\s*<-\s*\S+)?:\s*(?<rest>.*)$")]
    internal static partial Regex TrackLine();

    [GeneratedRegex(@"\((?<found>\d+)/(?<total>\d+) sectors\)")]
    internal static partial Regex SectorCount();

    [GeneratedRegex(@"Found (?<found>\d+) sectors of (?<total>\d+)")]
    internal static partial Regex Summary();

    internal static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
}

/// <summary>Accumulates per-track state from a live stream of gw read/write output lines.</summary>
public sealed class TrackProgressTracker
{
    private readonly Dictionary<(int Cyl, int Head), TrackObservation> _tracks = [];

    public IReadOnlyCollection<TrackObservation> Tracks => _tracks.Values;
    public SectorSummary? Summary { get; private set; }
    public int UnparsedLines { get; private set; }

    /// <summary>Feed one output line; returns the affected track, if any.</summary>
    public TrackObservation? Feed(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;

        var summary = GwOutputParser.Summary().Match(line);
        if (summary.Success)
        {
            Summary = new SectorSummary(GwOutputParser.Int(summary.Groups["found"]), GwOutputParser.Int(summary.Groups["total"]));
            return null;
        }

        var m = GwOutputParser.TrackLine().Match(line);
        if (!m.Success)
        {
            UnparsedLines++;
            return null;
        }

        var key = (GwOutputParser.Int(m.Groups["c"]), GwOutputParser.Int(m.Groups["h"]));
        var rest = m.Groups["rest"].Value;
        _tracks.TryGetValue(key, out var prev);
        var log = (prev?.LogLines ?? []).Append(line).ToArray();
        var retries = prev?.Retries ?? 0;

        TrackObservation next;
        var sectors = GwOutputParser.SectorCount().Match(rest);
        if (sectors.Success)
        {
            // A second sector report for the same track means gw retried it.
            if (prev is { SectorsTotal: not null, Status: not TrackStatus.Reading }) retries++;
            var found = GwOutputParser.Int(sectors.Groups["found"]);
            var total = GwOutputParser.Int(sectors.Groups["total"]);
            var status = found == total && total > 0
                ? (retries > 0 ? TrackStatus.RecoveredAfterRetry : TrackStatus.DecodedOk)
                : TrackStatus.Unreadable;
            next = new TrackObservation(key.Item1, key.Item2, status, found, total, retries, ParseConfidence.High, log);
        }
        else if (rest.Contains("Retry", StringComparison.OrdinalIgnoreCase))
        {
            next = (prev ?? Empty(key)) with { Status = TrackStatus.Reading, Retries = retries + 1, LogLines = log, Confidence = ParseConfidence.Medium };
        }
        else if (rest.StartsWith("Raw Flux", StringComparison.OrdinalIgnoreCase))
        {
            next = new TrackObservation(key.Item1, key.Item2, TrackStatus.FluxCapturedDecodeUnknown, null, null, retries, ParseConfidence.Medium, log);
        }
        else
        {
            // Recognised track header, unrecognised content: keep prior status, flag low confidence.
            next = (prev ?? Empty(key)) with { LogLines = log, Confidence = ParseConfidence.Low };
        }

        _tracks[key] = next;
        return next;
    }

    /// <summary>
    /// Call once the process has exited. Tracks left mid-retry with a known shortfall become
    /// <see cref="TrackStatus.Unreadable"/>; tracks with no evidence stay unresolved.
    /// </summary>
    public void Finish()
    {
        foreach (var (key, t) in _tracks.ToList())
        {
            if (t.Status == TrackStatus.Reading && t.SectorsTotal is { } total && t.SectorsFound < total)
                _tracks[key] = t with { Status = TrackStatus.Unreadable };
        }
    }

    public int UnresolvedTracks => _tracks.Values.Count(t => t.Status == TrackStatus.Reading);

    private static TrackObservation Empty((int, int) key) =>
        new(key.Item1, key.Item2, TrackStatus.Reading, null, null, 0, ParseConfidence.None, []);
}
