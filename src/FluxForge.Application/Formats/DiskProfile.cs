namespace FluxForge.Application.Formats;

/// <summary>
/// A user-facing preset mapped to an exact upstream Greaseweazle diskdef (the <c>--format</c> value).
/// </summary>
/// <param name="FirstSector">Number of the first sector on a track: 1 for IBM-style formats, 0 for Amiga and C64.</param>
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
    int? KnownTotalSectors = null,
    int FirstSector = 1)
{
    /// <summary>Total sectors; zoned formats (e.g. C64) supply an explicit count.</summary>
    public int TotalSectors => KnownTotalSectors ?? Cylinders * Heads * SectorsPerTrack;
    public long ExpectedSectorImageBytes => (long)TotalSectors * BytesPerSector;

    /// <summary>Sectors on one track. The 1541 uses zoned recording: 21, 19, 18 then 17 sectors per track.</summary>
    public int SectorsOnTrack(int cylinder) => GwFormat switch
    {
        "commodore.1541" => cylinder < 17 ? 21 : cylinder < 24 ? 19 : cylinder < 30 ? 18 : 17,
        _ => SectorsPerTrack,
    };
    public override string ToString() => $"{System} — {DisplayName}";
}
