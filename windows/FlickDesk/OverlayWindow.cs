using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FlickDesk;

internal sealed record EffectOptions(
    EffectPreset Preset, EffectPalette Palette, double Intensity, double Speed, bool ReduceMotion, bool ShowCard)
{
    public static EffectOptions From(AppSettings s) => new(
        s.ReduceMotion ? EffectPreset.Quiet : s.Effect,
        EffectPalette.Get(s.Palette),
        Math.Clamp(s.Intensity, 0.2, 1.0),
        s.Speed switch { EffectSpeed.Calm => 1.35, EffectSpeed.Snappy => 0.75, _ => 1.0 },
        s.ReduceMotion,
        s.ShowCard);
}

/// <summary>
/// One reusable transparent, click-through window that plays the arrival / send effects.
///
/// Performance: WPF copies a layered window to the desktop every frame, so the window only
/// covers a band along the phone's edge, is created once and hidden when idle, starts each
/// animation after the first render pass, and every animated element is a cached bitmap –
/// frames are just GPU transforms and opacity.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const double CardWidth = 150;
    private const double CardHeight = 176;

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private int _activeEffects;
    private Edge _edge = Edge.Right;
    private double _glowSize;
    private double _speed = 1;

    /// <summary>True when the compositor overlay draws the light; this window then only carries cards.</summary>
    public bool CardsOnly { get; set; }

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
        SourceInitialized += (_, _) => NativeMethods.MakeClickThrough(this);
    }

    private TimeSpan T(double ms) => TimeSpan.FromMilliseconds(ms * _speed);

    // ---------------------------------------------------------------- lifecycle

    private void Run(Edge edge, double durationMs, Action<Canvas> build)
    {
        if (_activeEffects == 0) Arrange(edge);
        _activeEffects++;

        var layer = new Canvas();
        _canvas.Children.Add(layer);
        if (!IsVisible) Show();

        Dispatcher.InvokeAsync(() =>
        {
            build(layer);
            var timer = new DispatcherTimer { Interval = T(durationMs) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _canvas.Children.Remove(layer);
                if (--_activeEffects == 0) Hide();
            };
            timer.Start();
        }, DispatcherPriority.Background);
    }

    private void Arrange(Edge edge)
    {
        _edge = edge;
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
        _glowSize = Math.Min(sw, sh) * 1.15;
        double band = Math.Min(CardsOnly ? 460 : _glowSize * 0.62, edge == Edge.Bottom ? sh : sw);
        switch (edge)
        {
            case Edge.Left: Left = 0; Top = 0; Width = band; Height = sh; break;
            case Edge.Right: Left = sw - band; Top = 0; Width = band; Height = sh; break;
            default: Left = 0; Top = sh - band; Width = sw; Height = band; break;
        }
    }

    private (Point origin, Vector inward) EdgeAt(double pos)
    {
        pos = Math.Clamp(pos, 0.08, 0.92);
        return _edge switch
        {
            Edge.Left => (new Point(0, pos * Height), new Vector(1, 0)),
            Edge.Right => (new Point(Width, pos * Height), new Vector(-1, 0)),
            _ => (new Point(pos * Width, Height), new Vector(0, -1)),
        };
    }

    private bool Vertical => _edge != Edge.Bottom;

    private Point ClampCenter(Point c) => new(
        Math.Clamp(c.X, CardWidth / 2 + 16, Math.Max(CardWidth / 2 + 16, Width - CardWidth / 2 - 16)),
        Math.Clamp(c.Y, CardHeight / 2 + 16, Math.Max(CardHeight / 2 + 16, Height - CardHeight / 2 - 16)));

    // ---------------------------------------------------------------- public API

    public void PlayArrival(Edge edge, double pos, EffectOptions o, double intensityScale = 1.0)
    {
        _speed = o.Speed;
        Run(edge, 1900, layer => AddLight(layer, pos, 0, o, intensityScale));
    }

    public void DropCard(Edge edge, double pos, string path, ImageSource? thumb, string caption, EffectOptions o, int stackIndex = 0)
    {
        _speed = o.Speed;
        Run(edge, 3800, layer =>
        {
            var (origin, inward) = EdgeAt(pos);
            var along = new Vector(inward.Y, inward.X);
            var end = ClampCenter(origin + inward * (CardWidth * 0.5 + 70) + along * (stackIndex * 12) + inward * (stackIndex * 6));
            var start = o.ReduceMotion ? end : end - inward * (Vector.Multiply(inward, end - origin) + CardWidth);
            double tilt = stackIndex == 0 ? 0 : (stackIndex % 2 == 0 ? 1 : -1) * Math.Min(8, 2.5 * stackIndex);

            var card = BuildCard(path, thumb, caption);
            var (scale, move) = PlaceCard(layer, card, start, tilt);

            var delay = TimeSpan.FromMilliseconds(stackIndex * 90);
            var duration = T(650);
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            Animate(move, TranslateTransform.XProperty, start.X - CardWidth / 2, end.X - CardWidth / 2, duration, ease, delay);
            Animate(move, TranslateTransform.YProperty, start.Y - CardHeight / 2, end.Y - CardHeight / 2, duration, ease, delay);
            double fromScale = o.ReduceMotion ? 0.96 : 0.6;
            Animate(scale, ScaleTransform.ScaleXProperty, fromScale, 1, duration, ease, delay);
            Animate(scale, ScaleTransform.ScaleYProperty, fromScale, 1, duration, ease, delay);

            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(T(o.ReduceMotion ? 300 : 150))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(T(3000))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(3500))));
            card.BeginAnimation(OpacityProperty, fade);
        });
    }

    /// <summary>File leaves for the phone: card gets pulled into the edge, then the edge flashes.</summary>
    public void PlaySend(Edge edge, Point? dropPointOnScreen, string firstPath, ImageSource? thumb, int count, EffectOptions o, bool includeLight = true)
    {
        _speed = o.Speed;
        double pos = 0.5;
        if (dropPointOnScreen is { } sp)
            pos = edge == Edge.Bottom ? sp.X / SystemParameters.PrimaryScreenWidth : sp.Y / SystemParameters.PrimaryScreenHeight;

        Run(edge, 2200, layer =>
        {
            var (origin, inward) = EdgeAt(pos);
            var local = dropPointOnScreen is { } p ? new Point(p.X - Left, p.Y - Top) : origin + inward * (CardWidth * 0.5 + 70);
            var start = ClampCenter(local);
            var end = o.ReduceMotion ? start : origin - inward * (CardWidth * 0.6);

            var card = BuildCard(firstPath, thumb, count > 1 ? $"+{count - 1} more → phone" : "→ phone");
            var (scale, move) = PlaceCard(layer, card, start, 0);
            card.Opacity = 1;

            var duration = T(420);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            Animate(move, TranslateTransform.XProperty, start.X - CardWidth / 2, end.X - CardWidth / 2, duration, ease);
            Animate(move, TranslateTransform.YProperty, start.Y - CardHeight / 2, end.Y - CardHeight / 2, duration, ease);
            Animate(scale, ScaleTransform.ScaleXProperty, 1, 0.45, duration, ease);
            Animate(scale, ScaleTransform.ScaleYProperty, 1, 0.45, duration, ease);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, T(140)) { BeginTime = T(o.ReduceMotion ? 200 : 320) });

            if (includeLight) AddLight(layer, pos, 300, o, 0.7);
        });
    }

    // ---------------------------------------------------------------- the light

    private void AddLight(Canvas layer, double pos, int delayMs, EffectOptions o, double intensityScale)
    {
        layer.Opacity = Math.Clamp(o.Intensity * intensityScale, 0.1, 1);
        var (origin, inward) = EdgeAt(pos);
        var along = new Vector(inward.Y, inward.X);
        double size = _glowSize;
        var p = o.Palette;

        switch (o.Preset)
        {
            case EffectPreset.Surge:
            case EffectPreset.Ripple:
                AddHalo(layer, origin + inward * size * 0.04, size * 0.85, p, 0.5, delayMs);
                AddTexture(layer, EffectTextures.Ring(p, 0.022), origin, size * 1.4, 0.04, 1.0, 0.95, delayMs, 900);
                AddTexture(layer, EffectTextures.Ring(p, 0.035), origin, size * 1.4, 0.04, 1.0, 0.75, delayMs + 160, 900);
                AddTexture(layer, EffectTextures.Ring(p, 0.055), origin, size * 1.4, 0.04, 1.0, 0.55, delayMs + 320, 900);
                // the glassy hairline with a 3 px warm/cool split
                AddTexture(layer, EffectTextures.Hairline(EffectTextures.WarmSplit), origin - along * 3, size * 1.4, 0.04, 1.0, 0.8, delayMs, 900);
                AddTexture(layer, EffectTextures.Hairline(EffectTextures.CoolSplit), origin + along * 3, size * 1.4, 0.04, 1.0, 0.8, delayMs, 900);
                AddCore(layer, origin, size * 0.5, 0.95, delayMs);
                AddEdgeFlash(layer, origin, delayMs);
                break;

            case EffectPreset.Aurora:
            case EffectPreset.Halo:
                AddVeil(layer, origin, inward, size, p, 0, delayMs, 0.8);
                AddVeil(layer, origin, inward, size, p, 0.33, delayMs + 140, 0.65);
                AddVeil(layer, origin, inward, size, p, 0.66, delayMs + 280, 0.5);
                AddCore(layer, origin, size * 0.35, 0.55, delayMs);
                break;

            case EffectPreset.Prism:
                AddShard(layer, origin, inward, size, p, delayMs);
                AddCore(layer, origin, size * 0.3, 0.7, delayMs);
                AddEdgeFlash(layer, origin, delayMs);
                break;

            case EffectPreset.Glass:
                AddTexture(layer, EffectTextures.GlassRim(p), origin, size * 1.1, 0.2, 1.12, 1.0, delayMs, 1100);
                AddTexture(layer, EffectTextures.GlassRim(p), origin, size * 0.8, 0.2, 1.05, 0.55, delayMs + 120, 1100);
                AddCore(layer, origin, size * 0.42, 0.85, delayMs + 60);
                break;

            default: // Quiet / reduce motion: a soft glow that fades in and out without moving much
                AddHalo(layer, origin, size * 0.7, p, 0.45, delayMs, grow: !o.ReduceMotion);
                AddCore(layer, origin, size * 0.38, 0.6, delayMs, grow: !o.ReduceMotion);
                break;
        }
    }

    /// <summary>A cached bitmap centred on the edge point that scales out and fades.</summary>
    private void AddTexture(Canvas layer, BitmapSource tex, Point center, double diameter,
                            double fromScale, double toScale, double peak, int delayMs, double growMs)
    {
        var scale = new ScaleTransform(fromScale, fromScale);
        var img = new Image
        {
            Source = tex,
            Width = diameter,
            Height = diameter,
            Stretch = Stretch.Fill,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
        Canvas.SetLeft(img, center.X - diameter / 2);
        Canvas.SetTop(img, center.Y - diameter / 2);
        layer.Children.Add(img);

        var begin = T(delayMs);
        var grow = new DoubleAnimation(fromScale, toScale, T(growMs))
        {
            BeginTime = begin,
            EasingFunction = new ExponentialEase { Exponent = 4, EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(T(90)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak * 0.6, KeyTime.FromTimeSpan(T(growMs * 0.7))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(growMs * 1.1)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        img.BeginAnimation(OpacityProperty, fade);
    }

    private void AddCore(Canvas layer, Point center, double diameter, double peak, int delayMs, bool grow = true)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(250, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.18));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(45, 220, 236, 255), 0.46));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 220, 236, 255), 1.0));
        brush.Freeze();
        AddBlob(layer, center, diameter, brush, peak, delayMs, grow ? 0.12 : 0.9, 700, 1500);
    }

    private void AddHalo(Canvas layer, Point center, double diameter, EffectPalette p, double peak, int delayMs, bool grow = true)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, p.Halo.R, p.Halo.G, p.Halo.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, p.HaloOuter.R, p.HaloOuter.G, p.HaloOuter.B), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.HaloOuter.R, p.HaloOuter.G, p.HaloOuter.B), 1.0));
        brush.Freeze();
        AddBlob(layer, center, diameter, brush, peak, delayMs, grow ? 0.2 : 0.9, 850, 1700);
    }

    private void AddBlob(Canvas layer, Point center, double diameter, Brush brush, double peak, int delayMs,
                         double fromScale, double growMs, double totalMs)
    {
        var scale = new ScaleTransform(fromScale, fromScale);
        var blob = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            CacheMode = new BitmapCache(0.5),
        };
        Canvas.SetLeft(blob, center.X - diameter / 2);
        Canvas.SetTop(blob, center.Y - diameter / 2);
        layer.Children.Add(blob);

        var begin = T(delayMs);
        var growAnim = new DoubleAnimation(fromScale, 1.0, T(growMs))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, growAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, growAnim);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(T(200)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak * 0.8, KeyTime.FromTimeSpan(T(totalMs * 0.4))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(totalMs)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        blob.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>A bright line right on the screen edge that flashes as the light "enters".</summary>
    private void AddEdgeFlash(Canvas layer, Point origin, int delayMs)
    {
        double length = (Vertical ? Height : Width) * 0.55;
        double thickness = 26;
        var white = Color.FromArgb(235, 255, 255, 255);
        var clear = Color.FromArgb(0, 210, 235, 255);
        var across = _edge switch
        {
            Edge.Left => (new Point(0, 0.5), new Point(1, 0.5)),
            Edge.Right => (new Point(1, 0.5), new Point(0, 0.5)),
            _ => (new Point(0.5, 1), new Point(0.5, 0)),
        };
        var fill = new LinearGradientBrush(white, clear, across.Item1, across.Item2);
        fill.GradientStops.Insert(1, new GradientStop(Color.FromArgb(90, 190, 225, 255), 0.25));
        fill.Freeze();
        var ends = Vertical
            ? new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, 0.35), new(Colors.Black, 0.65), new(Colors.Transparent, 1) }, new Point(0.5, 0), new Point(0.5, 1))
            : new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, 0.35), new(Colors.Black, 0.65), new(Colors.Transparent, 1) }, new Point(0, 0.5), new Point(1, 0.5));
        ends.Freeze();

        var rect = new Rectangle
        {
            Width = Vertical ? thickness : length,
            Height = Vertical ? length : thickness,
            Fill = fill,
            OpacityMask = ends,
            Opacity = 0,
            CacheMode = new BitmapCache(),
        };
        Canvas.SetLeft(rect, _edge switch { Edge.Left => 0, Edge.Right => Width - thickness, _ => origin.X - length / 2 });
        Canvas.SetTop(rect, Vertical ? origin.Y - length / 2 : Height - thickness);
        layer.Children.Add(rect);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = T(delayMs) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(T(70))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(650)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        rect.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Aurora: a soft curtain parallel to the edge that drifts inward and dissolves.</summary>
    private void AddVeil(Canvas layer, Point origin, Vector inward, double size, EffectPalette p, double hueShift, int delayMs, double peak)
    {
        double thickness = size * 0.14;
        double length = (Vertical ? Height : Width) * 0.9;
        var stops = new GradientStopCollection { new(Color.FromArgb(0, 255, 255, 255), 0) };
        for (int i = 0; i <= 4; i++)
        {
            var c = p.Sample(hueShift + i * 0.18);
            stops.Add(new GradientStop(Color.FromArgb(200, c.R, c.G, c.B), 0.15 + i * 0.175));
        }
        stops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        var fill = Vertical
            ? new LinearGradientBrush(stops, new Point(0.5, 0), new Point(0.5, 1))
            : new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
        fill.Freeze();
        var soft = new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, 0.5), new(Colors.Transparent, 1) };
        var mask = Vertical
            ? new LinearGradientBrush(soft, new Point(0, 0.5), new Point(1, 0.5))
            : new LinearGradientBrush(soft, new Point(0.5, 0), new Point(0.5, 1));
        mask.Freeze();

        var move = new TranslateTransform();
        var veil = new Rectangle
        {
            Width = Vertical ? thickness : length,
            Height = Vertical ? length : thickness,
            Fill = fill,
            OpacityMask = mask,
            Opacity = 0,
            RenderTransform = move,
            CacheMode = new BitmapCache(0.5),
        };
        Canvas.SetLeft(veil, Vertical ? origin.X - thickness / 2 : (Width - length) / 2);
        Canvas.SetTop(veil, Vertical ? (Height - length) / 2 : origin.Y - thickness / 2);
        layer.Children.Add(veil);

        var begin = T(delayMs);
        double travel = size * 0.42;
        var drift = new DoubleAnimation(0, (Vertical ? inward.X : inward.Y) * travel, T(1500))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        move.BeginAnimation(Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, drift);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(T(300)), new SineEase { EasingMode = EasingMode.EaseOut }));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(1500)), new SineEase { EasingMode = EasingMode.EaseIn }));
        veil.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Prism: one clean, slanted band of spectrum sweeping in from the edge.</summary>
    private void AddShard(Canvas layer, Point origin, Vector inward, double size, EffectPalette p, int delayMs)
    {
        double thickness = size * 0.11;
        double length = (Vertical ? Height : Width) * 1.3;
        var stops = new GradientStopCollection { new(Color.FromArgb(0, 255, 255, 255), 0) };
        for (int i = 0; i < p.Ring.Length; i++)
        {
            var c = p.Ring[i];
            stops.Add(new GradientStop(Color.FromArgb(230, c.R, c.G, c.B), 0.12 + 0.76 * i / Math.Max(1, p.Ring.Length - 1)));
        }
        stops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        var fill = Vertical
            ? new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5))
            : new LinearGradientBrush(stops, new Point(0.5, 0), new Point(0.5, 1));
        fill.Freeze();
        var ends = Vertical
            ? new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, 0.3), new(Colors.Black, 0.7), new(Colors.Transparent, 1) }, new Point(0.5, 0), new Point(0.5, 1))
            : new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, 0.3), new(Colors.Black, 0.7), new(Colors.Transparent, 1) }, new Point(0, 0.5), new Point(1, 0.5));
        ends.Freeze();

        var move = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(Vertical ? new SkewTransform(-16, 0) : new SkewTransform(0, -16));
        group.Children.Add(move);
        var shard = new Rectangle
        {
            Width = Vertical ? thickness : length,
            Height = Vertical ? length : thickness,
            Fill = fill,
            OpacityMask = ends,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group,
            CacheMode = new BitmapCache(0.75),
        };
        Canvas.SetLeft(shard, Vertical ? origin.X - thickness / 2 : (Width - length) / 2);
        Canvas.SetTop(shard, Vertical ? (Height - length) / 2 : origin.Y - thickness / 2);
        layer.Children.Add(shard);

        var begin = T(delayMs);
        double travel = (Vertical ? Width : Height) * 0.95;
        var sweep = new DoubleAnimation(0, (Vertical ? inward.X : inward.Y) * travel, T(950))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        move.BeginAnimation(Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, sweep);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(T(80))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(T(600))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(T(1000)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        shard.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- the file card

    private static void Animate(Animatable target, DependencyProperty prop, double from, double to,
                                TimeSpan duration, IEasingFunction ease, TimeSpan? begin = null) =>
        target.BeginAnimation(prop, new DoubleAnimation(from, to, duration) { EasingFunction = ease, BeginTime = begin ?? TimeSpan.Zero });

    private static (ScaleTransform scale, TranslateTransform move) PlaceCard(Canvas layer, FrameworkElement card, Point center, double tilt)
    {
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform(center.X - CardWidth / 2, center.Y - CardHeight / 2);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(new RotateTransform(tilt));
        group.Children.Add(move);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = group;
        card.Opacity = 0;
        Canvas.SetLeft(card, 0);
        Canvas.SetTop(card, 0);
        layer.Children.Add(card);
        return (scale, move);
    }

    private static FrameworkElement BuildCard(string path, ImageSource? thumb, string caption)
    {
        var picture = new Border
        {
            Width = 132,
            Height = 112,
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Child = new Image
            {
                Source = thumb,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var stack = new StackPanel();
        stack.Children.Add(picture);
        stack.Children.Add(new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            Foreground = Brushes.White,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = new SolidColorBrush(Color.FromArgb(175, 255, 255, 255)),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });

        return new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(225, 22, 25, 33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.45 },
            CacheMode = new BitmapCache(),
            Child = stack,
        };
    }
}

internal static class Thumbnails
{
    /// <summary>Decodes off the UI thread so starting an animation never waits on disk I/O.</summary>
    public static Task<ImageSource?> LoadAsync(string path) => Task.Run(() => Load(path));

    public static ImageSource? Load(string path)
    {
        if (Protocol.LooksLikeImage(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 264;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception) { /* e.g. HEIC without the codec – fall back to the shell icon */ }
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; }
    }
}
