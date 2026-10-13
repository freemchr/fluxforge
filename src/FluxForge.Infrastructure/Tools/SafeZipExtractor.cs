using System.IO.Compression;

namespace FluxForge.Infrastructure.Tools;

public sealed class UnsafeArchiveException(string message) : Exception(message);

/// <summary>
/// Extracts an untrusted ZIP while rejecting path traversal, absolute paths, symlinks and zip bombs.
/// Byte counts are enforced while copying, not just from (forgeable) headers.
/// </summary>
public static class SafeZipExtractor
{
    public sealed record Limits(int MaxEntries = 5_000, long MaxTotalBytes = 1L << 30, long MaxEntryBytes = 512L << 20, double MaxCompressionRatio = 250);

    public static void Extract(string zipPath, string destination, Limits? limits = null)
    {
        limits ??= new Limits();
        var root = Path.GetFullPath(destination);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > limits.MaxEntries)
            throw new UnsafeArchiveException($"Archive has too many entries ({archive.Entries.Count}).");

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.Length == 0 || name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                throw new UnsafeArchiveException($"Unsafe path in archive: '{entry.FullName}'.");

            const int S_IFMT = 0xF000, S_IFLNK = 0xA000;
            if (((entry.ExternalAttributes >> 16) & S_IFMT) == S_IFLNK)
                throw new UnsafeArchiveException($"Archive contains a symbolic link: '{entry.FullName}'.");

            var target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                throw new UnsafeArchiveException($"Entry escapes extraction folder: '{entry.FullName}'.");

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            if (entry.Length > limits.MaxEntryBytes)
                throw new UnsafeArchiveException($"Entry too large: '{entry.FullName}'.");
            if (entry.CompressedLength > 0 && entry.Length > 1 << 20 &&
                (double)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio)
                throw new UnsafeArchiveException($"Suspicious compression ratio for '{entry.FullName}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                written += read;
                total += read;
                if (written > limits.MaxEntryBytes || total > limits.MaxTotalBytes)
                    throw new UnsafeArchiveException("Archive expands beyond the allowed size.");
                output.Write(buffer, 0, read);
            }
        }
    }
}
