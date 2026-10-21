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
/// The drop target on the phone's edge: a slim rail of your devices that slides in only while
/// you drag files to that edge. The device under the cursor grows into a "dock" card – a
/// drawing of the phone whose screen lights up, framed by a slowly turning spectral rim. Drop,
/// and the file lands on that phone's screen, then "Sent ✓" and the rail tucks away.
///
/// Idle, nothing is visible and the edge is fully click-through. While the left mouse button
/// is held (see MouseButtonWatcher) a 1/255-alpha sensor strip on the edge becomes hit-testable,
/// so a file dragged to the edge opens the rail.
/// </summary>
internal sealed class RailWindow : Window
{
    private const double RailWidth = 280;
    private const double SensorDepth = 22;
    private const double CompactHeight = 62;
    private const double DockHeight = 268;

    private readonly Func<List<TrustedDevice>> _devices;
    private readonly Func<EffectPalette> _palette;
    private readonly Grid _root = new() { Background = Brushes.Transparent };
    private readonly Border _sensor = new() { Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
    private readonly Border _panel = new();
    private readonly StackPanel _rows = new();
    private readonly TranslateTransform _slide = new();
    private readonly List<Row> _rowList = new();
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer _armTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _safety = new() { Interval = TimeSpan.FromSeconds(20) };
    private Edge _edge = Edge.Right;
    private bool _open, _buttonDown, _busy;
    private Row? _hot;

    /// <summary>Args: file paths, the device they were dropped on, drop point in screen DIPs.</summary>
    public event Action<string[], TrustedDevice, Point>? FilesDropped;

    private sealed class Row
    {
        public required TrustedDevice Device { get; init; }
        public required Border Root { get; init; }
        public required FrameworkElement Compact { get; init; }
        public required FrameworkElement Dock { get; init; }
        public required ScaleTransform DockScale { get; init; }
        public required Border Lit { get; init; }
        public required Image Photo { get; init; }
        public required TextBlock Status { get; init; }
        public required Image Rim { get; init; }
        public required RotateTransform RimSpin { get; init; }
    }

    public RailWindow(Func<List<TrustedDevice>> devices, Func<EffectPalette> palette)
    {
        _devices = devices;
        _palette = palette;

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

        var header = new TextBlock
        {
            Text = "SEND TO", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Faint,
            Margin = new Thickness(6, 2, 0, 8),
        };
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_rows);
        _panel.Child = stack;
        _panel.Padding = new Thickness(10);
        _panel.CornerRadius = new CornerRadius(20);
        _panel.Background = new SolidColorBrush(Color.FromArgb(236, 20, 22, 29));
        _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        _panel.BorderThickness = new Thickness(1);
        _panel.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Opacity = 0.5 };
        _panel.RenderTransform = _slide;
        _panel.Opacity = 0;
        _panel.Visibility = Visibility.Collapsed;

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

