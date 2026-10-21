using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FlickDesk;

/// <summary>Effects &amp; customisation, paired phones and general options.</summary>
internal sealed class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly Action _changed;
    private readonly Action _preview;
    private readonly Action _openPairing;
    private readonly ContentControl _page = new();
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private string _current = "Effects";

    private static readonly (EffectPreset id, string name, string blurb)[] Presets =
    {
        (EffectPreset.Surge, "Spectral Surge", "The whole screen answers"),
        (EffectPreset.Ripple, "Spectral Ripple", "Rings across the screen"),
        (EffectPreset.Halo, "Halo", "The frame glows, nothing moves in"),
        (EffectPreset.Glass, "Liquid Glass", "A lens swells, then settles"),
        (EffectPreset.Quiet, "Quiet", "A soft glow at the edge"),
    };

    public SettingsWindow(AppSettings settings, Action changed, Action preview, Action openPairing)
    {
        _s = settings;
        _changed = changed;
        _preview = preview;
        _openPairing = openPairing;

        Title = "FlickDesk — Settings";
        Width = 1080;
        Height = 780;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Ui.StyleWindow(this);

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            _s.Save();
            _changed();
        };
        Closed += (_, _) => { if (_saveDebounce.IsEnabled) { _saveDebounce.Stop(); _s.Save(); _changed(); } };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nav = new StackPanel { Margin = new Thickness(10, 16, 10, 16) };
        foreach (var name in new[] { "Effects", "Phones", "General" })
        {
            var item = new RadioButton { Content = name, Style = Ui.Style("NavItem"), GroupName = "nav", IsChecked = name == _current, Margin = new Thickness(0, 0, 0, 2) };
            item.Checked += (_, _) => { _current = name; ShowPage(); };
            nav.Children.Add(item);
        }
        var navBorder = new Border { BorderBrush = Ui.B("#1E2129"), BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
        root.Children.Add(navBorder);

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _page, Padding = new Thickness(28, 24, 28, 28) };
        Grid.SetColumn(scroller, 1);
        root.Children.Add(scroller);

        Content = root;
        ShowPage();
    }

    /// <summary>Called by the app when phones pair/unpair or check in.</summary>
    public void RefreshDevices()
    {
        if (_current == "Phones") ShowPage();
    }

    private void Changed()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void ShowPage() => _page.Content = _current switch
    {
        "Phones" => PhonesPage(),
        "General" => GeneralPage(),
        _ => EffectsPage(),
    };

    // ================================================================ Effects

    private UIElement EffectsPage()
    {
        var page = new StackPanel();

        var header = new DockPanel { LastChildFill = true };
        var previewBtn = Ui.Button("Preview on screen", _preview);
        previewBtn.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(previewBtn, Dock.Right);
        header.Children.Add(previewBtn);
        var titles = new StackPanel();
        titles.Children.Add(Ui.T("Arrival effect", 22, FontWeights.SemiBold));
        titles.Children.Add(Ui.T("What your screen does when a file lands.", 13.5, brush: Ui.Muted, margin: new Thickness(0, 6, 0, 0)));
        header.Children.Add(titles);
        page.Children.Add(header);

        // presets
        var presets = new UniformGrid { Columns = 5, Margin = new Thickness(0, 22, 0, 0) };
        foreach (var (id, name, blurb) in Presets)
        {
            var content = new StackPanel();
            content.Children.Add(PresetPreview(id));
            content.Children.Add(Ui.T(name, 13.5, FontWeights.SemiBold, margin: new Thickness(4, 10, 4, 0)));
            content.Children.Add(Ui.T(blurb, 12, brush: Ui.Muted, margin: new Thickness(4, 2, 4, 2)));
            var card = new RadioButton
            {
                Style = Ui.Style("PresetCard"), GroupName = "preset", Content = content,
                IsChecked = Canonical(_s.Effect) == id, Margin = new Thickness(0, 0, 12, 0),
            };
            AutomationProperties.SetName(card, name);
            card.Checked += (_, _) => { _s.Effect = id; Changed(); };
            presets.Children.Add(card);
        }
        page.Children.Add(presets);

        // controls grid
        var grid = new Grid { Margin = new Thickness(0, 26, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        grid.RowDefinitions.Add(new RowDefinition());

        // palette
        var palette = new StackPanel();
        palette.Children.Add(Ui.T("Palette", 13, FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 8)));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        var paletteName = Ui.T(_s.Palette, 12.5, brush: Ui.Muted, margin: new Thickness(8, 0, 0, 0));
        paletteName.VerticalAlignment = VerticalAlignment.Center;
        foreach (var p in EffectPalette.All)
        {
            var sw = new RadioButton
            {
                Style = Ui.Style("Swatch"), GroupName = "palette", ToolTip = p.Name,
                IsChecked = string.Equals(_s.Palette, p.Name, StringComparison.OrdinalIgnoreCase),
                Content = new Ellipse { Fill = Ui.PaletteBrush(p) },
                Margin = new Thickness(0, 0, 6, 0),
            };
            AutomationProperties.SetName(sw, p.Name);
            sw.Checked += (_, _) =>
            {
                _s.Palette = p.Name;
                paletteName.Text = p.Name;
                EffectTextures.Prewarm(p);
                Changed();
            };
            swatches.Children.Add(sw);
        }
        swatches.Children.Add(paletteName);
        palette.Children.Add(swatches);
        Place(grid, palette, 0, 0);

        // intensity
        var intensity = new StackPanel();
        var valueText = Ui.T($"{_s.Intensity * 100:0}%", 12.5, brush: Ui.Muted);
        var intensityHeader = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        intensityHeader.Children.Add(valueText);
        intensityHeader.Children.Add(Ui.T("Intensity", 13, FontWeights.SemiBold));
        intensity.Children.Add(intensityHeader);
        var slider = new Slider { Minimum = 20, Maximum = 100, Value = _s.Intensity * 100, Margin = new Thickness(0, 12, 0, 0), SmallChange = 5, LargeChange = 10 };
        AutomationProperties.SetName(slider, "Intensity");
        slider.ValueChanged += (_, e) =>
        {
            _s.Intensity = e.NewValue / 100;
            valueText.Text = $"{e.NewValue:0}%";
            Changed();
        };
        intensity.Children.Add(slider);
        Place(grid, intensity, 0, 2);

        // speed
        var speed = new StackPanel();
        speed.Children.Add(Ui.T("Speed", 13, FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 8)));
        speed.Children.Add(Segments("speed", new[] { "Calm", "Natural", "Snappy" }, _s.Speed.ToString(),
            v => { _s.Speed = Enum.Parse<EffectSpeed>(v); Changed(); }));
        Place(grid, speed, 2, 0);

        // phone side
        var side = new StackPanel();
        side.Children.Add(Ui.T("My phone sits on the", 13, FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 8)));
        side.Children.Add(Segments("side", new[] { "Left", "Right", "Bottom" }, _s.PhoneEdge.ToString(),
            v => { _s.PhoneSide = v; Changed(); }));
        Place(grid, side, 2, 2);
        page.Children.Add(grid);

        // toggles
        var toggles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 22, 0, 0) };
        toggles.Children.Add(Switch("Arrival sound", "A soft glassy chime, under half a second", _s.Sound, v => _s.Sound = v));
        toggles.Children.Add(Switch("Show the file card", "Thumbnail lands, then fades after 3 s", _s.ShowCard, v => _s.ShowCard = v));
        toggles.Children.Add(Switch("Stay quiet in full-screen apps", "Games, video and presenting get a notification instead", _s.QuietInFullScreen, v => _s.QuietInFullScreen = v));
        toggles.Children.Add(Switch("Reduce motion", "Swap the moving light for a gentle fade", _s.ReduceMotion, v => _s.ReduceMotion = v));
        toggles.Children.Add(Switch("Drop zone on the phone's edge", "Drag files onto the glowing strip to send them", _s.ShowDropZone, v => _s.ShowDropZone = v));
        var separator = new Border { BorderBrush = Ui.B("#1E2129"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 22, 0, 0) };
        page.Children.Add(separator);
        page.Children.Add(toggles);
        return page;
    }

    private static EffectPreset Canonical(EffectPreset p) => p switch
    {
        EffectPreset.Aurora => EffectPreset.Halo,
        EffectPreset.Prism => EffectPreset.Ripple,
        _ => p,
    };

    private static void Place(Grid grid, UIElement e, int row, int column)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        grid.Children.Add(e);
    }

    private static UIElement Segments(string group, string[] options, string selected, Action<string> onPick)
    {
        var holder = new Border
        {
            Background = Ui.B("#0B0D11"), BorderBrush = Ui.B("#23272F"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var o in options)
        {
            var seg = new RadioButton { Content = o, Style = Ui.Style("Segment"), GroupName = group, IsChecked = o == selected };
            seg.Checked += (_, _) => onPick(o);
            row.Children.Add(seg);
        }
        holder.Child = row;
        return holder;
    }

    private CheckBox Switch(string title, string hint, bool value, Action<bool> set)
    {
        var text = new StackPanel();
        text.Children.Add(Ui.T(title, 13.5, FontWeights.SemiBold));
        text.Children.Add(Ui.T(hint, 12, brush: Ui.Muted, margin: new Thickness(0, 2, 0, 0)));
        var box = new CheckBox { Style = Ui.Style("Switch"), Content = text, IsChecked = value, Margin = new Thickness(0, 0, 36, 0) };
        AutomationProperties.SetName(box, title);
        box.Checked += (_, _) => { set(true); Changed(); };
        box.Unchecked += (_, _) => { set(false); Changed(); };
        return box;
    }

    /// <summary>A small, static rendering of each preset for its card.</summary>
    private UIElement PresetPreview(EffectPreset id)
    {
        var p = EffectPalette.Get(_s.Palette);
        var stage = new Grid
        {
            Height = 104,
            ClipToBounds = true,
            Background = new RadialGradientBrush(Color.FromRgb(23, 35, 58), Color.FromRgb(11, 16, 25))
            {
                Center = new Point(0.2, 0), GradientOrigin = new Point(0.2, 0), RadiusX = 1.2, RadiusY = 1,
            },
        };
        var clip = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = stage };
        clip.Clip = null;

        UIElement AtEdge(FrameworkElement e, double size)
        {
            e.Width = size;
            e.Height = size;
            e.HorizontalAlignment = HorizontalAlignment.Right;
            e.VerticalAlignment = VerticalAlignment.Center;
            e.Margin = new Thickness(0, -size, -size / 2, -size);
            return e;
        }

        Ellipse Core(double size, double opacity) => new()
        {
            Opacity = opacity,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(250, 255, 255, 255), 0), new(Color.FromArgb(110, 255, 255, 255), 0.3), new(Color.FromArgb(0, 255, 255, 255), 1),
            }),
        };

        // a soft spectral frame along the card's right edge (Surge / Halo)
        UIElement EdgeGlow(double width, double opacity)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i < p.Ring.Length; i++)
                stops.Add(new GradientStop(p.Ring[i], i / (double)Math.Max(1, p.Ring.Length - 1)));
            return new Rectangle
            {
                Width = width, HorizontalAlignment = HorizontalAlignment.Right, Opacity = opacity,
                Fill = new LinearGradientBrush(stops, 90),
                OpacityMask = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Colors.Transparent, 0), new(Color.FromArgb(120, 0, 0, 0), 0.6), new(Colors.Black, 1),
                }, 0),
            };
        }

        switch (id)
        {
            case EffectPreset.Surge:
                stage.Children.Add(EdgeGlow(46, 0.95));
                stage.Children.Add(AtEdge(new Image { Source = EffectTextures.Ring(p, 0.035), Opacity = 0.9 }, 300));
                stage.Children.Add(AtEdge(new Image { Source = EffectTextures.Hairline(EffectTextures.CoolSplit), Opacity = 0.9 }, 300));
                stage.Children.Add(AtEdge(Core(90, 0.95), 90));
                break;
            case EffectPreset.Halo:
                stage.Children.Add(EdgeGlow(70, 1));
                stage.Children.Add(AtEdge(Core(70, 0.7), 70));
                break;
            case EffectPreset.Ripple:
                stage.Children.Add(AtEdge(new Image { Source = EffectTextures.Ring(p, 0.035), Opacity = 0.95 }, 250));
                stage.Children.Add(AtEdge(new Image { Source = EffectTextures.Ring(p, 0.055), Opacity = 0.6 }, 170));
                stage.Children.Add(AtEdge(Core(90, 0.9), 90));
                break;
            case EffectPreset.Aurora:
                for (int i = 0; i < 2; i++)
                {
                    var c1 = p.Sample(i * 0.3);
                    var c2 = p.Sample(i * 0.3 + 0.35);
                    stage.Children.Add(new Rectangle
                    {
                        Width = 46 - i * 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -10, 28 + i * 34, -10),
                        Fill = new LinearGradientBrush(new GradientStopCollection
                        {
                            new(Color.FromArgb(0, c1.R, c1.G, c1.B), 0), new(Color.FromArgb(220, c1.R, c1.G, c1.B), 0.35),
                            new(Color.FromArgb(220, c2.R, c2.G, c2.B), 0.7), new(Color.FromArgb(0, c2.R, c2.G, c2.B), 1),
                        }, 90),
                        OpacityMask = new LinearGradientBrush(new GradientStopCollection
                        {
                            new(Colors.Transparent, 0), new(Colors.Black, 0.5), new(Colors.Transparent, 1),
                        }, 0),
                        Opacity = 0.9 - i * 0.25,
                    });
                }
                break;
            case EffectPreset.Prism:
                var stops = new GradientStopCollection { new(Color.FromArgb(0, 255, 255, 255), 0) };
                for (int i = 0; i < p.Ring.Length; i++)
                    stops.Add(new GradientStop(p.Ring[i], 0.15 + 0.7 * i / Math.Max(1, p.Ring.Length - 1)));
                stops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                stage.Children.Add(new Rectangle
                {
                    Width = 40, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -30, 46, -30),
                    Fill = new LinearGradientBrush(stops, 0),
                    RenderTransform = new SkewTransform(-16, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                });
                break;
            case EffectPreset.Glass:
                stage.Children.Add(AtEdge(new Image { Source = EffectTextures.GlassRim(p) }, 210));
                stage.Children.Add(AtEdge(Core(90, 0.8), 90));
                break;
            default:
                stage.Children.Add(AtEdge(Core(150, 0.75), 150));
                break;
        }
        return clip;
    }

    // ================================================================ Phones

    private UIElement PhonesPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.T("Phones", 22, FontWeights.SemiBold));
        page.Children.Add(Ui.T("Phones you've paired. Each one has its own encryption key.", 13.5, brush: Ui.Muted, margin: new Thickness(0, 6, 0, 18)));

        var devices = _s.Devices();
        if (devices.Count == 0)
            page.Children.Add(Ui.Card(Ui.T("No phones yet. Pair one with a QR code – it takes a few seconds.", 13.5, brush: Ui.Muted)));

        foreach (var d in devices)
        {
            var row = new DockPanel();
            var forget = Ui.Button("Forget", () =>
            {
                if (MessageBox.Show(this, $"Forget {d.Name}? It will need to pair again.", "FlickDesk",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _s.ForgetDevice(d.DeviceId);
                _changed();
                ShowPage();
            });
            DockPanel.SetDock(forget, Dock.Right);
            row.Children.Add(forget);
            var icon = Ui.Icon(Ui.IconPhone, 22, Ui.B("#CFE8FF"), 1.6);
            icon.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(Ui.T(d.Name, 14, FontWeights.SemiBold));
            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            status.Children.Add(Ui.Dot(d.Online ? Ui.Good : Ui.B("#5B6270"), 7));
            status.Children.Add(Ui.T(d.Online ? $"Online · {d.Address}" : $"Last seen {Ui.Ago(d.LastSeenUtc)}", 12, brush: Ui.Muted, margin: new Thickness(6, 0, 0, 0)));
            text.Children.Add(status);
            row.Children.Add(text);
            var card = Ui.Card(row);
            card.Margin = new Thickness(0, 0, 0, 10);
            page.Children.Add(card);
        }

        var pair = Ui.Button("Pair a phone…", _openPairing, "PrimaryButton");
        pair.HorizontalAlignment = HorizontalAlignment.Left;
        pair.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(pair);
        return page;
    }

    // ================================================================ General

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private UIElement GeneralPage()
    {
        var page = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(Ui.T("General", 22, FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 18)));

        var folderRow = new DockPanel();
        var change = Ui.Button("Change…", () =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Where should flicked files be saved?",
                SelectedPath = _s.SaveFolder,
                UseDescriptionForTitle = true,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _s.SaveFolder = dialog.SelectedPath;
                Changed();
                ShowPage();
            }
        });
        DockPanel.SetDock(change, Dock.Right);
        folderRow.Children.Add(change);
        var folderText = new StackPanel();
        folderText.Children.Add(Ui.T("Save received files to", 13.5, FontWeights.SemiBold));
        folderText.Children.Add(Ui.T(_s.SaveFolder, 12.5, brush: Ui.Muted, margin: new Thickness(0, 3, 12, 0)));
        folderRow.Children.Add(folderText);
        page.Children.Add(Ui.Card(folderRow));

        var switches = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        switches.Children.Add(Switch("Copy text from my phone automatically", "Text and links you send from the phone go straight to this PC's clipboard", _s.AutoCopyFromPhone, v => _s.AutoCopyFromPhone = v));
        switches.Children.Add(Switch("Start FlickDesk with Windows", "Sits quietly in the tray", IsStartupEnabled(), SetStartup));
        switches.Children.Add(Switch("Full-screen light (GPU)", "Drawn by the Windows compositor. Turn off if the light ever doesn't appear – you'll get the classic renderer", _s.UseCompositor, v => _s.UseCompositor = v));
        page.Children.Add(switches);
        return page;
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("FlickDesk") is string;
        }
        catch (Exception) { return false; }
    }

    private static void SetStartup(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && Environment.ProcessPath is { } exe) key.SetValue("FlickDesk", $"\"{exe}\"");
            else key.DeleteValue("FlickDesk", throwOnMissingValue: false);
        }
        catch (Exception) { /* policy may block it */ }
    }
}
