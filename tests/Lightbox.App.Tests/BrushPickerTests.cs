using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush picker, as a grid of marks rather than a list of names.
/// </summary>
/// <remarks>
/// <para>
/// The complaint this answers: with a collection imported there is no way to tell what a
/// brush does before drawing with it, because the only thing on offer is a name somebody
/// else chose. So the tile carries a real stroke rendered by the real engine.
/// </para>
/// <para>
/// These are window tests because what changed is the wiring: the list's items are no
/// longer presets but tiles wrapping them, and the pick handler reads the preset back out
/// of the tile. A view-model assertion would pass on a build where the handler still
/// pattern-matched <c>BrushPreset</c> and therefore never fired.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class BrushPickerTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <remarks>Wide, or the tool options bar overflows the picker button into a dropdown.</remarks>
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Pump();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Open the flyout the way an artist does, by clicking the button.</summary>
    private static ListBox OpenPicker(MainWindow window)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "BrushPickerButton");
        var at = button.TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Pump();

        return window.FindControl<ListBox>("BrushPresetList")
            ?? throw new InvalidOperationException("the brush picker list is not in the window");
    }

    private static List<BrushChoice> Tiles(ListBox list) =>
        (list.ItemsSource as IEnumerable<BrushChoice>)?.ToList()
        ?? throw new InvalidOperationException(
            $"the picker is showing {list.ItemsSource?.GetType().Name ?? "nothing"}, not tiles");

    [AvaloniaFact]
    public void OpeningThePickerFillsItWithTilesRatherThanBarePresets()
    {
        // The wiring, stated as the thing that would break silently: a list of presets
        // still renders — the template just finds no bindings — and the pick handler
        // stops matching. Both halves have to move together.
        var (window, _) = Open();

        var tiles = Tiles(OpenPicker(window));

        Assert.NotEmpty(tiles);
        Assert.All(tiles, t => Assert.NotNull(t.Preset));
    }

    [AvaloniaFact]
    public void EveryBrushOnOfferHasAPictureOfItsMark()
    {
        // The whole feature. A tile with no picture is the old list with extra spacing,
        // and the built-ins are where a missing renderer would show up first.
        var (window, _) = Open();

        var tiles = Tiles(OpenPicker(window));
        var blank = tiles.Where(t => t.Preview is null).Select(t => t.Name).ToList();

        Assert.True(blank.Count == 0, $"no mark was drawn for: {string.Join(", ", blank)}");
    }

    /// <summary>
    /// How many channel values a brush moves against the bare ground it is drawn on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendered through <see cref="BrushPreviewRenderer"/> rather than read back off the
    /// tile, and that is not a shortcut — <b>an Avalonia <c>Bitmap</c> has no readable pixels
    /// under headless drawing.</b> Its <c>PixelSize</c> comes back as 1×1 and
    /// <c>CopyPixels</c> hands over four bytes of nothing, so a comparison of two tiles
    /// passes or fails on garbage. One of these tests did pass that way before the sizes
    /// were printed, which is the whole reason this is written down.
    /// </para>
    /// <para>
    /// The ground comes from the renderer, because the obvious way to fake it is wrong: the
    /// same brush with flow and opacity at zero looked like the right baseline and reported
    /// <em>zero</em> difference for watercolour, on a tile that visibly had a mark on it — a
    /// simulated medium deposits pigment through a flow of zero. Hard-coding a backdrop here
    /// instead would re-state the renderer's choice (paper, or a test pattern for an effect
    /// brush) and drift from it.
    /// </para>
    /// </remarks>
    private static int ChannelsMoved(Lightbox.Core.Documents.BrushSettings settings, int threshold = 12)
    {
        var info = new SkiaSharp.SKImageInfo(112, 40, SkiaSharp.SKColorType.Rgba8888,
            SkiaSharp.SKAlphaType.Premul);
        using var marked = BrushPreviewRenderer.Render(settings, width: info.Width, height: info.Height);
        using var ground = BrushPreviewRenderer.Ground(settings, info);

        var moved = 0;
        for (var y = 0; y < marked.Height; y++)
        {
            for (var x = 0; x < marked.Width; x++)
            {
                var a = marked.GetPixel(x, y);
                var b = ground.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) > threshold
                    || Math.Abs(a.Green - b.Green) > threshold
                    || Math.Abs(a.Blue - b.Blue) > threshold) moved++;
            }
        }
        return moved;
    }

    /// <summary>
    /// Brushes known to be too faint to show up, each with the bug that says so.
    /// </summary>
    /// <remarks>
    /// Named rather than absorbed into a lower threshold, and the list is checked for
    /// staleness below. A threshold loose enough to let a broken brush through is a guard
    /// that has been switched off quietly; an exemption that complains when it is no longer
    /// needed is one that removes itself.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownFaint = new()
    {
        // B50 is fixed — the on-canvas mark went from a peak of 54/255 to 96, and the
        // medium holds up as the brush shrinks (71..107 over sizes 8 to 150). What is
        // left is the TILE: a uniform wash about 10/255 deep over paper, which reads as
        // clean paper at this threshold. That is a preview-framing problem rather than a
        // deposit one, so it is B101 and this exemption now names that instead.
        ["Watercolor"] = "B101 — the simulated watercolour's picker tile is too faint to read",
    };

    [Fact]
    public void EveryBuiltInBrushLeavesAMarkOnItsTile()
    {
        // Stronger than "the tile has a picture": a tile can be a bitmap of clean paper.
        // Three of the twelve built-ins were exactly that — smudge, blur and the blender
        // deposit nothing on a blank surface, so their tiles were empty rectangles,
        // indistinguishable from a picker that had failed to render.
        var faint = new List<string>();
        var fixedSince = new List<string>();

        foreach (var p in BuiltInPresets.Create())
        {
            var moved = ChannelsMoved(p.Settings);
            var known = KnownFaint.ContainsKey(p.Name);
            output.WriteLine($"{p.Name}: {moved} pixels moved{(known ? " (known faint)" : "")}");

            if (moved < 120 && !known) faint.Add($"{p.Name} ({moved})");
            if (moved >= 120 && known) fixedSince.Add(p.Name);
        }

        Assert.True(faint.Count == 0, $"these tiles are all but indistinguishable from bare ground: {string.Join(", ", faint)}");
        Assert.True(
            fixedSince.Count == 0,
            $"these are no longer faint — take them out of KnownFaint: {string.Join(", ", fixedSince)}");
    }

    [Fact]
    public void TheShippedBlurBrushActuallySoftensWhatItPassesOver()
    {
        // B49. Flow is the blur sigma — sigma = Flow × Size / 4 — and the preset shipped at
        // 0.1, a 0.6 px sigma, on the reasoning that the blur is applied per dab and stacks.
        // It does not stack: StampBlur snapshots once and draws that snapshot per dab with
        // SKBlendMode.Src, so one dab is all the softening there is. Asserted against the
        // shipped preset rather than a value, because the point is that what ships works.
        var blur = BuiltInPresets.Create().First(p => p.Id == "builtin-blur");

        var moved = ChannelsMoved(blur.Settings);
        output.WriteLine($"the shipped blur moved {moved} pixels");
        Assert.True(moved > 400, $"the blur brush changed only {moved} pixels — it is not blurring");
    }

    [AvaloniaFact]
    public void TheTileNamesTheBrushAndItsRealSize()
    {
        // The size is the one thing the picture deliberately does not carry: a tile maps
        // size onto the range it can draw, so a 40 px and a 300 px brush read as different
        // weights rather than as their widths. That makes the number load-bearing.
        var (window, vm) = Open();
        var airbrush = vm.BrushPresetChoices.First(p => p.Id == "builtin-airbrush");

        var tile = Tiles(OpenPicker(window)).First(t => t.Preset.Id == airbrush.Id);

        Assert.Equal(airbrush.Name, tile.Name);
        Assert.Equal($"{airbrush.Settings.Size:0.#}", tile.SizeLabel);
        Assert.Contains(airbrush.Name, tile.Tip);
    }

    [AvaloniaFact]
    public void PickingATileAppliesThatBrush()
    {
        var (window, vm) = Open();
        var list = OpenPicker(window);
        var tiles = Tiles(list);
        var wanted = tiles.First(t => t.Preset.Id == "builtin-airbrush");

        list.SelectedItem = wanted;
        Pump();

        Assert.Equal("builtin-airbrush", vm.SelectedBrushPreset?.Id);
        Assert.Equal(wanted.Preset.Settings.Size, vm.BrushSize);
    }

    [AvaloniaFact]
    public void ThePickerOpensOnTheBrushYouAreAlreadyUsing()
    {
        // Where you are is the answer to "which of these am I on", and in a grid that is
        // harder to work out by eye than in a list of names.
        var (window, vm) = Open();
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-soft-round"));
        Pump();

        var list = OpenPicker(window);

        Assert.Equal("builtin-soft-round", (list.SelectedItem as BrushChoice)?.Preset.Id);
    }

    [AvaloniaFact]
    public void SearchingNarrowsTheGridAndStillGivesTiles()
    {
        // Filtering is the common act in here, and it re-runs the whole mapping — which is
        // exactly where a picker that re-rendered every mark on every keystroke would
        // become unusable. Cheap because the previews are cached.
        var (window, _) = Open();
        var list = OpenPicker(window);
        var all = Tiles(list).Count;

        var search = window.FindControl<TextBox>("BrushSearchBox")!;
        search.Text = "airbrush";
        Pump();

        var narrowed = Tiles(list);
        Assert.True(narrowed.Count < all, $"searching kept all {all} brushes");
        Assert.All(narrowed, t => Assert.Contains("airbrush", t.Name, StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void NoMatchSaysSoRatherThanShowingAnEmptyGrid()
    {
        var (window, _) = Open();
        var list = OpenPicker(window);
        var search = window.FindControl<TextBox>("BrushSearchBox")!;

        search.Text = "zzzznotabrush";
        Pump();

        Assert.Empty(Tiles(list));
        Assert.True(window.FindControl<TextBlock>("BrushFilterEmpty")!.IsVisible);
    }

    [AvaloniaFact]
    public void TheSameBrushReusesItsPictureRatherThanRedrawingIt()
    {
        // The reason the picker can be opened and filtered freely. Instance equality is
        // the assertion because a re-render would be a different bitmap with the same
        // pixels, and that is precisely the cost being avoided.
        var preset = new BrushPreset
        {
            Id = "test-cached",
            Settings = new Lightbox.Core.Documents.BrushSettings { Size = 9, Hardness = 0.7 },
        };

        var first = BrushChoice.For(preset).Preview;
        var second = BrushChoice.For(preset).Preview;

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void EditingABrushChangesItsPicture()
    {
        // The cache key. Keyed on the id alone — the obvious choice — the tile would keep
        // showing the mark the brush used to make, and the artist would be choosing from
        // a picture of the past. The key is derived from the same canonical form the
        // modified-dot uses, so the two cannot disagree.
        var preset = new BrushPreset
        {
            Id = "test-edited",
            Settings = new Lightbox.Core.Documents.BrushSettings { Size = 6, Hardness = 1, Flow = 1 },
        };

        var before = BrushChoice.For(preset).Preview;
        preset.Settings.Size = 40;
        preset.Settings.Scatter = 0.7;
        var after = BrushChoice.For(preset).Preview;

        Assert.NotNull(before);
        Assert.NotSame(before, after);
    }

    [AvaloniaFact]
    public void AnImportedBrushShowsItsOwnTipRatherThanARoundDab()
    {
        // Imported presets carry their tip on the preset and only register it in the
        // engine when the brush is first used — so without registering it here, every
        // .abr in a collection would draw the same round dab, in the one case where the
        // tip is the entire difference between the brushes.
        var square = TipPngOf(solid: true);
        var withTip = new BrushPreset
        {
            Id = "test-tipped",
            TipPng = square,
            Settings = new Lightbox.Core.Documents.BrushSettings
            {
                Size = 30, Hardness = 1, Flow = 1, TipId = "tip_test_square",
            },
        };
        var plain = new BrushPreset
        {
            Id = "test-untipped",
            Settings = new Lightbox.Core.Documents.BrushSettings { Size = 30, Hardness = 1, Flow = 1 },
        };

        // BrushChoice is what registers the tip; the comparison then has to happen on real
        // pixels, which means going back through the renderer — see ChannelsMoved.
        Assert.NotNull(BrushChoice.For(withTip).Preview);
        using var tipped = BrushPreviewRenderer.Render(withTip.Settings, width: 112, height: 40);
        using var round = BrushPreviewRenderer.Render(plain.Settings, width: 112, height: 40);

        var differing = 0;
        for (var y = 0; y < tipped.Height; y++)
        {
            for (var x = 0; x < tipped.Width; x++)
            {
                if (Math.Abs(tipped.GetPixel(x, y).Red - round.GetPixel(x, y).Red) > 24) differing++;
            }
        }
        output.WriteLine($"{differing} pixels differ between the tipped and untipped marks");
        Assert.True(differing > 150, $"only {differing} pixels differ — the tip was not used");
    }

    /// <summary>A tip whose shape is a filled square, so it cannot be mistaken for a dab.</summary>
    private static string TipPngOf(bool solid)
    {
        var info = new SkiaSharp.SKImageInfo(64, 64, SkiaSharp.SKColorType.Rgba8888,
            SkiaSharp.SKAlphaType.Unpremul);
        using var bitmap = new SkiaSharp.SKBitmap(info);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White };
            if (solid) canvas.DrawRect(0, 0, 64, 64, paint);
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void ACollectionSizedPickerOpensWithoutStalling()
    {
        // The reported shape of the problem was importing fifty-six brushes and the window
        // going grey, and a picker that renders seventy marks on open would earn the same
        // reaction for the same reason. Measured on a cold cache: the first open is the
        // only one that pays.
        var (window, vm) = Open();
        for (var i = 0; i < 60; i++)
        {
            vm.BrushPresetChoices.Add(new BrushPreset
            {
                Id = $"perf-brush-{i}",
                Name = $"Imported {i}",
                Settings = new Lightbox.Core.Documents.BrushSettings
                {
                    Size = 6 + i % 40, Hardness = 0.4 + i % 5 * 0.12, Flow = 0.3 + i % 4 * 0.2,
                },
            });
        }

        var clock = Stopwatch.StartNew();
        var tiles = Tiles(OpenPicker(window));
        var elapsed = clock.Elapsed.TotalMilliseconds;

        Assert.True(tiles.Count >= 60);
        // Deliberately loose: this catches "the picker got an order of magnitude slower",
        // not drift. Measured around 300 ms cold for ~70 brushes.
        Assert.True(elapsed < 2500, $"opening the picker with {tiles.Count} brushes took {elapsed:F0} ms");
    }
}
