namespace FluxForge.Application.Formats;

/// <summary>
/// Built-in presets. Every <see cref="DiskProfile.GwFormat"/> must match a diskdef shipped with the
/// installed host tools; Phase 0 validates these against <c>gw read --help</c> / diskdefs for v1.23.
/// </summary>
public sealed class FormatRegistry
{
    public static readonly IReadOnlyList<DiskProfile> BuiltIn =
    [
        new("amiga-dd", "Commodore Amiga", "AmigaDOS DD 880 KB", "amiga.amigados", ".adf", 80, 2, 11, 512),
        new("atarist-720", "Atari ST", "Double-sided 720 KB (9 sectors)", "atarist.720", ".st", 80, 2, 9, 512),
        new("atarist-360", "Atari ST", "Single-sided 360 KB (9 sectors)", "atarist.360", ".st", 80, 1, 9, 512),
        new("atarist-800", "Atari ST", "Double-sided 800 KB (10 sectors)", "atarist.800", ".st", 80, 2, 10, 512),
        new("atarist-880", "Atari ST", "Double-sided 880 KB (11 sectors)", "atarist.880", ".st", 80, 2, 11, 512),
        new("ibm-1440", "IBM PC", "3.5\" HD 1.44 MB", "ibm.1440", ".img", 80, 2, 18, 512),
        new("ibm-720", "IBM PC", "3.5\" DD 720 KB", "ibm.720", ".img", 80, 2, 9, 512),
        // 1541 disks use zoned recording (17-21 sectors/track); 683 sectors = 174,848-byte D64.
        new("c64-1541", "Commodore 64", "1541 single-sided (35 tracks)", "commodore.1541", ".d64", 35, 1, 19, 256,
            KnownTotalSectors: 683),
    ];

    public IReadOnlyList<DiskProfile> Profiles { get; }

    public FormatRegistry(IEnumerable<DiskProfile>? userProfiles = null)
    {
        Profiles = [.. BuiltIn, .. userProfiles ?? []];
    }

    public DiskProfile? Find(string id) => Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// When a read finds sectors numbered beyond the selected format (gw: "Ignoring unexpected sector"),
    /// suggest a sibling profile of the same system and geometry that holds that many sectors per track.
    /// Sector numbers are 1-based, so the highest sector number is the sectors-per-track count.
    /// </summary>
    public DiskProfile? SuggestForExtraSectors(DiskProfile selected, int highestSectorNumber) =>
        Profiles
            .Where(p => p.System == selected.System && p.Cylinders == selected.Cylinders && p.Heads == selected.Heads
                        && p.BytesPerSector == selected.BytesPerSector && p.SectorsPerTrack >= highestSectorNumber && p.Id != selected.Id)
            .OrderBy(p => p.SectorsPerTrack)
            .FirstOrDefault();

    /// <summary>
    /// Profiles that could plausibly produce/consume a file with this extension. Generic extensions
    /// (.img, .dsk, .st) can match several; callers must ask the user to confirm when ambiguous.
    /// </summary>
    public IReadOnlyList<DiskProfile> CandidatesForExtension(string extension) =>
        [.. Profiles.Where(p => string.Equals(p.SectorExtension, extension, StringComparison.OrdinalIgnoreCase))];
}
