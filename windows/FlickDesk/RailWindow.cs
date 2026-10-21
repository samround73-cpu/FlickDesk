using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FlickDesk;

/// <summary>
/// The drop target on the phone's edge ("Halo Card"). While you drag files to that edge a
/// frosted card slides in for the phone you'll send to: a soft accent halo, a phone glyph in a
/// breathing glow, its name and "Release to send 1 photo". Other paired devices sit in a small
/// pill underneath – hover one to switch the card to it. A thin light line marks the edge.
/// Drop, and the photo appears on the glyph's screen, then "Sent ✓" and the card tucks away.
///
/// Idle, nothing is visible and the edge is fully click-through. While the left mouse button
/// is held (see MouseButtonWatcher) a 1/255-alpha sensor strip on the edge becomes hit-testable,
/// so a file dragged to the edge opens the card.
/// </summary>
internal sealed class RailWindow : Window
{
    private const double PanelWidth = 236;
    private const double CardWidth = 216;
    private const double CardHeight = 260;
    private const double SensorDepth = 22;

    private static readonly Color AccentColor = Color.FromRgb(0x7C, 0xC4, 0xFF);

    private readonly Func<List<TrustedDevice>> _devices;
    private readonly Grid _root = new() { Background = Brushes.Transparent };
    private readonly Border _sensor = new() { Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
    private readonly Border _edgeLine = new() { IsHitTestVisible = false, Opacity = 0, CornerRadius = new CornerRadius(2) };
    private readonly StackPanel _panel = new();
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _panelScale = new(1, 1);
    private readonly List<Row> _rowList = new();
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer _armTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _safety = new() { Interval = TimeSpan.FromSeconds(20) };

    // The card (shared by whichever device is "hot").
    private readonly Border _card = new();
    private readonly TextBlock _cardName = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CardWidth - 28 };
    private readonly TextBlock _cardStatus = new() { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Image _cardPhoto = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly ScaleTransform _haloScale = new(1, 1);
    private readonly Border _halo = new();
    private readonly WrapPanel _pillItems = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _pill = new();

    private Edge _edge = Edge.Right;
    private bool _open, _buttonDown, _busy;
    private Row? _hot;
    private string _dropText = "Release to send";

    /// <summary>Args: file paths, the device they were dropped on, drop point in screen DIPs.</summary>
    public event Action<string[], TrustedDevice, Point>? FilesDropped;

    private sealed class Row
    {
        public required TrustedDevice Device { get; init; }
        public required Border Item { get; init; }
    }

    public RailWindow(Func<List<TrustedDevice>> devices, Func<EffectPalette> palette)
    {
        _devices = devices;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;
        FontFamily = Ui.Font;
        Foreground = Ui.Text;
        UseLayoutRounding = true;

        BuildCard();
        _pill.Child = _pillItems;
        _pill.Padding = new Thickness(6);
        _pill.Margin = new Thickness(0, 16, 0, 0);
        _pill.CornerRadius = new CornerRadius(22);
        _pill.HorizontalAlignment = HorizontalAlignment.Center;
        _pill.Background = new SolidColorBrush(Color.FromArgb(200, 24, 27, 35));
        _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
        _pill.BorderThickness = new Thickness(1);

        _panel.Children.Add(_card);
        _panel.Children.Add(_pill);
        _panel.Width = PanelWidth;
        _panel.RenderTransformOrigin = new Point(0.5, 0.5);
        _panel.RenderTransform = new TransformGroup { Children = { _panelScale, _slide } };
        _panel.Opacity = 0;
        _panel.Visibility = Visibility.Collapsed;

        _root.Children.Add(_edgeLine);
        _root.Children.Add(_sensor);
        _root.Children.Add(_panel);
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            NativeMethods.MakeNoActivate(this);
            NativeMethods.SetClickThrough(this, true);
        };

        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); if (!_busy) CloseRail(); };
        _armTimer.Tick += (_, _) =>
        {
            _armTimer.Stop();
            // Only after the button has been held a moment: a plain click on whatever is under the
            // edge (a scrollbar, say) has already landed there and is never stolen.
            if (_buttonDown) NativeMethods.SetClickThrough(this, false);
        };
        _safety.Tick += (_, _) => { _safety.Stop(); _busy = false; CloseRail(); };

        DragEnter += (_, e) => { _closeTimer.Stop(); OnDrag(e); };
        DragOver += (_, e) => { _closeTimer.Stop(); OnDrag(e); };
        DragLeave += (_, _) => _closeTimer.Start();
        Drop += OnDrop;
    }

    private void BuildCard()
    {
        // Phone glyph: an outlined handset whose "screen" shows the photo once it's dropped.
        var screen = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(3), Child = _cardPhoto };
        var notch = new Border { Width = 8, Height = 2, CornerRadius = new CornerRadius(1), Background = Ui.B("#E6EEF8"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        var glyphGrid = new Grid();
        glyphGrid.Children.Add(screen);
        glyphGrid.Children.Add(notch);
        var glyph = new Border
        {
            Width = 40, Height = 68, CornerRadius = new CornerRadius(11),
            BorderBrush = Ui.B("#E6EEF8"), BorderThickness = new Thickness(2), Child = glyphGrid,
        };

        _halo.Width = 108;
        _halo.Height = 108;
        _halo.CornerRadius = new CornerRadius(54);
        _halo.Background = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.45), Center = new Point(0.5, 0.45), RadiusX = 0.5, RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(82, AccentColor.R, AccentColor.G, AccentColor.B), 0),
                new GradientStop(Color.FromArgb(0, AccentColor.R, AccentColor.G, AccentColor.B), 1),
            },
        };
        _halo.RenderTransformOrigin = new Point(0.5, 0.5);
        _halo.RenderTransform = _haloScale;
        var haloHost = new Grid { Width = 108, Height = 108, HorizontalAlignment = HorizontalAlignment.Center };
        haloHost.Children.Add(_halo);
        haloHost.Children.Add(new Border { Child = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(haloHost);
        _cardName.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(_cardName);
        stack.Children.Add(_cardStatus);

        _card.Width = CardWidth;
        _card.Height = CardHeight;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.CornerRadius = new CornerRadius(28);
        _card.Background = new SolidColorBrush(Color.FromArgb(214, 24, 27, 35));
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(150, AccentColor.R, AccentColor.G, AccentColor.B));
        _card.BorderThickness = new Thickness(1);
        // The halo: a soft accent glow around the card rather than a hard drop shadow.
        _card.Effect = new DropShadowEffect { Color = AccentColor, BlurRadius = 38, ShadowDepth = 0, Opacity = 0.45 };
        _card.Child = stack;
    }

    // ---------------------------------------------------------------- arming

    /// <summary>Called on every left-button press/release anywhere on screen.</summary>
    public void SetButton(bool down)
    {
        _buttonDown = down;
        if (down)
        {
            _armTimer.Stop();
            _armTimer.Start();
        }
        else
        {
            _armTimer.Stop();
            if (_open && !_busy) _closeTimer.Start(); // the drag ended somewhere else
            if (!_open) NativeMethods.SetClickThrough(this, true);
        }
    }

    // ---------------------------------------------------------------- layout

    public void Place(Edge edge)
    {
        _edge = edge;
        var wa = SystemParameters.WorkArea;
        bool vertical = edge != Edge.Bottom;
        var accent = Color.FromArgb(220, AccentColor.R, AccentColor.G, AccentColor.B);
        var clear = Color.FromArgb(0, AccentColor.R, AccentColor.G, AccentColor.B);

        if (vertical)
        {
            Width = PanelWidth + 60;
            Height = Math.Min(wa.Height * 0.86, 820);
            Top = wa.Top + (wa.Height - Height) / 2;
            Left = edge == Edge.Left ? wa.Left : wa.Right - Width;
            _sensor.Width = SensorDepth;
            _sensor.Height = double.NaN;
            _sensor.HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _sensor.VerticalAlignment = VerticalAlignment.Stretch;
            _edgeLine.Width = 3;
            _edgeLine.Height = Math.Min(Height * 0.6, 420);
            _edgeLine.HorizontalAlignment = _sensor.HorizontalAlignment;
            _edgeLine.VerticalAlignment = VerticalAlignment.Center;
            _edgeLine.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(clear, 0), new GradientStop(accent, 0.5), new GradientStop(clear, 1) },
            };
            _panel.HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _panel.VerticalAlignment = VerticalAlignment.Center;
            _panel.Margin = edge == Edge.Left ? new Thickness(22, 0, 0, 0) : new Thickness(0, 0, 22, 0);
        }
        else
        {
            Width = Math.Min(wa.Width * 0.7, 1000);
            Height = CardHeight + 160;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height;
            _sensor.Height = SensorDepth;
            _sensor.Width = double.NaN;
            _sensor.VerticalAlignment = VerticalAlignment.Bottom;
            _sensor.HorizontalAlignment = HorizontalAlignment.Stretch;
            _edgeLine.Height = 3;
            _edgeLine.Width = Math.Min(Width * 0.6, 520);
            _edgeLine.VerticalAlignment = VerticalAlignment.Bottom;
            _edgeLine.HorizontalAlignment = HorizontalAlignment.Center;
            _edgeLine.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops = { new GradientStop(clear, 0), new GradientStop(accent, 0.5), new GradientStop(clear, 1) },
            };
            _panel.HorizontalAlignment = HorizontalAlignment.Center;
            _panel.VerticalAlignment = VerticalAlignment.Bottom;
            _panel.Margin = new Thickness(0, 0, 0, 18);
        }
        ResetHidden();
    }

    private Vector HiddenOffset => _edge switch
    {
        Edge.Left => new Vector(-60, 0),
        Edge.Right => new Vector(60, 0),
        _ => new Vector(0, 60),
    };

    private void ResetHidden()
    {
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        _slide.X = HiddenOffset.X;
        _slide.Y = HiddenOffset.Y;
        _panel.Opacity = 0;
        _panel.Visibility = Visibility.Collapsed;
        _edgeLine.BeginAnimation(OpacityProperty, null);
        _edgeLine.Opacity = 0;
        _haloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _haloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _cardPhoto.BeginAnimation(OpacityProperty, null);
        _cardPhoto.Opacity = 0;
        _cardPhoto.Source = null;
        _open = false;
        _hot = null;
    }

    // ---------------------------------------------------------------- open / close

    private void OpenRail()
    {
        if (_open) return;
        _open = true;
        BuildRows();
        _panel.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(280);
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(HiddenOffset.X, 0, d) { EasingFunction = ease });
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(HiddenOffset.Y, 0, d) { EasingFunction = ease });
        var grow = new DoubleAnimation(0.94, 1, d) { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } };
        _panelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _panelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _edgeLine.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(260)));

        // The glow round the phone glyph breathes gently while the card is open.
        var breathe = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(1300))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _haloScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        _haloScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);

        if (_rowList.Count > 0) SetHot(_rowList[0]);
    }

    private void CloseRail()
    {
        if (!_open) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var d = TimeSpan.FromMilliseconds(220);
        var slideX = new DoubleAnimation(0, HiddenOffset.X, d) { EasingFunction = ease };
        slideX.Completed += (_, _) =>
        {
            if (_busy) return;
            ResetHidden();
            if (!_buttonDown) NativeMethods.SetClickThrough(this, true);
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, slideX);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, HiddenOffset.Y, d) { EasingFunction = ease });
        var shrink = new DoubleAnimation(1, 0.96, d) { EasingFunction = ease };
        _panelScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _panelScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, d));
        _edgeLine.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
    }

    // ---------------------------------------------------------------- drag & drop

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp", ".tif", ".tiff" };

    private static string DropText(string[]? paths)
    {
        int n = paths?.Length ?? 0;
        if (n == 0) return "Release to send";
        bool photos = paths!.All(p => ImageExts.Contains(System.IO.Path.GetExtension(p)));
        string noun = photos ? (n == 1 ? "photo" : "photos") : (n == 1 ? "file" : "files");
        return $"Release to send {n} {noun}";
    }

    private void OnDrag(DragEventArgs e)
    {
        e.Handled = true;
        if (!HasFiles(e) || _busy)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        if (!_open) _dropText = DropText(e.Data.GetData(DataFormats.FileDrop) as string[]);
        OpenRail();
        if (RowUnder(e) is { } under) SetHot(under);
        e.Effects = _hot is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private Row? RowUnder(DragEventArgs e)
    {
        foreach (var r in _rowList)
        {
            var p = e.GetPosition(r.Item);
            if (p.X >= 0 && p.Y >= 0 && p.X <= r.Item.ActualWidth && p.Y <= r.Item.ActualHeight) return r;
        }
        return null;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var hot = _hot;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (hot is null || paths is not { Length: > 0 }) { _closeTimer.Start(); return; }

        var p = e.GetPosition(this);
        var screenPoint = new Point(Left + p.X, Top + p.Y);
        _busy = true;
        _safety.Start();

        _cardStatus.Text = paths.Length > 1 ? $"Sending {paths.Length} files…" : "Sending…";
        _cardStatus.Foreground = Ui.Muted;
        _cardPhoto.Source = await Thumbnails.LoadAsync(paths[0]);
        _cardPhoto.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));

        // One soft pulse of the halo as the file "lands".
        var pulse = new DoubleAnimation(1, 1.22, TimeSpan.FromMilliseconds(220)) { AutoReverse = true, EasingFunction = new SineEase() };
        _haloScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        _haloScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        FilesDropped?.Invoke(paths, hot.Device, screenPoint);
    }

    /// <summary>Called by the app when the transfer finishes.</summary>
    public void ShowResult(TrustedDevice device, bool ok, string? message = null)
    {
        if (_hot is not null && _hot.Device.DeviceId == device.DeviceId)
        {
            _cardStatus.Text = ok ? "Sent ✓" : message ?? "Couldn't send";
            _cardStatus.Foreground = ok ? Ui.Good : Ui.B("#FF8A80");
        }
        _safety.Stop();
        var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ok ? 1100 : 2600) };
        done.Tick += (_, _) =>
        {
            done.Stop();
            _busy = false;
            CloseRail();
        };
        done.Start();
    }

    // ---------------------------------------------------------------- devices

    private void BuildRows()
    {
        _pillItems.Children.Clear();
        _rowList.Clear();
        _hot = null;
        var devices = _devices();
        if (devices.Count == 0)
        {
            _cardName.Text = "No phone paired";
            _cardStatus.Text = "Tray icon → Pair a phone…";
            _cardStatus.Foreground = Ui.Muted;
            _pill.Visibility = Visibility.Collapsed;
            return;
        }
        // Online phones first, then most recently seen.
        foreach (var d in devices.OrderByDescending(x => x.Online).ThenByDescending(x => x.LastSeenUtc))
        {
            var dot = Ui.Dot(d.Online ? Ui.Good : Ui.B("#5B6270"), 7);
            dot.Margin = new Thickness(0, 0, 7, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = d.Name, FontSize = 12, Foreground = d.Online ? Ui.B("#C9CED8") : Ui.Faint, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new Border
            {
                Child = row, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(2),
                CornerRadius = new CornerRadius(16), Background = Brushes.Transparent,
            };
            _pillItems.Children.Add(item);
            _rowList.Add(new Row { Device = d, Item = item });
        }
        // A pill with a single entry would just repeat the card.
        _pill.Visibility = _rowList.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Points the card at the hovered device and highlights it in the pill.</summary>
    private void SetHot(Row row)
    {
        if (ReferenceEquals(_hot, row)) return;
        _hot = row;
        foreach (var r in _rowList)
        {
            bool on = ReferenceEquals(r, row);
            r.Item.Background = on ? new SolidColorBrush(Color.FromArgb(46, AccentColor.R, AccentColor.G, AccentColor.B)) : Brushes.Transparent;
            r.Item.BorderBrush = on ? new SolidColorBrush(Color.FromArgb(120, AccentColor.R, AccentColor.G, AccentColor.B)) : null;
            r.Item.BorderThickness = new Thickness(on ? 1 : 0);
        }
        _cardName.Text = row.Device.Name;
        _cardStatus.Text = row.Device.Online ? _dropText : $"{_dropText} · may be asleep";
        _cardStatus.Foreground = Ui.Muted;

        // A quick fade of the name so switching devices reads as a change, not a flicker.
        _cardName.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)));
    }
}
