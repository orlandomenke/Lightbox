using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Lightbox.App.Controls;
using Lightbox.Core.Documents;
using static Lightbox.Gallery.Stories.Kit;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lightbox.Gallery.Stories;

/// <summary>
/// The brush tool's options, as built and split the way Krita splits them.
/// </summary>
/// <remarks>
/// <para>
/// As built, one docker carries 49 settings over five pages (General, Effects,
/// Medium, Pen pressure, Presets), the quick bar repeats four of them, and the
/// pressure curves live on a page of their own, away from the setting each one
/// drives. Inventory taken 2026-10-07 from MainWindow.axaml.
/// </para>
/// <para>
/// Krita answers the same job in three places, each with one kind of thing in
/// it: the <b>toolbar</b> holds what changes every few strokes (preset, size,
/// opacity, flow, blend, eraser, alpha lock); the <b>Tool Options docker</b>
/// holds how the tool behaves, never how the brush looks (smoothing,
/// stabiliser, assistants); and the <b>brush editor</b>, a popup from the
/// toolbar, holds the preset: a list of options each with an on/off check,
/// the selected option's panel, and every option's own sensor curves
/// (pressure, tilt, speed) beside the value they drive. Saving is explicit:
/// reload, overwrite, save new.
/// </para>
/// <para>
/// The recreation below files every one of today's settings into those three
/// homes. It is a layout study, not a decision: what moves where is the
/// question it puts to the owner.
/// </para>
/// </remarks>
public static partial class Catalog
{
    private static Story BrushOptionsAsBuilt() => new(
        "Brush options · 1 · as built",
        "One docker, five pages, 49 settings; the quick bar repeats Size, Opacity, Hardness and the smudge values, and the pressure "
        + "curves live on a page apart from what they drive. This is the Effects page, 23 controls long.",
        [new("Effects page", () => Panel("Tool options", BrushAsBuilt(), 300, 760, asBuilt: true))],
        Columns: 1);

    private static Story BrushOptionsBar() => new(
        "Brush options · 2 · Krita's bar and tool options",
        "Krita's toolbar holds what changes every few strokes: the preset, the editor that opens it, blend, eraser, alpha lock, size, "
        + "opacity, flow. Its Tool Options docker holds how the tool behaves and never how the brush looks: smoothing, the pen, edges.",
        [
            new("The bar", BrushBar),
            new("Tool options · behaviour only", () => Panel("Tool options", BrushToolBehaviour(), 300, 340, asBuilt: false)),
        ],
        Columns: 1);

    private static Story BrushOptionsEditor() => new(
        "Brush options · 3 · Krita's brush editor",
        "The preset, whole, in a popup from the bar: the name and its three save verbs; an option list where a check says whether the "
        + "option takes part; the chosen option's panel with its own pressure, tilt and speed curves beside the value; a scratchpad.",
        [new("Size selected", BrushEditor)],
        Columns: 1);

    private static Story BrushOptionsHomes() => new(
        "Brush options · 4 · where every setting goes",
        "Today's settings filed into the three homes. The table is the proposal; nothing is removed, only moved.",
        [new("The map", BrushHomes)],
        Columns: 1);

    // ---- As built ------------------------------------------------------------------

    /// <summary>Today's Effects page beside its category list: the one the owner called monstrous.</summary>
    private static Control BrushAsBuilt()
    {
        var categories = new ListBox
        {
            Width = 110,
            ItemsSource = new[] { "General", "Effects", "Medium", "Pen pressure", "Presets" },
            SelectedIndex = 1,
            Background = Brush("BackgroundSecondaryBrush"),
        };
        var page = new StackPanel { Spacing = 6 };
        void Head(string t) => page.Children.Add(BuiltHeading(t));
        void Slide(string t, double v) => page.Children.Add(BuiltDockerRow(t, v));
        Head("Shape dynamics");
        Slide("Size jitter", 0); Slide("Minimum size", 20); Slide("Roundness", 100); Slide("Roundness jitter", 0); Slide("Angle jitter", 0);
        page.Children.Add(new CheckBox { Content = "Angle follows stroke direction" });
        Head("Scattering"); Slide("Scatter", 0);
        Head("Texture");
        page.Children.Add(BuiltLabelRow("Surface", new ComboBox { ItemsSource = new[] { "None", "Cold press", "Canvas" }, SelectedIndex = 0 }));
        page.Children.Add(new Button { Content = "Paper image…" });
        Slide("Grain size", 40); Slide("Depth", 50); Slide("Tooth", 0); Slide("Granulation", 0); Slide("Wet edge", 0);
        Head("Transfer"); Slide("Flow jitter", 0);
        Head("Color dynamics");
        page.Children.Add(BuiltLabelRow("Second color", new Border { Height = 20, Background = Brush("SurfaceElevatedBrush") }));
        Slide("Toward second", 0); Slide("Hue jitter", 0); Slide("Saturation jitter", 0); Slide("Brightness jitter", 0);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,6,*") };
        grid.Children.Add(categories);
        var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        grid.Children.Add(scroll);
        return grid;
    }

