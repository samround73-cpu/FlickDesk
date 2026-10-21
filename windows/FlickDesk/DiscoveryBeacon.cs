using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FlickDesk;

/// <summary>Broadcasts "I'm a FlickDesk PC" on every IPv4 LAN every 2 seconds so phones can find it.</summary>
internal sealed class DiscoveryBeacon : IDisposable
{
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();

    public DiscoveryBeacon(AppSettings settings) => _settings = settings;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        while (!ct.IsCancellationRequested)
        {
            var payload = Encoding.UTF8.GetBytes(new JsonObject
            {
                ["app"] = "flick",
                ["v"] = Protocol.Version,
                ["type"] = "beacon",
                ["id"] = _settings.PcId,
                ["name"] = Environment.MachineName,
                ["port"] = Protocol.PcPort,
                ["paused"] = _settings.IsPaused,
            }.ToJsonString());

            foreach (var target in BroadcastTargets())
            {
                try { await udp.SendAsync(payload, payload.Length, new IPEndPoint(target, Protocol.BeaconPort)); }
                catch (SocketException) { /* interface went away; try again next round */ }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>255.255.255.255 plus the directed broadcast address of each active network (e.g. 192.168.1.255).</summary>
    private static IEnumerable<IPAddress> BroadcastTargets()
    {
        var targets = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    var bcast = new byte[4];
                    for (int i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | ~mask[i]);
                    targets.Add(new IPAddress(bcast));
                }
            }
        }
        catch (NetworkInformationException) { }
        return targets.Distinct();
    }

    public void Dispose() => _cts.Cancel();
}
