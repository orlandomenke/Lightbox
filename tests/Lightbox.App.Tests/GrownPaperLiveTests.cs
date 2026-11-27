using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A stroke in flight on grown paper shows where it will land (B422).
/// </summary>
/// <remarks>
/// <para>
/// <b>The committed picture was already right; the one under the pen was not.</b>
/// B409 taught the frame cache the paper's corner, so a finished stroke drew
/// in the right place. The live scratch a stroke is stamped into while the pen
/// is down is paper-sized with its corner at surface zero, and it was stamped
/// at stroke coordinates — so on paper grown left or up the mark in flight sat
/// the grown distance up and to the left of the pen, and jumped into place on
/// release. Zoomed in, it was off screen altogether: the stroke vanished while
/// it was being drawn.
/// </para>
/// <para>
/// Every test grows the paper first, because with the corner at zero the two
/// spaces coincide and the fault cannot be seen.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class GrownPaperLiveTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int GrewX = 24, GrewY = 10;

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainViewModel Vm, Func<RenderSnapshot> Latest) Grown()
    {
        var vm = new MainViewModel();
        vm.NewDocument(new NewDocumentSettings("Grown", 64, 64, 12, 72, "#ffffff", true));
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        var choice = new ResizeDialogViewModel(vm.Doc.Scene, ResizeMode.Canvas) { Anchor = ResizeAnchor.BottomRight };
        choice.Width = 64 + GrewX;
        choice.Height = 64 + GrewY;
        Assert.True(vm.ApplyResize(choice));
        Assert.Equal((-GrewX, -GrewY), (vm.Doc.Scene.Left, vm.Doc.Scene.Top));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#ff0000";
        vm.BrushSize = 8;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        return (vm, () => { Pump(); Assert.NotNull(latest); return latest!; });
    }

    /// <summary>The picture a snapshot shows, in surface pixels, and where its corner sits.</summary>
    private static (SKBitmap Bitmap, SKPointI Corner, double Scale) Picture(RenderSnapshot snapshot)
    {
        if (snapshot.Deferred is { } work)
        {
            using var image = work.Compose(null, out _);
            return (SKBitmap.FromImage(image), new SKPointI(work.Viewport.Left, work.Viewport.Top), work.RenderScale);
        }
        // A culled publish draws only the window onto the paper the canvas
        // asked for, at the window's own corner.
        var bitmap = SKBitmap.FromImage(snapshot.Image);
        var corner = snapshot.DocViewport is { } window && bitmap.Width < snapshot.DocWidth
            ? new SKPointI(window.Left, window.Top)
            : default;
        return (bitmap, corner, 1.0);
    }

    private static bool IsRed(SKColor c) => c.Alpha > 200 && c.Red > 200 && c.Green < 80 && c.Blue < 80;

    /// <summary>The bounds of every red pixel, in surface coordinates, or null.</summary>
    private static SKRectI? RedBounds(SKBitmap bitmap, SKPointI corner, double scale)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!IsRed(bitmap.GetPixel(x, y))) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        if (maxX < 0) return null;
        return new SKRectI(
            corner.X + (int)Math.Floor(minX / scale), corner.Y + (int)Math.Floor(minY / scale),
            corner.X + (int)Math.Ceiling((maxX + 1) / scale), corner.Y + (int)Math.Ceiling((maxY + 1) / scale));
    }

    /// <summary>
    /// The owner's report at its smallest: pen down at stroke (20, 20), moved
    /// to (30, 20), and the picture published before the pen lifts. The mark
    /// belongs at surface (44, 30)–(54, 30), the grown distance from where the
    /// stroke coordinates alone would put it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStrokeOnGrownPaperPreviewsWhereItLands(bool zoomedIn)
    {
        var (vm, latest) = Grown();
        // Zoomed in: the canvas composes only a window onto the paper, which
        // is the route an artist working close takes. The window holds where
        // the mark belongs and not where the un-moved scratch would put it.
        if (zoomedIn) vm.SetViewport(SKRectI.Create(36, 20, 30, 20));

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(25, 20, 1);
        vm.MoveStroke(30, 20, 1);
        // The publish the pen's own events pace, not a whole-canvas one asked
        // for by hand: mid-stroke, that is the route the canvas actually takes,
        // and a hand-made whole-canvas publish of a live stroke is what B170's
        // tripwire is there to catch.
        Pump();

        var (bitmap, corner, scale) = Picture(latest());
        using (bitmap)
        {
            var red = RedBounds(bitmap, corner, scale);
            output.WriteLine($"in flight: red at {red} (surface), picture {bitmap.Width}x{bitmap.Height} from {corner} at {scale}; belongs near (40,26)-(58,34)");
            Assert.NotNull(red);
            var r = red!.Value;
            Assert.InRange(r.Left, 20 + GrewX - 6, 20 + GrewX);
            Assert.InRange(r.Right, 30 + GrewX, 30 + GrewX + 6);
            Assert.InRange(r.Top, 20 + GrewY - 6, 20 + GrewY);
            Assert.InRange(r.Bottom, 20 + GrewY, 20 + GrewY + 6);
        }

        // And the commit lands on the same pixels the preview showed.
        vm.EndStroke();
        vm.PublishSnapshot();
        var (after, afterCorner, afterScale) = Picture(latest());
        using (after)
        {
            var red = RedBounds(after, afterCorner, afterScale);
            output.WriteLine($"committed: red at {red}");
            Assert.NotNull(red);
            Assert.InRange(red!.Value.Left, 20 + GrewX - 6, 20 + GrewX);
            Assert.InRange(red.Value.Top, 20 + GrewY - 6, 20 + GrewY);
        }
    }

    /// <summary>
    /// A mark begun in the new margin, where stroke coordinates are negative,
    /// shows while it is being drawn — the case the un-moved scratch could not
    /// hold at all, because it has no pixels left of or above stroke zero.
    /// </summary>
    [AvaloniaFact]
    public void AStrokeInTheNewMarginShowsWhileItIsDrawn()
    {
        var (vm, latest) = Grown();

        vm.BeginStroke(-14, -5, 1);
        vm.MoveStroke(-8, -5, 1);
        Pump();

        var (bitmap, corner, scale) = Picture(latest());
        using (bitmap)
        {
            var red = RedBounds(bitmap, corner, scale);
            output.WriteLine($"in the margin, in flight: red at {red}; belongs near ({-14 + GrewX},{-5 + GrewY})");
            Assert.NotNull(red);
            Assert.InRange(red!.Value.Left, -14 + GrewX - 6, -14 + GrewX);
            Assert.InRange(red.Value.Top, -5 + GrewY - 6, -5 + GrewY);
        }
    }

    /// <summary>The red box in the picture the latest snapshot shows, in surface pixels.</summary>
    private static SKRectI? RedNow(Func<RenderSnapshot> latest)
    {
        var (bitmap, corner, scale) = Picture(latest());
        using (bitmap) return RedBounds(bitmap, corner, scale);
    }

    /// <summary>
    /// A mirrored stroke: the drawn mark and its reflection both show in flight
    /// where they land. The reflections are made in stroke coordinates and the
    /// live dabs are now in surface pixels, so each copy has to be carried
    /// across the corner and back — or the reflection previews about the wrong axis.
    /// </summary>
    [AvaloniaFact]
    public void AMirroredStrokeOnGrownPaperPreviewsWhereItLands()
    {
        var (vm, latest) = Grown();
        vm.SymmetryEnabled = true;

        vm.BeginStroke(10, 20, 1);
        vm.MoveStroke(15, 20, 1);
        vm.MoveStroke(20, 20, 1);
        Pump();
        var inFlight = RedNow(latest);

        vm.EndStroke();
        vm.PublishSnapshot();
        var committed = RedNow(latest);

        output.WriteLine($"mirrored: in flight {inFlight}, committed {committed}");
        Assert.NotNull(committed);
        Assert.Equal(committed, inFlight);
    }

    /// <summary>
    /// A shape drag previews through the brush engine whole, with no dab walk
    /// to move — the corner has to reach it as an argument.
    /// </summary>
    [AvaloniaFact]
    public void AShapeDragOnGrownPaperPreviewsWhereItLands()
    {
        var (vm, latest) = Grown();
        vm.ActiveTool = ToolId.Shape;
        vm.ActiveShape = ShapeKind.Rectangle;

        vm.BeginShape(10, 10);
        vm.MoveShape(30, 25);
        vm.PublishSnapshot();
        var inFlight = RedNow(latest);

        vm.EndShape(30, 25);
        vm.PublishSnapshot();
        var committed = RedNow(latest);

        output.WriteLine($"shape: in flight {inFlight}, committed {committed}");
        Assert.NotNull(committed);
        Assert.Equal(committed, inFlight);
    }
}
