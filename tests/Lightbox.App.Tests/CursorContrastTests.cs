using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush ring is one line, and it changes ink rather than doubling up
/// (owner's request, 2026-08-31: the black-plus-white pair hides too much of
/// the drawing to aim with).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the old ring bought, and what it cost.</b> A dark outline with a
/// light one nested inside it is visible over any artwork whatsoever, and it
/// covers 2.4 px of the drawing all the way round the brush. The trade here goes
/// the other way: one 1.1 px line, inked dark or light from the artwork under
/// the pointer, which is thinner than either half of the pair was.
/// </para>
/// <para>
/// <b>The painting is reachable at last.</b> <c>BrushGizmoTests</c> says, at
/// length, that the ring is drawn inside the canvas's render op on the render
/// thread and cannot be captured by this suite — which is why the shape and the
/// wiring were guarded there and the appearance was left to a manual check.
/// Moving the drawing into <see cref="BrushRingPainter"/> makes the appearance a
/// question a bare <see cref="SKBitmap"/> can answer, and "how many lines" is
/// exactly the kind of question that was going unasked.
/// </para>
/// </remarks>
public class CursorContrastTests
{
    // ---- which ink -------------------------------------------------------------------

    [Fact]
    public void LightArtworkTakesTheDarkInkAndDarkArtworkTakesTheLight()
    {
        Assert.Equal(CursorInk.Dark, CursorContrast.Choose(SKColors.White, CursorInk.Light));
        Assert.Equal(CursorInk.Light, CursorContrast.Choose(SKColors.Black, CursorInk.Dark));

        // Paper, which is the tone most of this application's pixels are.
        Assert.Equal(CursorInk.Dark, CursorContrast.Choose(new SKColor(0xf2, 0xf0, 0xea), CursorInk.Light));
    }

    /// <summary>
    /// Between the two thresholds the ring keeps the ink it has.
    /// </summary>
    /// <remarks>
    /// <b>A single threshold is what makes a contrast-following cursor
    /// unbearable.</b> Sitting on a gradient, or on a dithered edge, the sample
    /// crosses the line every few pixels and the ring strobes between black and
    /// white — far more distracting than either ink is on its own. The dead band
    /// is where both inks read well enough that nobody has to be told which one
    /// they got.
    /// </remarks>
    [Fact]
    public void AToneBetweenTheThresholdsKeepsTheInkItHas()
    {
        var mid = new SKColor(128, 128, 128);
        Assert.InRange(CursorContrast.Luminance(mid), CursorContrast.ToLight, CursorContrast.ToDark);

        Assert.Equal(CursorInk.Dark, CursorContrast.Choose(mid, CursorInk.Dark));
        Assert.Equal(CursorInk.Light, CursorContrast.Choose(mid, CursorInk.Light));

        // And it is a band, not a latch: past either edge the ink does change.
        Assert.Equal(CursorInk.Light, CursorContrast.Choose(new SKColor(80, 80, 80), CursorInk.Dark));
        Assert.Equal(CursorInk.Dark, CursorContrast.Choose(new SKColor(190, 190, 190), CursorInk.Light));
    }

    /// <summary>Luminance is weighted the way the eye is, not the way the bytes are.</summary>
    [Fact]
    public void GreenCountsForMoreThanRedAndRedForMoreThanBlue()
    {
        Assert.True(CursorContrast.Luminance(SKColors.Lime) > CursorContrast.Luminance(SKColors.Red));
        Assert.True(CursorContrast.Luminance(SKColors.Red) > CursorContrast.Luminance(SKColors.Blue));

        // Saturated blue is dark enough to need the light ink; saturated green is not.
        Assert.Equal(CursorInk.Light, CursorContrast.Choose(SKColors.Blue, CursorInk.Dark));
        Assert.Equal(CursorInk.Dark, CursorContrast.Choose(SKColors.Lime, CursorInk.Light));
    }

    // ---- one line --------------------------------------------------------------------

