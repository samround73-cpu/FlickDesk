using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FlickDesk;

/// <summary>
/// Helpers for large and resumable transfers (shared protocol with the Android app):
///  • every file carries an <c>id</c>; the receiver keeps a partial copy keyed by sender + id + size,
///    and its "ready" reply says how many bytes it already has (<c>offset</c>), so a dropped
///    transfer carries on where it stopped instead of starting again;
///  • a folder travels as one zip (no compression) with <c>folder</c> = its name, and is unpacked
///    on arrival.
/// </summary>
internal static class Transfers
{
    /// <summary>Anything at or above this size gets a progress panel.</summary>
    public const long ProgressThreshold = 16L * 1024 * 1024;

    /// <summary>Partial files older than this are deleted at start-up.</summary>
    private static readonly TimeSpan PartialLifetime = TimeSpan.FromDays(3);

    public static string IncomingDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlickDesk", "incoming");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string Hex(byte[] bytes, int chars) => Convert.ToHexString(bytes).ToLowerInvariant()[..chars];

    /// <summary>Same file, unchanged → same id, so a resend after a restart can still resume.</summary>
    public static string StableId(string path)
    {
        var f = new FileInfo(path);
        var raw = $"{f.FullName.ToLowerInvariant()}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(raw)), 32);
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Only short, plain ids are accepted from the network.</summary>
    public static string? CleanId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;

    /// <summary>Where a partial copy of (device, id, size) lives. No id → a one-off name.</summary>
    public static string PartialPath(string deviceId, string? id, long size)
    {
        var key = id is null
            ? NewId()
            : Hex(SHA256.HashData(Encoding.UTF8.GetBytes($"{deviceId}|{id}|{size}")), 32);
        return Path.Combine(IncomingDir, key + ".part");
    }

    public static void CleanupOldPartials()
    {
        try
        {
            foreach (var f in new DirectoryInfo(IncomingDir).EnumerateFiles("*.part"))
                if (DateTime.UtcNow - f.LastWriteTimeUtc > PartialLifetime) f.Delete();
            foreach (var f in new DirectoryInfo(Path.GetTempPath()).EnumerateFiles("flickdesk-*.zip"))
                if (DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromDays(1)) f.Delete();
        }
        catch (Exception) { /* best effort */ }
    }

    // ---------------------------------------------------------------- folders

    /// <summary>Zips a folder (contents only, no compression – it's about bundling, not size) to a temp file.</summary>
    public static string ZipFolder(string folder)
    {
        var zip = Path.Combine(Path.GetTempPath(), $"flickdesk-{NewId()}.zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.NoCompression, includeBaseDirectory: false);
        return zip;
    }

    /// <summary>
    /// Unpacks a received folder zip into <paramref name="parent"/>\<paramref name="name"/> (made unique).
    /// Every path segment is sanitised and nothing may escape the target folder.
    /// </summary>
    public static string ExtractFolder(string zipPath, string parent, string name, long maxTotalBytes)
    {
        string target;
        lock (ExtractGate)
        {
            target = Protocol.UniquePath(parent, Protocol.SafeFileName(name));
            Directory.CreateDirectory(target);
        }
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        long total = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var segments = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s != "." && s != "..")
                .Select(Protocol.SafeFileName)
                .ToArray();
            if (segments.Length == 0) continue;

            var dest = Path.GetFullPath(Path.Combine(target, Path.Combine(segments)));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside the folder

            bool isDir = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isDir)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            total += entry.Length;
            if (total > maxTotalBytes) throw new InvalidDataException("Folder is larger than allowed.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest)) dest = Protocol.UniquePath(Path.GetDirectoryName(dest)!, Path.GetFileName(dest));
            entry.ExtractToFile(dest);
        }
        return target;
    }

    private static readonly object ExtractGate = new();

    public static long DirectorySize(string folder)
    {
        try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (Exception) { return 0; }
    }
}
