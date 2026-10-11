using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using SkiaSharp;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Lightbox.App.Tests;

/// <summary>
/// Where the icons landed, and what they cost to have.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IconSetTests"/> asks whether every icon exists. This file asks the
/// two things that were never measured: where an icon ends up inside its button
/// once layout has rounded it, and how many of them a window carries. Both are
/// read off the laid-out main window by <see cref="IconCensus"/>, which is also
/// what the performance lab's <c>icon-buttons</c> scenario reads on the real
/// window — this is the 100%-scaling half, that one is the owner's display.
/// </para>
/// <para>
/// <b>What the baseline of 2026-10-10 found, so the limits below are not
/// folklore.</b> Stroking an icon costs about 7 µs (median; 24 µs for the grip,
/// the heaviest), so every icon a 30-layer window shows is about 1.6 ms of a
/// full repaint — cheap, and not worth a bitmap cache that would cost the
/// set its themeable stroke. What grows is the <em>count</em>: a layer row
/// realizes about thirteen paths and shows five, the rest being the link and
/// shape brackets waiting invisibly. Removing those brackets outright did not
/// move the time to build thirty rows outside its own run-to-run noise, so they
/// stay; the count is pinned so that it is a decision the next time it grows.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class IconPlacementTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump()
    {
        for (var i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, MainViewModel Vm) Open(int layers)
    {
        var window = new MainWindow { Width = 1920, Height = 1080 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < layers) vm.AddPaintedLayerCommand.Execute(null);
        Pump();
        return (window, vm);
    }

    // ---- placement -----------------------------------------------------------------------

    /// <summary>
    /// Every icon the main window shows is on whole pixels, inside its button,
    /// and — where it is the button's whole content — within half a pixel of
    /// the centre.
    /// </summary>
    /// <remarks>
    /// <b>Exactly, for the two sizes the styles give</b> — 12 for an icon, 16
    /// for a stateful pair (Q241). They were 13 and 15, whose centre in a 26
    /// tile is a half pixel, so layout rounding picked a side: 96 of 111 icons
    /// sat half a pixel off, and the same icon in the same tile landed on
    /// either side depending on where the tile was. The inline one-offs (a 9 px
    /// caption close, the stacked reorder chevrons in their 11 px halves) are
    /// still allowed their half pixel, and are printed so the list is in view.
    /// <para>
    /// <b>What this cannot see, said here so nobody reads more into a pass.</b>
    /// An icon that rounding left half a pixel one way and somebody then moved
    /// one pixel the other way still reads half a pixel off; only the exactly
    /// centred ones (pinned below, so that count cannot fall) catch a one-pixel
    /// nudge. An icon in no button — the Quick options bar's — is counted and
    /// checked for the pixel grid, and has no centre to be judged against. An
    /// icon beside a label is not centred by design. Menus and flyouts are
    /// their own popup roots and are not walked. A button that sizes to its
    /// content grows round an oversized icon instead of spilling it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void EveryIconSitsOnWholePixelsInsideItsButtonAndCentred()
    {
        var (window, _) = Open(layers: 11);
        IReadOnlyList<IconPlacement> icons;
        try
        {
            icons = IconCensus.Take(window);
        }
        finally
        {
            window.Close();
        }

        var lone = icons.Where(i => i.Alone).ToList();
        var exact = lone.Count(i => Math.Abs(i.OffCentreX) < 0.01 && Math.Abs(i.OffCentreY) < 0.01);
        output.WriteLine($"{icons.Count} icons, {lone.Count} alone in a button, {exact} exactly centred, " +
                         $"{lone.Count - exact} half a pixel off");
        foreach (var g in lone.GroupBy(i => FormattableString.Invariant(
                         $"{i.Width:0.#} in {i.HostWidth:0.#}x{i.HostHeight:0.#} off by {i.OffCentreX:+0.#;-0.#;0},{i.OffCentreY:+0.#;-0.#;0}"))
                     .OrderByDescending(g => g.Count()))
        {
            output.WriteLine($"  {g.Count(),3}x {g.Key}  ({string.Join(", ", g.Select(i => i.Name).Distinct().Take(4))})");
        }

        // Enough of them that an empty census cannot pass for a clean one.
        Assert.True(icons.Count >= 80, $"only {icons.Count} icons were found; the census is not seeing the window");
        Assert.True(exact >= ExactlyCentred, $"{exact} icons are exactly centred, under the {ExactlyCentred} that were");
        Assert.Empty(icons.Where(i => i.OffPixel > 0.01).Select(i => $"{i.Name} in {i.Host}: {i.OffPixel:0.##} px off the grid"));
        Assert.Empty(icons.Where(i => i.Spill > 0.01).Select(i => $"{i.Name} in {i.Host}: spills {i.Spill:0.##} px"));
        Assert.Empty(lone.Where(i => (i.Width is 12 or 16 ? 0.01 : 0.51) < Math.Max(Math.Abs(i.OffCentreX), Math.Abs(i.OffCentreY)))
            .Select(i => $"{i.Name} in {i.Host}: off centre by {i.OffCentreX:0.##},{i.OffCentreY:0.##}"));
    }

    /// <summary>Every icon at a class size, on 2026-10-10; the 25 left over are inline one-offs.</summary>
    private const int ExactlyCentred = 86;

    /// <summary>
    /// The census reports an icon that is pushed off centre, off the pixel grid
    /// or out of its button — so the test above is not passing on a measure
    /// that cannot see.
    /// </summary>
    [AvaloniaFact]
    public void TheCensusSeesAnIconThatIsMisplaced()
    {
        IconPlacement Place(Thickness margin, double size)
        {
            var path = new ShapePath
            {
                Data = IconSet.Resolve(IconSet.Plus), Width = size, Height = size, Stretch = Stretch.Uniform,
                Margin = margin, UseLayoutRounding = false,
            };
            var button = new Button
            {
                Width = 26, Height = 26, Padding = default, Content = path, UseLayoutRounding = false,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            var window = new Window { Width = 200, Height = 200, Content = button, UseLayoutRounding = false };
            window.Show();
            try
            {
                Pump();
                return Assert.Single(IconCensus.Take(window));
            }
            finally
            {
                window.Close();
            }
        }

        var centred = Place(default, 12);
        var pushed = Place(new Thickness(6, 0, 0, 0), 12);
        var halfPixel = Place(default, 13);
        var tooBig = Place(default, 40);
        output.WriteLine(FormattableString.Invariant(
            $"centred {centred.OffCentreX:0.##},{centred.OffCentreY:0.##}; pushed {pushed.OffCentreX:0.##}; 13 in 26 without rounding {halfPixel.OffPixel:0.##} off the grid; 40 in 26 spills {tooBig.Spill:0.##}"));

        Assert.True(centred.Alone);
        Assert.Equal(0, centred.OffCentreX, 2);
        Assert.Equal(0, centred.OffPixel, 2);
        Assert.Equal(0, centred.Spill, 2);
        Assert.Equal(3, pushed.OffCentreX, 2);
        Assert.Equal(0.5, halfPixel.OffPixel, 2);
        Assert.True(tooBig.Spill > 6, $"a 40 px icon in a 26 px button spilt only {tooBig.Spill}");
    }

    // ---- what a window carries -----------------------------------------------------------

    /// <summary>
    /// A layer costs the window no more paths than it does today.
    /// </summary>
    /// <remarks>
    /// A count, not a time, so it cannot flake: the paths realized by twenty
    /// more layers, divided by twenty. Nothing virtualizes the layer list, so
    /// whatever a row carries is carried once per layer for the life of the
    /// document — one more always-realized, usually-hidden glyph in the row
    /// template is two hundred controls in a two-hundred-layer file. It counts
    /// the panels a fresh workspace shows: a row template in a hidden panel is
    /// not built, and so not counted.
    /// </remarks>
    [AvaloniaFact]
    public void ALayerRowRealizesNoMorePathsThanItDoesToday()
    {
        (int Realized, int Visible) Count(int layers)
        {
            var (window, _) = Open(layers);
            try
            {
                return IconCensus.CountPaths(window);
            }
            finally
            {
                window.Close();
            }
        }

        var (fewRealized, fewVisible) = Count(layers: 1);
        var (manyRealized, manyVisible) = Count(layers: 21);

        var perLayer = (manyRealized - fewRealized) / 20.0;
        var visiblePerLayer = (manyVisible - fewVisible) / 20.0;
        output.WriteLine(FormattableString.Invariant(
            $"1 layer: {fewRealized} paths ({fewVisible} visible); 21 layers: {manyRealized} ({manyVisible} visible); per layer {perLayer:0.0} realized, {visiblePerLayer:0.0} visible"));

        Assert.True(visiblePerLayer >= 3, $"a layer row shows {visiblePerLayer} icons; the rows are not being built");
        Assert.True(perLayer <= PathsPerLayer, $"a layer now realizes {perLayer} paths, over the {PathsPerLayer} it did");
    }

    /// <summary>What a layer row realized on 2026-10-10: five shown, eight brackets hidden.</summary>
    private const double PathsPerLayer = 13;

    // ---- what one costs to draw ----------------------------------------------------------

    /// <summary>
    /// No icon is a heavier path than the heaviest one in the set today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stroke cost follows the number of segments — the grip's six dots are 38
    /// verbs and 24 µs, a minus sign is 4 and 2 µs — so the guard is the count,
    /// which is exact, and the timing is printed beside it rather than asserted.
    /// An icon pasted in from an export tool arrives with hundreds of segments
    /// and looks identical at 13 px.
    /// </para>
    /// <para>
    /// It also requires every path to parse as SVG path data, which is the
    /// grammar a redraw of the set will be exchanged in.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoIconIsAHeavierPathThanTheGrip()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Lightbox.App", "Styles", "Icons.axaml"));
        var drawn = Regex.Matches(xaml, @"x:Key=""(Icon[A-Za-z]+)"">([^<]*)<")
            .Select(m => (Name: m.Groups[1].Value, Path: SKPath.ParseSvgPathData(m.Groups[2].Value)))
            .ToList();
        Assert.Equal(IconSet.All.Count, drawn.Count);
        Assert.Empty(drawn.Where(d => d.Path is null).Select(d => d.Name));

        using var surface = SKSurface.Create(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.25f,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = SKColors.White,
        };
        var cost = new List<(string Name, int Verbs, double Micros)>();
        foreach (var (name, path) in drawn)
        {
            using var scaled = new SKPath(path);
            scaled.Transform(SKMatrix.CreateScale(13f / 24f, 13f / 24f));
            var best = double.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < 500; i++) surface.Canvas.DrawPath(scaled, paint);
                surface.Flush();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / 500);
            }
            cost.Add((name, path!.VerbCount, best));
            path.Dispose();
        }

        cost.Sort((a, b) => b.Verbs.CompareTo(a.Verbs));
        var micros = cost.Select(c => c.Micros).OrderBy(v => v).ToList();
        output.WriteLine(FormattableString.Invariant(
            $"{cost.Count} icons at 13 px: {micros.Sum():0} us for the set, median {micros[micros.Count / 2]:0.0} us, worst {micros[^1]:0.0} us"));
        foreach (var (name, verbs, us) in cost.Take(5))
        {
            output.WriteLine(FormattableString.Invariant($"  {name,-20} {verbs,3} verbs  {us:0.0} us"));
        }

        Assert.Empty(cost.Where(c => c.Verbs > HeaviestIconVerbs).Select(c => $"{c.Name}: {c.Verbs} verbs"));
    }

    /// <summary>The grip, on 2026-10-10: two moves for the grid and six dots.</summary>
    private const int HeaviestIconVerbs = 38;
}
