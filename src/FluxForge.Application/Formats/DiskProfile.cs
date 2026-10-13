namespace FluxForge.Application.Formats;

/// <summary>
/// A user-facing preset mapped to an exact upstream Greaseweazle diskdef (the <c>--format</c> value).
/// </summary>
public sealed record DiskProfile(
    string Id,
    string System,
    string DisplayName,
    string GwFormat,
    string SectorExtension,
    int Cylinders,
    int Heads,
    int SectorsPerTrack,
    int BytesPerSector,
    bool CanWrite = true,
    int? KnownTotalSectors = null)
{
    /// <summary>Total sectors; zoned formats (e.g. C64) supply an explicit count.</summary>
    public int TotalSectors => KnownTotalSectors ?? Cylinders * Heads * SectorsPerTrack;
    public long ExpectedSectorImageBytes => (long)TotalSectors * BytesPerSector;
    public override string ToString() => $"{System} — {DisplayName}";
}
