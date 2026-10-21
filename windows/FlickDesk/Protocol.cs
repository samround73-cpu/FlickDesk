using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FlickDesk;

/// <summary>Which edge of the PC screen the phone sits next to.</summary>
internal enum Edge { Left, Right, Bottom }

/// <summary>
/// Wire protocol v2, shared with the Android app (see README "Protocol").
/// Every TCP connection starts with ONE plain UTF-8 JSON line ending in '\n':
///   • {"type":"pair",...}           – pairing (ECDH key exchange), plain reply line
///   • {"from":"&lt;id&gt;","nonce":"…"} – open an encrypted channel; plain {"ok":true} reply,
///                                     then everything is AES-GCM frames (see SecureChannel).
/// </summary>
internal static class Protocol
{
    public const int Version = 2;
    public const int BeaconPort = 47800; // UDP broadcast: PC -> phones
    public const int PcPort = 47801;     // TCP: phone -> PC
    public const int PhonePort = 47802;  // TCP: PC -> phone

    private const int MaxHeaderBytes = 64 * 1024;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public static async Task<JsonObject> ReadHeaderAsync(Stream stream, CancellationToken ct)
    {
        // Read byte-by-byte so we never consume file bytes that follow the header.
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Connection closed before the header was complete.");
            if (one[0] == (byte)'\n') break;
            buffer.WriteByte(one[0]);
            if (buffer.Length > MaxHeaderBytes) throw new InvalidDataException("Header too large.");
        }
        return JsonNode.Parse(Encoding.UTF8.GetString(buffer.ToArray())) as JsonObject
               ?? throw new InvalidDataException("Header is not a JSON object.");
    }

    public static async Task WriteHeaderAsync(Stream stream, JsonObject header, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(header.ToJsonString() + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Copies exactly <paramref name="size"/> bytes; fails if the peer stalls for 30 s.</summary>
    public static async Task CopyExactAsync(Stream source, Stream destination, long size, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long remaining = size;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (remaining > 0)
        {
            idle.CancelAfter(IdleTimeout);
            int n = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), idle.Token)
                                .ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException($"Connection closed with {remaining} bytes still to come.");
            await destination.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            remaining -= n;
        }
    }

    public static JsonObject Ok() => new() { ["ok"] = true };
    public static JsonObject Error(string code) => new() { ["ok"] = false, ["error"] = code };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Strips any path and characters Windows won't accept. Never trust a name from the network.</summary>
    public static string SafeFileName(string? name)
    {
        var n = (name ?? "").Replace('/', '\\');
        n = n[(n.LastIndexOf('\\') + 1)..];
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().TrimEnd('.', ' ');
        if (n.Length == 0) n = "flicked-file";
        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(n))) n = "_" + n;
        if (n.Length > 150)
        {
            var ext = Path.GetExtension(n);
            if (ext.Length > 20) ext = "";
            n = n[..(150 - ext.Length)] + ext;
        }
        return n;
    }

    public static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    private static readonly Dictionary<string, string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".heic"] = "image/heic", [".bmp"] = "image/bmp",
        [".mp4"] = "video/mp4", [".mov"] = "video/quicktime", [".mkv"] = "video/x-matroska", [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".wav"] = "audio/wav",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".apk"] = "application/vnd.android.package-archive",
    };

    public static string GuessMime(string path) =>
        Mimes.TryGetValue(Path.GetExtension(path), out var m) ? m : "application/octet-stream";

    public static bool LooksLikeImage(string path) =>
        GuessMime(path).StartsWith("image/", StringComparison.Ordinal);
}
