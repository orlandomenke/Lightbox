using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using static Lightbox.Gallery.Stories.Kit;
using CoreGradient = Lightbox.Core.Documents.Gradient;
using CoreStop = Lightbox.Core.Documents.GradientStop;

namespace Lightbox.Gallery.Stories;

/// <summary>
/// The app's more particular dockers, twice each (Q203): <b>as built</b> —
/// today's structure, literal margins and glyph buttons, read off
/// MainWindow.axaml and BoneOptionsBar.axaml — and <b>recreated</b> under the
/// gallery's rules: tokens, one row layout, quiet headings, selectors for
/// modes, text-at-rest dropdowns, flat actions.
/// </summary>
/// <remarks>
/// Recreations, not the live panels: hosting the real ones would mean running
/// the app's whole view model — its settings, workspace store and background
/// services — inside the gallery. The controls are the real ones (Docker,
/// HueRing, ColorSpectrum, GradientRamp, ColorField); the data is sample data.
/// </remarks>
public static partial class Catalog
{
    private static Story BonesDocker() => new(
        "Docker · Bones",
        "The bone tool's options, in the Tool options docker. As built: radio buttons for the mode, 52 px labels, SemiBold headings, "
        + "every action a text button. Recreated: a selector for the mode, quiet section headings, rows on the token grid, "
        + "actions grouped flat at the end of each section.",
        [
            new("As built", () => Panel("Tool options", BonesAsBuilt(), 300, 660, asBuilt: true)),
            new("Recreated", () => Panel("Tool options", BonesRecreated(), 300, 660, asBuilt: false)),
        ],
        Columns: 2);

