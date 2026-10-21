using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using MColor = System.Windows.Media.Color;
using WColor = Windows.UI.Color;

namespace FlickDesk;

/// <summary>
/// The full-screen light, drawn by the Windows compositor (Windows.UI.Composition) into a
/// click-through, redirection-free window. Everything runs on the GPU at the display's refresh
/// rate, independent of our UI thread, so it can cover the whole screen and stay smooth.
///
/// Layers (Spectral Surge): a brief radial dim, the screen's frame igniting from the phone's
/// edge and running round, a bright "glass" wave crossing the screen, two spectral rings riding
/// it, a crisp warm/cool caustic hairline, and a flare (core + anamorphic streak) at the entry.
/// No bitmaps: soft rings are radial gradients; colour comes from gradient brushes masked by them.
/// </summary>
internal sealed class CompositionOverlay
{
    private readonly object _queueController;
    private readonly IntPtr _hwnd;
    private readonly Compositor _c;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private int _active;
    private float _w, _h;
    private float _speed = 1;

    public static CompositionOverlay? TryCreate()
    {
        try { return new CompositionOverlay(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlickDesk] compositor overlay unavailable, using WPF fallback: {ex}");
            return null;
        }
    }

    private CompositionOverlay()
    {
        _queueController = Native.EnsureDispatcherQueue();
        _hwnd = Native.CreateOverlayWindow();
        _c = new Compositor();
        var interop = _c.As<Native.ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(_hwnd, true, out var raw);
        _target = DesktopWindowTarget.FromAbi(raw);
        _root = _c.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _root;
    }

    // ================================================================ public API

    public void PlayArrival(Edge edge, double pos, EffectOptions o, double intensityScale = 1.0) =>
        Run(o, intensityScale, layer => BuildArrival(layer, Origin(edge, pos), o));

    public void PlaySend(Edge edge, double pos, EffectOptions o) =>
        Run(o, 0.9, layer => BuildConverge(layer, Origin(edge, pos), o));

    // ================================================================ lifecycle

    private void Run(EffectOptions o, double intensityScale, Action<ContainerVisual> build)
    {
        if (_active == 0) FitToScreen();
        _active++;
        _speed = (float)o.Speed;

        var layer = _c.CreateContainerVisual();
        layer.Size = new Vector2(_w, _h);
        layer.Opacity = (float)Math.Clamp(o.Intensity * intensityScale, 0.1, 1.0);
        _root.Children.InsertAtTop(layer);
        Native.ShowNoActivate(_hwnd);

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        build(layer);
        batch.End();
        batch.Completed += (_, _) =>
        {
            _root.Children.Remove(layer);
            layer.Dispose();
            if (--_active == 0) Native.Hide(_hwnd);
        };
    }

