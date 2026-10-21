using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlickDesk;

internal sealed class TrustedDevice
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Base64 long-term AES key agreed during pairing.</summary>
    public string Key { get; set; } = "";
    public string? Address { get; set; }
    public int Port { get; set; } = Protocol.PhonePort;
    public DateTime LastSeenUtc { get; set; }

    [JsonIgnore] public byte[] KeyBytes => FlickCrypto.FromB64(Key);
    [JsonIgnore] public bool Online => DateTime.UtcNow - LastSeenUtc < TimeSpan.FromSeconds(45);
}

/// <summary>Aurora and Prism are kept only so older settings files still load (they map to Halo / Ripple).</summary>
internal enum EffectPreset { Ripple, Aurora, Prism, Glass, Quiet, Surge, Halo }
internal enum EffectSpeed { Calm, Natural, Snappy }

/// <summary>Settings persisted to %AppData%\FlickDesk\settings.json. Thread-safe.</summary>
internal sealed class AppSettings
{
    public string PcId { get; set; } = Guid.NewGuid().ToString("N");
    public string PhoneSide { get; set; } = nameof(Edge.Right);
    public string SaveFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public bool ShowDropZone { get; set; } = true;
    public List<TrustedDevice> TrustedDevices { get; set; } = new();

    // Effects
    public EffectPreset Effect { get; set; } = EffectPreset.Surge;
    /// <summary>Bumped when the default look changes so existing installs get the new one once.</summary>
    public int EffectVersion { get; set; }
    public string Palette { get; set; } = "Spectrum";
    public double Intensity { get; set; } = 0.9;
    public EffectSpeed Speed { get; set; } = EffectSpeed.Natural;
    public bool Sound { get; set; } = true;
    public bool ShowCard { get; set; } = true;
    public bool QuietInFullScreen { get; set; } = true;
    public bool ReduceMotion { get; set; }
    /// <summary>Full-screen light on the Windows compositor (GPU). Off = the classic WPF renderer.</summary>
    public bool UseCompositor { get; set; } = true;

    // Clipboard / transfers
    public bool AutoCopyFromPhone { get; set; } = true;
    public DateTime? PausedUntilUtc { get; set; }

    [JsonIgnore]
    public Edge PhoneEdge => Enum.TryParse<Edge>(PhoneSide, true, out var e) ? e : Edge.Right;

    [JsonIgnore]
    public bool IsPaused => PausedUntilUtc is { } until && until > DateTime.UtcNow;

    private readonly object _gate = new();

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlickDesk");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (s is not null)
                {
                    if (string.IsNullOrWhiteSpace(s.PcId)) s.PcId = Guid.NewGuid().ToString("N");
                    if (string.IsNullOrWhiteSpace(s.SaveFolder))
                        s.SaveFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    // Phones paired with v1 have no key – they must pair again.
                    s.TrustedDevices = (s.TrustedDevices ?? new()).Where(d => !string.IsNullOrEmpty(d.Key)).ToList();
                    s.Intensity = Math.Clamp(s.Intensity, 0.2, 1.0);
                    if (s.EffectVersion < 3)
                    {
                        s.Effect = EffectPreset.Surge;
                        s.EffectVersion = 3;
                        s.Save();
                    }
                    return s;
                }
            }
        }
        catch (Exception) { /* corrupt file: start fresh */ }

        var fresh = new AppSettings { EffectVersion = 3 };
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    public TrustedDevice? FindDevice(string deviceId)
    {
        lock (_gate) return TrustedDevices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public TrustedDevice? MostRecentDevice()
    {
        lock (_gate)
            return TrustedDevices.Where(d => d.Address is not null)
                                 .OrderByDescending(d => d.LastSeenUtc)
                                 .FirstOrDefault();
    }

    public List<TrustedDevice> Devices()
    {
        lock (_gate) return TrustedDevices.ToList();
    }

    public void AddOrReplaceDevice(TrustedDevice device)
    {
        lock (_gate)
        {
            // Same id, or the same phone paired again after a reinstall (new id, same name):
            // replace the old entry so the drop panel doesn't list the phone twice.
            TrustedDevices.RemoveAll(d => d.DeviceId == device.DeviceId
                || string.Equals(d.Name, device.Name, StringComparison.OrdinalIgnoreCase));
            TrustedDevices.Add(device);
        }
        Save();
    }

    /// <summary>Updates where a phone can be reached. Saves at most once a minute per phone.</summary>
    public void Touch(string deviceId, string? name, string address, int port)
    {
        bool save = false;
        lock (_gate)
        {
            var d = TrustedDevices.FirstOrDefault(x => x.DeviceId == deviceId);
            if (d is null) return;
            save = d.Address != address || d.Port != port || DateTime.UtcNow - d.LastSeenUtc > TimeSpan.FromMinutes(1);
            if (!string.IsNullOrWhiteSpace(name)) d.Name = name;
            d.Address = address;
            d.Port = port;
            d.LastSeenUtc = DateTime.UtcNow;
        }
        if (save) Save();
    }

    public void ForgetDevice(string deviceId)
    {
        lock (_gate) TrustedDevices.RemoveAll(d => d.DeviceId == deviceId);
        Save();
    }

    public void ForgetAllDevices()
    {
        lock (_gate) TrustedDevices.Clear();
        Save();
    }
}
