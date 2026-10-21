using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FlickDesk;

internal sealed record ClipInfo(string Text, string Device, DateTime Utc);

/// <summary>The flyout that opens from the tray icon: status, quick actions, clipboard, today's activity.</summary>
internal sealed class TrayFlyout : Window
{
    public sealed class Actions
    {
        public required Action SendFiles { get; init; }
        public required Action SendClipboard { get; init; }
        public required Action TogglePause { get; init; }
        public required Action OpenSettings { get; init; }
        public required Action OpenPairing { get; init; }
        public required Action OpenFolder { get; init; }
        public required Action<string> CopyText { get; init; }
        public required Action<string> ShowFile { get; init; }
    }

    private readonly AppSettings _s;
    private readonly History _history;
    private readonly Actions _actions;
    private readonly Func<ClipInfo?> _lastClip;
    private readonly Border _panel;
    private DateTime _hiddenAt;

    public TrayFlyout(AppSettings settings, History history, Func<ClipInfo?> lastClip, Actions actions)
    {
        _s = settings;
        _history = history;
        _lastClip = lastClip;
        _actions = actions;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        FontFamily = Ui.Font;
        Foreground = Ui.Text;
        FontSize = 13.5;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _panel = new Border
        {
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(14),
            Background = Ui.B("#F5181A21"),
            BorderBrush = Ui.B("#2A2E37"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.5 },
        };
        Content = _panel;

        Deactivated += (_, _) => { _hiddenAt = DateTime.UtcNow; Hide(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Hide(); };
    }

    /// <summary>Opens above the tray, or closes if it was just open (a tray click deactivates it first).</summary>
    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        if (DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(300)) return;
        _panel.Child = Build();
        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 4;
        Top = wa.Bottom - ActualHeight - 4;
        Activate();
    }

    public void RefreshIfOpen()
    {
        if (IsVisible) _panel.Child = Build();
    }

