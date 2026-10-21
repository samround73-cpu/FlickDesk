using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FlickDesk;

internal sealed record IncomingFile(
    Guid Id, string DeviceName, string FileName, long Size, string Mime, double EdgePos,
    int BatchIndex, int BatchCount);

/// <summary>Accepts pairing and encrypted requests from phones on TCP 47801.</summary>
internal sealed class TransferServer : IDisposable
{
    private const long MaxFileBytes = 50L * 1024 * 1024 * 1024;
    private const int MaxClipChars = 512 * 1024;

    private readonly AppSettings _settings;
    private readonly PairingService _pairing;
    private readonly TcpListener _listener = new(IPAddress.Any, Protocol.PcPort);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    public event Action<IncomingFile>? FileStarting;
    public event Action<IncomingFile, string>? FileSaved;
    public event Action<IncomingFile, string>? FileFailed;
    /// <summary>Args: phone name, text.</summary>
    public event Action<string, string>? ClipReceived;
    /// <summary>A phone checked in (hello) – used to refresh "online" state in the UI.</summary>
    public event Action? DevicesChanged;

    public TransferServer(AppSettings settings, PairingService pairing)
    {
        _settings = settings;
        _pairing = pairing;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
                if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
                var stream = client.GetStream();

                JsonObject first;
                using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    headerCts.CancelAfter(TimeSpan.FromSeconds(15));
                    first = await Protocol.ReadHeaderAsync(stream, headerCts.Token);
                }

                if ((string?)first["type"] == "pair")
                {
                    await Protocol.WriteHeaderAsync(stream, _pairing.HandlePair(first, remote), ct);
                    return;
                }

                var from = (string?)first["from"];
                byte[] nonce;
                try { nonce = FlickCrypto.FromB64((string?)first["nonce"]); }
                catch (FormatException) { nonce = Array.Empty<byte>(); }
                if (string.IsNullOrEmpty(from) || nonce.Length != 16)
                {
                    await Protocol.WriteHeaderAsync(stream, Protocol.Error("bad-request"), ct);
                    return;
                }

                var device = _settings.FindDevice(from);
                var pending = device is null ? _pairing.FindPending(from) : null;
                var key = device?.KeyBytes ?? pending?.Key;
                if (key is null || key.Length != 32)
                {
                    await Protocol.WriteHeaderAsync(stream, Protocol.Error("not-paired"), ct);
                    return;
                }
                await Protocol.WriteHeaderAsync(stream, Protocol.Ok(), ct);

                using var channel = new SecureChannel(stream, key, nonce, isClient: false);
                var msg = await channel.ReadJsonAsync(ct);
                var type = (string?)msg["type"];

                if (pending is not null)
                {
                    var reply = type == "pair-confirm"
                        ? await _pairing.ConfirmAsync(pending, remote.ToString())
                        : Protocol.Error("not-paired");
                    await channel.WriteJsonAsync(reply, ct);
                    if (reply["ok"] is not null && (bool)reply["ok"]!) DevicesChanged?.Invoke();
                    return;
                }

                switch (type)
                {
                    case "hello":
                        await HandleHelloAsync(channel, msg, device!, remote, ct);
                        break;
                    case "file":
                        await HandleFileAsync(channel, msg, device!, remote, ct);
                        break;
                    case "clip":
                        await HandleClipAsync(channel, msg, device!, remote, ct);
                        break;
                    case "unpair":
                        _settings.ForgetDevice(device!.DeviceId);
                        await channel.WriteJsonAsync(Protocol.Ok(), ct);
                        DevicesChanged?.Invoke();
                        break;
                    default:
                        await channel.WriteJsonAsync(Protocol.Error("unknown-type"), ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Bad keys, tampering, vanished clients… none of them may take the server down.
                Debug.WriteLine($"[FlickDesk] connection error: {ex.Message}");
            }
        }
    }

    private async Task HandleHelloAsync(SecureChannel ch, JsonObject msg, TrustedDevice device, IPAddress remote, CancellationToken ct)
    {
        int port = (int?)msg["port"] ?? device.Port;
        if (port is < 1 or > 65535) port = Protocol.PhonePort;
        _settings.Touch(device.DeviceId, (string?)msg["deviceName"], remote.ToString(), port);
        var ok = Protocol.Ok();
        ok["name"] = Environment.MachineName;
        ok["pcId"] = _settings.PcId;
        ok["paused"] = _settings.IsPaused;
        await ch.WriteJsonAsync(ok, ct);
        DevicesChanged?.Invoke();
    }

    private async Task HandleClipAsync(SecureChannel ch, JsonObject msg, TrustedDevice device, IPAddress remote, CancellationToken ct)
    {
        var text = (string?)msg["text"] ?? "";
        if (text.Length == 0 || text.Length > MaxClipChars)
        {
            await ch.WriteJsonAsync(Protocol.Error("bad-size"), ct);
            return;
        }
        _settings.Touch(device.DeviceId, null, remote.ToString(), device.Port);
        await ch.WriteJsonAsync(Protocol.Ok(), ct);
        ClipReceived?.Invoke(device.Name, text);
    }

    private async Task HandleFileAsync(SecureChannel ch, JsonObject h, TrustedDevice device, IPAddress remote, CancellationToken ct)
    {
        if (_settings.IsPaused)
        {
            await ch.WriteJsonAsync(Protocol.Error("paused"), ct);
            return;
        }

        long size = (long?)h["size"] ?? -1;
        if (size < 0 || size > MaxFileBytes)
        {
            await ch.WriteJsonAsync(Protocol.Error("bad-size"), ct);
            return;
        }

        var name = Protocol.SafeFileName((string?)h["name"]);
        var mime = (string?)h["mime"] ?? "application/octet-stream";
        double pos = Math.Clamp((double?)h["edgePos"] ?? 0.5, 0, 1);
        int batchIndex = Math.Max(0, (int?)h["batchIndex"] ?? 0);
        int batchCount = Math.Max(1, (int?)h["batchCount"] ?? 1);
        var info = new IncomingFile(Guid.NewGuid(), device.Name, name, size, mime, pos, batchIndex, batchCount);

        // Tell the phone we're ready, then start the glow while the bytes stream in.
        await ch.WriteJsonAsync(Protocol.Ok(), ct);
        FileStarting?.Invoke(info);

        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlickDesk", "incoming");
        Directory.CreateDirectory(tempDir);
        var temp = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".part");

        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await ch.ReceiveToStreamAsync(fs, size, ct);
            }

            var folder = _settings.SaveFolder;
            Directory.CreateDirectory(folder);
            string final;
            lock (_gate)
            {
                final = Protocol.UniquePath(folder, name);
                File.Move(temp, final);
            }

            _settings.Touch(device.DeviceId, null, remote.ToString(), device.Port);
            var ok = Protocol.Ok();
            ok["savedAs"] = Path.GetFileName(final);
            await ch.WriteJsonAsync(ok, ct);
            FileSaved?.Invoke(info, final);
        }
        catch (Exception ex)
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            FileFailed?.Invoke(info, ex.Message);
            try { await ch.WriteJsonAsync(Protocol.Error("save-failed"), ct); } catch { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
