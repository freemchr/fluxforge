using FluxForge.Application.Jobs;

namespace FluxForge.Application.Library;

public enum DiskCondition { Unknown, Good, Worn, Damaged }

/// <summary>A physical disk in the catalogue. Its images stay on disk where they were saved; only paths and hashes are stored.</summary>
public sealed record LibraryDisk
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? System { get; init; }
    public string? ProfileId { get; init; }
    public DiskCondition Condition { get; init; }
    public string Notes { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required DateTimeOffset AddedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<LibraryImage> Images { get; init; } = [];
}

/// <summary>One image file of a disk, with where it came from (the job that made it) and its last check.</summary>
public sealed record LibraryImage
{
    public required Guid Id { get; init; }
    public required Guid DiskId { get; init; }
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public long Bytes { get; init; }
    public ArtifactKind Kind { get; init; }
    public string? GwFormat { get; init; }
    public bool Complete { get; init; } = true;

    /// <summary>The job that produced this image; null for files added by hand.</summary>
    public Guid? JobId { get; init; }
    public JobKind? JobKind { get; init; }
    public JobStatus? JobStatus { get; init; }
    public string? JobDetail { get; init; }
    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>Last time the disk was verified against this image, and how that went.</summary>
    public DateTimeOffset? LastVerifiedAt { get; init; }
    public JobStatus? LastVerifyStatus { get; init; }
    public string? LastVerifyDetail { get; init; }
}

public interface ILibraryStore
{
    IReadOnlyList<LibraryDisk> Search(string? text = null, int max = 500);
    LibraryDisk? Get(Guid diskId);
    LibraryImage? FindImageByHash(string sha256);
    LibraryImage? FindImageByPath(string path);
    bool HasJob(Guid jobId);
    void MarkJob(Guid jobId);
    void SaveDisk(LibraryDisk disk);
    void SaveImage(LibraryImage image);
    void DeleteDisk(Guid diskId);
    void DeleteImage(Guid imageId);

    /// <summary>True the first time the catalogue is opened, so existing job history can be imported once.</summary>
    bool IsNew { get; }
}
