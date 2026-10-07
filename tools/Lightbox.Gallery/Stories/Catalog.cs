using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using static Lightbox.Gallery.Stories.Kit;

namespace Lightbox.Gallery.Stories;

/// <summary>
/// Every story, in the order the gallery lists them. Real controls only: each
/// one is the app's own control under the app's own styles, so a look judged
/// here is a look the app would draw.
/// </summary>
public static class Catalog
{
    public static readonly IReadOnlyList<Story> All =
    [
        Light(),
        Buttons(),
        IconButtons(),
        Fields(),
        SliderPairs(),
        EffectSliders(),
        Selectors(),
        Active(),
        Tabs(),
        Dockers(),
        OverlayBars(),
        LayerRows(),
        Badges(),
        IconSheet(),
    ];

    /// <summary>
    /// The look's light, bare: an island, a raised control and a well, plus a
    /// hard-coded shadow as the control sample — if that one shows and the
    /// look's do not, the look's resources are wrong, not the renderer.
    /// </summary>
    private static Story Light() => new(
        "Light",
        "Q203's physical part, on its own: islands lit from above, raised controls, sunken wells. The last swatch is a fixed shadow, the control for the other three.",
        [
            new("Island", () => Surface("SurfacePanelBrush", "LookIslandShadow", rim: true, 120, 72)),
            new("Raised", () => Surface("SurfaceElevatedBrush", "LookRaisedShadow", rim: true, 80, 26)),
            new("Well", () => Surface("BackgroundPrimaryBrush", "LookInsetShadow", rim: false, 80, 26)),
            new("Control: fixed shadow", () => new Border
            {
                Width = 120, Height = 72, CornerRadius = new CornerRadius(6),
                Background = Brush("SurfacePanelBrush"),
                BoxShadow = BoxShadows.Parse("0 10 28 -12 #8C000000, 0 2 6 -2 #66000000"),
            }),
        ]);

    private static Control Surface(string brush, string shadow, bool rim, double w, double h)
    {
        var app = Application.Current!;
        var border = new Border
        {
            Width = w,
            Height = h,
            Background = Brush(brush),
            CornerRadius = app.TryFindResource("LookContainerRadius", out var r) && r is CornerRadius cr ? cr : new CornerRadius(6),
        };
        if (app.TryFindResource(shadow, out var s) && s is BoxShadows shadows) border.BoxShadow = shadows;
        return border;
    }

    private static Story Buttons()
    {
        var ranks = new[] { ("Default", (string?)null), ("Primary", "primary"), ("Secondary", "secondary"), ("Tertiary", "tertiary"), ("Ghost", "ghost") };
        var states = new List<StoryState>();
        foreach (var (rank, cls) in ranks)
        {
            states.Add(new($"{rank}", () => Make(cls, "Import…")));
            states.Add(new($"{rank} · hover", () => Force(Make(cls, "Import…"), ":pointerover")));
            states.Add(new($"{rank} · pressed", () => Force(Make(cls, "Import…"), ":pointerover", ":pressed")));
            states.Add(new($"{rank} · disabled", () => { var b = Make(cls, "Import…"); b.IsEnabled = false; return b; }));
        }
        return new("Buttons", "The five ranks at the text size (--row 24). Emphasis is a rank, size is a role; they compose.", states);

        static Button Make(string? rank, string text)
        {
            var b = With(new Button { Content = text }, "text");
            if (rank is not null) b.Classes.Add(rank);
            return b;
        }
    }

