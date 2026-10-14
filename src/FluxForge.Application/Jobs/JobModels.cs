using FluxForge.Application.Gw;
using FluxForge.Application.Processes;
using FluxForge.Application.Verify;

namespace FluxForge.Application.Jobs;

public enum JobKind { Read, Write, Convert, DriveTool, Verify }

public enum JobStatus
{
    Pending,
    Running,
    /// <summary>Completed with positive evidence (e.g. every sector decoded).</summary>
    Succeeded,
    /// <summary>Completed, but some tracks/sectors were not recovered.</summary>
    Partial,
    /// <summary>Tool exited cleanly but FluxForge has no evidence about data quality.</summary>
    CompletedUnverified,
    Failed,
    Cancelled,
    /// <summary>Stopped mid-write: the floppy is in an unknown state.</summary>
    Interrupted,
}

public enum ArtifactKind { RawFlux, SectorImage }

public sealed record JobArtifact(string Path, string Sha256, long Bytes, ArtifactKind Kind, bool Complete);

/// <summary>Outcome of a sector-by-sector comparison between a disk and an image.</summary>
public sealed record VerifySummary(int Total, int Matched, int Differs, int UnreadableOnDisk, int NotInImage, IReadOnlyList<SectorProblem> Problems)
{
    public static VerifySummary From(VerifyComparison c) => new(c.Total, c.Matched,
        c.Count(SectorVerdict.Differs), c.Count(SectorVerdict.UnreadableOnDisk), c.Count(SectorVerdict.NotInImage), [.. c.Problems]);
}

public sealed record JobRecord
{
    public required Guid Id { get; init; }
    public required JobKind Kind { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public JobStatus Status { get; init; } = JobStatus.Pending;
    public string StatusDetail { get; init; } = "";
    public int? ExitCode { get; init; }
    public required string ToolsPath { get; init; }
    public string? ToolsVersion { get; init; }
    public bool Simulated { get; init; }
    public string? DevicePort { get; init; }
    public string? DeviceModel { get; init; }
    public string? Firmware { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public string? ProfileId { get; init; }
    public string? GwFormat { get; init; }
    public CaptureMode? CaptureMode { get; init; }
    public string? DiskLabel { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<JobArtifact> Artifacts { get; init; } = [];
    public IReadOnlyList<TrackObservation> Tracks { get; init; } = [];
    public SectorSummary? Sectors { get; init; }

    /// <summary>Format that better fits the disk, when sectors fell outside the selected one.</summary>
    public string? SuggestedProfileId { get; init; }

    /// <summary>Set on verify jobs that got as far as comparing sectors.</summary>
    public VerifySummary? Verify { get; init; }
    public string? LogPath { get; init; }
    public string AppVersion { get; init; } = typeof(JobRecord).Assembly.GetName().Version?.ToString() ?? "0";
}

public sealed record JobProgress(JobStatus Status, TimeSpan Elapsed, OutputLine? Line = null, TrackObservation? Track = null);

public interface IJobStore
{
    string JobsDirectory { get; }
    Task<JobRecord> SaveAsync(JobRecord record, IReadOnlyList<OutputLine> log, CancellationToken ct);
    IReadOnlyList<JobRecord> List(int max = 200);
}

public sealed class DeviceBusyException(string message) : Exception(message);