    private static Control BonesAsBuilt()
    {
        var bones = new ListBox { MaxHeight = 160, ItemsSource = BoneNames.Select(n => new TextBlock { Text = n, FontFamily = Mono, FontSize = 12 }).ToList(), SelectedIndex = 3 };
        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 4, 0),
            Children =
            {
                Row(new RadioButton { Content = "Bind", GroupName = "m" }, new RadioButton { Content = "Pose", GroupName = "m", IsChecked = true }, new RadioButton { Content = "Weights", GroupName = "m" }),
                new StackPanel { Spacing = 4, Children = { With(new ToggleButton { IsChecked = true, Padding = new Thickness(2, 0), HorizontalAlignment = HorizontalAlignment.Left, Content = Row(Icon("IconChevronDown", 8), BuiltHeading("Bones")) }, "ghost"), bones } },
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        BuiltLabelRow("Name", new TextBox { Text = "head" }),
                        BuiltLabelRow("Length", new NumericUpDown { Value = 48, FormatString = "0" }),
                        Row(new Button { Content = "Add child" }, new Button { Content = "Delete" }),
                        new CheckBox { Content = "Jiggle", IsChecked = true },
                        BuiltLabelRow("Catch up", new NumericUpDown { Value = 0.35m, FormatString = "0.00" }),
                        BuiltLabelRow("Settle", new NumericUpDown { Value = 0.6m, FormatString = "0.00" }),
                    },
                },
                new StackPanel { Spacing = 6, Children = { BuiltHeading("IK"), BuiltLabelRow("Bones", new NumericUpDown { Value = 2, FormatString = "0" }), BuiltLabelRow("Pole", new ComboBox { ItemsSource = new[] { "arm.l" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }), new Button { Content = "Remove IK" } } },
                new Button { Content = "Add spline" },
                new StackPanel { Spacing = 6, Children = { BuiltHeading("Binding"), Row(new Button { Content = "Assign" }, new Button { Content = "Auto-bind" }), new Button { Content = "Bake pose" }, new Button { Content = "Drawing from pose" } } },
            },
        };
    }

    private static Control BonesRecreated()
    {
        var bones = new ListBox
        {
            MaxHeight = 150,
            ItemsSource = BoneNames.Select(n => new TextBlock { Text = n, FontFamily = Mono, FontSize = 11 }).ToList(),
            SelectedIndex = 3,
        };
        return Rows(
            SettingRow(SegmentedBare(["Bind", "Pose", "Weights"], 1), null, "IconBone"),
            Section("Bones"),
            bones,
            Section("Selected bone"),
            SettingRow(new TextBox { Text = "head" }, null, null),
            SettingRow(Label("Length"), ValueField(48, 4, 4000), null),
            SettingRow(Label("Jiggle"), null, null).Let2(g => g.Children.Add(Right(new ToggleSwitch { IsChecked = true, OnContent = null, OffContent = null }, 2))),
            SettingRow(Label("Catch up"), ValueField(35, 1, 100), null),
            SettingRow(Label("Settle"), ValueField(60, 2, 100), null),
            Actions(("Add child", false), ("Delete", false)),
            Section("Inverse kinematics"),
            SettingRow(Label("Chain"), ValueField(2, 1, 16), null),
            SettingRow(new ComboBox { ItemsSource = new[] { "Pole: arm.l", "Pole: none" }, SelectedIndex = 0 }, null, null),
            Actions(("Remove IK", false), ("Add spline", false)),
            Section("Binding"),
            Actions(("Auto-bind", true), ("Assign", false), ("Bake pose", false)));
    }

    private static Story ColorDocker() => new(
        "Docker · Color",
        "As built: a 120 px combo for the mode, the wheel, a round swatch and its hex in the bottom bar. Recreated: the mode as a "
        + "selector, the wheel on the panel inset, the swatch and hex on the row grid.",
        [
            new("As built", () => Panel("Color", ColorBody(), 300, 330, asBuilt: true, top: Bar(new ComboBox { Width = 120, ItemsSource = new[] { "Wheel", "HSV", "HSL", "RGB", "CMYK" }, SelectedIndex = 0 }), bottom: Bar(Row(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderBrush = new SolidColorBrush(Color.Parse("#555")), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.Parse("#C04A3B")) }, new TextBlock { Text = "#C04A3B", FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) })))),
            new("Recreated", () => Panel("Color", Rows(
                SegmentedBare(["Wheel", "HSV", "RGB", "CMYK"], 0),
                ColorBody(),
                SettingRow(Row(new ColorField { Hex = "#C04A3B" }, new TextBlock { Text = "#C04A3B", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("TextSecondaryBrush") }), null, null)), 300, 360, asBuilt: false)),
        ],
        Columns: 2);

    private static Control ColorBody() => new Viewbox
    {
        Stretch = Stretch.Uniform,
        MaxHeight = 190,
        Child = new Panel
        {
            Width = 220,
            Height = 220,
            Children =
            {
                new HueRing { RingWidth = 16, HsvColor = new HsvColor(1, 8, 0.7, 0.75) },
                new ColorSpectrum { Width = 130, Height = 130, Shape = ColorSpectrumShape.Box, Components = ColorSpectrumComponents.SaturationValue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Color = Color.Parse("#C04A3B") },
            },
        },
    };

    private static Story PaletteDocker() => new(
        "Docker · Palette",
        "As built: a palette tree with a splitter, a row of five text and glyph buttons (\"＋ Swatch\", −, Duplicate, Import…, Export…), "
        + "a checkbox to edit a swatch. Recreated: the palette as a text dropdown with its actions as icons, the swatches straight "
        + "below, swatch editing on the row grid. Swatches stay square: they are colour, which is artwork.",
        [
            new("As built", () => Panel("Palette", PaletteAsBuilt(), 300, 430, asBuilt: true,
                top: Bar(Column(4,
                    Row(new TextBlock { Text = "Palettes", FontSize = 11, Opacity = 0.6, Width = 180, VerticalAlignment = VerticalAlignment.Center }, IconButton("IconPlus", 10), IconButton("IconFolder", 10), IconButton("IconClose", 10)),
                    Row(With(new Button { Content = "＋ Swatch" }, "text"), IconButton("IconMinus", 11), With(new Button { Content = "Duplicate" }, "text"), With(new Button { Content = "Import…" }, "text")))),
                bottom: Bar(Column(4, new CheckBox { Content = "Edit selected swatch" })))),
            new("Recreated", () => Panel("Palette", Rows(
                SettingRow(new ComboBox { ItemsSource = new[] { "Hero skin", "Hero costume", "Forest", "Default" }, SelectedIndex = 0 }, null, "IconFolder"),
                Swatches(square: true),
                SettingRow(new ColorField { Hex = "#C04A3B" }, null, null),
                Actions(("Add swatch", false), ("Duplicate", false), ("Import…", false))), 300, 360, asBuilt: false)),
        ],
        Columns: 2);

    private static Control PaletteAsBuilt()
    {
        var tree = new TreeView
        {
            Height = 128,
            ItemsSource = new[]
            {
                new TreeViewItem { Header = "📁 Characters", IsExpanded = true, ItemsSource = new[] { new TreeViewItem { Header = "🎨 Hero skin" }, new TreeViewItem { Header = "🎨 Hero costume" } } },
                new TreeViewItem { Header = "📁 Backgrounds", ItemsSource = new[] { new TreeViewItem { Header = "🎨 Forest" } } },
                new TreeViewItem { Header = "🎨 Default" },
            },
        };
        return Column(0, tree, new Border { Height = 4 }, new Border { Margin = new Thickness(4), Child = Swatches(square: false) });
    }

    private static readonly string[] SwatchColours =
    [
        "#F2E3D5", "#E8C4A6", "#D9A27F", "#C07A55", "#8F5236", "#5A3122",
        "#2B1A14", "#C04A3B", "#E07A3A", "#F0C05A", "#7AA35C", "#3F7A6A",
        "#3A5A8C", "#6A4C9C", "#B7B2C8", "#FFFFFF", "#808080", "#1A1A1A",
    ];

    /// <summary>As built: 26 px tiles, radius 3, a #666 outline. Recreated: square colour, no outline, the token gap.</summary>
    private static Control Swatches(bool square)
    {
        var wrap = new WrapPanel();
        foreach (var hex in SwatchColours)
        {
            wrap.Children.Add(new Border
            {
                Width = square ? 24 : 26,
                Height = square ? 24 : 26,
                Margin = new Thickness(square ? 0 : 2, square ? 0 : 2, square ? 2 : 2, square ? 2 : 2),
                CornerRadius = new CornerRadius(square ? 0 : 3),
                BorderBrush = square ? null : new SolidColorBrush(Color.Parse("#666")),
                BorderThickness = new Thickness(square ? 0 : 1),
                Background = new SolidColorBrush(Color.Parse(hex)),
            });
        }
        return wrap;
    }

    private static Story GradientDocker() => new(
        "Docker · Gradient",
        "As built: a combo with icon buttons, a preview strip, two 92 px combos for kind and spread, the ramp, then stops as "
        + "slider + field rows with 34 px labels. Recreated: kind and spread as selectors, every stop and the tool opacity on "
        + "the row grid as effect sliders.",
        [
            new("As built", () => Panel("Gradient", GradientAsBuilt(), 300, 420, asBuilt: true,
                top: Bar(Column(4,
                    Row(new ComboBox { Width = 200, ItemsSource = new[] { "Sunset", "Sky", "Fire" }, SelectedIndex = 0 }, IconButton("IconPlus", 10), IconButton("IconClose", 10)),
                    new Border { Height = 22, CornerRadius = new CornerRadius(3), BorderBrush = new SolidColorBrush(Color.Parse("#666")), BorderThickness = new Thickness(1), Background = SunsetBrush() },
                    Row(new ComboBox { Width = 92, FontSize = 11, ItemsSource = new[] { "Linear", "Radial" }, SelectedIndex = 0 }, new ComboBox { Width = 92, FontSize = 11, ItemsSource = new[] { "Pad", "Repeat", "Reflect" }, SelectedIndex = 0 }))))),
            new("Recreated", () => Panel("Gradient", Rows(
                SettingRow(new ComboBox { ItemsSource = new[] { "Sunset", "Sky", "Fire" }, SelectedIndex = 0 }, null, "IconGradient"),
                new GradientRamp { Gradient = Sunset(), Height = 56 },
                SettingRow(SegmentedBare(["Linear", "Radial"], 0), null, null),
                SettingRow(SegmentedBare(["Pad", "Repeat", "Reflect"], 0), null, null),
                Section("Stop"),
                SettingRow(EffectSlider(80, checker: true), ValueField(80, 0, 100), "LookIconOpacity"),
                SettingRow(new ColorField { Hex = "#E07A3A" }, null, null),
                Section("Tool"),
                SettingRow(EffectSlider(100, checker: true), ValueField(100, 0, 100), "LookIconOpacity")), 300, 440, asBuilt: false)),
        ],
        Columns: 2);

    private static Control GradientAsBuilt() => new StackPanel
    {
        Margin = new Thickness(6),
        Spacing = 8,
        Children =
        {
            new GradientRamp { Gradient = Sunset(), Height = 56 },
            new TextBlock { Text = "Click above the ramp for an opacity stop, below it for a colour stop. Drag to move, middle-click to remove.", FontSize = 10, Opacity = 0.55, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Opacity stop", FontSize = 10, Opacity = 0.6 },
            BuiltSliderRow(null, 80, 56),
            new TextBlock { Text = "Colour stop", FontSize = 10, Opacity = 0.6 },
            new ColorField { Hex = "#E07A3A", HorizontalAlignment = HorizontalAlignment.Left },
            BuiltSliderRow("Tool", 100, 56),
        },
    };

    private static Story HistoryDocker() => new(
        "Docker · Undo history",
        "As built: 20 px rows of 11 px text, a ● on the current step, redo steps dimmed. Recreated: rows on the row height, an "
        + "icon for what each step did, the current step as the flat \"on\" fill rather than a dot.",
        [
            new("As built", () => Panel("Undo history", HistoryList(recreated: false), 300, 260, asBuilt: true)),
            new("Recreated", () => Panel("Undo history", HistoryList(recreated: true), 300, 280, asBuilt: false)),
        ],
        Columns: 2);

    private static readonly (string Label, string Icon)[] Steps =
    [
        ("Open", "IconFolder"), ("Brush stroke", "IconBrush"), ("Brush stroke", "IconBrush"), ("Add layer", "IconPlus"),
        ("Move", "IconMove"), ("Fill", "IconFill"), ("Erase", "IconEraser"), ("Brush stroke", "IconBrush"),
    ];

    private static Control HistoryList(bool recreated)
    {
        const int current = 5;
        var panel = new StackPanel { Spacing = recreated ? 1 : 0, Margin = recreated ? default : new Thickness(2) };
        for (var i = 0; i < Steps.Length; i++)
        {
            var (label, icon) = Steps[i];
            var redo = i > current;
            if (!recreated)
            {
                var row = new DockPanel { MinHeight = 20, Opacity = redo ? 0.4 : 1, Margin = new Thickness(2, 0) };
                if (i == current)
                {
                    var dot = new TextBlock { Text = "●", FontSize = 10, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(dot, Dock.Right);
                    row.Children.Add(dot);
                }
                row.Children.Add(new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(row);
                continue;
            }
            var item = new Border
            {
                Classes = { "layerRow" },
                Height = Application.Current!.TryFindResource("LookRowHeight", out var h) && h is double rh ? rh : 24,
                Opacity = redo ? 0.45 : 1,
                Padding = new Thickness(8, 0),
                Child = Row(Icon(icon, 12), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }),
            };
            if (i == current) item.Classes.Add("active");
            panel.Children.Add(item);
        }
        return panel;
    }

    private static Story NavigatorDocker() => new(
        "Docker · Navigator",
        "As built: the picture with a 4 px margin and the viewport drawn on it, nothing else. Recreated: the picture on the panel "
        + "inset, and the zoom it already implies as a row — the one control people reach for here. (The picture is a stand-in; "
        + "the real one needs a document.)",
        [
            new("As built", () => Panel("Navigator", new Border { Margin = new Thickness(4), Child = NavigatorPicture() }, 300, 220, asBuilt: true)),
            new("Recreated", () => Panel("Navigator", Rows(
                NavigatorPicture(),
                SettingRow(With(new Slider { Minimum = 10, Maximum = 800, Value = 100 }, "param"), ValueField(100, 10, 800), "IconHome")), 300, 250, asBuilt: false)),
        ],
        Columns: 2);

    private static Control NavigatorPicture()
    {
        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#E0703A")),
            BorderThickness = new Thickness(1.5),
            Width = 120, Height = 70,
            Margin = new Thickness(90, 34, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        return new Panel { Height = 140, Children = { new Border { Background = Painting() }, frame } };
    }

    // ---- the shared parts ----------------------------------------------------

    private static readonly string[] BoneNames = ["root", "  spine", "    neck", "      head", "    arm.l", "      forearm.l", "    arm.r"];

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");

    /// <summary>
    /// A docker around a body. As built keeps today's zero padding — a local
    /// value, so the look's LookPanelPadding cannot reach it — because today
    /// each view carries its own margins.
    /// </summary>
    private static Control Panel(string title, Control body, double width, double height, bool asBuilt, Control? top = null, Control? bottom = null)
    {
        var docker = new Docker { PanelId = DockPanelId.ToolOptions, Width = width, Height = height, Content = body, TopBar = top, BottomBar = bottom };
        if (asBuilt) docker.Padding = new Thickness(0);
        docker.ShowTabs([new DockPanelInfo(DockPanelId.ToolOptions, title, null, 300, 120)], DockPanelId.ToolOptions);
        return docker;
    }

    private static Border Bar(Control child) => new() { Classes = { "dockerBar" }, Child = child };

    private static Control BuiltHeading(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 12 };

    private static Control BuiltLabelRow(string label, Control editor)
    {
        var row = new DockPanel();
        var l = new TextBlock { Text = label, Width = 52, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.7 };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(editor);
        return row;
    }

    private static Control BuiltSliderRow(string? label, double value, double fieldWidth)
    {
        var row = new DockPanel();
        if (label is not null)
        {
            var l = new TextBlock { Text = label, FontSize = 11, Width = 34, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(l, Dock.Left);
            row.Children.Add(l);
        }
        var field = new NumericUpDown { Width = fieldWidth, FontSize = 11, Value = (decimal)value, FormatString = "0" };
        DockPanel.SetDock(field, Dock.Right);
        row.Children.Add(field);
        row.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = value, Margin = new Thickness(4, 0) });
        return row;
    }

    private static Button IconButton(string icon, double size) => With(new Button { Content = Icon(icon, size) }, "icon");

    /// <summary>A section heading under the rules: quiet, sentence case, with room above.</summary>
    private static Control Section(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Margin = new Thickness(0, 6, 0, 0),
        Foreground = Brush("TextSecondaryBrush"),
    };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Put a control in a setting row's value column, right-aligned there.</summary>
    private static Control Right(Control control, int column)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>A section's actions, flat, at its end; the first may be the section's primary.</summary>
    private static Control Actions(params (string Text, bool Primary)[] actions)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, primary) in actions)
        {
            var b = With(new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) }, "text");
            if (primary) b.Classes.Add("primary");
            row.Children.Add(b);
        }
        return row;
    }

    private static CoreGradient Sunset() => new()
    {
        Name = "Sunset",
        Stops =
        [
            new CoreStop { Position = 0, Color = "#2D3A5C" },
            new CoreStop { Position = 0.45, Color = "#C98A64" },
            new CoreStop { Position = 0.7, Color = "#E07A3A" },
            new CoreStop { Position = 1, Color = "#F0C05A" },
        ],
    };

    private static IBrush SunsetBrush() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#2D3A5C"), 0),
            new GradientStop(Color.Parse("#C98A64"), 0.45),
            new GradientStop(Color.Parse("#E07A3A"), 0.7),
            new GradientStop(Color.Parse("#F0C05A"), 1),
        },
    };
}

internal static class GridExtensions
{
    /// <summary>Do something to a value and hand it back — for adding to a row built inline.</summary>
    public static T Let2<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