    private UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Header());
        root.Children.Add(QuickActions());

        if (_lastClip() is { } clip && DateTime.UtcNow - clip.Utc < TimeSpan.FromHours(12))
            root.Children.Add(ClipCard(clip));

        root.Children.Add(new TextBlock
        {
            Text = "TODAY", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Faint,
            Margin = new Thickness(16, 16, 16, 6),
        });
        var today = _history.Today.Take(6).ToList();
        if (today.Count == 0)
            root.Children.Add(Ui.T("Nothing flicked yet today.", 13, brush: Ui.Muted, margin: new Thickness(16, 0, 16, 10)));
        var list = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };
        foreach (var item in today) list.Children.Add(HistoryRow(item));
        root.Children.Add(list);

        root.Children.Add(Footer());
        return root;
    }

    private UIElement Header()
    {
        var phone = _s.MostRecentDevice();
        var row = new DockPanel { Margin = new Thickness(16, 16, 12, 14) };

        var settings = new Button { Style = Ui.Style("GhostButton"), Width = 40, Height = 40, Padding = new Thickness(0), Content = Ui.Icon(Ui.IconSliders, 18, Ui.B("#C9CED8")) };
        AutomationProperties.SetName(settings, "Settings");
        settings.Click += (_, _) => { Hide(); _actions.OpenSettings(); };
        DockPanel.SetDock(settings, Dock.Right);
        row.Children.Add(settings);

        var iconBox = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
        iconBox.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Ui.B("#0F1824") });
        iconBox.Children.Add(Ui.Icon(Ui.IconPhone, 20, Ui.B("#CFE8FF"), 1.7));
        if (phone is not null)
        {
            var dot = Ui.Dot(phone.Online ? Ui.Good : Ui.B("#5B6270"), 11);
            dot.HorizontalAlignment = HorizontalAlignment.Right;
            dot.VerticalAlignment = VerticalAlignment.Bottom;
            dot.BorderBrush = Ui.B("#181A21");
            dot.BorderThickness = new Thickness(2);
            dot.Margin = new Thickness(0, 0, -2, -2);
            iconBox.Children.Add(dot);
        }
        DockPanel.SetDock(iconBox, Dock.Left);
        row.Children.Add(iconBox);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (phone is null)
        {
            text.Children.Add(Ui.T("No phone paired", 14.5, FontWeights.SemiBold));
            var pair = new Button { Style = Ui.Style("GhostButton"), Content = "Pair a phone with a QR code →", Foreground = Ui.Accent, Padding = new Thickness(0), MinHeight = 22, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
            pair.Click += (_, _) => { Hide(); _actions.OpenPairing(); };
            text.Children.Add(pair);
        }
        else
        {
            text.Children.Add(Ui.T(phone.Name, 14.5, FontWeights.SemiBold));
            var side = _s.PhoneEdge switch { Edge.Left => "on the left", Edge.Bottom => "below", _ => "on the right" };
            var status = _s.IsPaused ? $"Paused until {_s.PausedUntilUtc!.Value.ToLocalTime():HH:mm}"
                       : phone.Online ? $"Connected · same Wi-Fi · {side}"
                       : $"Last seen {Ui.Ago(phone.LastSeenUtc)}";
            text.Children.Add(Ui.T(status, 12, brush: Ui.Muted));
        }
        row.Children.Add(text);

        return new Border { BorderBrush = Ui.B("#22252D"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    private UIElement QuickActions()
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(16, 14, 16, 4) };
        grid.Children.Add(Tile(Ui.IconSend, "Send files", () => { Hide(); _actions.SendFiles(); }));
        grid.Children.Add(Tile(Ui.IconClip, "Send clipboard", () => { _actions.SendClipboard(); }));
        grid.Children.Add(_s.IsPaused
            ? Tile(Ui.IconPlay, "Resume", () => { _actions.TogglePause(); RefreshIfOpen(); })
            : Tile(Ui.IconPause, "Pause 1 hr", () => { _actions.TogglePause(); RefreshIfOpen(); }));
        return grid;
    }

    private static Button Tile(string icon, string label, Action onClick)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var i = Ui.Icon(icon, 18);
        i.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(i);
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button { Style = Ui.Style("FlickButton"), Content = content, Height = 68, Margin = new Thickness(4, 0, 4, 0), Background = Ui.B("#1E2129"), BorderBrush = Ui.B("#2A2E37") };
        b.Click += (_, _) => onClick();
        return b;
    }

    private UIElement ClipCard(ClipInfo clip)
    {
        var p = new StackPanel();
        var head = new DockPanel();
        var when = Ui.T(Ui.Ago(clip.Utc), 11.5, brush: Ui.Faint);
        when.FontFamily = Ui.Mono;
        DockPanel.SetDock(when, Dock.Right);
        head.Children.Add(when);
        head.Children.Add(new TextBlock { Text = $"COPIED ON {clip.Device.ToUpperInvariant()}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Faint, TextTrimming = TextTrimming.CharacterEllipsis });
        p.Children.Add(head);
        p.Children.Add(new TextBlock
        {
            Text = clip.Text.Length > 300 ? clip.Text[..300] + "…" : clip.Text,
            FontFamily = Ui.Mono, FontSize = 12.5, Foreground = Ui.B("#DCE7F5"),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 40, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 8, 0, 10),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = Ui.Button("Copy here", () => _actions.CopyText(clip.Text), "PrimaryButton");
        copy.MinHeight = 32;
        buttons.Children.Add(copy);
        if (Uri.TryCreate(clip.Text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            var open = Ui.Button("Open link", () =>
            {
                Hide();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            });
            open.MinHeight = 32;
            open.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(open);
        }
        p.Children.Add(buttons);
        var card = Ui.Card(p);
        card.Margin = new Thickness(16, 12, 16, 0);
        return card;
    }

    private UIElement HistoryRow(HistoryEntry e)
    {
        var row = new DockPanel();
        var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Background = Ui.B("#2A3142"), Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true };
        if (e.IsClipboard)
        {
            thumb.Child = Ui.Icon(Ui.IconClip, 18, Ui.B("#C9CED8"));
        }
        else if (e.Path is { } path && File.Exists(path))
        {
            var src = Thumbnails.Load(path);
            if (src is not null) thumb.Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
        }
        DockPanel.SetDock(thumb, Dock.Left);
        row.Children.Add(thumb);

        var badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = e.Incoming ? Ui.B("#10243A") : Ui.B("#2A2233"),
            Child = new TextBlock { Text = e.Incoming ? "←" : "→", Foreground = e.Incoming ? Ui.B("#9FD3FF") : Ui.B("#E3B8FF"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(badge, e.Incoming ? "Received" : "Sent");
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = e.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = $"{(e.Incoming ? "From" : "To")} {e.Device} · {e.Utc.ToLocalTime():HH:mm}" + (e.Size > 0 ? $" · {Ui.Size(e.Size)}" : "");
        text.Children.Add(new TextBlock { Text = meta, FontSize = 12, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);

        var button = new Button { Style = Ui.Style("RowButton"), Content = row };
        AutomationProperties.SetName(button, e.Name);
        button.Click += (_, _) =>
        {
            if (e.IsClipboard) { _actions.CopyText(e.Name); return; }
            if (e.Incoming && e.Path is { } p) { Hide(); _actions.ShowFile(p); }
        };
        return button;
    }

    private UIElement Footer()
    {
        var row = new DockPanel { Margin = new Thickness(16, 10, 16, 12) };
        string effectName = _s.Effect switch
        {
            EffectPreset.Surge => "Spectral Surge",
            EffectPreset.Ripple or EffectPreset.Prism => "Spectral Ripple",
            EffectPreset.Halo or EffectPreset.Aurora => "Halo",
            EffectPreset.Glass => "Liquid Glass",
            _ => "Quiet",
        };
        var effect = Ui.T("Effect: " + effectName, 12.5, brush: Ui.Muted);
        effect.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(effect, Dock.Right);
        row.Children.Add(effect);
        var folder = new Button { Style = Ui.Style("GhostButton"), Content = "Open received files", Foreground = Ui.Accent, Padding = new Thickness(0), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Left };
        folder.Click += (_, _) => { Hide(); _actions.OpenFolder(); };
        row.Children.Add(folder);
        return new Border { BorderBrush = Ui.B("#22252D"), BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
    }
}