    /// <summary>A docker row as built: a 90 px label, a slider, a growing field.</summary>
    private static Control BuiltDockerRow(string label, double value)
    {
        var row = new DockPanel();
        var l = new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        var field = new NumericUpDown { MinWidth = 44, Value = (decimal)value, FormatString = "0" };
        DockPanel.SetDock(field, Dock.Right);
        row.Children.Add(field);
        row.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = value, Margin = new Thickness(4, 0) });
        return row;
    }

    // ---- The bar -------------------------------------------------------------------

    /// <summary>
    /// What changes every few strokes, and nothing else: the preset, the editor
    /// that opens it, blend, eraser, alpha lock, then size, opacity and flow.
    /// Hardness goes to the tip (Krita keeps it there); the stabiliser goes to
    /// the tool's own options.
    /// </summary>
    private static Control BrushBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(PresetChip("Ink pen", modified: true));
        bar.Children.Add(With(Tip(With(new Button { Content = Icon("IconGear", 13) }, "icon"), "Edit this brush (F5)"), "ghost"));
        bar.Children.Add(Divider());
        bar.Children.Add(new ComboBox { ItemsSource = new[] { "Normal", "Multiply", "Screen", "Overlay" }, SelectedIndex = 0, Width = 88 });
        bar.Children.Add(With(Tip(new ToggleButton { Content = Icon("IconEraser", 13) }, "Erase with this brush (E)"), "icon"));
        bar.Children.Add(With(Tip(new ToggleButton { Content = Icon("IconAlphaLock", 13) }, "Lock the layer's transparency"), "icon"));
        bar.Children.Add(Divider());
        bar.Children.Add(BarValue("Size", 24, 1, 500, null, 90));
        bar.Children.Add(BarValue("Opacity", 100, 0, 100, "LookIconOpacity", 90));
        bar.Children.Add(BarValue("Flow", 45, 0, 100, "LookIconFlow", 90));
        return new Border
        {
            Background = Brush("SurfacePanelBrush"),
            CornerRadius = Radius("LookContainerRadius"),
            Padding = new Thickness(8, 3),
            Child = bar,
        };
    }

    private static Control PresetChip(string name, bool modified)
    {
        var dot = Tip(new Ellipse { Width = 6, Height = 6, Fill = Brush("TextPrimaryBrush"), IsVisible = modified, VerticalAlignment = VerticalAlignment.Center }, "Changed since it was saved");
        return Tip(new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(4, 2),
            Content = Row(StrokeThumb(56, 22), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }, dot, Icon("IconChevronDown", 8)),
        }, "Brush presets (F6)");
    }

    private static T Tip<T>(T control, string tip) where T : Control
    {
        ToolTip.SetTip(control, tip);
        return control;
    }

    private static Control BarValue(string label, double value, double min, double max, string? icon, double width)
    {
        var slider = icon is null ? new Slider { Minimum = min, Maximum = max, Value = value } : EffectSlider(value, icon == "LookIconOpacity");
        slider.Width = width;
        slider.MinWidth = 0; // the effect slider's own floor is a docker's, not a bar's
        slider.VerticalAlignment = VerticalAlignment.Center;
        // Opacity and flow are named by their icon, as on the effect sliders;
        // size by its word, because no glyph says "size" better than the number.
        Control name = icon is null
            ? new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("TextSecondaryBrush") }
            : Tip(RowIcon(icon), label);
        return Row(name, slider, ValueField(value, min, max));
    }

    private static Control Divider() => new Border { Width = 1, Height = 18, Background = Brush("BorderStrongBrush"), VerticalAlignment = VerticalAlignment.Center };

    // ---- Tool options: behaviour only ----------------------------------------------

    /// <summary>How the tool behaves, never how the brush looks: Krita's Tool Options for its freehand brush.</summary>
    private static Control BrushToolBehaviour() => Rows(
        Section("Smoothing"),
        SettingRow(SegmentedBare(["Off", "Average", "String", "Weighted"], 2), null, null),
        SettingRow(Label("Lazy radius"), ValueField(24, 4, 200), null),
        SwitchRow("This brush keeps its own", false),
        Section("Pen"),
        SwitchRow("Brush ring follows pressure", true),
        SettingRow(new TextBlock { Text = "Pressure 0.62 · tilt 41° · Windows Ink", FontSize = 11, Foreground = Brush("TextSecondaryBrush") }, null, null),
        Section("Edges"),
        SwitchRow("Anti-aliasing, every brush", true));

    // ---- The brush editor ----------------------------------------------------------

    /// <summary>
    /// The preset, whole: a header with the name, its preview and the three
    /// save verbs; the option list with a check each; the chosen option's panel
    /// with its own curves; a scratchpad to try it on.
    /// </summary>
    private static Control BrushEditor()
    {
        var header = new DockPanel { LastChildFill = true };
        var verbs = Row(
            With(new Button { Content = "Reload" }, "text", "ghost"),
            With(new Button { Content = "Overwrite" }, "text"),
            With(new Button { Content = "Save new…" }, "text", "primary"));
        DockPanel.SetDock(verbs, Dock.Right);
        header.Children.Add(verbs);
        header.Children.Add(Row(
            StrokeThumb(120, 34),
            Column(2,
                new TextBlock { Text = "Ink pen", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Changed since it was saved · Round tip · Ink", FontSize = 11, Foreground = Brush("TextSecondaryBrush") })));

        var options = new ListBox
        {
            Width = 170,
            SelectedIndex = 1,
            ItemsSource = new Control[]
            {
                OptionItem("Tip", true, locked: true),
                OptionItem("Size", true),
                OptionItem("Opacity", true),
                OptionItem("Flow", true),
                OptionItem("Spacing", true),
                OptionItem("Shape dynamics", false),
                OptionItem("Scatter", false),
                OptionItem("Texture", false),
                OptionItem("Colour dynamics", false),
                OptionItem("Smudge", false, enabled: false),
                OptionItem("Medium", false),
                OptionItem("Blend", true, locked: true),
            },
        };

        var panel = Rows(
            new TextBlock { Text = "Size", FontSize = 15, FontWeight = FontWeight.SemiBold },
            SettingRow(Label("Diameter"), ValueField(24, 1, 500), null),
            SettingRow(Label("Smallest"), ValueField(20, 0, 100), null),
            Section("Driven by"),
            SensorRow("Pressure", true, selected: true),
            SensorRow("Tilt", false),
            SensorRow("Speed", false),
            new Border
            {
                Background = Brush("FieldGroundBrush"),
                CornerRadius = Radius("LookControlRadius"),
                Height = 150,
                Child = new CurveEditor { Curve = SoftKnee(), IsActive = true, Margin = new Thickness(6) },
            },
            new TextBlock
            {
                Text = "Light pressure at the left, full at the right. The curve is this option's own: Flow keeps a different one.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSecondaryBrush"),
            });

        var scratch = new Border
        {
            Width = 220,
            Background = Brush("CanvasSurroundBrush"),
            CornerRadius = Radius("LookControlRadius"),
            Child = new Grid
            {
                Children =
                {
                    new Border { Margin = new Thickness(10), Background = Brushes.White, CornerRadius = Radius("LookControlInnerRadius") },
                    new Path
                    {
                        Data = Geometry.Parse("M20,60 C60,20 90,140 140,70 S170,150 185,110"),
                        Stroke = Brushes.Black, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round, Margin = new Thickness(0, 30, 0, 0),
                    },
                    new TextBlock { Text = "Scratchpad", FontSize = 11, Margin = new Thickness(16, 14), Foreground = Brushes.Gray },
                },
            },
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("170,16,*,16,220"), Height = 420 };
        body.Children.Add(options);
        var panelScroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(panelScroll, 2);
        body.Children.Add(panelScroll);
        Grid.SetColumn(scratch, 4);
        body.Children.Add(scratch);

        return new Border
        {
            Width = 900,
            Background = Brush("SurfaceElevatedBrush"),
            CornerRadius = Radius("LookContainerRadius"),
            Padding = new Thickness(16),
            Child = Column(14, header, body),
        };
    }

    /// <summary>An option in the list: its check says whether it takes part at all.</summary>
    private static Control OptionItem(string name, bool on, bool locked = false, bool enabled = true)
    {
        var check = new CheckBox { IsChecked = on, IsEnabled = enabled && !locked, MinWidth = 0, Padding = new Thickness(6, 0, 0, 0), Content = name };
        if (!enabled) ToolTip.SetTip(check, "Only for smudge brushes");
        if (locked) ToolTip.SetTip(check, "Every brush has one");
        return check;
    }

    private static Control SensorRow(string name, bool on, bool selected = false)
    {
        var row = SettingRow(new CheckBox { Content = name, IsChecked = on }, null, null);
        if (selected)
        {
            row.Background = Brush("ActiveFillBrush");
        }
        return row;
    }

    private static ResponseCurve SoftKnee() => new() { Points = [new(0, 0.2), new(0.45, 0.55), new(1, 1)] };

    /// <summary>A stand-in for a preset's stroke preview: a swelling line on paper.</summary>
    private static Control StrokeThumb(double width, double height) => new Border
    {
        Width = width,
        Height = height,
        Background = Brushes.White,
        CornerRadius = Radius("LookControlInnerRadius"),
        Child = new Path
        {
            Data = Geometry.Parse($"M4,{height * 0.7:0} C{width * 0.3:0},{height * 0.1:0} {width * 0.6:0},{height:0} {width - 4:0},{height * 0.35:0}"),
            Stroke = Brushes.Black,
            StrokeThickness = 2.5,
            StrokeLineCap = PenLineCap.Round,
        },
    };

    // ---- Where every setting goes --------------------------------------------------

    /// <summary>Today's 49 settings, filed into the three homes. The table is the proposal.</summary>
    private static Control BrushHomes()
    {
        (string Home, string Today, string What)[] rows =
        [
            ("Bar", "quick bar + General", "Preset · Size · Opacity · Flow · Blend · Eraser · Alpha lock"),
            ("Tool options", "quick bar + Pen pressure + General", "Smoothing mode and its value · Per-brush smoothing · Ring follows pressure · Anti-aliasing (global)"),
            ("Editor · Tip", "General + quick bar", "Tip · Hardness · Roundness · Angle follows stroke"),
            ("Editor · Size", "quick bar + Effects + Pen pressure", "Diameter · Smallest · Size jitter · pressure/tilt/speed curves"),
            ("Editor · Opacity, Flow", "General + Effects + Pen pressure", "Value · Flow jitter · curves"),
            ("Editor · Spacing", "General", "Spacing"),
            ("Editor · Shape dynamics", "Effects + Pen pressure", "Roundness jitter · Angle jitter · roundness curve"),
            ("Editor · Scatter", "Effects + Pen pressure", "Scatter · scatter curve"),
            ("Editor · Texture", "Effects", "Surface · Paper image · Grain size · Depth · Tooth · Granulation · Wet edge"),
            ("Editor · Colour dynamics", "Effects", "Second colour · Toward second · Hue · Saturation · Brightness jitter"),
            ("Editor · Smudge", "Effects + Pen pressure", "Mode · Length · Radius · Colour rate · their curves (smudge brushes only)"),
            ("Editor · Medium", "Medium", "Medium · Fluid (6) · Pigment (4) · Paper (3) · Interaction (3) · Body (3)"),
            ("Editor · header", "Presets", "Name · Reload · Overwrite · Save new · Tags · Delete"),
        ];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,200,*"), Width = 840 };
        var r = 0;
        void Cell(string text, int col, bool head)
        {
            var t = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 12, 4),
                FontWeight = head ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = col == 1 ? Brush("TextSecondaryBrush") : Brush("TextPrimaryBrush"),
            };
            Grid.SetRow(t, r);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell("Home", 0, true); Cell("Today", 1, true); Cell("Settings", 2, true);
        foreach (var (home, today, what) in rows)
        {
            r++;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(home, 0, false); Cell(today, 1, false); Cell(what, 2, false);
        }
        return OnPanel(grid);
    }
}
