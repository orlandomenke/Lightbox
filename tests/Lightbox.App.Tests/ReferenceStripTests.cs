using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// An imported animation reference on the canvas: which frame of the sheet
/// shows, where it sits, and — the one that matters most — that none of it
/// ever reaches an exported pixel.
///
/// The slicing itself is covered without a window by
/// <c>Lightbox.Core.Tests.StripSlicerTests</c>.
/// </summary>
[Collection("BrushState")]
public class ReferenceStripTests : BrushStateIsolated
{
    private static readonly SKColor[] Colours =
        [new(220, 20, 20), new(20, 200, 20), new(20, 20, 220), new(220, 200, 20)];

    /// <summary>
    /// A four-frame sheet: 40×10, one 10-wide cell per frame, each holding a
    /// 6×6 block of its own colour so a test can say which frame it is looking
    /// at from a single pixel.
    /// </summary>
    private static string Sheet(int cells = 4)
    {
        using var bmp = new SKBitmap(cells * 10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            for (var i = 0; i < cells; i++)
            {
                using var paint = new SKPaint { Color = Colours[i % Colours.Length] };
                canvas.DrawRect(SKRect.Create(i * 10 + 2, 2, 6, 6), paint);
            }
        }
        return PngCodec.Encode(bmp);
    }

