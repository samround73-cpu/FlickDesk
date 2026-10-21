using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FlickDesk;

/// <summary>PC → phone requests over an encrypted channel (phone listens on TCP 47802).</summary>
internal static class PhoneClient
{
    /// <summary>Waits before each retry of an interrupted transfer.</summary>
    private static readonly TimeSpan[] RetryDelays =
        { TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Sends a file, or a folder (zipped on the fly, unpacked on the phone). If the connection drops,
    /// it reconnects and carries on from the byte the phone already has, up to four times.
    /// <paramref name="progress"/> gets (bytes done, total bytes, note) – note is non-null while retrying.
    /// </summary>
    /// <returns>null on success, otherwise a human-readable error.</returns>
    public static async Task<string?> SendFileAsync(AppSettings settings, TrustedDevice phone, string path,
                                                    int batchIndex = 0, int batchCount = 1,
                                                    Action<long, long, string?>? progress = null, CancellationToken ct = default)
    {
        string? zip = null;
        try
        {
            string sendPath, name, id;
            string? folder = null;
            if (Directory.Exists(path))
            {
                folder = new DirectoryInfo(path).Name;
                progress?.Invoke(0, 0, "Packing folder…");
                zip = await Task.Run(() => Transfers.ZipFolder(path), ct).ConfigureAwait(false);
                sendPath = zip;
                name = folder + ".zip";
                id = Transfers.NewId();
            }
            else if (File.Exists(path))
            {
                sendPath = path;
                name = Path.GetFileName(path);
                id = Transfers.StableId(path);
            }
            else return "file not found";

            long size = new FileInfo(sendPath).Length;
            string? error = null;
            bool started = false; // once the phone has accepted the file, dropped connections are retried
            for (int attempt = 0; ; attempt++)
            {
                bool retryable = false;
                error = await WithChannelAsync(settings, phone, async ch =>
                {
                    var header = new JsonObject
                    {
                        ["type"] = "file",
                        ["deviceName"] = Environment.MachineName,
                        ["name"] = name,
                        ["size"] = size,
                        ["mime"] = folder is null ? Protocol.GuessMime(path) : "application/zip",
                        ["batchIndex"] = batchIndex,
                        ["batchCount"] = batchCount,
                        ["id"] = id,
                    };
                    if (folder is not null) header["folder"] = folder;
                    await ch.WriteJsonAsync(header, ct);

                    var ready = await ch.ReadJsonAsync(ct);
                    if ((bool?)ready["ok"] != true)
                    {
                        retryable = (string?)ready["error"] == "busy";
                        return Describe((string?)ready["error"]);
                    }
                    long offset = Math.Clamp((long?)ready["offset"] ?? 0, 0, size);
                    started = true;

                    retryable = true; // from here on, a dropped connection is worth resuming
                    await using (var fs = new FileStream(sendPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        progress?.Invoke(offset, size, null);
                        long last = 0;
                        await ch.SendStreamAsync(fs, size - offset, ct, sent =>
                        {
                            // At most every 1 MB, so the UI isn't flooded on fast networks.
                            if (sent - last < 1024 * 1024 && offset + sent < size) return;
                            last = sent;
                            progress?.Invoke(offset + sent, size, null);
                        });
                    }
                    var reply = await ch.ReadJsonAsync(ct);
                    retryable = false;
                    return (bool?)reply["ok"] == true ? null : Describe((string?)reply["error"]);
                }, ct, onConnectFailure: () => retryable = started).ConfigureAwait(false);

                if (error is null || !retryable || attempt >= RetryDelays.Length || ct.IsCancellationRequested) return error;
                progress?.Invoke(-1, size, $"Connection dropped – resuming ({attempt + 1}/{RetryDelays.Length})…");
                await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                phone = settings.FindDevice(phone.DeviceId) ?? phone; // the phone may have a new address
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            return ex.Message;
        }
        finally
        {
            if (zip is not null) { try { File.Delete(zip); } catch { /* best effort */ } }
        }
    }

    public static Task<string?> SendClipAsync(AppSettings settings, TrustedDevice phone, string text, CancellationToken ct = default) =>
        WithChannelAsync(settings, phone, async ch =>
        {
            await ch.WriteJsonAsync(new JsonObject { ["type"] = "clip", ["text"] = text }, ct);
            var reply = await ch.ReadJsonAsync(ct);
            return (bool?)reply["ok"] == true ? null : Describe((string?)reply["error"]);
        }, ct);

    private static async Task<string?> WithChannelAsync(AppSettings settings, TrustedDevice phone,
                                                        Func<SecureChannel, Task<string?>> body, CancellationToken ct,
                                                        Action? onConnectFailure = null)
    {
        if (phone.Address is null) return "phone address unknown – open Flick on the phone";
        try
        {
            using var client = new TcpClient { NoDelay = true };
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(4));
                try { await client.ConnectAsync(phone.Address, phone.Port, connectCts.Token); }
                catch (Exception e) when (e is OperationCanceledException or SocketException && !ct.IsCancellationRequested)
                {
                    onConnectFailure?.Invoke();
                    return "phone didn't answer – is Flick running on it?";
                }
            }

            var stream = client.GetStream();
            var nonce = FlickCrypto.RandomBytes(16);
            await Protocol.WriteHeaderAsync(stream, new JsonObject
            {
                ["v"] = Protocol.Version,
                ["from"] = settings.PcId,
                ["nonce"] = FlickCrypto.B64(nonce),
            }, ct);

            JsonObject open;
            using (var replyCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                replyCts.CancelAfter(TimeSpan.FromSeconds(10));
                open = await Protocol.ReadHeaderAsync(stream, replyCts.Token);
            }
            if ((bool?)open["ok"] != true) return Describe((string?)open["error"]);

            using var channel = new SecureChannel(stream, phone.KeyBytes, nonce, isClient: true);
            return await body(channel);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
                                         or InvalidDataException or UnauthorizedAccessException
                                         or System.Security.Cryptography.CryptographicException
                                         or System.Text.Json.JsonException or InvalidOperationException)
        {
            return ex is System.Security.Cryptography.CryptographicException
                ? "encryption check failed – try pairing again"
                : ex.Message;
        }
    }

    private static string Describe(string? error) => error switch
    {
        "not-paired" => "this PC isn't paired with the phone any more – pair again",
        "paused" => "the phone has paused receiving",
        "busy" => "the phone is still finishing the last attempt",
        null or "" => "phone refused the file",
        _ => error,
    };
}