    private static Story IconButtons() => new(
        "Icon buttons and tools",
        "--tile 26 icon buttons and the --tool rail toggles, rest and on. The rail is where \"tools are easily found\" is won or lost.",
        [
            new("Icon", () => With(new Button { Content = Icon("IconPlus") }, "icon")),
            new("Icon · hover", () => Force(With(new Button { Content = Icon("IconPlus") }, "icon"), ":pointerover")),
            new("Icon · disabled", () => new Button { Classes = { "icon" }, Content = Icon("IconTrash"), IsEnabled = false }),
            new("Ghost close", () => With(new Button { Content = Icon("IconClose", 10) }, "icon", "ghost")),
            new("Tool", () => With(new ToggleButton { Content = Icon("IconBrush", 16) }, "tool")),
            new("Tool · hover", () => Force(With(new ToggleButton { Content = Icon("IconBrush", 16) }, "tool"), ":pointerover")),
            new("Tool · on", () => With(new ToggleButton { Content = Icon("IconBrush", 16), IsChecked = true }, "tool")),
            new("Tool rail", ToolRail),
        ]);

    private static Control ToolRail()
    {
        string[] tools = ["IconBrush", "IconEraser", "IconFill", "IconPicker", "IconSelectBox", "IconSelectLasso", "IconMove", "IconText", "IconCrop"];
        var col = new StackPanel { Spacing = 2 };
        for (var i = 0; i < tools.Length; i++)
        {
            col.Children.Add(With(new ToggleButton { Content = Icon(tools[i], 16), IsChecked = i == 0 }, "tool"));
        }
        return new Border { Classes = { "railSample" }, Padding = new Thickness(4), Child = col, Background = Brush("SurfacePanelBrush") };
    }

    private static Story Fields() => new(
        "Fields",
        "Text, numeric and combo at --row. A field is a well cut into its panel.",
        [
            new("Text", () => new TextBox { Width = 160, PlaceholderText = "Search brushes" }),
            new("Text · value", () => new TextBox { Width = 160, Text = "Colour flats" }),
            new("Text · focused", () => Force(new TextBox { Width = 160, Text = "Colour flats" }, ":focus", ":focus-within")),
            new("Numeric", () => With(new NumericUpDown { Value = 24, FormatString = "0", Minimum = 0, Maximum = 999 }, "value")),
            new("Combo", () => new ComboBox { Width = 160, ItemsSource = new[] { "Normal", "Multiply", "Screen" }, SelectedIndex = 0 }),
            new("Combo · hover", () => Force(new ComboBox { Width = 160, ItemsSource = new[] { "Normal" }, SelectedIndex = 0 }, ":pointerover")),
            new("Check", () => new CheckBox { Content = "Pressure size", IsChecked = true }),
            new("Switch", () => new ToggleSwitch { IsChecked = true, OnContent = null, OffContent = null }),
        ]);

    private static Story SliderPairs() => new(
        "Slider and field",
        "DESIGN.md: a value explored by feel gets both. Rows from the tool options bar, at the shared track length.",
        [
            new("Options bar", () => Column(6,
                Pair("Size", 24, 0, 500, "0"),
                Pair("Opacity", 100, 0, 100, "0"),
                Pair("Flow", 12, 0, 100, "0"))),
            new("Disabled", () => { var p = Pair("Hardness", 75, 0, 100, "0"); p.IsEnabled = false; return p; }),
        ],
        Columns: 2);