    private static MainViewModel Vm() => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 30,
        BrushHardness = 1,
        BrushOpacity = 1,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
        AntiAliasing = false,
    };

    private static MainViewModel WithReference(out ReferenceStrip strip, int cells = 4)
    {
        var vm = Vm();
        strip = vm.ImportReference("Run", Sheet(cells))!;
        Assert.NotNull(strip);
        vm.ReferenceOpacity = 1; // exact colours; the default 0.5 is for drawing
        return vm;
    }

    private static SKColor Sample(MainViewModel vm, int x, int y)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        Assert.NotNull(latest);
        using var bmp = SKBitmap.FromImage(latest!.Image);
        return bmp.GetPixel(x, y);
    }

    /// <summary>The middle of the block the reference is showing.</summary>
    private static (int X, int Y) Spot(MainViewModel vm)
    {
        var strip = vm.ActiveReference!;
        var cell = vm.ActiveReferenceCell!;
        return (
            (int)(strip.OffsetX + cell.Dx + 5 * strip.Scale),
            (int)(strip.OffsetY + cell.Dy + 5 * strip.Scale));
    }

    // ---- importing ---------------------------------------------------------------

    [AvaloniaFact]
    public void ImportingASheetFindsItsFrames()
    {
        var vm = WithReference(out var strip);

        Assert.Equal(4, strip.Cells.Count);
        Assert.All(strip.Cells, c => Assert.Equal(10, c.Width));
        Assert.True(vm.HasReferences);
    }

    [AvaloniaFact]
    public void ImportingExtendsTheTimelineToFitTheReference()
    {
        // A twelve-frame reference on a one-frame document with eleven of it
        // invisible is not a state anybody asked for: you imported it because
        // you are here to draw those frames.
        var vm = Vm();
        Assert.Equal(1, vm.Doc.Scene.FrameCount);

        vm.ImportReference("Run", Sheet());

        Assert.Equal(4, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void AskingNotToAddFramesLeavesTheTimelineAlone()
    {
        var vm = Vm();

        vm.ImportReference("Run", Sheet(), addFrames: false);

        Assert.Equal(1, vm.Doc.Scene.FrameCount);
        Assert.Equal(4, vm.ActiveReference!.Cells.Count);
    }

    [AvaloniaFact]
    public void AnOversizedSheetIsScaledToFitTheCanvas()
    {
        // 1:1 would put a 2000px sprite sheet mostly off screen, with no
        // obvious way back.
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Small", 8, 8, 12, 72, "#ffffff", false));

        var strip = vm.ImportReference("Big", Sheet())!;

        Assert.Equal(4, strip.Cells.Count);
        Assert.True(strip.Scale < 1, $"expected the sheet scaled down, got {strip.Scale}");
        Assert.True(strip.Cells[0].Width * strip.Scale <= 8);
    }

    [AvaloniaFact]
    public void ASheetThatWillNotDecodeIsRefusedRatherThanCrashing()
    {
        var vm = Vm();

        Assert.Null(vm.ImportReference("Bad", "not a png"));
        Assert.False(vm.HasReferences);
    }

    [AvaloniaFact]
    public void ASheetCanBeCutOnADeclaredGridInstead()
    {
        // The arrangement detection cannot find: rows that touch. The artist
        // says so, and the pixels are not consulted.
        var vm = Vm();

        var strip = vm.ImportReference("Grid", Sheet(), new SliceOptions(Columns: 8, Rows: 2))!;

        Assert.Equal(16, strip.Cells.Count);
        Assert.All(strip.Cells, c => Assert.Equal(5, c.Width));
    }

    // ---- what lands on the canvas ---------------------------------------------

    [AvaloniaFact]
    public void EachFrameShowsItsOwnPartOfTheSheet()
    {
        // The headline behaviour. Frame 1 of the document shows frame 1 of the
        // reference, and so on down the row.
        var vm = WithReference(out _);

        for (var i = 0; i < 4; i++)
        {
            vm.CurrentFrameIndex = i;
            var (x, y) = Spot(vm);
            Assert.Equal(Colours[i], Sample(vm, x, y));
        }
    }

    [AvaloniaFact]
    public void TheReferenceSitsOverThePaperAndUnderTheDrawing()
    {
        // Where you would tape a photograph to a lightbox: over the paper,
        // because the paper is opaque and would hide it, and under the
        // drawing, because it is what you are drawing against.
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);
        Assert.Equal(Colours[0], Sample(vm, x, y));

        vm.BeginStroke(x - 20, y, 1);
        vm.MoveStroke(x + 20, y, 1);
        vm.EndStroke();

        Assert.Equal(SKColors.Black, Sample(vm, x, y));
    }

    [AvaloniaFact]
    public void HidingTheReferenceTakesItOffTheCanvas()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);

        vm.ReferenceVisible = false;

        Assert.Equal(SKColors.White, Sample(vm, x, y));
    }

    [AvaloniaFact]
    public void AFrameWithNoReferenceShowsNothing()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);
        vm.AddFrameCommand.Execute(null); // an inbetween, with no reference of its own

        Assert.Equal(SKColors.White, Sample(vm, x, y));
    }

    // ---- alignment and scale -----------------------------------------------------

    [AvaloniaFact]
    public void NudgingACellMovesThatFrameAndOnlyThatFrame()
    {
        var vm = WithReference(out var strip);
        var (x, y) = Spot(vm);

        vm.NudgeReferenceCell(20, 0);

        Assert.Equal(SKColors.White, Sample(vm, x, y));
        Assert.Equal(Colours[0], Sample(vm, x + 20, y));

        // Frame 2 is untouched: alignment is per drawing.
        vm.CurrentFrameIndex = 1;
        Assert.Equal(0, strip.Cells[1].Dx, 5);
        Assert.Equal(Colours[1], Sample(vm, x, y));
    }

    [AvaloniaFact]
    public void NudgingTheSheetMovesEveryFrameTogether()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);

        vm.NudgeReference(0, 15);

        Assert.Equal(Colours[0], Sample(vm, x, y + 15));
        vm.CurrentFrameIndex = 2;
        Assert.Equal(Colours[2], Sample(vm, x, y + 15));
    }

    [AvaloniaFact]
    public void ScaleAppliesToEveryFrame()
    {
        // Per-frame scale would be worse than unnecessary: it would put the
        // character at a different size on each drawing, which is the one
        // thing a size reference exists to prevent.
        var vm = WithReference(out var strip);
        var before = Spot(vm);

        vm.ReferenceScale = 4;

        Assert.Equal(4, strip.Scale, 5);
        // The block is now 24 document pixels across instead of 6, so a point
        // well outside the old one is inside the new one.
        var corner = ((int)(strip.OffsetX + 3 * 4), (int)(strip.OffsetY + 3 * 4));
        Assert.Equal(Colours[0], Sample(vm, corner.Item1, corner.Item2));
        vm.CurrentFrameIndex = 3;
        Assert.Equal(Colours[3], Sample(vm, corner.Item1, corner.Item2));
        Assert.NotEqual(before, Spot(vm));
    }

    [AvaloniaFact]
    public void ClearingAlignmentUndoesEveryNudge()
    {
        var vm = WithReference(out var strip);
        vm.NudgeReferenceCell(9, 9);
        vm.CurrentFrameIndex = 2;
        vm.NudgeReferenceCell(-4, 3);

        vm.ClearReferenceAlignmentCommand.Execute(null);

        Assert.All(strip.Cells, c => Assert.Equal(0, c.Dx, 5));
        Assert.All(strip.Cells, c => Assert.Equal(0, c.Dy, 5));
    }

    [AvaloniaFact]
    public void AlignmentIsUndoable()
    {
        var vm = WithReference(out var strip);

        vm.NudgeReferenceCell(11, 0);
        Assert.Equal(11, strip.Cells[0].Dx, 5);

        vm.UndoCommand.Execute(null);

        Assert.Equal(0, vm.ActiveReference!.Cells[0].Dx, 5);
    }

    // ---- following the timeline -------------------------------------------------

    [AvaloniaFact]
    public void AddingAFrameMovesTheReferenceAlongWithTheAnimation()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);

        vm.AddFrameCommand.Execute(null); // playhead lands on the new frame 2

        Assert.Equal(1, vm.CurrentFrameIndex);
        Assert.Equal(SKColors.White, Sample(vm, x, y));
        vm.CurrentFrameIndex = 2;
        Assert.Equal(Colours[1], Sample(vm, x, y));
    }

    [AvaloniaFact]
    public void AReferencePinnedToAbsoluteTimingStaysPut()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);
        vm.ReferenceFollowsTimeline = false;

        vm.AddFrameCommand.Execute(null);

        Assert.Equal(1, vm.CurrentFrameIndex);
        Assert.Equal(Colours[1], Sample(vm, x, y));
    }

    // ---- absence, and staying out of the artwork ---------------------------------

    [AvaloniaFact]
    public void AReferenceIsNeverExported()
    {
        // The line this feature must not cross. A reference is an aid, not
        // artwork: it is not in the stroke record, and it must not appear in
        // anything that leaves the app.
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);
        Assert.Equal(Colours[0], Sample(vm, x, y));

        var png = vm.RenderFramePng(0);
        using var exported = PngCodec.Decode(png);

        Assert.Equal(SKColors.White, exported.GetPixel(x, y));
    }

    [AvaloniaFact]
    public void RemovingTheLastReferenceLeavesTheDocumentWithNoKeyForOne()
    {
        var vm = WithReference(out _);
        var (x, y) = Spot(vm);

        vm.RemoveReferenceCommand.Execute(null);

        Assert.False(vm.HasReferences);
        Assert.Null(vm.Doc.Scene.References);
        Assert.Equal(SKColors.White, Sample(vm, x, y));
    }

    [AvaloniaFact]
    public void ADocumentWithNoReferenceCompositesExactlyAsItAlwaysHas()
    {
        // The camera's absence test, reapplied. Nothing about the pass list
        // changes for the documents that never use this.
        var plain = Vm();
        plain.BeginStroke(100, 100, 1);
        plain.MoveStroke(200, 140, 1);
        plain.EndStroke();

        Assert.False(plain.HasReferences);
        Assert.Null(plain.Doc.Scene.References);
        Assert.Equal(SKColors.Black, Sample(plain, 150, 120));
        Assert.Equal(SKColors.White, Sample(plain, 600, 400));
    }

    [AvaloniaFact]
    public void AReferenceSurvivesSavingAndReopening()
    {
        var vm = WithReference(out var strip);
        vm.NudgeReferenceCell(7, -3);
        var json = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);

        var reopened = Vm();
        reopened.OpenDocumentTab(Lightbox.Core.Serialization.DocJson.Deserialize(json), null);
        reopened.CurrentFrameIndex = 0;

        var back = reopened.ActiveReference!;
        Assert.Equal(strip.Cells.Count, back.Cells.Count);
        Assert.Equal(7, back.Cells[0].Dx, 5);
        var (x, y) = Spot(reopened);
        Assert.Equal(Colours[0], Sample(reopened, x, y));
    }
}
