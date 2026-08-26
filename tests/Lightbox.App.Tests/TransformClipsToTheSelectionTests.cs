using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A transform on a selection moves the selected part and leaves the rest.
/// </summary>
/// <remarks>
/// <para>
/// B319 was reported as two bugs that read as opposites — freehand, circle and
/// square "do not seem to trigger transform at all", polygon and magic wand
/// "transform whatever's on canvas" — and they were one rule seen from two
/// sides. Every selection shape converged on the same mask and the same
/// filter, with no branch between them; the filter took a stroke only when the
/// majority of its visible points were inside, and then moved all of it. The
/// shapes an artist reaches for to grab <em>part</em> of a drawing fail that
/// majority and do nothing; a generously drawn polygon and a wand click on a
/// connected region enclose whole strokes and move everything.
/// </para>
/// <para>
/// Both assertions here are made against the record rather than against
/// pixels, on purpose: the promise is about what the document says, and a
/// pixel probe near a soft edge would be arguing with antialiasing instead
/// (see the brush-measurement skill).
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformClipsToTheSelectionTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>One hard line straight across the middle, from x=100 to x=500.</summary>
    private static MainViewModel Drawn()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.MoveStroke(500, 200, 1);
        vm.EndStroke();
        return vm;
    }

    private static Frame ActiveFrame(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;

    private static List<StrokePoint> Box(double x0, double y0, double x1, double y1) =>
        [new(x0, y0, 1), new(x1, y0, 1), new(x1, y1, 1), new(x0, y1, 1)];

    // ---- the refusal ---------------------------------------------------------

    /// <summary>
    /// A box over a corner of one long line has something to transform.
    /// </summary>
    /// <remarks>
    /// The reported symptom, exactly: <c>BeginTransform</c> returned false and
    /// the status line said <em>"Nothing to transform in this scope."</em> —
    /// with the marquee plainly sitting on the drawing. Two of the line's five
    /// points were inside, the majority was not, and the tool reported that as
    /// an empty canvas.
    /// </remarks>
    [AvaloniaFact]
    public void ASmallBoxOverPartOfALineFindsSomethingToTransform()
    {
        var vm = Drawn();
        vm.ApplySelectionShape(Box(100, 150, 200, 250), false, false);
        Assert.True(vm.HasSelection);

        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");
    }

    // ---- the clip ------------------------------------------------------------

    /// <summary>
    /// Selecting the left half and dragging it down moves the left half.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured before the fix, on this exact drawing: the record came back
    /// <c>first=(100,300) last=(500,300)</c> — both ends down, the whole line
    /// carried off by a box that covered a bit over half of it.
    /// </para>
    /// <para>
    /// The line is not cut in two to fix it. It becomes two entries carrying
    /// complementary clips, so both re-render dab for dab as the original did
    /// — cutting the polyline would restart the dab walk and re-roll every
    /// <c>Hash01</c> dynamic along both halves, which is invariant 2.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void HalfSelectedIsHalfMoved()
    {
        var vm = Drawn();
        vm.ApplySelectionShape(Box(50, 150, 320, 250), false, false);
        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");

        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 100); // straight down 100

        var strokes = ActiveFrame(vm).Strokes;
        output.WriteLine(
            $"{strokes.Count} strokes: "
            + string.Join(
                "; ",
                strokes.Select(s =>
                    $"[{s.Points[0].X:0},{s.Points[0].Y:0}]..[{s.Points[^1].X:0},{s.Points[^1].Y:0}] clip={s.ClipId ?? "none"}")));

        // Two entries: the part that stayed and the part that travelled.
        Assert.Equal(2, strokes.Count);
        var stayed = strokes[0];
        var moved = strokes[1];

        // The one left behind did not move a pixel…
        Assert.Equal(200, stayed.Points[0].Y, 3);
        Assert.Equal(200, stayed.Points[^1].Y, 3);
        // …the one that travelled moved the whole way…
        Assert.Equal(300, moved.Points[0].Y, 3);
        Assert.Equal(300, moved.Points[^1].Y, 3);
        // …and each carries the clip that makes only its own half visible.
        Assert.NotNull(stayed.ClipId);
        Assert.NotNull(moved.ClipId);
        Assert.NotEqual(stayed.ClipId, moved.ClipId);
    }

    /// <summary>
    /// A line wholly inside the selection still moves whole, with no clip.
    /// </summary>
    /// <remarks>
    /// The common case, and the one that must not get more expensive or more
    /// complicated to serve the crossing case: no copy, no clip, one entry in
    /// the record exactly as before. Clipping everything the region catches
    /// would also cut a soft brush's spill at the boundary — ink the artist
    /// sees as inside the marquee.
    /// </remarks>
    [AvaloniaFact]
    public void AWhollySelectedLineMovesWholeAndUnclipped()
    {
        var vm = Drawn();
        vm.ApplySelectionShape(Box(0, 100, 600, 300), false, false);
        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");

        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 100);

        var strokes = ActiveFrame(vm).Strokes;
        Assert.Single(strokes);
        Assert.Null(strokes[0].ClipId);
        Assert.Equal(300, strokes[0].Points[0].Y, 3);
        Assert.Equal(300, strokes[0].Points[^1].Y, 3);
    }

    /// <summary>The clips reach the document, or a reload loses the split.</summary>
    /// <remarks>
    /// A clip is part of the record (invariant 3). One the strokes reference
    /// and <c>Doc.ClipRegions</c> does not would reload as two <em>unclipped</em>
    /// copies of the line — the whole thing back, drawn twice, at both
    /// positions. That is a worse outcome than the bug being fixed, so it gets
    /// its own assertion rather than riding on the one above.
    /// </remarks>
    [AvaloniaFact]
    public void TheSplitsClipsAreStoredOnTheDocument()
    {
        var vm = Drawn();
        vm.ApplySelectionShape(Box(50, 150, 320, 250), false, false);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 100);

        foreach (var stroke in ActiveFrame(vm).Strokes)
        {
            Assert.NotNull(stroke.ClipId);
            Assert.True(
                vm.Doc.ClipRegions.ContainsKey(stroke.ClipId!),
                $"clip {stroke.ClipId} is referenced but not stored");
        }
    }

    /// <summary>Undo puts the one line back, clip and all.</summary>
    [AvaloniaFact]
    public void UndoRestoresTheSingleUnclippedLine()
    {
        var vm = Drawn();
        vm.ApplySelectionShape(Box(50, 150, 320, 250), false, false);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 100);
        Assert.Equal(2, ActiveFrame(vm).Strokes.Count);

        vm.UndoCommand.Execute(null);

        var strokes = ActiveFrame(vm).Strokes;
        Assert.Single(strokes);
        Assert.Null(strokes[0].ClipId);
        Assert.Equal(200, strokes[0].Points[0].Y, 3);
        Assert.Equal(200, strokes[0].Points[^1].Y, 3);
    }

    // ---- picked lines are not a region ---------------------------------------

    /// <summary>
    /// A line picked with the Arrow moves whole, however little of it the last
    /// marquee covered.
    /// </summary>
    /// <remarks>
    /// The manual's asymmetry, and the reason the split asks
    /// <c>HasSelection</c> rather than testing the filter: "a region copies
    /// what you boxed; picked lines copy whole". Clipping a picked line to a
    /// marquee sitting somewhere else would be the same class of surprise
    /// Q97 already ruled on for precedence.
    /// </remarks>
    [AvaloniaFact]
    public void APickedLineIsNotClippedByAMarquee()
    {
        var vm = Drawn();
        var id = ActiveFrame(vm).Strokes[0].Id;
        vm.ActiveTool = ToolId.Arrow;
        vm.Selection.SelectStroke(id);
        Assert.True(vm.HasStrokeSelection);
        Assert.False(vm.HasSelection);

        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");
        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 100);

        var strokes = ActiveFrame(vm).Strokes;
        Assert.Single(strokes);
        Assert.Null(strokes[0].ClipId);
        Assert.Equal(300, strokes[0].Points[0].Y, 3);
    }
}
