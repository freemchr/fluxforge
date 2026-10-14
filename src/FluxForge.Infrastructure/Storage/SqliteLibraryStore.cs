using System.Globalization;
using FluxForge.Application.Jobs;
using FluxForge.Application.Library;
using Microsoft.Data.Sqlite;

namespace FluxForge.Infrastructure.Storage;

/// <summary>
/// The Library catalogue in Data\library.db. Only metadata, paths and SHA-256 fingerprints are stored;
/// image files stay where they were saved. Schema changes go through <see cref="Migrate"/> (PRAGMA user_version).
/// </summary>
public sealed class SqliteLibraryStore : ILibraryStore
{
    private const int SchemaVersion = 1;
    private readonly string _connectionString;

    public SqliteLibraryStore(AppPaths paths) : this(Path.Combine(paths.Data, "library.db")) { }

    public SqliteLibraryStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        IsNew = !File.Exists(databasePath);
        // No pooling: the file is released as soon as each call finishes (backups, tests, uninstall).
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        using var db = Open();
        Migrate(db);
    }

    public bool IsNew { get; }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        Exec(db, "PRAGMA foreign_keys = ON;");
        return db;
    }

    private static void Migrate(SqliteConnection db)
    {
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion) return;
        using var tx = db.BeginTransaction();
        Exec(db, """
            CREATE TABLE IF NOT EXISTS disks (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                system TEXT,
                profile_id TEXT,
                condition TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                tags TEXT NOT NULL DEFAULT '',
                added_at TEXT NOT NULL,
                updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS images (
                id TEXT PRIMARY KEY,
                disk_id TEXT NOT NULL REFERENCES disks(id) ON DELETE CASCADE,
                path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                bytes INTEGER NOT NULL,
                kind TEXT NOT NULL,
                gw_format TEXT,
                complete INTEGER NOT NULL,
                job_id TEXT,
                job_kind TEXT,
                job_status TEXT,
                job_detail TEXT,
                added_at TEXT NOT NULL,
                last_verified_at TEXT,
                last_verify_status TEXT,
                last_verify_detail TEXT);
            CREATE INDEX IF NOT EXISTS images_disk ON images(disk_id);
            CREATE INDEX IF NOT EXISTS images_sha ON images(sha256);
            CREATE TABLE IF NOT EXISTS recorded_jobs (job_id TEXT PRIMARY KEY);
            """, tx);
        Exec(db, $"PRAGMA user_version = {SchemaVersion};", tx);
        tx.Commit();
    }

    public IReadOnlyList<LibraryDisk> Search(string? text = null, int max = 500)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var where = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            // Every word must appear somewhere: disk fields, or any of its images' path, hash or format.
            where.Add($"""
                (d.title LIKE $w{i} ESCAPE '\' OR d.system LIKE $w{i} ESCAPE '\' OR d.notes LIKE $w{i} ESCAPE '\' OR d.tags LIKE $w{i} ESCAPE '\'
                 OR d.condition LIKE $w{i} ESCAPE '\'
                 OR EXISTS (SELECT 1 FROM images i WHERE i.disk_id = d.id AND (i.path LIKE $w{i} ESCAPE '\' OR i.sha256 LIKE $w{i} ESCAPE '\' OR i.gw_format LIKE $w{i} ESCAPE '\')))
                """);
            cmd.Parameters.AddWithValue($"$w{i}", "%" + words[i].Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%");
        }
        cmd.CommandText = $"SELECT * FROM disks d {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY d.updated_at DESC LIMIT $max;";
        cmd.Parameters.AddWithValue("$max", max);
        var disks = new List<LibraryDisk>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) disks.Add(ReadDisk(r));
        var images = ImagesFor(db, disks.Select(d => d.Id));
        return [.. disks.Select(d => d with { Images = images.TryGetValue(d.Id, out var list) ? list : [] })];
    }

    public LibraryDisk? Get(Guid diskId)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT * FROM disks WHERE id = $id;", ("$id", diskId.ToString()));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var disk = ReadDisk(r);
        return disk with { Images = ImagesFor(db, [disk.Id]).GetValueOrDefault(disk.Id) ?? [] };
    }

    public LibraryImage? FindImageByHash(string sha256) =>
        FirstImage("SELECT * FROM images WHERE sha256 = $v ORDER BY added_at LIMIT 1;", sha256.ToLowerInvariant());

    public LibraryImage? FindImageByPath(string path) =>
        FirstImage("SELECT * FROM images WHERE path = $v COLLATE NOCASE ORDER BY added_at LIMIT 1;", Path.GetFullPath(path));

    public bool HasJob(Guid jobId)
    {
        using var db = Open();
        using var cmd = Command(db, "SELECT 1 FROM recorded_jobs WHERE job_id = $id;", ("$id", jobId.ToString()));
        return cmd.ExecuteScalar() is not null;
    }

    public void MarkJob(Guid jobId)
    {
        using var db = Open();
        using var cmd = Command(db, "INSERT OR IGNORE INTO recorded_jobs (job_id) VALUES ($id);", ("$id", jobId.ToString()));
        cmd.ExecuteNonQuery();
    }

    public void SaveDisk(LibraryDisk disk)
    {
        using var db = Open();
        using var cmd = Command(db, """
            INSERT INTO disks (id, title, system, profile_id, condition, notes, tags, added_at, updated_at)
            VALUES ($id, $title, $system, $profile, $condition, $notes, $tags, $added, $updated)
            ON CONFLICT(id) DO UPDATE SET title = $title, system = $system, profile_id = $profile, condition = $condition,
                notes = $notes, tags = $tags, updated_at = $updated;
            """,
            ("$id", disk.Id.ToString()), ("$title", disk.Title), ("$system", disk.System), ("$profile", disk.ProfileId),
            ("$condition", disk.Condition.ToString()), ("$notes", disk.Notes), ("$tags", string.Join('\n', disk.Tags)),
            ("$added", Time(disk.AddedAt)), ("$updated", Time(disk.UpdatedAt == default ? disk.AddedAt : disk.UpdatedAt)));
        cmd.ExecuteNonQuery();
    }

    public void SaveImage(LibraryImage image)
    {
        using var db = Open();
        using var cmd = Command(db, """
            INSERT INTO images (id, disk_id, path, sha256, bytes, kind, gw_format, complete, job_id, job_kind, job_status, job_detail,
                added_at, last_verified_at, last_verify_status, last_verify_detail)
            VALUES ($id, $disk, $path, $sha, $bytes, $kind, $format, $complete, $job, $jobKind, $jobStatus, $jobDetail,
                $added, $verifiedAt, $verifyStatus, $verifyDetail)
            ON CONFLICT(id) DO UPDATE SET disk_id = $disk, path = $path, sha256 = $sha, bytes = $bytes, kind = $kind, gw_format = $format,
                complete = $complete, job_id = $job, job_kind = $jobKind, job_status = $jobStatus, job_detail = $jobDetail,
                last_verified_at = $verifiedAt, last_verify_status = $verifyStatus, last_verify_detail = $verifyDetail;
            """,
            ("$id", image.Id.ToString()), ("$disk", image.DiskId.ToString()), ("$path", Path.GetFullPath(image.Path)),
            ("$sha", image.Sha256.ToLowerInvariant()), ("$bytes", image.Bytes), ("$kind", image.Kind.ToString()), ("$format", image.GwFormat),
            ("$complete", image.Complete ? 1 : 0), ("$job", image.JobId?.ToString()), ("$jobKind", image.JobKind?.ToString()),
            ("$jobStatus", image.JobStatus?.ToString()), ("$jobDetail", image.JobDetail), ("$added", Time(image.AddedAt)),
            ("$verifiedAt", image.LastVerifiedAt is { } v ? Time(v) : null), ("$verifyStatus", image.LastVerifyStatus?.ToString()),
            ("$verifyDetail", image.LastVerifyDetail));
        cmd.ExecuteNonQuery();
        // Touch the disk so recently changed disks sort first.
        using var touch = Command(db, "UPDATE disks SET updated_at = MAX(updated_at, $t) WHERE id = $disk;",
            ("$t", Time(image.LastVerifiedAt is { } at && at > image.AddedAt ? at : image.AddedAt)), ("$disk", image.DiskId.ToString()));
        touch.ExecuteNonQuery();
    }

    public void DeleteDisk(Guid diskId)
    {
        using var db = Open();
        using var cmd = Command(db, "DELETE FROM disks WHERE id = $id;", ("$id", diskId.ToString()));
        cmd.ExecuteNonQuery();
    }

    public void DeleteImage(Guid imageId)
    {
        using var db = Open();
        using var cmd = Command(db, "DELETE FROM images WHERE id = $id;", ("$id", imageId.ToString()));
        cmd.ExecuteNonQuery();
    }

    private LibraryImage? FirstImage(string sql, string value)
    {
        using var db = Open();
        using var cmd = Command(db, sql, ("$v", value));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadImage(r) : null;
    }

    private static Dictionary<Guid, List<LibraryImage>> ImagesFor(SqliteConnection db, IEnumerable<Guid> diskIds)
    {
        var result = new Dictionary<Guid, List<LibraryImage>>();
        var ids = diskIds.ToList();
        if (ids.Count == 0) return result;
        using var cmd = db.CreateCommand();
        var names = ids.Select((id, i) => { cmd.Parameters.AddWithValue($"$d{i}", id.ToString()); return $"$d{i}"; });
        cmd.CommandText = $"SELECT * FROM images WHERE disk_id IN ({string.Join(',', names)}) ORDER BY added_at;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var image = ReadImage(r);
            if (!result.TryGetValue(image.DiskId, out var list)) result[image.DiskId] = list = [];
            list.Add(image);
        }
        return result;
    }

    private static LibraryDisk ReadDisk(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("id"))),
        Title = r.GetString(r.GetOrdinal("title")),
        System = Str(r, "system"),
        ProfileId = Str(r, "profile_id"),
        Condition = Enum.TryParse<DiskCondition>(r.GetString(r.GetOrdinal("condition")), out var c) ? c : DiskCondition.Unknown,
        Notes = r.GetString(r.GetOrdinal("notes")),
        Tags = r.GetString(r.GetOrdinal("tags")).Split('\n', StringSplitOptions.RemoveEmptyEntries),
        AddedAt = ParseTime(r.GetString(r.GetOrdinal("added_at"))),
        UpdatedAt = ParseTime(r.GetString(r.GetOrdinal("updated_at"))),
    };

    private static LibraryImage ReadImage(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("id"))),
        DiskId = Guid.Parse(r.GetString(r.GetOrdinal("disk_id"))),
        Path = r.GetString(r.GetOrdinal("path")),
        Sha256 = r.GetString(r.GetOrdinal("sha256")),
        Bytes = r.GetInt64(r.GetOrdinal("bytes")),
        Kind = Enum.Parse<ArtifactKind>(r.GetString(r.GetOrdinal("kind"))),
        GwFormat = Str(r, "gw_format"),
        Complete = r.GetInt64(r.GetOrdinal("complete")) != 0,
        JobId = Str(r, "job_id") is { } job ? Guid.Parse(job) : null,
        JobKind = Str(r, "job_kind") is { } k && Enum.TryParse<JobKind>(k, out var kind) ? kind : null,
        JobStatus = Str(r, "job_status") is { } s && Enum.TryParse<JobStatus>(s, out var status) ? status : null,
        JobDetail = Str(r, "job_detail"),
        AddedAt = ParseTime(r.GetString(r.GetOrdinal("added_at"))),
        LastVerifiedAt = Str(r, "last_verified_at") is { } v ? ParseTime(v) : null,
        LastVerifyStatus = Str(r, "last_verify_status") is { } vs && Enum.TryParse<JobStatus>(vs, out var verify) ? verify : null,
        LastVerifyDetail = Str(r, "last_verify_detail"),
    };

    private static string? Str(SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static string Time(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void Exec(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