        if (vertical)
        {
            Width = RailWidth + 60;
            Height = Math.Min(wa.Height * 0.86, 820);
            Top = wa.Top + (wa.Height - Height) / 2;
            Left = edge == Edge.Left ? wa.Left : wa.Right - Width;
            _sensor.Width = SensorDepth;
            _sensor.Height = double.NaN;
            _sensor.HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _sensor.VerticalAlignment = VerticalAlignment.Stretch;
            _panel.Width = RailWidth;
            _panel.HorizontalAlignment = edge == Edge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _panel.VerticalAlignment = VerticalAlignment.Center;
            _panel.Margin = edge == Edge.Left ? new Thickness(14, 0, 0, 0) : new Thickness(0, 0, 14, 0);
            _rows.Orientation = Orientation.Vertical;
        }
        else
        {
            Width = Math.Min(wa.Width * 0.7, 1000);
            Height = DockHeight + 110;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height;
            _sensor.Height = SensorDepth;
            _sensor.Width = double.NaN;
            _sensor.VerticalAlignment = VerticalAlignment.Bottom;
            _sensor.HorizontalAlignment = HorizontalAlignment.Stretch;
            _panel.Width = double.NaN;
            _panel.HorizontalAlignment = HorizontalAlignment.Center;
            _panel.VerticalAlignment = VerticalAlignment.Bottom;
            _panel.Margin = new Thickness(0, 0, 0, 14);
            _rows.Orientation = Orientation.Horizontal;
        }
        ResetHidden();
    }

    private Vector HiddenOffset => _edge switch
    {
        Edge.Left => new Vector(-(RailWidth + 80), 0),
        Edge.Right => new Vector(RailWidth + 80, 0),
        _ => new Vector(0, DockHeight + 140),
    };

    private void ResetHidden()
    {
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        _slide.X = HiddenOffset.X;
        _slide.Y = HiddenOffset.Y;
        _panel.Opacity = 0;
        _panel.Visibility = Visibility.Collapsed;
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
        var ease = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(300);
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(HiddenOffset.X, 0, d) { EasingFunction = ease });
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(HiddenOffset.Y, 0, d) { EasingFunction = ease });
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        if (_rowList.Count == 1) SetHot(_rowList[0]); // one phone: open straight into the dock
    }

    private void CloseRail()
    {
        if (!_open) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var d = TimeSpan.FromMilliseconds(240);
        var slideX = new DoubleAnimation(0, HiddenOffset.X, d) { EasingFunction = ease };
        slideX.Completed += (_, _) =>
        {
            if (_busy) return;
            ResetHidden();
            if (!_buttonDown) NativeMethods.SetClickThrough(this, true);
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, slideX);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, HiddenOffset.Y, d) { EasingFunction = ease });
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, d));
        foreach (var r in _rowList) r.RimSpin.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    // ---------------------------------------------------------------- drag & drop

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDrag(DragEventArgs e)
    {
        e.Handled = true;
        if (!HasFiles(e) || _busy)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        OpenRail();
        if (RowUnder(e) is { } under) SetHot(under);
        e.Effects = _hot is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private Row? RowUnder(DragEventArgs e)
    {
        foreach (var r in _rowList)
        {
            var p = e.GetPosition(r.Root);
            if (p.X >= 0 && p.Y >= 0 && p.X <= r.Root.ActualWidth && p.Y <= r.Root.ActualHeight) return r;
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

        hot.Status.Text = paths.Length > 1 ? $"Sending {paths.Length} files…" : "Sending…";
        hot.Status.Foreground = Ui.Muted;
        var thumb = await Thumbnails.LoadAsync(paths[0]);
        hot.Photo.Source = thumb;
        var land = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        var photoScale = new ScaleTransform(0.6, 0.6);
        hot.Photo.RenderTransformOrigin = new Point(0.5, 0.5);
        hot.Photo.RenderTransform = photoScale;
        photoScale.BeginAnimation(ScaleTransform.ScaleXProperty, land);
        photoScale.BeginAnimation(ScaleTransform.ScaleYProperty, land);
        hot.Photo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        hot.Lit.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(500)));

        FilesDropped?.Invoke(paths, hot.Device, screenPoint);
    }

    /// <summary>Called by the app when the transfer finishes.</summary>
    public void ShowResult(TrustedDevice device, bool ok, string? message = null)
    {
        var row = _rowList.FirstOrDefault(r => r.Device.DeviceId == device.DeviceId);
        if (row is not null)
        {
            row.Status.Text = ok ? "Sent ✓" : message ?? "Couldn't send";
            row.Status.Foreground = ok ? Ui.Good : Ui.B("#FF8A80");
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

    // ---------------------------------------------------------------- rows

    private void BuildRows()
    {
        _rows.Children.Clear();
        _rowList.Clear();
        _hot = null;
        var devices = _devices();
        if (devices.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "No phone paired yet.\nRight-click the tray icon → Pair a phone…",
                Foreground = Ui.Muted, FontSize = 12.5, Margin = new Thickness(6, 4, 6, 8), TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        bool horizontal = _edge == Edge.Bottom;
        foreach (var d in devices.OrderByDescending(x => x.LastSeenUtc))
        {
            var row = BuildRow(d);
            row.Root.Margin = horizontal ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 8);
            if (horizontal)
            {
                row.Root.Width = 220;
                row.Root.VerticalAlignment = VerticalAlignment.Bottom;
            }
            _rows.Children.Add(row.Root);
            _rowList.Add(row);
        }
    }

    private Row BuildRow(TrustedDevice d)
    {
        // A slowly turning spectral ring behind the card shows through a 2 px gap as a living rim.
        var spin = new RotateTransform();
        var rim = new Image
        {
            Source = EffectTextures.Disc(_palette()),
            Width = 560, Height = 560,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = spin,
            Opacity = 0,
            IsHitTestVisible = false,
            Margin = new Thickness(-300),
        };

        // ---- compact: glyph + name + status
        var glyph = new Border
        {
            Width = 20, Height = 36, CornerRadius = new CornerRadius(5),
            BorderBrush = Ui.B("#CFE8FF"), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 12, 0),
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = d.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        statusRow.Children.Add(Ui.Dot(d.Online ? Ui.Good : Ui.B("#5B6270"), 6));
        statusRow.Children.Add(new TextBlock { Text = d.Online ? "  Online" : "  Last seen " + Ui.Ago(d.LastSeenUtc), FontSize = 11.5, Foreground = Ui.Muted });
        info.Children.Add(statusRow);
        var compact = new DockPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Height = CompactHeight - 4 };
        DockPanel.SetDock(glyph, Dock.Left);
        compact.Children.Add(glyph);
        compact.Children.Add(info);

        // ---- dock: the phone drawing with a screen that lights up, name, status
        var lit = new Border
        {
            Opacity = 0,
            Background = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.45), Center = new Point(0.35, 0.45), RadiusX = 0.9, RadiusY = 0.7,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(220, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(160, 61, 199, 255), 0.4),
                    new GradientStop(Color.FromArgb(90, 214, 92, 255), 0.75),
                    new GradientStop(Color.FromArgb(0, 214, 92, 255), 1),
                },
            },
        };
        var photo = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        var screenGrid = new Grid();
        screenGrid.Children.Add(lit);
        screenGrid.Children.Add(photo);
        var screen = new Border { CornerRadius = new CornerRadius(13), Background = Ui.B("#0B0D12"), ClipToBounds = true, Child = screenGrid };
        var phone = new Border
        {
            Width = 86, Height = 168, CornerRadius = new CornerRadius(18), Padding = new Thickness(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), BorderThickness = new Thickness(2),
            Child = screen,
        };
        var status = new TextBlock { Text = "Drop to send", FontSize = 12, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        var dockStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        dockStack.Children.Add(phone);
        dockStack.Children.Add(new TextBlock { Text = d.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        dockStack.Children.Add(status);
        var dockScale = new ScaleTransform(0.85, 0.85);
        dockStack.RenderTransformOrigin = new Point(0.5, 0.3);
        dockStack.RenderTransform = dockScale;
        dockStack.Opacity = 0;
        dockStack.VerticalAlignment = VerticalAlignment.Top;

        var content = new Grid();
        content.Children.Add(compact);
        content.Children.Add(dockStack);
        var inner = new Border
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(13),
            Background = Ui.B("#F51A1D24"),
            Child = content,
            ClipToBounds = true,
        };

        var rootGrid = new Grid { ClipToBounds = true };
        rootGrid.Children.Add(rim);
        rootGrid.Children.Add(inner);
        var root = new Border
        {
            Height = CompactHeight,
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            ClipToBounds = true,
            Child = rootGrid,
        };

        return new Row
        {
            Device = d, Root = root, Compact = compact, Dock = dockStack, DockScale = dockScale,
            Lit = lit, Photo = photo, Status = status, Rim = rim, RimSpin = spin,
        };
    }

    /// <summary>The hovered device grows into the dock card; the previous one shrinks back.</summary>
    private void SetHot(Row row)
    {
        if (ReferenceEquals(_hot, row)) return;
        if (_hot is { } old) Collapse(old);
        _hot = row;

        var d = TimeSpan.FromMilliseconds(320);
        var ease = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut };
        row.Root.BeginAnimation(HeightProperty, new DoubleAnimation(row.Root.ActualHeight > 0 ? row.Root.ActualHeight : CompactHeight, DockHeight, d) { EasingFunction = ease });
        row.Compact.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        row.Dock.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(80) });
        var grow = new DoubleAnimation(0.85, 1, d) { EasingFunction = ease };
        row.DockScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        row.DockScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        row.Lit.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)));
        row.Rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(200)));
        row.RimSpin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
        row.Root.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
    }

    private static void Collapse(Row row)
    {
        var d = TimeSpan.FromMilliseconds(220);
        row.Root.BeginAnimation(HeightProperty, new DoubleAnimation(CompactHeight, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        row.Compact.BeginAnimation(OpacityProperty, new DoubleAnimation(1, d));
        row.Dock.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        row.Lit.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
        row.Rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d));
        row.RimSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        row.Root.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    }
}
