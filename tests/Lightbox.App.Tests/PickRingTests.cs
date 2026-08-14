using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The eyedropper's ring: the colour it would take against the colour in hand,
/// with the pixel itself still visible between them.
/// </summary>
/// <remarks>
/// <para>
/// Split the way the feature is: <see cref="PickRing.For"/> decides whether
/// there is a ring, <see cref="PickRing.Draw"/> puts it on a surface, and the
/// view model decides what colour it names. All three are reachable without a
/// window, which is the whole reason the drawing does not live inside
/// <c>CanvasControl.Render</c> — synthetic input is unreliable here, so anything
/// only reachable that way ships unguarded.
/// </para>
/// <para>
/// <b>The pixel assertions are the point rather than decoration.</b> "Top half
/// shows the sampled colour" is the promise an artist aims with, and the two
/// ways it breaks — the halves swapped, and the hole filled in — both look like
/// working code and neither shows up in a test of the decision alone.
/// </para>
/// </remarks>
public class PickRingTests(ITestOutputHelper output)
{
    private static readonly SKColor Artwork = new(0x2e, 0x8b, 0x57);   // neither swatch, nor white, nor black
    private static readonly SKColor Sampled = new(0xff, 0x00, 0x00);
    private static readonly SKColor InHand = new(0x00, 0x00, 0xff);

    private const int Size = 100;
    private const float Centre = 50f;

    /// <summary>Mid-band radius: solidly inside a swatch, clear of both edges.</summary>
    private const float Band = (PickRing.OuterRadius + PickRing.HoleRadius) / 2f;

    // ---- when there is a ring at all ---------------------------------------------------

    [Fact]
    public void OnlyTheEyedropperGetsARing()
    {
        var armed = Enum.GetValues<ToolId>()
            .Where(t => PickRing.For(
                CanvasCursor.Armed(t), (0f, 0f), Sampled, InHand) is not null)
            .ToList();

        output.WriteLine($"tools showing the ring: {string.Join(", ", armed)}");
        Assert.Equal([ToolId.Picker], armed);
    }

    /// <summary>
    /// The eyedropper borrowed with Ctrl gets it too, and that is the case it
    /// matters in most: the borrow exists so the stroke does not have to be
    /// broken to fetch a colour, and comparing it without breaking the stroke is
    /// the same argument one step further.
    /// </summary>
    [Fact]
    public void TheBorrowedEyedropperGetsTheRingToo()
    {
        var held = CanvasCursor.For(ToolId.Brush, new CanvasTarget(), KeyModifiers.Control);
        Assert.NotNull(PickRing.For(held, (0f, 0f), Sampled, InHand));

        var plain = CanvasCursor.For(ToolId.Brush, new CanvasTarget());
        Assert.Null(PickRing.For(plain, (0f, 0f), Sampled, InHand));
    }

    /// <summary>
    /// No pointer and no colour each mean no ring — a ring showing the last
    /// colour it managed to read would promise a click that will not happen.
    /// </summary>
    [Fact]
    public void NothingToPickMeansNoRing()
    {
        Assert.Null(PickRing.For(CanvasCursorKind.Pick, null, Sampled, InHand));
        Assert.Null(PickRing.For(CanvasCursorKind.Pick, (0f, 0f), null, InHand));
    }

    // ---- what it looks like -------------------------------------------------------------

    [Fact]
    public void TheSampledColourIsOnTopAndTheColourInHandBelow()
    {
        using var surface = Draw();
        using var pixels = Read(surface);

        var top = pixels.GetPixel((int)Centre, (int)(Centre - Band));
        var bottom = pixels.GetPixel((int)Centre, (int)(Centre + Band));
        output.WriteLine($"top {top}, bottom {bottom} (sampled {Sampled}, in hand {InHand})");

        Assert.Equal(Sampled, top);
        Assert.Equal(InHand, bottom);
    }

