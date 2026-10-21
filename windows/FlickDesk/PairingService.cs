using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FlickDesk;

internal sealed class QrSession
{
    public byte[] Token { get; } = FlickCrypto.RandomBytes(16);
    public DateTime ExpiresUtc { get; } = DateTime.UtcNow.AddSeconds(60);
    public bool Expired => DateTime.UtcNow > ExpiresUtc;
}

internal sealed class PendingPair
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required byte[] Key { get; init; }
    public required string Address { get; set; }
    public required int Port { get; init; }
    public required bool ViaQr { get; init; }
    public required string CheckNumber { get; init; }
    public Task<bool> PcApproval { get; set; } = Task.FromResult(true);
    public DateTime ExpiresUtc { get; } = DateTime.UtcNow.AddMinutes(3);
}

/// <summary>
/// Pairing, two ways:
///  • QR (preferred): the code carries a one-time 16-byte token. The phone proves it scanned
///    it (HMAC), the token salts the key, so no prompt is needed on the PC. The phone still
///    shows the 6-digit check number and the user taps "Yes, pair".
///  • Manual (IP / discovered chip): plain ECDH; both screens show the check number and the PC
///    asks "does it match?".
/// Pairing completes when the phone sends "pair-confirm" over the new encrypted channel.
/// </summary>
internal sealed class PairingService
{
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingPair> _pending = new();
    private QrSession? _qr;

    /// <summary>QR flow: a phone scanned the code. Args: phone name, check number.</summary>
    public event Action<string, string>? PairStarted;
    public event Action<TrustedDevice>? Paired;
    public event Action<string>? PairFailed;

    /// <summary>Manual flow: ask the user. Args: phone name, check number.</summary>
    public Func<string, string, Task<bool>>? ApproveManual { get; init; }

    public PairingService(AppSettings settings) => _settings = settings;

    // ---------------------------------------------------------------- QR

    public QrSession NewQrSession()
    {
        lock (_gate) return _qr = new QrSession();
    }

    public void EndQrSession()
    {
        lock (_gate) _qr = null;
    }

    public string QrText(QrSession session)
    {
        var hosts = string.Join(",", LocalAddresses().Take(3));
        return "flick://pair?v=2" +
               "&id=" + Uri.EscapeDataString(_settings.PcId) +
               "&n=" + Uri.EscapeDataString(Environment.MachineName) +
               "&h=" + Uri.EscapeDataString(hosts) +
               "&p=" + Protocol.PcPort +
               "&t=" + FlickCrypto.B64Url(session.Token);
    }

    /// <summary>Private IPv4 addresses of active, non-virtual adapters, Wi-Fi/Ethernet first.</summary>
    public static IEnumerable<string> LocalAddresses()
    {
        var result = new List<(int rank, string ip)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") || desc.Contains("vethernet")) continue;
                int rank = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    bool isPrivate = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                    if (isPrivate) result.Add((rank, ua.Address.ToString()));
                }
            }
        }
        catch (NetworkInformationException) { }
        return result.OrderBy(r => r.rank).Select(r => r.ip).Distinct();
    }

    // ---------------------------------------------------------------- protocol handlers

    /// <summary>Handles the plain {"type":"pair"} request; returns the plain reply.</summary>
    public JsonObject HandlePair(JsonObject h, IPAddress remote)
    {
        var deviceId = (string?)h["deviceId"];
        var name = (string?)h["deviceName"] ?? "Android phone";
        if (name.Length > 60) name = name[..60];
        int port = (int?)h["port"] ?? Protocol.PhonePort;
        byte[] phonePub;
        byte[] mac;
        try
        {
            phonePub = FlickCrypto.FromB64((string?)h["pub"]);
            mac = FlickCrypto.FromB64((string?)h["mac"]);
        }
        catch (FormatException) { return Protocol.Error("bad-request"); }

        bool viaQr = (bool?)h["qr"] == true;
        if (string.IsNullOrWhiteSpace(deviceId) || phonePub.Length == 0 || port is < 1 or > 65535)
            return Protocol.Error("bad-request");

        byte[] salt = FlickCrypto.ZeroSalt;
        byte[]? token = null;
        if (viaQr)
        {
            lock (_gate)
            {
                if (_qr is null || _qr.Expired) return Protocol.Error("qr-expired");
                var expected = FlickCrypto.Hmac(_qr.Token, FlickCrypto.Utf8("flick-pair-v2"), phonePub, FlickCrypto.Utf8(deviceId));
                if (!FlickCrypto.SameBytes(expected, mac)) return Protocol.Error("bad-code");
                token = _qr.Token;
                _qr = null; // single use
            }
            salt = token;
        }

        byte[] pcPub, key;
        try
        {
            using var ecdh = FlickCrypto.NewKeyPair();
            pcPub = FlickCrypto.PublicKey(ecdh);
            var secret = FlickCrypto.Agree(ecdh, phonePub);
            key = FlickCrypto.Hkdf(secret, salt, "flick-v2-key");
        }
        catch (CryptographicException) { return Protocol.Error("bad-key"); }

        var check = FlickCrypto.CheckNumber(phonePub, pcPub);
        var pending = new PendingPair
        {
            DeviceId = deviceId, Name = name, Key = key, Address = remote.ToString(), Port = port,
            ViaQr = viaQr, CheckNumber = check,
        };
        if (!viaQr)
            pending.PcApproval = ApproveManual?.Invoke(name, check) ?? Task.FromResult(false);

        lock (_gate)
        {
            foreach (var stale in _pending.Where(p => DateTime.UtcNow > p.Value.ExpiresUtc).Select(p => p.Key).ToList())
                _pending.Remove(stale);
            _pending[deviceId] = pending;
        }
        if (viaQr) PairStarted?.Invoke(name, check);

        var reply = Protocol.Ok();
        reply["pcId"] = _settings.PcId;
        reply["name"] = Environment.MachineName;
        reply["pub"] = FlickCrypto.B64(pcPub);
        reply["needsPcApproval"] = !viaQr;
        if (token is not null)
            reply["mac"] = FlickCrypto.B64(FlickCrypto.Hmac(token, FlickCrypto.Utf8("flick-pair-v2-pc"), pcPub, phonePub));
        return reply;
    }

    public PendingPair? FindPending(string deviceId)
    {
        lock (_gate)
            return _pending.TryGetValue(deviceId, out var p) && DateTime.UtcNow <= p.ExpiresUtc ? p : null;
    }

    /// <summary>"pair-confirm" arrived over the encrypted channel (which already proves the key).</summary>
    public async Task<JsonObject> ConfirmAsync(PendingPair p, string remote)
    {
        bool approved;
        try { approved = await p.PcApproval.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (TimeoutException) { approved = false; }

        lock (_gate) _pending.Remove(p.DeviceId);

        if (!approved)
        {
            PairFailed?.Invoke(p.Name);
            return Protocol.Error("denied");
        }

        var device = new TrustedDevice
        {
            DeviceId = p.DeviceId,
            Name = p.Name,
            Key = FlickCrypto.B64(p.Key),
            Address = remote,
            Port = p.Port,
            LastSeenUtc = DateTime.UtcNow,
        };
        _settings.AddOrReplaceDevice(device);
        Paired?.Invoke(device);

        var ok = Protocol.Ok();
        ok["name"] = Environment.MachineName;
        ok["pcId"] = _settings.PcId;
        return ok;
    }
}
