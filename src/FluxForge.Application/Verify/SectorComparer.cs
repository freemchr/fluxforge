using System.Text;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;

namespace FluxForge.Application.Verify;

public enum SectorVerdict
{
    Match,
    /// <summary>Both sides have data and it is not identical.</summary>
    Differs,
    /// <summary>gw could not decode the sector from the disk.</summary>
    UnreadableOnDisk,
    /// <summary>The image holds gw's bad-sector fill here, so there is nothing to compare against.</summary>
    NotInImage,
}

/// <summary>One sector that did not match. <see cref="Sector"/> is the format's own sector number.</summary>
public sealed record SectorProblem(int Cylinder, int Head, int Sector, SectorVerdict Verdict);

public sealed record TrackComparison(int Cylinder, int Head, int Sectors, IReadOnlyList<SectorProblem> Problems)
{
    public int Matched => Sectors - Problems.Count;

    public TrackStatus Status =>
        Problems.Any(p => p.Verdict == SectorVerdict.Differs) ? TrackStatus.Mismatch :
        Problems.Any(p => p.Verdict == SectorVerdict.UnreadableOnDisk) ? TrackStatus.Unreadable :
        Problems.Count > 0 ? TrackStatus.NotInImage :
        TrackStatus.Match;
}

public sealed record VerifyComparison(IReadOnlyList<TrackComparison> Tracks)
{
    public int Total => Tracks.Sum(t => t.Sectors);
    public int Matched => Tracks.Sum(t => t.Matched);
    public IEnumerable<SectorProblem> Problems => Tracks.SelectMany(t => t.Problems);
    public int Count(SectorVerdict verdict) => Problems.Count(p => p.Verdict == verdict);
    public int TracksWith(SectorVerdict verdict) => Tracks.Count(t => t.Problems.Any(p => p.Verdict == verdict));
}

/// <summary>
/// Compares two sector images of the same format sector by sector. gw writes sector images track by
/// track (cylinder, then head) and fills sectors it could not decode with "-=[BAD SECTOR]=-", which is
/// how an unreadable sector is told apart from one that read back with different data.
/// </summary>
public static class SectorComparer
{
    private static readonly byte[] FillPattern = Encoding.ASCII.GetBytes("-=[BAD SECTOR]=-");

    public static bool IsBadSectorFill(ReadOnlySpan<byte> sector)
    {
        if (sector.Length == 0 || sector.Length % FillPattern.Length != 0) return false;
        for (var i = 0; i < sector.Length; i += FillPattern.Length)
            if (!sector.Slice(i, FillPattern.Length).SequenceEqual(FillPattern)) return false;
        return true;
    }

    /// <summary>Compares <paramref name="disk"/> (what was read) against <paramref name="image"/> (the reference).</summary>
    public static VerifyComparison Compare(ReadOnlySpan<byte> image, ReadOnlySpan<byte> disk, DiskProfile profile)
    {
        var expected = profile.ExpectedSectorImageBytes;
        if (image.Length != expected)
            throw new ArgumentException($"The image is {image.Length:N0} bytes; {profile.DisplayName} is {expected:N0} bytes.", nameof(image));
        if (disk.Length != expected)
            throw new ArgumentException($"The disk read back {disk.Length:N0} bytes; {profile.DisplayName} is {expected:N0} bytes.", nameof(disk));

        var size = profile.BytesPerSector;
        var tracks = new List<TrackComparison>();
        var offset = 0;
        for (var c = 0; c < profile.Cylinders; c++)
        for (var h = 0; h < profile.Heads; h++)
        {
            var sectors = profile.SectorsOnTrack(c);
            var problems = new List<SectorProblem>();
            for (var s = 0; s < sectors; s++, offset += size)
            {
                var want = image.Slice(offset, size);
                var got = disk.Slice(offset, size);
                // An image with no data here can't be compared, even if the disk reads back the same fill.
                var verdict =
                    IsBadSectorFill(want) ? SectorVerdict.NotInImage :
                    IsBadSectorFill(got) ? SectorVerdict.UnreadableOnDisk :
                    want.SequenceEqual(got) ? SectorVerdict.Match :
                    SectorVerdict.Differs;
                if (verdict != SectorVerdict.Match)
                    problems.Add(new SectorProblem(c, h, s + profile.FirstSector, verdict));
            }
            tracks.Add(new TrackComparison(c, h, sectors, problems));
        }
        return new VerifyComparison(tracks);
    }

    /// <summary>Track map cells for a comparison: matching sectors count as "found".</summary>
    public static IReadOnlyList<TrackObservation> ToObservations(VerifyComparison comparison) =>
        [.. comparison.Tracks.Select(t => new TrackObservation(t.Cylinder, t.Head, t.Status, t.Matched, t.Sectors, 0,
            ParseConfidence.High, [], Detail: Describe(t.Problems)))];

    /// <summary>"Differs: sectors 3, 7 · Unreadable: sector 5", or null when the track matched.</summary>
    public static string? Describe(IReadOnlyList<SectorProblem> problems)
    {
        if (problems.Count == 0) return null;
        var parts = new[] { (SectorVerdict.Differs, "Differs"), (SectorVerdict.UnreadableOnDisk, "Unreadable on disk"), (SectorVerdict.NotInImage, "No data in image") }
            .Select(k => (Label: k.Item2, Sectors: problems.Where(p => p.Verdict == k.Item1).Select(p => p.Sector).ToList()))
            .Where(x => x.Sectors.Count > 0)
            .Select(x => $"{x.Label}: sector{(x.Sectors.Count == 1 ? "" : "s")} {string.Join<int>(", ", x.Sectors)}");
        return string.Join(" · ", parts);
    }
}
