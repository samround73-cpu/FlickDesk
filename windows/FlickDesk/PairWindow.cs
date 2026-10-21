using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using QRCoder;

namespace FlickDesk;

/// <summary>
/// "Pair a phone": a QR code (rotating every 60 s, single-use) beside the steps. When a phone
/// scans it the right-hand side switches to the 6-digit check number, then "Paired".
/// </summary>
internal sealed class PairWindow : Window
{
    private const double QrSize = 296;

    private readonly PairingService _pairing;
    private readonly Grid _qrHost = new() { Width = QrSize, Height = QrSize };
    private readonly TextBlock _countdown = Ui.T("", 12.5, brush: Ui.Muted);
    private readonly ContentControl _status = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private QrSession? _session;
    private bool _busy; // a phone is mid-pairing: don't rotate the code

    public PairWindow(PairingService pairing)
    {
        _pairing = pairing;
        Title = "Pair a phone — FlickDesk";
        Width = 880;
        Height = 600;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.StyleWindow(this);

        Content = Build();
        NewCode();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _pairing.PairStarted += OnPairStarted;
        _pairing.Paired += OnPaired;
        _pairing.PairFailed += OnPairFailed;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _pairing.PairStarted -= OnPairStarted;
            _pairing.Paired -= OnPaired;
            _pairing.PairFailed -= OnPairFailed;
            _pairing.EndQrSession();
        };
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(48, 36, 48, 36) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- left: QR with a slow spectral halo
        var qrColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var qrStage = new Grid { Width = 340, Height = 340 };