    /// <summary>
    /// The middle is a hole, and the assertion is over an area rather than the
    /// one pixel: a rim drawn a shade too far in is invisible at the centre and
    /// covers exactly the pixels an artist checks the sample against.
    /// </summary>
    [Fact]
    public void TheMiddleIsAHoleTheArtworkShowsThrough()
    {
        using var surface = Draw();
        using var pixels = Read(surface);

        // Derived from the constant rather than typed, less a margin for the
        // rim's antialiasing: widening the hole must not quietly widen the
        // area this stops looking at.
        var clear = (int)PickRing.HoleRadius - 2;

        var covered = new List<(int X, int Y)>();
        for (var y = -clear; y <= clear; y++)
        {
            for (var x = -clear; x <= clear; x++)
            {
                if (x * x + y * y > clear * clear) continue;   // the disc, not its bounding box
                if (pixels.GetPixel((int)Centre + x, (int)Centre + y) != Artwork)
                {
                    covered.Add((x, y));
                }
            }
        }

        output.WriteLine(covered.Count == 0
            ? $"the middle is clear to a radius of {clear}"
            : $"{covered.Count} covered pixels, first at {covered[0]}");
        Assert.Empty(covered);
    }

    /// <summary>
    /// The two swatches never touch: the seam is a slot of artwork rather than a
    /// drawn line, because any line between two arbitrary colours disappears
    /// against one of them sooner or later.
    /// </summary>
    [Fact]
    public void TheTwoColoursAreSeparated()
    {
        using var surface = Draw();
        using var pixels = Read(surface);

        Assert.Equal(Artwork, pixels.GetPixel((int)(Centre - Band), (int)Centre));
        Assert.Equal(Artwork, pixels.GetPixel((int)(Centre + Band), (int)Centre));
    }

    /// <summary>
    /// It is a ring rather than a wash over the drawing: outside the outer edge
    /// the artwork is untouched.
    /// </summary>
    [Fact]
    public void NothingIsDrawnOutsideTheRing()
    {
        using var surface = Draw();
        using var pixels = Read(surface);

        var outside = (int)(PickRing.OuterRadius + 4);
        Assert.Equal(Artwork, pixels.GetPixel((int)Centre, (int)Centre - outside));
        Assert.Equal(Artwork, pixels.GetPixel((int)Centre, (int)Centre + outside));
        Assert.Equal(Artwork, pixels.GetPixel(0, 0));
    }

    /// <summary>
    /// A translucent colour is shown as the colour, not as the colour blended
    /// with whatever the pointer happens to be over.
    /// </summary>
    [Fact]
    public void ASwatchIsOpaqueWhateverItWasHanded()
    {
        using var surface = Draw(sampled: Sampled.WithAlpha(40));
        using var pixels = Read(surface);

        Assert.Equal(Sampled, pixels.GetPixel((int)Centre, (int)(Centre - Band)));
    }

    private static SKSurface Draw(SKColor? sampled = null)
    {
        var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        surface.Canvas.Clear(Artwork);
        PickRing.Draw(surface.Canvas, new PickRing(Centre, Centre, sampled ?? Sampled, InHand));
        return surface;
    }

    private static SKBitmap Read(SKSurface surface)
    {
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }
}

