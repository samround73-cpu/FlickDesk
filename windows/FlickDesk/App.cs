using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace FlickDesk;

/// <summary>Tray-only app: wires networking, pairing and the visual effects together.</summary>
internal sealed class App : Application
{
    private AppSettings _settings = null!;
    private History _history = null!;
    private PairingService _pairing = null!;
    private TransferServer? _server;
    private DiscoveryBeacon? _beacon;
    private RailWindow? _rail;
    private MouseButtonWatcher? _mouse;
    private OverlayWindow? _overlay;
    private TrayFlyout? _flyout;
    private SettingsWindow? _settingsWindow;
    private PairWindow? _pairWindow;
    private WF.NotifyIcon? _tray;
    private Mutex? _singleInstance;
    private string? _balloonPath;
    private ClipInfo? _lastClip;

    private CompositionOverlay? _light;
    private OverlayWindow Overlay => _overlay ??= new OverlayWindow { CardsOnly = _light is not null };
    private EffectOptions Fx => EffectOptions.From(_settings);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = new Mutex(true, "FlickDesk.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("FlickDesk is already running – look for it in the system tray.", "FlickDesk");
            Shutdown();
            return;
        }

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/FlickDesk;component/Theme.xaml", UriKind.Absolute),
        });

        _settings = AppSettings.Load();
        if (_settings.UseCompositor) _light = CompositionOverlay.TryCreate();
        _history = new History();
        EffectTextures.Prewarm(EffectPalette.Get(_settings.Palette));

        _pairing = new PairingService(_settings) { ApproveManual = ApproveManualPairingAsync };
        _pairing.Paired += d => Dispatcher.InvokeAsync(() => OnPaired(d));

        _server = new TransferServer(_settings, _pairing);
        _server.FileStarting += f => Dispatcher.InvokeAsync(() => OnFileStarting(f));
        _server.FileSaved += (f, path) => Dispatcher.InvokeAsync(() => OnFileSaved(f, path));
        _server.FileFailed += (f, err) => Dispatcher.InvokeAsync(() => OnFileFailed(f, err));
        _server.FileProgress += (f, done) => Dispatcher.InvokeAsync(() => OnFileProgress(f, done));
        _server.ClipReceived += (device, text) => Dispatcher.InvokeAsync(() => OnClipReceived(device, text));
        _server.DevicesChanged += () => Dispatcher.InvokeAsync(() => { _settingsWindow?.RefreshDevices(); _flyout?.RefreshIfOpen(); });
        try
        {
            _server.Start();
        }
        catch (SocketException ex)
        {
            MessageBox.Show($"FlickDesk couldn't listen on port {Protocol.PcPort}:\n{ex.Message}", "FlickDesk");
            Shutdown();
            return;
        }

        _beacon = new DiscoveryBeacon(_settings);
        _beacon.Start();

        _flyout = new TrayFlyout(_settings, _history, () => _lastClip, new TrayFlyout.Actions
        {
            SendFiles = PickAndSend,
            SendClipboard = () => _ = SendClipboardAsync(),
            TogglePause = TogglePause,
            OpenSettings = OpenSettings,
            OpenPairing = OpenPairing,
            OpenFolder = OpenSaveFolder,
            CopyText = text => { SetClipboard(text); Balloon("Copied", "It's on this PC's clipboard now."); },
            ShowFile = ShowInExplorer,
        });

        _tray = CreateTray();
        _mouse = new MouseButtonWatcher();
        _mouse.LeftButtonChanged += down => _rail?.SetButton(down);
        UpdatePortal();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(UpdatePortal);

        if (_settings.Devices().Count == 0)
            OpenPairing();
        else
            Balloon("FlickDesk is running", "Flick something from your phone – or click the tray icon.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _beacon?.Dispose();
        _mouse?.Dispose();
        _server?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    // ---------------------------------------------------------------- light

    /// <summary>Full-screen light via the compositor; the WPF renderer if that isn't available.</summary>
    private void PlayLight(double pos, double intensityScale = 1.0)
    {
        var edge = _settings.PhoneEdge;
        if (_light is not null && TryLight(l => l.PlayArrival(edge, pos, Fx, intensityScale))) return;
        Overlay.CardsOnly = false;
        Overlay.PlayArrival(edge, pos, Fx, intensityScale);
    }

    /// <summary>Runs a compositor effect; on any failure switches to the WPF renderer for good.</summary>
    private bool TryLight(Action<CompositionOverlay> play)
    {
        if (_light is null) return false;
        try
        {
            play(_light);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlickDesk] compositor light failed, falling back: {ex}");
            _light = null;
            if (_overlay is not null) _overlay.CardsOnly = false;
            return false;
        }
    }

    // ---------------------------------------------------------------- pairing

    private Task<bool> ApproveManualPairingAsync(string phone, string check) =>
        Dispatcher.InvokeAsync(() =>
        {
            var owner = new Window
            {
                Width = 1, Height = 1, Left = -10000, Top = -10000,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true,
            };
            owner.Show();
            owner.Activate();
            try
            {
                return MessageBox.Show(owner,
                           $"“{phone}” wants to pair with this PC.\n\n" +
                           $"Check your phone shows:   {check}\n\nDo the numbers match?",
                           "FlickDesk – pair a phone", MessageBoxButton.YesNo, MessageBoxImage.Question,
                           MessageBoxResult.No) == MessageBoxResult.Yes;
            }
            finally
            {
                owner.Close();
            }
        }).Task;

    private void OnPaired(TrustedDevice device)
    {
        UpdatePortal();
        _settingsWindow?.RefreshDevices();
        _flyout?.RefreshIfOpen();
        if (_pairWindow is null) Balloon($"Paired with {device.Name}", "Flick away – transfers between you are encrypted.");
        PlayLight(0.5, 0.8);
    }

    private void OpenPairing()
    {
        if (_pairWindow is { } open) { open.Activate(); return; }
        _pairWindow = new PairWindow(_pairing);
        _pairWindow.Closed += (_, _) => _pairWindow = null;
        _pairWindow.Show();
        _pairWindow.Activate();
    }

    private void OpenSettings() => OpenSettings("Effects");

    private void OpenSettings(string page)
    {
        if (_settingsWindow is { } open) { open.Close(); }
        _settingsWindow = new SettingsWindow(_settings, OnSettingsChanged, PlayTestEffect, OpenPairing, page);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged()
    {
        if (_settings.UseCompositor && _light is null) _light = CompositionOverlay.TryCreate();
        if (!_settings.UseCompositor) _light = null;
        if (_overlay is not null) _overlay.CardsOnly = _light is not null;
        UpdatePortal();
        _flyout?.RefreshIfOpen();
    }

    // ---------------------------------------------------------------- phone -> PC

    private bool QuietNow => _settings.QuietInFullScreen && NativeMethods.IsBusyFullScreen();

    private void OnFileStarting(IncomingFile f)
    {
        if (QuietNow || f.BatchIndex > 0) return; // one burst of light per flick, even for a stack
        PlayLight(f.EdgePos);
        if (_settings.Sound) Chime.Play();
    }

    // Big transfers get a progress panel, keyed so a resumed attempt picks up the same panel.
    private readonly Dictionary<string, ProgressToast> _toasts = new();
    private static string ToastKey(IncomingFile f) => $"in|{f.DeviceName}|{f.DisplayName}|{f.Size}";

    private void OnFileProgress(IncomingFile f, long done)
    {
        if (f.Size < Transfers.ProgressThreshold) return;
        var key = ToastKey(f);
        if (!_toasts.TryGetValue(key, out var toast))
        {
            toast = ProgressToast.Show($"Receiving {f.DisplayName}");
            toast.Closed += (_, _) => _toasts.Remove(key);
            _toasts[key] = toast;
        }
        toast.Report(done, f.Size, f.ResumedFrom > 0 ? $"From {f.DeviceName} (resumed) · " : $"From {f.DeviceName} · ");
    }

    private void OnFileFailed(IncomingFile f, string error)
    {
        if (_toasts.TryGetValue(ToastKey(f), out var toast))
        {
            toast.Finish($"Paused – it carries on when {f.DeviceName} reconnects", ok: false, seconds: 10);
            return;
        }
        Balloon("Transfer failed", $"{f.DisplayName}: {error}", WF.ToolTipIcon.Warning);
    }

    private async void OnFileSaved(IncomingFile f, string path)
    {
        var folder = FolderLabel(Path.GetDirectoryName(path));
        if (_toasts.TryGetValue(ToastKey(f), out var done))
            done.Finish(f.IsFolder ? $"Folder saved to {folder}" : $"Saved to {folder}", ok: true);
        _history.Add(new HistoryEntry { Name = Path.GetFileName(path), Path = path, Incoming = true, Device = f.DeviceName, Size = f.Size });
        _flyout?.RefreshIfOpen();

        bool quiet = QuietNow;
        if (!quiet && _settings.ShowCard)
        {
            var thumb = f.IsFolder ? null : await Thumbnails.LoadAsync(path);
            Overlay.DropCard(_settings.PhoneEdge, f.EdgePos, path, thumb, $"Saved to {folder}", Fx, Math.Min(f.BatchIndex, 6));
        }

        bool lastOfBatch = f.BatchIndex >= f.BatchCount - 1;
        if (lastOfBatch && (quiet || !_settings.ShowCard || f.BatchCount > 1))
        {
            _balloonPath = path;
            var title = f.BatchCount > 1 ? $"Saved {f.BatchCount} files from {f.DeviceName}" : $"Saved {Path.GetFileName(path)}";
            _tray?.ShowBalloonTip(5000, title, $"In your {folder} folder. Click to show.", WF.ToolTipIcon.Info);
        }
    }

    private void OnClipReceived(string device, string text)
    {
        _lastClip = new ClipInfo(text, device, DateTime.UtcNow);
        var preview = text.Length > 80 ? text[..80] + "…" : text;
        _history.Add(new HistoryEntry { Name = preview, Incoming = true, Device = device, IsClipboard = true });
        _flyout?.RefreshIfOpen();
        if (_settings.AutoCopyFromPhone && SetClipboard(text))
            Balloon($"Copied from {device}", preview);
        else
            Balloon($"{device} sent text", "Open FlickDesk from the tray to copy it.");
    }

    private static bool SetClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException) { Thread.Sleep(40); } // another app has the clipboard open
        }
        return false;
    }

    private string FolderLabel(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return "save";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(desktop).TrimEnd('\\'),
                             StringComparison.OrdinalIgnoreCase)
            ? "Desktop"
            : Path.GetFileName(folder.TrimEnd('\\'));
    }

    private static void ShowInExplorer(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- PC -> phone

    private TrustedDevice? TargetPhone(TrustedDevice? chosen = null)
    {
        if (chosen is not null) return _settings.FindDevice(chosen.DeviceId) ?? chosen;
        var phone = _settings.MostRecentDevice();
        if (phone is null)
            Balloon("No phone paired yet", "Right-click the tray icon → Pair a phone…");
        return phone;
    }

    private async Task SendToPhoneAsync(IReadOnlyList<string> paths, Point? dropPoint, TrustedDevice? target = null, bool fromRail = false)
    {
        var phone = TargetPhone(target);
        if (phone is null) return;

        // Files and folders (folders are zipped on the fly and unpacked on the phone).
        var files = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (files.Count == 0)
        {
            Balloon("Nothing to send", "That item couldn't be found.");
            if (fromRail) _rail?.ShowResult(phone, false, "Nothing to send");
            return;
        }

        var thumb = File.Exists(files[0]) ? await Thumbnails.LoadAsync(files[0]) : null;
        var edge = _settings.PhoneEdge;
        double sendPos = dropPoint is { } dp
            ? (edge == Edge.Bottom ? dp.X / SystemParameters.PrimaryScreenWidth : dp.Y / SystemParameters.PrimaryScreenHeight)
            : 0.5;
        bool compositorLight = _light is not null && TryLight(l => l.PlaySend(edge, sendPos, Fx));
        // From the rail the file visibly lands on the phone card, so only the light plays here.
        if (!fromRail || !compositorLight)
            Overlay.PlaySend(edge, dropPoint, files[0], thumb, files.Count, Fx, includeLight: !compositorLight);

        // Runs on a background thread so I/O continuations never compete with the animation.
        var settings = _settings;
        var results = await Task.Run(async () =>
        {
            var list = new List<(string file, string? error)>();
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                int index = i; // the loop variable is shared by every closure below
                bool isDir = Directory.Exists(file);
                var label = Path.GetFileName(file.TrimEnd('\\', '/'));
                ProgressToast? toast = null;
                var error = await PhoneClient.SendFileAsync(settings, phone, file, index, files.Count, (done, total, note) =>
                    Dispatcher.InvokeAsync(() =>
                    {
                        var count = files.Count > 1 ? $" ({index + 1} of {files.Count})" : "";
                        bool big = isDir || total >= Transfers.ProgressThreshold;
                        if (note is not null)
                        {
                            _rail?.ShowProgress(phone, note);
                            if (big)
                            {
                                toast ??= ProgressToast.Show($"Sending {label} to {phone.Name}");
                                toast.Report(Math.Max(0, done), total, note + " · ");
                            }
                            return;
                        }
                        if (!big) return;
                        int pct = total > 0 ? (int)(done * 100 / total) : 0;
                        _rail?.ShowProgress(phone, $"Sending{count}… {pct} %");
                        toast ??= ProgressToast.Show($"Sending {label} to {phone.Name}");
                        toast.Report(done, total, count.Length > 0 ? $"File{count} · " : "");
                    })).ConfigureAwait(false);
                // Queued after any pending progress updates, so the panel (if one was shown) exists by now.
                await Dispatcher.InvokeAsync(() =>
                    toast?.Finish(error is null ? "Sent ✓" : error, error is null, error is null ? 2 : 6));
                list.Add((file, error));
            }
            return list;
        });

        foreach (var (file, error) in results.Where(r => r.error is null))
            _history.Add(new HistoryEntry
            {
                Name = Path.GetFileName(file.TrimEnd('\\', '/')), Path = file, Incoming = false, Device = phone.Name,
                Size = File.Exists(file) ? new FileInfo(file).Length : Transfers.DirectorySize(file),
            });

        var failures = results.Where(r => r.error is not null).Select(r => $"{Path.GetFileName(r.file)}: {r.error}").ToList();
        if (failures.Count > 0)
            Balloon($"Couldn't send to {phone.Name}", string.Join("\n", failures.Take(3)), WF.ToolTipIcon.Warning);
        if (fromRail) _rail?.ShowResult(phone, failures.Count == 0);
        _flyout?.RefreshIfOpen();
    }

    private async Task SendClipboardAsync()
    {
        var phone = TargetPhone();
        if (phone is null) return;
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (COMException) { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            Balloon("Clipboard is empty", "Copy some text or a link first.");
            return;
        }

        var settings = _settings;
        var error = await Task.Run(() => PhoneClient.SendClipAsync(settings, phone, text));
        if (error is null)
        {
            var preview = text.Length > 80 ? text[..80] + "…" : text;
            _history.Add(new HistoryEntry { Name = preview, Incoming = false, Device = phone.Name, IsClipboard = true });
            Balloon($"Sent to {phone.Name}", preview);
            _flyout?.RefreshIfOpen();
        }
        else
        {
            Balloon("Couldn't send clipboard", error, WF.ToolTipIcon.Warning);
        }
    }

    private void TogglePause()
    {
        _settings.PausedUntilUtc = _settings.IsPaused ? null : DateTime.UtcNow.AddHours(1);
        _settings.Save();
    }

    private void UpdatePortal()
    {
        if (!_settings.ShowDropZone)
        {
            _rail?.Close();
            _rail = null;
            return;
        }
        if (_rail is null)
        {
            _rail = new RailWindow(() => _settings.Devices(), () => EffectPalette.Get(_settings.Palette));
            _rail.FilesDropped += (paths, device, point) => _ = SendToPhoneAsync(paths, point, device, fromRail: true);
            _rail.Show();
        }
        _rail.Place(_settings.PhoneEdge);
    }

    // ---------------------------------------------------------------- tray

    private WF.NotifyIcon CreateTray()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Opening += (_, _) => BuildMenu(menu);
        BuildMenu(menu);

        var tray = new WF.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "FlickDesk",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) _flyout?.Toggle(); };
        tray.BalloonTipClicked += (_, _) => { if (_balloonPath is not null) ShowInExplorer(_balloonPath); };
        return tray;
    }

    private void BuildMenu(WF.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Open FlickDesk", null, (_, _) => _flyout?.Toggle());
        menu.Items.Add("Pair a phone…", null, (_, _) => OpenPairing());
        menu.Items.Add("Send files to phone…", null, (_, _) => PickAndSend());
        menu.Items.Add("Send clipboard to phone", null, (_, _) => _ = SendClipboardAsync());
        var phones = new WF.ToolStripMenuItem("Remove a phone");
        foreach (var d in _settings.Devices())
        {
            var id = d.DeviceId; var name = d.Name;
            phones.DropDownItems.Add($"{name}  ·  {(d.Online ? "online" : "last seen " + Ui.Ago(d.LastSeenUtc))}", null, (_, _) => ForgetPhone(id, name));
        }
        if (phones.DropDownItems.Count == 0) phones.Enabled = false;
        else
        {
            phones.DropDownItems.Add(new WF.ToolStripSeparator());
            phones.DropDownItems.Add("Manage phones…", null, (_, _) => OpenSettings("Phones"));
        }
        menu.Items.Add(phones);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(_settings.IsPaused ? "Resume receiving" : "Pause for 1 hour", null, (_, _) => TogglePause());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add($"Open received files ({FolderLabel(_settings.SaveFolder)})", null, (_, _) => OpenSaveFolder());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quit FlickDesk", null, (_, _) => Shutdown());
    }

    private void ForgetPhone(string deviceId, string name)
    {
        if (MessageBox.Show($"Remove {name} from FlickDesk? It will need to pair again to send or receive.", "FlickDesk",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.ForgetDevice(deviceId);
        OnSettingsChanged();
        _settingsWindow?.RefreshDevices();
        _flyout?.RefreshIfOpen();
    }

    private void PickAndSend()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Send to phone" };
        if (dialog.ShowDialog() == true) _ = SendToPhoneAsync(dialog.FileNames, null);
    }

    private void OpenSaveFolder()
    {
        Directory.CreateDirectory(_settings.SaveFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.SaveFolder}\"") { UseShellExecute = true });
    }

    private async void PlayTestEffect()
    {
        var sample = Environment.ProcessPath ?? typeof(App).Assembly.Location;
        var thumb = await Thumbnails.LoadAsync(sample);
        PlayLight(0.5);
        if (_settings.Sound) Chime.Play();
        if (_settings.ShowCard)
            Overlay.DropCard(_settings.PhoneEdge, 0.5, sample, thumb, "Preview – nothing was saved", Fx);
    }

    private void Balloon(string title, string text, WF.ToolTipIcon icon = WF.ToolTipIcon.Info)
    {
        _balloonPath = null;
        _tray?.ShowBalloonTip(4000, title, text, icon);
    }

    private static SD.Icon MakeTrayIcon()
    {
        // The app icon, picked at the tray's size (16 px at 100 %, larger on high-DPI screens).
        using var stream = typeof(App).Assembly.GetManifestResourceStream("FlickDesk.ico");
        if (stream is not null) return new SD.Icon(stream, WF.SystemInformation.SmallIconSize);

        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(SD.Color.Transparent);
            using var brush = new SD.SolidBrush(SD.Color.FromArgb(255, 124, 196, 255));
            g.FillEllipse(brush, 2, 2, 28, 28);
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }
}
