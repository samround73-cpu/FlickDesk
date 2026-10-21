using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FlickDesk;

/// <summary>Small helpers for building the dark UI in code.</summary>
internal static class Ui
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Brush Bg = B("#101217");
    public static readonly Brush Surface = B("#171A21");
    public static readonly Brush Surface2 = B("#1C2029");
    public static readonly Brush Line = B("#262A33");
    public static readonly Brush Text = B("#F2F3F5");
    public static readonly Brush Muted = B("#A3A9B6");
    public static readonly Brush Faint = B("#8C93A1");
    public static readonly Brush Accent = B("#7CC4FF");
    public static readonly Brush Good = B("#4CD98A");

    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock T(string text, double size = 13.5, FontWeight? weight = null, Brush? brush = null, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Text,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0),
    };

    public static TextBlock Label(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = Faint,
        Margin = new Thickness(0, 0, 0, 8),
    };

    public static Button Button(string text, Action onClick, string style = "FlickButton")
    {
        var b = new Button { Content = text, Style = Style(style) };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Path Icon(string data, double size = 18, Brush? stroke = null, double thickness = 1.8)
    {
        var geo = Geometry.Parse(data);
        geo.Freeze();
        return new Path
        {
            Data = geo,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Stroke = stroke ?? Text,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    // 24×24 stroke icons
    public const string IconMonitor = "M3,4 H21 V16 H3 Z M8,20 H16 M12,16 V20";
    public const string IconPhone = "M7,2.5 H17 V21.5 H7 Z M11,18.5 H13";
    public const string IconSend = "M5,12 H19 M13,6 L19,12 L13,18";
    public const string IconClip = "M7,4 H17 V20 H7 Z M10,4 V3 H14 V4";
    public const string IconPause = "M9,6 V18 M15,6 V18";
    public const string IconPlay = "M8,5 L19,12 L8,19 Z";
    public const string IconSliders = "M4,7 H14 M18,7 H20 M4,17 H8 M12,17 H20 M16,5 V9 M10,15 V19";
    public const string IconQr = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H16 M18,14 H20 V16 M14,18 V20 H16 M18,18 H20 V20";

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = B("#11141A"),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = padding ?? new Thickness(14, 12, 14, 12),
        Child = child,
    };

    public static Border Dot(Brush fill, double size = 8) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = fill,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours} h ago";
        return utc.ToLocalTime().ToString("d MMM");
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>A swatch for a palette: a soft diagonal sweep through its colours.</summary>
    public static Brush PaletteBrush(EffectPalette p)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < p.Ring.Length; i++)
            brush.GradientStops.Add(new GradientStop(p.Ring[i], i / (double)Math.Max(1, p.Ring.Length - 1)));
        brush.Freeze();
        return brush;
    }

    /// <summary>Applies the shared dark window look.</summary>
    public static void StyleWindow(Window w)
    {
        w.Background = Bg;
        w.Foreground = Text;
        w.FontFamily = Font;
        w.FontSize = 13.5;
        w.SnapsToDevicePixels = true;
        w.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        w.SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(w);
    }
}