/// <summary>
/// What the ring says it will pick, and the click that has to agree with it.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it paints, and brush parameters
/// live in a process-wide store — running beside a test that assumes defaults
/// would hand it this one's brush.
/// </remarks>
[Collection("BrushState")]
public class PickPreviewTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const string Painted = "#e04040";

    private static MainViewModel Painted200x200()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = Painted,
            BrushSize = 24,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushGranulation = 0,
            BrushWetEdge = 0,
        };
        vm.BeginStroke(200, 200, 1);
        vm.EndStroke();
        return vm;
    }

    /// <summary>
    /// The promise and the thing it promises come from one function, so this
    /// cannot drift — which is the reason it is one function.
    /// </summary>
    [AvaloniaFact]
    public void ThePreviewIsExactlyWhatTheClickWillTake()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Picker;
        vm.ColorHex = "#123456";

        vm.UpdatePointerContext(200, 200, KeyModifiers.None);
        var promised = vm.PickPreviewHex;
        vm.PickColorAt(200, 200);

        output.WriteLine($"ring promised {promised}, click took {vm.ColorHex}");
        Assert.Equal(Painted, promised);
        Assert.Equal(vm.ColorHex, promised);
    }

    /// <summary>
    /// The paper counts. Most of a fresh drawing is transparent, and a ring that
    /// went blank over all of it would be a ring nobody trusted — the click
    /// takes the paper colour there, so the ring shows it.
    /// </summary>
    [AvaloniaFact]
    public void OverBareCanvasTheRingShowsThePaper()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Picker;

        vm.UpdatePointerContext(700, 400, KeyModifiers.None);
        Assert.Equal(vm.Doc.Scene.BackgroundColor, vm.PickPreviewHex);

        vm.PickColorAt(700, 400);
        Assert.Equal(vm.ColorHex, vm.PickPreviewHex);
    }

    /// <summary>Off the paper there is nothing to pick, so there is no ring.</summary>
    [AvaloniaFact]
    public void OffThePaperThereIsNoRing()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Picker;

        vm.UpdatePointerContext(200, 200, KeyModifiers.None);
        Assert.NotNull(vm.PickPreviewHex);

        vm.UpdatePointerContext(-4000, -4000, KeyModifiers.None);
        Assert.Null(vm.PickPreviewHex);
    }

    [AvaloniaFact]
    public void NoRingWhileAnotherToolIsInHand()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Brush;

        vm.UpdatePointerContext(200, 200, KeyModifiers.None);
        Assert.Null(vm.PickPreviewHex);

        // Ctrl borrows the eyedropper, and the ring follows the borrow.
        vm.UpdatePointerContext(200, 200, KeyModifiers.Control);
        Assert.Equal(Painted, vm.PickPreviewHex);
    }

    /// <summary>
    /// Picking the tool up is enough. An artist who presses <c>I</c> without
    /// moving the pointer has changed the answer without moving anything, and
    /// waiting for a pointer event would leave the ring off until they jiggled
    /// the pen.
    /// </summary>
    [AvaloniaFact]
    public void ChangingToolWithAStillPointerArmsTheRing()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Brush;
        vm.UpdatePointerContext(200, 200, KeyModifiers.None);
        Assert.Null(vm.PickPreviewHex);

        vm.ActiveTool = ToolId.Picker;
        Assert.Equal(Painted, vm.PickPreviewHex);

        vm.ActiveTool = ToolId.Brush;
        Assert.Null(vm.PickPreviewHex);
    }

    [AvaloniaFact]
    public void LeavingTheCanvasTakesTheRingWithIt()
    {
        var vm = Painted200x200();
        vm.ActiveTool = ToolId.Picker;
        vm.UpdatePointerContext(200, 200, KeyModifiers.None);
        Assert.NotNull(vm.PickPreviewHex);

        vm.ClearPointerContext();
        Assert.Null(vm.PickPreviewHex);
    }

    /// <summary>
    /// The one-pixel composite is an optimisation over compositing the canvas,
    /// and this is the case that would catch it having become a second, simpler
    /// answer: two layers, the top one half transparent, so the colour picked
    /// exists on neither layer.
    /// </summary>
    [AvaloniaFact]
    public void TheSampleIsTheRealStack_NotTheTopLayer()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            BrushSize = 40,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushGranulation = 0,
            BrushWetEdge = 0,
            ColorHex = "#000000",
        };
        vm.BeginStroke(200, 200, 1);
        vm.EndStroke();

        vm.AddPaintedLayerCommand.Execute(null);
        vm.ColorHex = "#ffffff";
        vm.BeginStroke(200, 200, 1);
        vm.EndStroke();
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Opacity = 0.5;

        var picked = vm.PickedColorHexAt(200, 200);
        output.WriteLine($"white at 50% over black picked as {picked}");

        // Neither layer's own colour: half of white over black.
        Assert.NotEqual("#ffffff", picked);
        Assert.NotEqual("#000000", picked);
        var (r, _, _) = Lightbox.Core.Geometry.ColorOps.HexToRgb(picked!);
        Assert.InRange(r, 120, 136);
    }
}
