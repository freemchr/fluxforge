namespace FluxForge.Application.Gw;

/// <summary>Track legend from PRD §6. Never infer <see cref="DecodedOk"/> without sector evidence.</summary>
public enum TrackStatus
{
    NotRead,
    Reading,
    DecodedOk,
    RecoveredAfterRetry,
    Unreadable,
    FluxCapturedDecodeUnknown,
    NotApplicable,
}

/// <summary>How sure the parser is that it understood the tool's output for this track.</summary>
public enum ParseConfidence { None, Low, Medium, High }

public sealed record TrackObservation(
    int Cylinder,
    int Head,
    TrackStatus Status,
    int? SectorsFound,
    int? SectorsTotal,
    int Retries,
    ParseConfidence Confidence,
    IReadOnlyList<string> LogLines);

public sealed record SectorSummary(int Found, int Total)
{
    public bool Complete => Found == Total && Total > 0;
}
