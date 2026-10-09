using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Paper grown to the left or upward, and what the canvas shows of it (B409).
/// </summary>
/// <remarks>
/// The export half is <see cref="GrownPaperExportTests"/>. This is the half
/// that was inferred from the code and not measured when the bug was filed:
/// the canvas renders through the same frame cache the exports do, so it
/// should have the same fault — the drawing un-moved on bigger paper, and a
/// mark made in the new margin landing nowhere. Each test grows the paper
/// first, because until an origin goes non-zero the two coordinate spaces
/// coincide and everything passes whichever way the code is.
/// </remarks>
[Collection("BrushState")]
public class GrownPaperCanvasTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private const int GrewX = 24, GrewY = 10;

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainViewModel Vm, Func<SKBitmap> Shown) Open()
    {
        var vm = new MainViewModel();
        vm.NewDocument(new NewDocumentSettings("Grown", 64, 64, 12, 72, "#ffffff", true));
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        // An opaque red 8x8 square whose corner is at stroke (4, 6).
        // A fresh frame object rather than a stroke pushed into the one the
        // canvas has already rendered: the cache keys a drawing by its id, and
        // a record edited behind its back would still show the empty render.
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Tool = ToolKind.Fill,
                    Color = "#ff0000",
                    Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
                    Points = [new(4, 6, 1), new(12, 6, 1), new(12, 14, 1), new(4, 14, 1)],
                },
            ],
        };
        return (vm, () =>
        {
            Pump();
            Assert.NotNull(latest);
            return SKBitmap.FromImage(latest!.Image);
        });
    }

    private static void GrowLeftAndUp(MainViewModel vm)
    {
        var choice = new ResizeDialogViewModel(vm.Doc.Scene, ResizeMode.Canvas)
        {
            Anchor = ResizeAnchor.BottomRight,
        };
        choice.Width = vm.Doc.Scene.Width + GrewX;
        choice.Height = vm.Doc.Scene.Height + GrewY;
        Assert.True(vm.ApplyResize(choice));
        Assert.Equal((-GrewX, -GrewY), (vm.Doc.Scene.Left, vm.Doc.Scene.Top));
    }

    /// <summary>The top-left corner of the first red region in a picture, or null.</summary>
    private static (int X, int Y)? RedCorner(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha > 200 && c.Red > 200 && c.Green < 60 && c.Blue < 60) return (x, y);
            }
        }
        return null;
    }

    [AvaloniaFact]
    public void TheCanvasShowsAGrownDocumentWhereTheToolsSayItIs()
    {
        var (vm, shown) = Open();
        vm.PublishSnapshot();
        using (var before = shown())
        {
            Assert.Equal((4, 6), RedCorner(before));
        }

        GrowLeftAndUp(vm);

        using var after = shown();
        var corner = RedCorner(after);
        output.WriteLine($"canvas {after.Width}x{after.Height}; the square's corner is at {corner}; expected ({4 + GrewX}, {6 + GrewY})");
        Assert.Equal((64 + GrewX, 64 + GrewY), (after.Width, after.Height));
        Assert.Equal((4 + GrewX, 6 + GrewY), corner);
    }

    [AvaloniaFact]
    public void AMarkMadeInTheNewPaperShowsWhereItWasMade()
    {
        var (vm, shown) = Open();
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame = new Frame();
        GrowLeftAndUp(vm);
        vm.SmoothStrokes = false;
        vm.ColorHex = "#ff0000";
        vm.BrushSize = 8;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        // A dab in the new margin, addressed the way the view addresses it:
        // in stroke coordinates, which are negative out there.
        vm.BeginStroke(-14, -5, 1);
        vm.MoveStroke(-13, -5, 1);
        vm.EndStroke();
        vm.PublishSnapshot();

        using var after = shown();
        var centre = after.GetPixel(-14 + GrewX, -5 + GrewY);
        output.WriteLine($"pixel at ({-14 + GrewX}, {-5 + GrewY}) where the mark was made: {centre}; first red at {RedCorner(after)}");
        Assert.True(centre.Red > 200 && centre.Green < 80 && centre.Alpha > 200, centre.ToString());
    }

    /// <summary>
    /// An eraser that rubs ink out on grown paper is kept. The commit asks the
    /// pixels whether an erasure changed anything (B236) by holding the cached
    /// drawing across the stamp — and on grown paper the stamp is replaced by
    /// a rebuild, so the picture it holds never changes and a real erasure
    /// reads as one that did nothing.
    /// </summary>
    [AvaloniaFact]
    public void AnErasureOnGrownPaperIsKept()
    {
        var (vm, shown) = Open();
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame = new Frame();
        GrowLeftAndUp(vm);
        vm.SmoothStrokes = false;
        vm.ColorHex = "#ff0000";
        vm.BrushSize = 10;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BeginStroke(10, 30, 1);
        vm.MoveStroke(30, 30, 1);
        vm.MoveStroke(50, 30, 1);
        vm.EndStroke();
        vm.PublishSnapshot();
        using (var inked = shown()) Assert.NotNull(RedCorner(inked));
        var frame = (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;
        Assert.Single(frame.Strokes);

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 10;
        vm.BeginStroke(20, 30, 1);
        vm.MoveStroke(30, 30, 1);
        vm.MoveStroke(40, 30, 1);
        vm.EndStroke();
        vm.PublishSnapshot();

        frame = (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;
        using var after = shown();
        var middle = after.GetPixel(30 + GrewX, 30 + GrewY);
        output.WriteLine($"strokes after erasing: {frame.Strokes.Count}; pixel in the rubbed middle: {middle}");
        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal(ToolKind.Eraser, frame.Strokes[^1].Tool);
        Assert.False(middle.Red > 200 && middle.Green < 80, $"the ink is still there: {middle}");
    }

    /// <summary>
    /// What a variant wears is drawn in the same place a stored placement is:
    /// moved with the paper. The overlay is its own render, beside the frame
    /// cache rather than through it, so it has to be told separately.
    /// </summary>
    [AvaloniaFact]
    public void WhatAVariantWearsMovesWithThePaper()
    {
        var symbol = new Symbol
        {
            Name = "Badge",
            Layers = Symbol.Flat("Badge",
            [
                new Frame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Tool = ToolKind.Fill,
                            Color = "#ff0000",
                            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
                            Points = [new(0, 0, 1), new(8, 0, 1), new(8, 8, 1), new(0, 8, 1)],
                        },
                    ],
                },
            ]),
        };
        Lightbox.Raster.SymbolRegistry.Register(symbol);
        var before = AttachmentOverlay.Resolver;
        try
        {
            AttachmentOverlay.Resolver = (_, _) => [new SymbolPlacement { SymbolId = symbol.Id, X = 4, Y = 6 }];

            var still = DocumentFactory.CreateDoc(64, 64, 12).Scene;
            using var unmoved = AttachmentOverlay.Render(still, 0)!;
            var at = RedCorner(unmoved);
            Assert.NotNull(at);

            var grown = DocumentFactory.CreateDoc(64, 64, 12).Scene;
            CanvasResize.Apply(grown, 64 + GrewX, 64 + GrewY, ResizeAnchor.BottomRight);
            Assert.Equal((-GrewX, -GrewY), (grown.Left, grown.Top));
            using var moved = AttachmentOverlay.Render(grown, 0)!;

            output.WriteLine($"worn piece: {at} on plain paper, {RedCorner(moved)} on grown; expected ({at!.Value.X + GrewX}, {at.Value.Y + GrewY})");
            Assert.Equal((at.Value.X + GrewX, at.Value.Y + GrewY), RedCorner(moved));
        }
        finally
        {
            AttachmentOverlay.Resolver = before;
            Lightbox.Raster.SymbolRegistry.Clear();
        }
    }
}
