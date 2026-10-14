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
    /// <summary>Every expected sector decoded, but the track also holds sectors the format doesn't define (not in a sector image).</summary>
    ExtraSectors,
    /// <summary>Write in progress on this track.</summary>
    Writing,
    /// <summary>gw reported the track written; verification not (yet) confirmed.</summary>
    Written,
    /// <summary>Written and confirmed by gw's verify pass.</summary>
    Verified,
    /// <summary>Verify: every sector read from the disk is identical to the image.</summary>
    Match,
    /// <summary>Verify: at least one sector read from the disk differs from the image.</summary>
    Mismatch,
    /// <summary>Verify: the image itself has no data for some sectors (gw's bad-sector fill), so they could not be compared.</summary>
    NotInImage,
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
    IReadOnlyList<string> LogLines,
    int UnexpectedSectors = 0,
    string? Detail = null);

public sealed record SectorSummary(int Found, int Total)
{
    public bool Complete => Found == Total && Total > 0;
}