        var halo = new Image
        {
            Source = EffectTextures.Ring(EffectPalette.Get("Spectrum"), 0.055),
            Width = 470, Height = 470,
            Margin = new Thickness(-65),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = 0.85,
        };
        var spin = new RotateTransform();
        halo.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever });
        qrStage.Children.Add(halo);

        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22),
            Child = _qrHost,
        };
        AutomationProperties.SetName(card, "QR code to pair your phone");
        qrStage.Children.Add(card);
        qrColumn.Children.Add(qrStage);

        var countdownRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        countdownRow.Children.Add(_countdown);
        qrColumn.Children.Add(countdownRow);
        root.Children.Add(qrColumn);

        // ---- right: steps + live status
        var right = new StackPanel { Margin = new Thickness(56, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        right.Children.Add(Ui.T("Scan to pair", 26, FontWeights.SemiBold));
        right.Children.Add(Ui.T("No typing, no hunting for IP addresses. The code carries a one-time key, so everything you flick between these two is encrypted.",
                                14, brush: Ui.Muted, margin: new Thickness(0, 8, 0, 22)));
        right.Children.Add(Step(1, "Open Flick on your phone and tap + next to your devices."));
        right.Children.Add(Step(2, "Point the camera at this code."));
        right.Children.Add(Step(3, "Check the number matches on both screens."));

        _status.Margin = new Thickness(0, 20, 0, 0);
        right.Children.Add(_status);
        ShowWaiting();

        var hosts = PairingService.LocalAddresses().ToList();
        var fallback = new StackPanel();
        fallback.Children.Add(Ui.T("Can’t scan?", 13, FontWeights.SemiBold));
        fallback.Children.Add(Ui.T(hosts.Count > 0
                ? $"In Flick, tap this PC under “Nearby”, or choose Enter IP and type {hosts[0]}. You’ll confirm the number here instead."
                : "In Flick, tap this PC under “Nearby”. You’ll confirm the number here instead.",
            13, brush: Ui.Muted, margin: new Thickness(0, 6, 0, 0)));
        var fallbackCard = Ui.Card(fallback);
        fallbackCard.Margin = new Thickness(0, 22, 0, 0);
        right.Children.Add(fallbackCard);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Ui.Button("New code", NewCode));
        var close = Ui.Button("Close", Close, "GhostButton");
        close.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(close);
        right.Children.Add(buttons);

        root.Children.Add(right);
        return root;
    }

    private static UIElement Step(int n, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = Ui.B("#1C2230"),
            Child = new TextBlock { Text = n.ToString(), Foreground = Ui.B("#9FD3FF"), FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(Ui.T(text, 14, margin: new Thickness(0, 4, 0, 0)));
        return row;
    }

    // ---------------------------------------------------------------- QR

    private void NewCode()
    {
        _busy = false;
        _session = _pairing.NewQrSession();
        DrawQr(_pairing.QrText(_session));
        ShowWaiting();
        Tick();
    }

    private void Tick()
    {
        if (_session is null || _busy) return;
        var left = _session.ExpiresUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            NewCode();
            return;
        }
        _countdown.Text = $"New code in {left:m\\:ss}";
    }

    private void DrawQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.H);
        var matrix = data.ModuleMatrix;
        const int quiet = 4; // QRCoder includes a 4-module quiet zone
        int n = matrix.Count - 2 * quiet;
        double cell = QrSize / n;

        var modules = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (!matrix[r + quiet][c + quiet]) continue;
                modules.Children.Add(new RectangleGeometry(
                    new Rect(c * cell + cell * 0.06, r * cell + cell * 0.06, cell * 0.88, cell * 0.88),
                    cell * 0.28, cell * 0.28));
            }
        }
        modules.Freeze();

        _qrHost.Children.Clear();
        _qrHost.Children.Add(new Path { Data = modules, Fill = Ui.B("#0B0F1A") });

        // Centre mark – ECC level H tolerates covering it.
        var orb = new Ellipse
        {
            Width = 26, Height = 26,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Colors.White, 0), new(Color.FromRgb(159, 211, 255), 0.4),
                new(Color.FromArgb(50, 90, 169, 255), 0.75), new(Color.FromArgb(0, 90, 169, 255), 1),
            }),
        };
        var mark = new Border
        {
            Width = 62, Height = 62, CornerRadius = new CornerRadius(16), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Background = Ui.B("#0B0F1A"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = orb,
            },
        };
        _qrHost.Children.Add(mark);
        _qrHost.Opacity = 1;
    }

    // ---------------------------------------------------------------- status

    private void ShowWaiting()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = Ui.Dot(Ui.Accent);
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        });
        p.Children.Add(dot);
        p.Children.Add(Ui.T("Waiting for your phone…", 14, brush: Ui.Muted, margin: new Thickness(10, 0, 0, 0)));
        _status.Content = p;
    }

    private void OnPairStarted(string phone, string check) => Dispatcher.InvokeAsync(() =>
    {
        _busy = true;
        _qrHost.Opacity = 0.25;
        _countdown.Text = "Code used";
        var p = new StackPanel();
        p.Children.Add(Ui.T($"{phone} scanned the code. Check it shows:", 14));
        var digits = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 8) };
        foreach (var ch in check)
        {
            if (ch == ' ') { digits.Children.Add(new Border { Width = 14 }); continue; }
            digits.Children.Add(new Border
            {
                Width = 44, Height = 56, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(10),
                Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = ch.ToString(), FontFamily = Ui.Mono, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        p.Children.Add(digits);
        p.Children.Add(Ui.T("Then tap “Yes, pair” on the phone.", 13, brush: Ui.Muted));
        _status.Content = p;
    });

    private void OnPaired(TrustedDevice device) => Dispatcher.InvokeAsync(() =>
    {
        _busy = true;
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Ui.Icon("M5,12.5 L10,17 L19,7", 22, Ui.Good, 2.4));
        p.Children.Add(Ui.T($"Paired with {device.Name}", 18, FontWeights.SemiBold, margin: new Thickness(10, 0, 0, 0)));
        _status.Content = p;
        var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        close.Tick += (_, _) => { close.Stop(); Close(); };
        close.Start();
    });

    private void OnPairFailed(string phone) => Dispatcher.InvokeAsync(() =>
    {
        _status.Content = Ui.T($"Pairing with {phone} was cancelled. Make a new code to try again.", 14, brush: Ui.B("#FF8A80"));
        _busy = false;
    });
}