    private void FitToScreen()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds; // physical pixels (per-monitor DPI aware)
        _w = b.Width;
        _h = b.Height;
        Native.Place(_hwnd, b.X, b.Y, b.Width, b.Height);
    }

    private readonly record struct Entry(Vector2 Point, Vector2 Inward);

    private Entry Origin(Edge edge, double pos)
    {
        float p = (float)Math.Clamp(pos, 0.06, 0.94);
        return edge switch
        {
            Edge.Left => new Entry(new Vector2(0, p * _h), new Vector2(1, 0)),
            Edge.Right => new Entry(new Vector2(_w, p * _h), new Vector2(-1, 0)),
            _ => new Entry(new Vector2(p * _w, _h), new Vector2(0, -1)),
        };
    }

    private TimeSpan T(double ms) => TimeSpan.FromMilliseconds(ms * _speed);

    // ================================================================ compositions

    private void BuildArrival(ContainerVisual layer, Entry o, EffectOptions opt)
    {
        var p = opt.Palette;
        float diag = MathF.Sqrt(_w * _w + _h * _h);
        float minDim = MathF.Min(_w, _h);

        switch (opt.Preset)
        {
            case EffectPreset.Surge:
                Dim(layer, o, 0);
                Frame(layer, o, p, minDim * 0.13f, 0, holdMs: 520, reverse: false);
                Wave(layer, o, diag, 30);
                Ring(layer, o, p, diag, 40, soft: false);
                Ring(layer, o, p, diag, 180, soft: true);
                Hairlines(layer, o, diag, 40);
                Flare(layer, o, minDim, 0);
                break;

            case EffectPreset.Ripple:
            case EffectPreset.Prism:
                Wave(layer, o, diag * 0.8f, 0);
                Ring(layer, o, p, diag * 0.8f, 0, soft: false);
                Ring(layer, o, p, diag * 0.8f, 150, soft: true);
                Ring(layer, o, p, diag * 0.8f, 300, soft: true);
                Hairlines(layer, o, diag * 0.8f, 0);
                Flare(layer, o, minDim * 0.85f, 0);
                break;

            case EffectPreset.Halo:
            case EffectPreset.Aurora:
                Dim(layer, o, 0, 0.6f);
                Frame(layer, o, p, minDim * 0.17f, 0, holdMs: 1100, reverse: false);
                Flare(layer, o, minDim * 0.7f, 60);
                break;

            case EffectPreset.Glass:
                Dim(layer, o, 0, 0.7f);
                GlassRing(layer, o, p, diag * 0.9f, 0);
                GlassRing(layer, o, p, diag * 0.6f, 140);
                Hairlines(layer, o, diag * 0.9f, 0);
                Flare(layer, o, minDim * 0.8f, 40);
                break;

            default: // Quiet / reduce motion
                Frame(layer, o, p, minDim * 0.09f, 0, holdMs: 400, reverse: false, onlyEntryEdge: true);
                Flare(layer, o, minDim * 0.6f, 0, streak: false, grow: !opt.ReduceMotion);
                break;
        }
    }

    /// <summary>Sending to the phone: the screen's light collapses into the edge, then flashes.</summary>
    private void BuildConverge(ContainerVisual layer, Entry o, EffectOptions opt)
    {
        var p = opt.Palette;
        float diag = MathF.Sqrt(_w * _w + _h * _h);
        float minDim = MathF.Min(_w, _h);
        if (opt.Preset == EffectPreset.Quiet || opt.ReduceMotion)
        {
            Flare(layer, o, minDim * 0.6f, 200, streak: false, grow: false);
            return;
        }
        Frame(layer, o, p, minDim * 0.12f, 0, holdMs: 0, reverse: true);
        RingIn(layer, o, p, diag, 0, soft: false);
        RingIn(layer, o, p, diag, 110, soft: true);
        Flare(layer, o, minDim, 560);
    }

    // ================================================================ layers

    private void Dim(ContainerVisual layer, Entry o, int delayMs, float strength = 1f)
    {
        var brush = _c.CreateRadialGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        brush.EllipseCenter = o.Point;
        brush.EllipseRadius = new Vector2(_w * 1.15f, _h * 1.15f);
        brush.ColorStops.Add(_c.CreateColorGradientStop(0f, Argb(0.34f * strength, 4, 6, 12)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.45f, Argb(0.12f * strength, 4, 6, 12)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.85f, Argb(0, 4, 6, 12)));
        var sprite = Sprite(layer, brush, Vector2.Zero, new Vector2(_w, _h));
        Fade(sprite, delayMs, new[] { (0f, 0f), (0.08f, 1f), (0.45f, 0.7f), (1f, 0f) }, 1500);
    }

    /// <summary>
    /// The screen's frame glows in from the entry point and runs round the edges.
    /// Each edge reveals outward from the point nearest the origin, delayed by distance.
    /// </summary>
    private void Frame(ContainerVisual layer, Entry o, EffectPalette p, float thickness, int delayMs, int holdMs, bool reverse, bool onlyEntryEdge = false)
    {
        float perimeter = 2 * (_w + _h);
        var edges = new[]
        {
            // name, offset, size, along-axis vertical?, bright side ("near" = 0 or 1 across), perimeter start, perimeter direction
            (Edge: "top", Pos: new Vector2(0, 0), Size: new Vector2(_w, thickness), Vertical: false, BrightAtStart: true),
            (Edge: "right", Pos: new Vector2(_w - thickness, 0), Size: new Vector2(thickness, _h), Vertical: true, BrightAtStart: false),
            (Edge: "bottom", Pos: new Vector2(0, _h - thickness), Size: new Vector2(_w, thickness), Vertical: false, BrightAtStart: false),
            (Edge: "left", Pos: new Vector2(0, 0), Size: new Vector2(thickness, _h), Vertical: true, BrightAtStart: true),
        };

        foreach (var e in edges)
        {
            float length = e.Vertical ? _h : _w;
            // the point on this edge closest to the origin, in the edge's own along-axis coordinate
            float entry = e.Vertical ? Math.Clamp(o.Point.Y, 0, _h) : Math.Clamp(o.Point.X, 0, _w);
            var entryPoint = e.Edge switch
            {
                "top" => new Vector2(entry, 0),
                "bottom" => new Vector2(entry, _h),
                "left" => new Vector2(0, entry),
                _ => new Vector2(_w, entry),
            };
            float distance = Vector2.Distance(entryPoint, o.Point);
            bool isEntryEdge = distance < 2f;
            if (onlyEntryEdge && !isEntryEdge) continue;

            // colour runs continuously round the perimeter (clockwise from top-left)
            float startS = e.Edge switch { "top" => 0, "right" => _w, "bottom" => _w + _h, _ => 2 * _w + _h };
            bool forward = e.Edge is "top" or "right"; // along-axis increases clockwise on top/right
            var source = _c.CreateLinearGradientBrush();
            source.StartPoint = e.Vertical ? new Vector2(0, 0) : new Vector2(0, 0);
            source.EndPoint = e.Vertical ? new Vector2(0, 1) : new Vector2(1, 0);
            const int stops = 7;
            for (int i = 0; i < stops; i++)
            {
                float u = i / (float)(stops - 1);
                float s = startS + (forward ? u : 1 - u) * length;
                var c = p.Sample(s / perimeter * 1.5);
                source.ColorStops.Add(_c.CreateColorGradientStop(u, Argb(1f, c.R, c.G, c.B)));
            }

            // alpha falls off from the screen edge inward
            var mask = _c.CreateLinearGradientBrush();
            Vector2 a, b;
            if (e.Vertical) { a = new Vector2(e.BrightAtStart ? 0 : 1, 0.5f); b = new Vector2(e.BrightAtStart ? 1 : 0, 0.5f); }
            else { a = new Vector2(0.5f, e.BrightAtStart ? 0 : 1); b = new Vector2(0.5f, e.BrightAtStart ? 1 : 0); }
            mask.StartPoint = a;
            mask.EndPoint = b;
            mask.ColorStops.Add(_c.CreateColorGradientStop(0f, Argb(1f, 255, 255, 255)));
            mask.ColorStops.Add(_c.CreateColorGradientStop(0.08f, Argb(0.85f, 255, 255, 255)));
            mask.ColorStops.Add(_c.CreateColorGradientStop(0.35f, Argb(0.35f, 255, 255, 255)));
            mask.ColorStops.Add(_c.CreateColorGradientStop(1f, Argb(0f, 255, 255, 255)));

            var brush = _c.CreateMaskBrush();
            brush.Source = source;
            brush.Mask = mask;
            var sprite = Sprite(layer, brush, e.Pos, e.Size);

            // reveal outward from the entry point (or retract toward it when sending)
            var clip = _c.CreateInsetClip();
            sprite.Clip = clip;
            string startProp = e.Vertical ? "TopInset" : "LeftInset";
            string endProp = e.Vertical ? "BottomInset" : "RightInset";
            int delay = delayMs + (int)(distance / (_w + _h) * 420);
            float revealMs = 520 + length / (_w + _h) * 380;
            var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 0.8f), new Vector2(0.2f, 1f));

            if (!reverse)
            {
                clip.StartAnimation(startProp, Scalar(delay, revealMs, (0f, entry), (1f, 0f), ease));
                clip.StartAnimation(endProp, Scalar(delay, revealMs, (0f, length - entry), (1f, 0f), ease));
                float total = revealMs + holdMs + 900;
                Fade(sprite, delay, new[] { (0f, 0f), (60 / total, 1f), ((revealMs + holdMs) / total, 0.9f), (1f, 0f) }, total);
            }
            else
            {
                // send: light drains back into the entry point
                int rdelay = (int)((1 - distance / (_w + _h)) * 260);
                var easeIn = _c.CreateCubicBezierEasingFunction(new Vector2(0.6f, 0f), new Vector2(0.4f, 1f));
                clip.StartAnimation(startProp, Scalar(rdelay, 620, (0f, 0f), (1f, entry), easeIn));
                clip.StartAnimation(endProp, Scalar(rdelay, 620, (0f, 0f), (1f, length - entry), easeIn));
                Fade(sprite, rdelay, new[] { (0f, 0f), (0.15f, 1f), (0.85f, 1f), (1f, 0f) }, 660);
            }
        }
    }

    /// <summary>A bright, wide, almost-white band – the "glass" wave front.</summary>
    private void Wave(ContainerVisual layer, Entry o, float reach, int delayMs)
    {
        float d = reach * 2.15f;
        var brush = Ring(Argb(0f, 255, 255, 255), Argb(0.42f, 235, 245, 255), 0.80f, 0.87f, 0.885f, 0.93f);
        var sprite = Centered(layer, brush, o.Point, d);
        Expand(sprite, delayMs, 1250, new[] { (0f, 0f), (0.04f, 1f), (0.55f, 0.6f), (1f, 0f) });
    }

    /// <summary>A spectral ring: palette gradient masked by a radial ring.</summary>
    private void Ring(ContainerVisual layer, Entry o, EffectPalette p, float reach, int delayMs, bool soft)
    {
        float d = reach * 2.15f;
        var sprite = Centered(layer, SpectralRingBrush(p, soft), o.Point, d);
        Expand(sprite, delayMs, 1250, soft
            ? new[] { (0f, 0f), (0.05f, 0.8f), (0.5f, 0.45f), (1f, 0f) }
            : new[] { (0f, 0f), (0.03f, 1f), (0.5f, 0.7f), (1f, 0f) });
    }

    private void RingIn(ContainerVisual layer, Entry o, EffectPalette p, float reach, int delayMs, bool soft)
    {
        float d = reach * 2.15f;
        var sprite = Centered(layer, SpectralRingBrush(p, soft), o.Point, d);
        var easeIn = _c.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(0.84f, 0f));
        sprite.Scale = new Vector3(1, 1, 1);
        var scale = _c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scale.InsertKeyFrame(1f, new Vector3(0.02f, 0.02f, 1f), easeIn);
        Time(scale, delayMs, 620);
        sprite.StartAnimation("Scale", scale);
        Fade(sprite, delayMs, new[] { (0f, 0f), (0.25f, soft ? 0.6f : 0.95f), (0.92f, 1f), (1f, 0f) }, 640);
    }

    private CompositionBrush SpectralRingBrush(EffectPalette p, bool soft)
    {
        var source = _c.CreateLinearGradientBrush();
        source.StartPoint = new Vector2(0, 0);
        source.EndPoint = new Vector2(1, 1);
        for (int i = 0; i < p.Ring.Length; i++)
        {
            var c = p.Ring[i];
            source.ColorStops.Add(_c.CreateColorGradientStop(i / (float)Math.Max(1, p.Ring.Length - 1), Argb(1f, c.R, c.G, c.B)));
        }
        var mask = soft
            ? Ring(Argb(0, 255, 255, 255), Argb(0.75f, 255, 255, 255), 0.80f, 0.875f, 0.89f, 0.96f)
            : Ring(Argb(0, 255, 255, 255), Argb(1f, 255, 255, 255), 0.865f, 0.888f, 0.893f, 0.915f);
        var brush = _c.CreateMaskBrush();
        brush.Source = source;
        brush.Mask = mask;
        return brush;
    }

    /// <summary>The crisp caustic: a white hairline flanked by warm and cool hairlines a few pixels apart.</summary>
    private void Hairlines(ContainerVisual layer, Entry o, float reach, int delayMs)
    {
        float d = reach * 2.15f;
        var along = new Vector2(o.Inward.Y, o.Inward.X);
        var specs = new[]
        {
            (Offset: Vector2.Zero, Color: Argb(0.95f, 255, 255, 255)),
            (Offset: along * 3.5f, Color: Argb(0.75f, 255, 150, 170)),
            (Offset: -along * 3.5f, Color: Argb(0.75f, 140, 220, 255)),
        };
        foreach (var s in specs)
        {
            var brush = Ring(WithAlpha(s.Color, 0), s.Color, 0.8955f, 0.8975f, 0.8985f, 0.9005f);
            var sprite = Centered(layer, brush, o.Point + s.Offset, d);
            Expand(sprite, delayMs, 1250, new[] { (0f, 0f), (0.03f, 1f), (0.45f, 0.7f), (0.85f, 0f), (1f, 0f) });
        }
    }

    private void GlassRing(ContainerVisual layer, Entry o, EffectPalette p, float reach, int delayMs)
    {
        float d = reach * 2.15f;
        var tint = p.Sample(0.55);
        var brush = _c.CreateRadialGradientBrush();
        brush.ColorStops.Add(_c.CreateColorGradientStop(0f, Argb(0, 255, 255, 255)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.70f, Argb(0.05f, 220, 235, 255)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.86f, Argb(0.35f, tint.R, tint.G, tint.B)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.89f, Argb(0.95f, 240, 248, 255)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.905f, Argb(0.5f, 200, 230, 255)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(0.95f, Argb(0, 255, 255, 255)));
        var sprite = Centered(layer, brush, o.Point, d);
        Expand(sprite, delayMs, 1400, new[] { (0f, 0f), (0.06f, 1f), (0.6f, 0.7f), (1f, 0f) });
    }

    /// <summary>The flare at the entry point: a white core plus a wide anamorphic streak along the edge.</summary>
    private void Flare(ContainerVisual layer, Entry o, float size, int delayMs, bool streak = true, bool grow = true)
    {
        var core = _c.CreateRadialGradientBrush();
        core.ColorStops.Add(_c.CreateColorGradientStop(0f, Argb(1f, 255, 255, 255)));
        core.ColorStops.Add(_c.CreateColorGradientStop(0.16f, Argb(0.75f, 255, 255, 255)));
        core.ColorStops.Add(_c.CreateColorGradientStop(0.45f, Argb(0.2f, 200, 228, 255)));
        core.ColorStops.Add(_c.CreateColorGradientStop(1f, Argb(0f, 200, 228, 255)));
        var coreSprite = Centered(layer, core, o.Point, size * 0.6f);
        if (grow)
        {
            var scale = _c.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new Vector3(0.1f, 0.1f, 1));
            scale.InsertKeyFrame(0.45f, new Vector3(1f, 1f, 1), Ease());
            scale.InsertKeyFrame(1f, new Vector3(1.1f, 1.1f, 1));
            Time(scale, delayMs, 900);
            coreSprite.StartAnimation("Scale", scale);
        }
        Fade(coreSprite, delayMs, new[] { (0f, 0f), (0.08f, 1f), (0.4f, 0.9f), (1f, 0f) }, 1000);

        if (!streak) return;
        // the streak runs along the edge (perpendicular to the inward direction)
        bool vertical = MathF.Abs(o.Inward.X) > 0.5f;
        var size2 = vertical ? new Vector2(30, _h * 0.9f) : new Vector2(_w * 0.9f, 30);
        var s = _c.CreateRadialGradientBrush();
        s.ColorStops.Add(_c.CreateColorGradientStop(0f, Argb(0.95f, 255, 255, 255)));
        s.ColorStops.Add(_c.CreateColorGradientStop(0.4f, Argb(0.5f, 160, 215, 255)));
        s.ColorStops.Add(_c.CreateColorGradientStop(1f, Argb(0f, 120, 180, 255)));
        var streakSprite = Sprite(layer, s, o.Point - size2 / 2 + o.Inward * 18, size2);
        streakSprite.CenterPoint = new Vector3(size2 / 2, 0);
        var sx = _c.CreateVector3KeyFrameAnimation();
        sx.InsertKeyFrame(0f, vertical ? new Vector3(1, 0.1f, 1) : new Vector3(0.1f, 1, 1));
        sx.InsertKeyFrame(0.5f, new Vector3(1, 1, 1), Ease());
        sx.InsertKeyFrame(1f, vertical ? new Vector3(1, 1.15f, 1) : new Vector3(1.15f, 1, 1));
        Time(sx, delayMs, 800);
        streakSprite.StartAnimation("Scale", sx);
        Fade(streakSprite, delayMs, new[] { (0f, 0f), (0.08f, 1f), (0.45f, 0.8f), (1f, 0f) }, 800);
    }

    // ================================================================ helpers

    private CompositionRadialGradientBrush Ring(WColor clear, WColor solid, float a, float b, float c, float d)
    {
        var brush = _c.CreateRadialGradientBrush();
        brush.ColorStops.Add(_c.CreateColorGradientStop(0f, clear));
        brush.ColorStops.Add(_c.CreateColorGradientStop(a, clear));
        brush.ColorStops.Add(_c.CreateColorGradientStop(b, solid));
        brush.ColorStops.Add(_c.CreateColorGradientStop(c, solid));
        brush.ColorStops.Add(_c.CreateColorGradientStop(d, clear));
        brush.ColorStops.Add(_c.CreateColorGradientStop(1f, clear));
        return brush;
    }

    private SpriteVisual Sprite(ContainerVisual layer, CompositionBrush brush, Vector2 offset, Vector2 size)
    {
        var s = _c.CreateSpriteVisual();
        s.Brush = brush;
        s.Offset = new Vector3(offset, 0);
        s.Size = size;
        s.Opacity = 0;
        layer.Children.InsertAtTop(s);
        return s;
    }

    private SpriteVisual Centered(ContainerVisual layer, CompositionBrush brush, Vector2 center, float diameter)
    {
        var s = Sprite(layer, brush, center - new Vector2(diameter / 2), new Vector2(diameter));
        s.CenterPoint = new Vector3(diameter / 2, diameter / 2, 0);
        return s;
    }

    private void Expand(SpriteVisual s, int delayMs, double durationMs, (float, float)[] opacity)
    {
        s.Scale = new Vector3(0.02f, 0.02f, 1);
        var scale = _c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.02f, 0.02f, 1));
        scale.InsertKeyFrame(1f, new Vector3(1f, 1f, 1), _c.CreateCubicBezierEasingFunction(new Vector2(0.16f, 0.7f), new Vector2(0.24f, 1f)));
        Time(scale, delayMs, durationMs);
        s.StartAnimation("Scale", scale);
        Fade(s, delayMs, opacity, durationMs);
    }

    private void Fade(Visual v, int delayMs, (float t, float value)[] keys, double durationMs)
    {
        var a = _c.CreateScalarKeyFrameAnimation();
        foreach (var (t, value) in keys) a.InsertKeyFrame(Math.Clamp(t, 0f, 1f), value);
        Time(a, delayMs, durationMs);
        v.StartAnimation("Opacity", a);
    }

    private ScalarKeyFrameAnimation Scalar(int delayMs, double durationMs, (float t, float v) from, (float t, float v) to, CompositionEasingFunction ease)
    {
        var a = _c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(from.t, from.v);
        a.InsertKeyFrame(to.t, to.v, ease);
        Time(a, delayMs, durationMs);
        return a;
    }

    private void Time(KeyFrameAnimation a, int delayMs, double durationMs)
    {
        a.Duration = T(Math.Max(16, durationMs));
        a.DelayTime = T(delayMs);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }

    private CompositionEasingFunction Ease() =>
        _c.CreateCubicBezierEasingFunction(new Vector2(0.16f, 0.84f), new Vector2(0.24f, 1f));

    private static WColor Argb(float a, byte r, byte g, byte b) =>
        WColor.FromArgb((byte)Math.Clamp(a * 255, 0, 255), r, g, b);

    private static WColor WithAlpha(WColor c, byte a) => WColor.FromArgb(a, c.R, c.G, c.B);

    // ================================================================ Win32 / WinRT plumbing

    internal static class Native
    {
        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICompositorDesktopInterop
        {
            void CreateDesktopWindowTarget(IntPtr hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr result);
            void EnsureOnThread(uint threadId);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public int threadType;    // DQTYPE_THREAD_CURRENT = 2
            public int apartmentType; // DQTAT_COM_STA = 2
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

        /// <summary>The compositor needs a DispatcherQueue on this (the WPF UI) thread.</summary>
        public static object EnsureDispatcherQueue()
        {
            var existing = Windows.System.DispatcherQueue.GetForCurrentThread();
            if (existing is not null) return existing;
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = 2,
                apartmentType = 2,
            };
            int hr = CreateDispatcherQueueController(options, out var ptr);
            Marshal.ThrowExceptionForHR(hr);
            return Windows.System.DispatcherQueueController.FromAbi(ptr);
        }

        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint LWA_ALPHA = 0x2;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001;

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        // Kept alive for the process lifetime: Windows calls it for every message.
        private static readonly WndProc Proc = DefWindowProc;
        private static bool _registered;

        /// <summary>A borderless, click-through, never-activated, topmost window with no GDI surface.</summary>
        public static IntPtr CreateOverlayWindow()
        {
            const string cls = "FlickDeskLight";
            var hInstance = GetModuleHandle(null);
            if (!_registered)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Proc,
                    hInstance = hInstance,
                    lpszClassName = cls,
                };
                if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
                _registered = true;
            }
            int ex = WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            var hwnd = CreateWindowEx(ex, cls, "FlickDesk light", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            return hwnd;
        }

        public static void Place(IntPtr hwnd, int x, int y, int w, int h) =>
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);

        public static void ShowNoActivate(IntPtr hwnd)
        {
            ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        public static void Hide(IntPtr hwnd) => ShowWindow(hwnd, 0 /* SW_HIDE */);
    }
}
