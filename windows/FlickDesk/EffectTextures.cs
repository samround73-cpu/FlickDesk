using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlickDesk;

internal sealed record EffectPalette(string Name, Color[] Ring, Color Halo, Color HaloOuter)
{
    // Saturated on purpose: pale pastels turned into a grey smudge on light wallpapers.
    public static readonly IReadOnlyList<EffectPalette> All = new[]
    {
        new EffectPalette("Spectrum",
            new[] { Hex("#ff4d7d"), Hex("#ff9f43"), Hex("#ffd84d"), Hex("#3df5a0"), Hex("#3dc7ff"), Hex("#6f6bff"), Hex("#d65cff") },
            Hex("#8cc8ff"), Hex("#ff8cc8")),
        new EffectPalette("Aurora",
            new[] { Hex("#2ff5c4"), Hex("#3dc7ff"), Hex("#6f6bff"), Hex("#b45cff") },
            Hex("#6effd2"), Hex("#8c78ff")),
        new EffectPalette("Ember",
            new[] { Hex("#ff6a3d"), Hex("#ffb84d"), Hex("#ff4d8a"), Hex("#ff7f4d") },
            Hex("#ffaa6e"), Hex("#ff6496")),
        new EffectPalette("Glacier",
            new[] { Hex("#ffffff"), Hex("#8fd2ff"), Hex("#9fb0ff"), Hex("#d6efff") },
            Hex("#c8e6ff"), Hex("#aabeff")),
    };

    public static EffectPalette Get(string? name)
    {
        foreach (var p in All) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return All[0];
    }

    /// <summary>Colour at position t (0..1) around the cyclic palette.</summary>
    public Color Sample(double t)
    {
        t = (t % 1 + 1) % 1;
        double x = t * Ring.Length;
        int i = (int)Math.Floor(x) % Ring.Length;
        var a = Ring[i];
        var b = Ring[(i + 1) % Ring.Length];
        double f = x - Math.Floor(x);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * f),
            (byte)(a.G + (b.G - a.G) * f),
            (byte)(a.B + (b.B - a.B) * f));
    }

    private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);
}

/// <summary>
/// Pre-rendered ring bitmaps. WPF has no conic gradient or cheap blur, so each "light ring" is
/// baked once into a 512×512 premultiplied bitmap: hue follows the angle, alpha is a Gaussian
/// band around radius 0.8. Animating a cached bitmap is just a GPU transform – smooth and cheap.
/// </summary>
internal static class EffectTextures
{
    private const int Size = 512;
    private const double Radius = 0.8;
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new();

    public static readonly Color WarmSplit = Color.FromRgb(255, 150, 170);
    public static readonly Color CoolSplit = Color.FromRgb(140, 220, 255);

    /// <summary>Soft spectral ring. softness ≈ 0.02 (crisp) … 0.07 (very soft).</summary>
    public static BitmapSource Ring(EffectPalette p, double softness) =>
        Cache.GetOrAdd($"ring:{p.Name}:{softness:F3}", _ => Render((t, r) =>
        {
            double a = Band(r, Radius, softness);
            return (p.Sample(t + 0.58), a);
        }));

    /// <summary>A single-colour hairline (the glassy caustic).</summary>
    public static BitmapSource Hairline(Color c) =>
        Cache.GetOrAdd($"hair:{c}", _ => Render((t, r) => (c, 0.85 * Band(r, Radius, 0.0065))));

    /// <summary>Liquid-glass rim: a bright bluish-white band with a faint spectral tint and an inner sheen.</summary>
    public static BitmapSource GlassRim(EffectPalette p) =>
        Cache.GetOrAdd($"rim:{p.Name}", _ => Render((t, r) =>
        {
            double band = Band(r, Radius, 0.03);
            double sheen = r < Radius ? 0.10 * Math.Pow(r / Radius, 6) : 0;
            var tint = p.Sample(t);
            var c = Color.FromRgb(
                (byte)(0.65 * 235 + 0.35 * tint.R),
                (byte)(0.65 * 245 + 0.35 * tint.G),
                (byte)(0.65 * 255 + 0.35 * tint.B));
            return (c, Math.Min(1, 0.95 * band + sheen));
        }));

    /// <summary>A disc whose hue follows the angle – spun behind a card it makes a living spectral rim.</summary>
    public static BitmapSource Disc(EffectPalette p) =>
        Cache.GetOrAdd($"disc:{p.Name}", _ => Render((t, r) => (p.Sample(t), r < 0.985 ? 1.0 : 0.0)));

    public static void Prewarm(EffectPalette p) => Task.Run(() =>
    {
        Ring(p, 0.022); Ring(p, 0.035); Ring(p, 0.055);
        Hairline(WarmSplit); Hairline(CoolSplit);
        GlassRim(p);
    });

    private static double Band(double r, double center, double width)
    {
        double d = (r - center) / width;
        return Math.Exp(-d * d);
    }

    private static BitmapSource Render(Func<double, double, (Color color, double alpha)> shade)
    {
        var pixels = new byte[Size * Size * 4];
        double half = Size / 2.0;
        for (int y = 0; y < Size; y++)
        {
            double dy = (y + 0.5 - half) / half;
            for (int x = 0; x < Size; x++)
            {
                double dx = (x + 0.5 - half) / half;
                double r = Math.Sqrt(dx * dx + dy * dy);
                if (r > 1) continue;
                double t = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1;
                var (c, a) = shade(t, r);
                if (a < 0.002) continue;
                a = Math.Clamp(a, 0, 1);
                int i = (y * Size + x) * 4;
                pixels[i + 0] = (byte)(c.B * a); // premultiplied BGRA
                pixels[i + 1] = (byte)(c.G * a);
                pixels[i + 2] = (byte)(c.R * a);
                pixels[i + 3] = (byte)(255 * a);
            }
        }
        var bmp = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, pixels, Size * 4);
        bmp.Freeze();
        return bmp;
    }
}