    private static Control Pair(string label, double value, double min, double max, string format)
    {
        var slider = With(new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center }, "param");
        var field = With(new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, FormatString = format }, "value");
        return Row(new TextBlock { Text = label, Width = 52, VerticalAlignment = VerticalAlignment.Center }, slider, field);
    }

    /// <summary>The paint colour the effect sliders preview with; the app would bind the brush's own.</summary>
    private static readonly Color Paint = Color.Parse("#E0703A");

    private static Story EffectSliders() => new(
        "Effect sliders (prototype)",
        "The owner's reference: a pill that shows what its value does, a ring thumb, the icon at the end naming it. "
        + "Opacity fades the paint in over transparency; flow fades it in over the ground, the paint each dab lays down. "
        + "With and without the number, because DESIGN.md asks for a field beside any slider explored by feel.",
        [
            new("Opacity", () => EffectRow("LookIconOpacity", 82, checker: true, field: false)),
            new("Opacity · with value", () => EffectRow("LookIconOpacity", 82, checker: true, field: true)),
            new("Flow", () => EffectRow("LookIconFlow", 12, checker: false, field: false)),
            new("Flow · with value", () => EffectRow("LookIconFlow", 12, checker: false, field: true)),
            new("In a docker", EffectDocker),
        ],
        Columns: 2);

    private static Control EffectRow(string icon, double value, bool checker, bool field)
    {
        var slider = With(new Slider { Minimum = 0, Maximum = 100, Value = value, Width = 180 }, "effect");
        if (checker) slider.Classes.Add("checker");
        slider.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, Paint.R, Paint.G, Paint.B), 0), new GradientStop(Paint, 1) },
        };
        var glyph = new Panel { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        glyph.Children.Add(new Avalonia.Controls.Shapes.Path { Classes = { "effectIcon" }, Data = (Geometry)Application.Current!.FindResource(icon)! });
        if (icon == "LookIconOpacity")
        {
            glyph.Children.Add(new Avalonia.Controls.Shapes.Path { Classes = { "effectIconFill" }, Data = (Geometry)Application.Current!.FindResource("LookIconOpacityFill")! });
        }
        var row = Row(slider);
        row.Spacing = 10;
        if (field) row.Children.Add(With(new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)value, FormatString = "0" }, "value"));
        row.Children.Add(glyph);
        return row;
    }

    private static Control EffectDocker() => new Border
    {
        Background = Brush("SurfacePanelBrush"),
        Padding = new Thickness(12),
        CornerRadius = Application.Current!.TryFindResource("LookContainerRadius", out var r) && r is CornerRadius cr ? cr : new CornerRadius(6),
        Child = Column(10,
            Row(new TextBlock { Text = "Size", Width = 52, VerticalAlignment = VerticalAlignment.Center },
                With(new Slider { Minimum = 1, Maximum = 500, Value = 24, VerticalAlignment = VerticalAlignment.Center }, "param"),
                With(new NumericUpDown { Minimum = 1, Maximum = 500, Value = 24, FormatString = "0" }, "value")),
            EffectRow("LookIconOpacity", 82, checker: true, field: true),
            EffectRow("LookIconFlow", 12, checker: false, field: true)),
    };

    private static Story Selectors() => new(
        "Selectors (prototype)",
        "Segmented choices from the reference: a well holding three to five options, the chosen one a lighter flat pill. "
        + "Text for named modes, icons where the shape is the name.",
        [
            new("Stabiliser", () => Segmented(["None", "Rope", "Average"], 1)),
            new("Timing", () => Segmented(["On 1s", "On 2s", "On 3s"], 1)),
            new("Export", () => Segmented(["PNG", "GIF", "Sheet"], 0)),
            new("Shape", () => IconSegmented(["IconShapeRect", "IconShapeEllipse", "IconShapePolygon", "IconShapeLine"], 0)),
            new("Selection", () => IconSegmented(["IconSelectBox", "IconSelectEllipse", "IconSelectLasso", "IconSelectWand"], 2)),
        ],
        Columns: 3);

    // On a panel, because that is where a selector lives: its well is the
    // ground colour, and on the ground it would vanish.
    private static Control Segmented(string[] items, int selected) =>
        OnPanel(With(new ListBox { ItemsSource = items, SelectedIndex = selected }, "segmented"));

    private static Control IconSegmented(string[] icons, int selected) =>
        OnPanel(With(new ListBox { ItemsSource = icons.Select(i => Icon(i, 14)).ToList(), SelectedIndex = selected }, "segmented", "icons"));

    private static Border OnPanel(Control child) => new()
    {
        Background = Brush("SurfacePanelBrush"),
        Padding = new Thickness(10),
        CornerRadius = Application.Current!.TryFindResource("LookContainerRadius", out var r) && r is CornerRadius cr ? cr : new CornerRadius(6),
        Child = child,
    };

    private static Story Active() => new(
        "Active",
        "What \"on\" looks like: one quiet flat fill for the tool in hand, a toggle that is on, the layer being drawn on and the menu item "
        + "under the pointer. (The glow-rim-bar effect was tried here and rejected for flat, 2026-10-07.)",
        [
            new("Tool rail", ToolRail),
            new("Layers", () => OnPanel(Column(1,
                LayerRow("Ink", false),
                LayerRow("Colour flats", true),
                LayerRow("Rough, on 2s", false)))),
            new("Menu", () => OnPanel(Column(0,
                new MenuItem { Header = "New document…", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.N, Avalonia.Input.KeyModifiers.Control) },
                Force(new MenuItem { Header = "Open…", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Control) }, ":selected"),
                new MenuItem { Header = "Save" }))),
            new("Toggles", () => OnPanel(Row(
                With(new ToggleButton { Content = "Onion skin", IsChecked = true }, "text"),
                With(new ToggleButton { Content = "Loop" }, "text")))),
        ],
        Columns: 3);

    private static Story Tabs() => new(
        "Tabs",
        "Docker tabs and section tabs.",
        [
            new("Docker tabs", () => TabList("tabs", "Layers", "Channels", "History")),
            new("Section tabs", () => TabList("sectiontabs", "Timeline", "X-sheet", "Graph editor")),
        ],
        Columns: 2);

    private static ListBox TabList(string cls, params string[] items)
    {
        var list = With(new ListBox { ItemsSource = items, SelectedIndex = 0 }, cls);
        list.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
        return list;
    }

    private static Story Dockers() => new(
        "Docker",
        "The real Docker control: tabbed header, content. Hover shows what a docker offers when the pointer is in it.",
        [
            new("Rest", () => Docker(hover: false)),
            new("Hover", () => Docker(hover: true)),
        ],
        Columns: 2);

    private static Control Docker(bool hover)
    {
        var docker = new Docker
        {
            PanelId = DockPanelId.Layers,
            Width = 280,
            Height = 220,
            CanFloat = true,
            Content = Column(6,
                Row(new ComboBox { Width = 120, ItemsSource = new[] { "Normal" }, SelectedIndex = 0 }),
                Pair("Opacity", 82, 0, 100, "0"),
                LayerRow("Ink", false),
                LayerRow("Colour flats", true),
                LayerRow("Rough, on 2s", false)),
        };
        docker.ShowTabs([DockPanels.Of(DockPanelId.Layers), DockPanels.Of(DockPanelId.Channels), DockPanels.Of(DockPanelId.History)], DockPanelId.Layers);
        return hover ? Force(docker, ":pointerover") : docker;
    }

    private static Story OverlayBars() => new(
        "Canvas bar",
        "The bar that floats over the canvas, on a top edge and on a side edge. Only icons show; the one that is on gets a fill "
        + "that fits the bar. Anything here repaints with every canvas frame.",
        [
            new("Top edge", () => OverlayBar(CanvasEdge.Top)),
            new("Side edge", () => OverlayBar(CanvasEdge.Right)),
        ],
        Columns: 2);

    private static Control OverlayBar(CanvasEdge edge)
    {
        var bar = new CanvasOverlayBar { Title = "View", Edge = edge };
        var buttons = new StackPanel { Spacing = 2, Orientation = edge is CanvasEdge.Left or CanvasEdge.Right ? Orientation.Vertical : Orientation.Horizontal };
        buttons.Children.Add(With(new Button { Content = Icon("IconMinus") }, "icon"));
        buttons.Children.Add(new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 });
        buttons.Children.Add(With(new Button { Content = Icon("IconPlus") }, "icon"));
        buttons.Children.Add(With(new Button { Content = Icon("IconRotateCw") }, "icon"));
        buttons.Children.Add(With(new Button { Content = Icon("IconMirrorHorizontal") }, "icon"));
        buttons.Children.Add(With(new ToggleButton { Content = Icon("IconOnionOn"), IsChecked = true }, "icon"));
        buttons.Children.Add(With(new ToggleButton { Content = Icon("IconLoop") }, "icon"));
        bar.Content = buttons;
        // On a painting, because that is where it lives.
        return new Border { Background = Painting(), Padding = new Thickness(16), Child = bar };
    }

    private static Story LayerRows() => new(
        "Layer rows",
        "Assembled from the app's row classes (Border.layerRow, ToggleButton.eye), not its data template, which needs a document. Rest, active, hidden, locked.",
        [
            new("Rows", () => Column(1,
                LayerRow("Ink", false),
                LayerRow("Colour flats", true),
                With(LayerRow("Rough, on 2s", false), "hiddenLayer"),
                With(LayerRow("Paper", false), "lockedLayer"))),
        ],
        Columns: 1);

    private static Border LayerRow(string name, bool active)
    {
        var eye = With(new ToggleButton { IsChecked = true, Content = Icon("IconEyeOpen", 12) }, "eye", "icon");
        var thumb = new Border { Width = 26, Height = 18, Background = Painting() };
        var row = new Border
        {
            Classes = { "layerRow" },
            Width = 260,
            Child = Row(eye, Icon("IconLockOpen", 11), thumb, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }),
        };
        if (active) row.Classes.Add("active");
        return row;
    }

    private static Story Badges() => new(
        "Badges",
        "Status, the one place colour is allowed to mean something in the chrome.",
        [
            new("Info", () => Badge("info", "Info")),
            new("Warning", () => Badge("warning", "Warning")),
            new("Error", () => Badge("error", "Error")),
            new("Success", () => Badge("success", "Success")),
        ]);

    private static Border Badge(string kind, string text) =>
        new() { Classes = { "badge", kind }, Child = new TextBlock { Text = text } };

    private static Story IconSheet() => new(
        "Icon set",
        "Every geometry in Icons.axaml at the app's sizes — the one icon language the 52 glyph icons still need to join.",
        [new("Icons.axaml", () => IconGrid(IconKeys()))],
        Columns: 1);

    /// <summary>
    /// Read off the loaded resources rather than listed here, so a new icon
    /// appears in the sheet without anybody remembering to add it.
    /// </summary>
    private static IReadOnlyList<string> IconKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        Walk(Application.Current!.Resources);
        return [.. keys];

        void Walk(IResourceProvider provider)
        {
            var dict = provider switch
            {
                Avalonia.Markup.Xaml.Styling.ResourceInclude include => include.Loaded as IResourceDictionary,
                IResourceDictionary d => d,
                _ => null,
            };
            if (dict is null) return;
            foreach (var (key, value) in dict)
            {
                if (key is string name && name.StartsWith("Icon", StringComparison.Ordinal) && value is Geometry) keys.Add(name);
            }
            foreach (var merged in dict.MergedDictionaries) Walk(merged);
        }
    }

    private static Control IconGrid(IReadOnlyList<string> keys)
    {
        var wrap = new WrapPanel { ItemWidth = 72, ItemHeight = 52 };
        foreach (var key in keys)
        {
            wrap.Children.Add(Column(4,
                new Border { Height = 20, Child = Icon(key, 16) },
                new TextBlock { Text = key.Replace("Icon", ""), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "stateLabel" } }));
        }
        return new Border { Width = 800, Child = wrap };
    }

    private static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;

    /// <summary>A stand-in painting: dusk, so chrome over it can be judged against something warm and busy.</summary>
    private static IBrush Painting() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#2d3a5c"), 0),
            new GradientStop(Color.Parse("#6b5c7c"), 0.35),
            new GradientStop(Color.Parse("#c98a64"), 0.6),
            new GradientStop(Color.Parse("#e7b07a"), 0.7),
            new GradientStop(Color.Parse("#2c2730"), 1),
        },
    };
}