    /// <summary>
    /// Draw the ring on nothing and total up the ink it put on a ray from the
    /// centre outwards.
    /// </summary>
    private static (int Total, int Bands) InkOnARayUpwards(float roundness, float angleDeg)
    {
        using var bmp = new SKBitmap(new SKImageInfo(160, 160, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        BrushRingPainter.Draw(canvas, 80, 80, radius: 50, roundness: roundness, angleDeg: angleDeg);

        var total = 0;
        var bands = 0;
        var inside = false;
        for (var y = 79; y >= 0; y--)
        {
            int a = bmp.GetPixel(80, y).Alpha;
            total += a;
            if (a > 0 && !inside) bands++;
            inside = a > 0;
        }
        return (total, bands);
    }

    /// <summary>
    /// The ring covers one line's worth of the drawing, not two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the whole complaint, as a number.</b> The old ring laid down
    /// 1.2 px at 200/255 twice over — 480 units of ink across the band — where
    /// this one lays 1.1 px at 165/255, about 182. That is the drawing an artist
    /// gets back.
    /// </para>
    /// <para>
    /// <b>Counting bands would not have caught it</b>, and it was the first thing
    /// tried: the two old rings are 1.2 px apart and 1.2 px wide, so they touch,
    /// and a walk outwards from the centre crosses one contiguous run of ink in
    /// both versions. The band count is asserted anyway, because a future second
    /// ring drawn further out is a real thing to forbid — but it is the total
    /// that fails on the version this replaces.
    /// </para>
    /// <para>
    /// Straight up from the centre, where the outline of an unrotated shape runs
    /// horizontally: the ray crosses it square on, so the total is the line's
    /// width times its opacity and nothing to do with the geometry. A rotated
    /// shape is not measured this way for exactly that reason — an oblique
    /// crossing smears the same ink over more pixels — so it is checked for
    /// contiguity only.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1f)]      // the plain circle
    [InlineData(0.4f)]    // the flattened ellipse a round dab really is
    public void TheRingCoversOneLinesWorthOfTheDrawing(float roundness)
    {
        var (total, bands) = InkOnARayUpwards(roundness, angleDeg: 0f);
        var expected = CursorContrast.StrokeWidth * CursorContrast.Dark.Alpha;

        Assert.Equal(1, bands);
        Assert.InRange(total, expected * 0.8, expected * 1.6);
    }

    /// <summary>A turned tip is still one run of ink, not two.</summary>
    [Fact]
    public void ATurnedRingIsStillOneLine()
    {
        var (_, bands) = InkOnARayUpwards(roundness: 0.4f, angleDeg: 30f);
        Assert.Equal(1, bands);
    }

    /// <summary>The ring is an outline, never a fill — the middle stays clear.</summary>
    [Fact]
    public void NothingIsDrawnInsideTheRing()
    {
        using var bmp = new SKBitmap(new SKImageInfo(160, 160, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        BrushRingPainter.Draw(canvas, 80, 80, radius: 50);

        for (var y = 60; y < 100; y++)
        {
            for (var x = 60; x < 100; x++)
            {
                Assert.Equal(0, bmp.GetPixel(x, y).Alpha);
            }
        }
    }

    /// <summary>
    /// Each ink lays down one line's worth of a translucent colour against the
    /// artwork it is chosen for, and no pixel of it is ever solid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured as a total across the ray rather than as a peak</b>, because a
    /// 1.1 px line landing on a pixel grid splits its coverage between two rows
    /// and neither of them is ever fully inked. A peak reading would therefore
    /// say "faint" about a line that is exactly as strong as it was asked to be,
    /// and the number would move with where the circle happened to fall.
    /// </para>
    /// <para>
    /// <b>Not solid is the half of this that is about taste</b>, and it is the
    /// point of the change: a line an artist can read the drawing through is one
    /// they aim past, and an opaque one is one they look at.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CursorInk.Dark, (byte)255)]
    [InlineData(CursorInk.Light, (byte)0)]
    public void TheChosenInkIsOneLinesWorthAndNeverSolid(CursorInk ink, byte background)
    {
        using var bmp = new SKBitmap(new SKImageInfo(160, 160, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(background, background, background));

        BrushRingPainter.Draw(canvas, 80, 80, radius: 50, ink: ink);

        int alpha = CursorContrast.ColorFor(ink).Alpha;
        Assert.True(alpha < 200, $"{ink} at {alpha}/255 is opaque enough to read as a line, not as chrome");

        var total = 0;
        var peak = 0;
        for (var y = 79; y >= 0; y--)
        {
            var moved = Math.Abs(bmp.GetPixel(80, y).Red - background);
            total += moved;
            peak = Math.Max(peak, moved);
        }

        // Nowhere does the ring replace the artwork: the ink's own alpha is the ceiling.
        Assert.True(peak <= alpha + 1, $"{ink} reached {peak} where its ink is only {alpha}");
        // And it is there: a line of width x opacity, wherever the coverage landed.
        Assert.InRange(total, alpha * CursorContrast.StrokeWidth * 0.8, alpha * CursorContrast.StrokeWidth * 1.6);
    }

    // ---- the ink reaches the ring ----------------------------------------------------

    /// <summary>
    /// The canvas repaints when the ink changes, and the window actually binds it.
    /// </summary>
    /// <remarks>
    /// Both halves, for the reason <c>BrushGizmoTests</c> gives for the size: a
    /// value that changes and announces itself is useless if nothing invalidates,
    /// and a registered property is useless if no binding ever delivers a new
    /// value. Neither half fails loudly on its own.
    /// </remarks>
    [Fact]
    public void TheInkIsRegisteredForRepaintAndBoundInTheWindow()
    {
        Assert.Contains(CanvasControl.BrushCursorInkProperty, CanvasControl.RepaintOnChange);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(
            Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));

        Assert.Contains("BrushCursorInk=\"{Binding BrushCursorInk}\"", xaml);
    }

    private static MainViewModel OnPaper(string background)
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Ink", 400, 300, 12, 72, background, false));
        return vm;
    }

    /// <summary>The ink follows what is under the pointer, on a hover.</summary>
    [AvaloniaFact]
    public void TheRingInksItselfFromTheArtworkUnderThePointer()
    {
        var dark = OnPaper("#101010");
        dark.UpdatePointerContext(200, 150, KeyModifiers.None);
        Assert.Equal(CursorInk.Light, dark.BrushCursorInk);

        var light = OnPaper("#f4f2ec");
        light.UpdatePointerContext(200, 150, KeyModifiers.None);
        Assert.Equal(CursorInk.Dark, light.BrushCursorInk);
    }

    /// <summary>
    /// Off the paper the last ink stands, and the next hover on it re-decides.
    /// </summary>
    /// <remarks>
    /// <b>The opposite rule to the pick preview beside it</b>, which is absent
    /// rather than stale on purpose. The eyedropper's ring is showing a colour and
    /// a wrong one would be a lie; this ring is showing a brush, and the ink is
    /// only how you see it — so blanking it at the edge of the paper would be a
    /// flash for no information.
    /// </remarks>
    [AvaloniaFact]
    public void LeavingThePaperKeepsTheInkAndComingBackRedecidesIt()
    {
        var vm = OnPaper("#101010");
        vm.UpdatePointerContext(200, 150, KeyModifiers.None);
        Assert.Equal(CursorInk.Light, vm.BrushCursorInk);

        vm.UpdatePointerContext(-500, -500, KeyModifiers.None);
        Assert.Equal(CursorInk.Light, vm.BrushCursorInk);

        vm.ClearPointerContext();
        Assert.Equal(CursorInk.Light, vm.BrushCursorInk);
    }

    /// <summary>
    /// The eyedropper does not pay for an ink it is not showing.
    /// </summary>
    /// <remarks>
    /// Its own ring replaces the brush ring while it is armed, and it is already
    /// reading the composite for its swatch — sampling again here would be the
    /// same pixel fetched twice, once for a gizmo that is not on screen.
    /// </remarks>
    [AvaloniaFact]
    public void TheEyedropperDoesNotResampleForARingItHides()
    {
        var vm = OnPaper("#f4f2ec");
        vm.UpdatePointerContext(200, 150, KeyModifiers.None);
        Assert.Equal(CursorInk.Dark, vm.BrushCursorInk);

        vm.SelectToolCommand.Execute(ToolId.Picker);
        Assert.Equal(CanvasCursorKind.Pick, vm.PointerIntent);

        // A hover over ink dark enough to flip the ring, which is not drawn.
        vm.UpdatePointerContext(40, 40, KeyModifiers.None);
        Assert.Equal(CursorInk.Dark, vm.BrushCursorInk);
    }
}
