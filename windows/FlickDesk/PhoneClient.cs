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
    /// <returns>null on success, otherwise a human-readable error.</returns>
    public static Task<string?> SendFileAsync(AppSettings settings, TrustedDevice phone, string path,
                                              int batchIndex = 0, int batchCount = 1, CancellationToken ct = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return Task.FromResult<string?>("file not found");

        return WithChannelAsync(settings, phone, async ch =>
        {
            await ch.WriteJsonAsync(new JsonObject
            {
                ["type"] = "file",
                ["deviceName"] = Environment.MachineName,
                ["name"] = file.Name,
                ["size"] = file.Length,
                ["mime"] = Protocol.GuessMime(path),
                ["batchIndex"] = batchIndex,
                ["batchCount"] = batchCount,
            }, ct);

            var ready = await ch.ReadJsonAsync(ct);
            if ((bool?)ready["ok"] != true) return Describe((string?)ready["error"]);

            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                await ch.SendStreamAsync(fs, file.Length, ct);
            }
            var reply = await ch.ReadJsonAsync(ct);
            return (bool?)reply["ok"] == true ? null : Describe((string?)reply["error"]);
        }, ct);
    }

    public static Task<string?> SendClipAsync(AppSettings settings, TrustedDevice phone, string text, CancellationToken ct = default) =>
        WithChannelAsync(settings, phone, async ch =>
        {
            await ch.WriteJsonAsync(new JsonObject { ["type"] = "clip", ["text"] = text }, ct);
            var reply = await ch.ReadJsonAsync(ct);
            return (bool?)reply["ok"] == true ? null : Describe((string?)reply["error"]);
        }, ct);

    private static async Task<string?> WithChannelAsync(AppSettings settings, TrustedDevice phone,
                                                        Func<SecureChannel, Task<string?>> body, CancellationToken ct)
    {
        if (phone.Address is null) return "phone address unknown – open Flick on the phone";
        try
        {
            using var client = new TcpClient { NoDelay = true };
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(4));
                try { await client.ConnectAsync(phone.Address, phone.Port, connectCts.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
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
        null or "" => "phone refused the file",
        _ => error,
    };
}
