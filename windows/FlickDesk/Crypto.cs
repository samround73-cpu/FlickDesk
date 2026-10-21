using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FlickDesk;

/// <summary>
/// Crypto primitives for protocol v2 (mirrored exactly in the Android app's Crypto.kt):
///   • pairing: ECDH on P-256, keys exchanged as X.509 SubjectPublicKeyInfo (DER)
///   • long-term key  K  = HKDF-SHA256(ikm = ECDH secret, salt = QR token or 32 zero bytes, info "flick-v2-key")
///   • per-connection  S = HKDF-SHA256(ikm = K, salt = 16-byte connection nonce, info "flick-v2-session")
///   • frames: AES-256-GCM, 12-byte nonce = [0,0,0,dir] + 8-byte big-endian counter, dir 0 = client→server
///   • 6-digit check number = first 4 bytes of SHA-256("flick-v2-sas" ‖ phonePub ‖ pcPub), big-endian, mod 10^6
/// </summary>
internal static class FlickCrypto
{
    public static readonly byte[] ZeroSalt = new byte[32];

    public static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    public static ECDiffieHellman NewKeyPair() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] PublicKey(ECDiffieHellman ecdh) => ecdh.ExportSubjectPublicKeyInfo();

    public static byte[] Agree(ECDiffieHellman mine, byte[] otherSpki)
    {
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(otherSpki, out _);
        return mine.DeriveRawSecretAgreement(other.PublicKey);
    }

    public static byte[] Hkdf(byte[] ikm, byte[] salt, string info) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt, Encoding.UTF8.GetBytes(info));

    public static byte[] Hmac(byte[] key, params byte[][] parts) => HMACSHA256.HashData(key, Concat(parts));

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var result = new byte[len];
        int at = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, at, p.Length);
            at += p.Length;
        }
        return result;
    }

    public static bool SameBytes(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    /// <summary>"482 916" – shown on both screens so the user can confirm nobody is in the middle.</summary>
    public static string CheckNumber(byte[] phonePub, byte[] pcPub)
    {
        var hash = SHA256.HashData(Concat(Utf8("flick-v2-sas"), phonePub, pcPub));
        uint v = BinaryPrimitives.ReadUInt32BigEndian(hash);
        var digits = (v % 1_000_000).ToString("D6");
        return digits[..3] + " " + digits[3..];
    }

    public static string B64(byte[] b) => Convert.ToBase64String(b);
    public static byte[] FromB64(string? s) => string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Convert.FromBase64String(s);

    public static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// An encrypted, authenticated channel over one TCP connection. Each frame on the wire is
/// [4-byte big-endian length][AES-GCM ciphertext ‖ 16-byte tag]. Nonces are implicit counters,
/// so frames can't be replayed, reordered or dropped without the next decrypt failing.
/// </summary>
internal sealed class SecureChannel : IDisposable
{
    public const int MaxPlainFrame = 1 << 20;
    private const int ChunkSize = 256 * 1024;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private readonly Stream _stream;
    private readonly AesGcm _aes;
    private readonly bool _isClient;
    private ulong _sendCounter;
    private ulong _recvCounter;

    public SecureChannel(Stream stream, byte[] longTermKey, byte[] connectionNonce, bool isClient)
    {
        _stream = stream;
        _isClient = isClient;
        _aes = new AesGcm(FlickCrypto.Hkdf(longTermKey, connectionNonce, "flick-v2-session"), 16);
    }

    private byte[] NextNonce(bool sending)
    {
        var nonce = new byte[12];
        bool fromClient = sending ? _isClient : !_isClient;
        nonce[3] = fromClient ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), sending ? _sendCounter++ : _recvCounter++);
        return nonce;
    }

    public async Task WriteFrameAsync(ReadOnlyMemory<byte> plain, CancellationToken ct)
    {
        if (plain.Length > MaxPlainFrame) throw new InvalidDataException("Frame too large.");
        var wire = new byte[4 + plain.Length + 16];
        BinaryPrimitives.WriteInt32BigEndian(wire, plain.Length + 16);
        _aes.Encrypt(NextNonce(true), plain.Span,
                     wire.AsSpan(4, plain.Length), wire.AsSpan(4 + plain.Length, 16));
        await _stream.WriteAsync(wire, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadFrameAsync(CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(IdleTimeout);

        var lenBuf = new byte[4];
        await _stream.ReadExactlyAsync(lenBuf, idle.Token).ConfigureAwait(false);
        int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len < 16 || len > MaxPlainFrame + 16) throw new InvalidDataException("Bad frame length.");

        var buf = new byte[len];
        await _stream.ReadExactlyAsync(buf, idle.Token).ConfigureAwait(false);
        var plain = new byte[len - 16];
        _aes.Decrypt(NextNonce(false), buf.AsSpan(0, len - 16), buf.AsSpan(len - 16, 16), plain); // throws if tampered
        return plain;
    }

    public Task WriteJsonAsync(JsonObject message, CancellationToken ct) =>
        WriteFrameAsync(FlickCrypto.Utf8(message.ToJsonString()), ct);

    public async Task<JsonObject> ReadJsonAsync(CancellationToken ct)
    {
        var bytes = await ReadFrameAsync(ct).ConfigureAwait(false);
        return JsonNode.Parse(Encoding.UTF8.GetString(bytes)) as JsonObject
               ?? throw new InvalidDataException("Message is not a JSON object.");
    }

    public async Task SendStreamAsync(Stream source, long size, CancellationToken ct, Action<long>? progress = null)
    {
        var buffer = new byte[ChunkSize];
        long remaining = size;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buffer.Length, remaining);
            int got = 0;
            while (got < want)
            {
                int n = await source.ReadAsync(buffer.AsMemory(got, want - got), ct).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("File ended early.");
                got += n;
            }
            await WriteFrameAsync(buffer.AsMemory(0, got), ct).ConfigureAwait(false);
            remaining -= got;
            progress?.Invoke(size - remaining);
        }
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task ReceiveToStreamAsync(Stream destination, long size, CancellationToken ct, Action<long>? progress = null)
    {
        long remaining = size;
        while (remaining > 0)
        {
            var chunk = await ReadFrameAsync(ct).ConfigureAwait(false);
            if (chunk.Length == 0 || chunk.Length > remaining) throw new InvalidDataException("Unexpected chunk size.");
            await destination.WriteAsync(chunk, ct).ConfigureAwait(false);
            remaining -= chunk.Length;
            progress?.Invoke(size - remaining);
        }
    }

    public void Dispose() => _aes.Dispose();
}
