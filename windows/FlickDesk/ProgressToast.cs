using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FlickDesk;

/// <summary>
/// A small panel above the tray for big transfers: name, "1.2 GB of 3.4 GB · 28 MB/s · 1 min left"
/// and a thin spectral progress bar. One panel per transfer key; it fades out when done.
/// Never takes focus and lets clicks through except on itself.
/// </summary>
internal sealed class ProgressToast : Window
{
    private const double PanelWidth = 340;

    private readonly TextBlock _title = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { FontSize = 12, Foreground = Ui.Muted, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _barFill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Border _barTrack = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.B("#262A33"), Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _hideTimer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _startBytes = -1;
    private double _rate; // bytes per second, smoothed
    private long _lastBytes;
    private double _lastSeconds;

    private static readonly System.Collections.Generic.List<ProgressToast> Open = new();

    public ProgressToast(string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + 24;
        FontFamily = Ui.Font;
        Foreground = Ui.Text;

        _title.Text = title;
        _barFill.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(Color.FromRgb(0x3D, 0xC7, 0xFF), 0), new GradientStop(Color.FromRgb(0x9B, 0x7B, 0xFF), 0.55), new GradientStop(Color.FromRgb(0xFF, 0x6B, 0xD6), 1) },
        };
        var barGrid = new Grid();
        barGrid.Children.Add(_barTrack);
        _barFill.Margin = _barTrack.Margin;
        barGrid.Children.Add(_barFill);

        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_detail);
        stack.Children.Add(barGrid);

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(16, 14, 16, 16),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(244, 24, 27, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.45 },
            Child = stack,
        };

        SourceInitialized += (_, _) => NativeMethods.MakeNoActivate(this);
        Loaded += (_, _) => Place();
        Closed += (_, _) => { Open.Remove(this); foreach (var t in Open) t.Place(); };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); FadeOut(); };
        Opacity = 0;
    }

    public static ProgressToast Show(string title)
    {
        var t = new ProgressToast(title);
        Open.Add(t);
        t.Show();
        t.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        return t;
    }

    private void Place()
    {
        var wa = SystemParameters.WorkArea;
        int index = Math.Max(0, Open.IndexOf(this));
        Left = wa.Right - Width - 4;
        Top = wa.Bottom - (ActualHeight > 0 ? ActualHeight : 110) * (index + 1) - 4;
    }

    /// <summary>Updates the bar and the "x of y · speed · time left" line.</summary>
    public void Report(long done, long total, string prefix)
    {
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (total <= 0)
        {
            _detail.Text = prefix.TrimEnd(' ', '·');
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        if (_startBytes < 0) { _startBytes = done; _lastBytes = done; _lastSeconds = now; }
        double dt = now - _lastSeconds;
        if (dt >= 0.5)
        {
            double inst = (done - _lastBytes) / dt;
            _rate = _rate <= 0 ? inst : _rate * 0.7 + inst * 0.3;
            _lastBytes = done;
            _lastSeconds = now;
        }

        double fraction = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
        _barFill.Width = Math.Max(4, (PanelWidth - 32) * fraction);
        var detail = $"{prefix}{Ui.Size(done)} of {Ui.Size(total)}";
        if (_rate > 1024)
        {
            detail += $" · {Ui.Size((long)_rate)}/s";
            double left = (total - done) / _rate;
            if (left > 1) detail += left < 60 ? $" · {Math.Ceiling(left)} s left" : $" · {Math.Ceiling(left / 60)} min left";
        }
        _detail.Text = detail;
    }

    /// <summary>Shows a final line and fades out after a moment.</summary>
    public void Finish(string text, bool ok, double seconds = 2.5)
    {
        _detail.Text = text;
        _detail.Foreground = ok ? Ui.Good : Ui.B("#FFB38A");
        if (ok) _barFill.Width = PanelWidth - 32;
        _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
        _hideTimer.Start();
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
