using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Match lines mode (Q271): on an inbetween, both keys are shown cut into
/// pieces; a click on a line of key A starts a match, clicks on lines of key B
/// complete it, and every click that changes a match saves it on key A and
/// redraws the run's inbetweens in the same undo step.
/// </summary>
[Collection("BrushState")]
public sealed class LineMatchModeTests : BrushStateIsolated
{
    private const double Reach = 6;

    private static void Draw(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 0.5);
        foreach (var p in points.Skip(1)) vm.MoveStroke(p.X, p.Y, 0.5);
        vm.EndStroke();
    }

    /// <summary>
    /// Key A: an L as one stroke (cut at its corner into the upright, piece 0,
    /// and the foot, piece 1). Key B: the same L 40 to the right as two strokes,
    /// the foot first and backwards. One inbetween between them, already drawn
    /// the scrambled way, with the playhead on it.
    /// </summary>
    private static MainViewModel OnTheInbetween()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, (100, 100), (100, 200), (100, 300), (150, 300), (200, 300));
        vm.AddFrameCommand.Execute(null);
        Draw(vm, (240, 300), (190, 300), (146, 300));
        Draw(vm, (140, 294), (140, 200), (140, 100));
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        vm.InsertInbetweensCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    private static double OffTheHalfwayL(Frame frame)
    {
        StrokePoint[] l = [new(120, 100, 0), new(120, 300, 0), new(220, 300, 0)];
        return frame.Strokes.SelectMany(s => s.Points).Max(p =>
            Math.Min(GeometryOps.DistToSegment(p, l[0], l[1]), GeometryOps.DistToSegment(p, l[1], l[2])));
    }

    /// <summary>The whole match, clicked: A's upright, A's foot (shift), then B's foot and B's upright.</summary>
    private static void MatchTheL(MainViewModel vm)
    {
        vm.LineMatchPress(100, 150, shift: false, Reach);
        vm.LineMatchPress(160, 300, shift: true, Reach);
        vm.LineMatchPress(220, 300, shift: false, Reach);
        vm.LineMatchPress(140, 150, shift: false, Reach);
    }

    [AvaloniaFact]
    public void EnteringOnAnInbetweenShowsBothKeysInPieces()
    {
        var vm = OnTheInbetween();

        vm.LineMatchMode = true;

        Assert.True(vm.LineMatchMode);
        var view = vm.LineMatchView!;
        Assert.Equal(2, view.Lines.Count(l => l.Key == 0));   // the L, cut at its corner
        Assert.Equal(2, view.Lines.Count(l => l.Key == 1));   // two strokes, nothing to cut
        Assert.All(view.Lines, l => Assert.Null(l.Match));
    }

    [AvaloniaFact]
    public void ClickingALineOfAThenItsLinesInBMatchesThemAndRedrawsTheInbetween()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;

        MatchTheL(vm);

        var layer = vm.PaintLayer();
        var match = Assert.Single(layer.Cels[0].Frame!.Matches!);
        Assert.Equal(2, match.A.Count);
        Assert.Equal(2, match.B.Count);
        var off = OffTheHalfwayL(layer.Cels[1].Frame!);
        Assert.True(off < 4, $"{off:0.0}px off the halfway L");
        Assert.All(vm.LineMatchView!.Lines, l => Assert.Equal(0, l.Match));
    }

    [AvaloniaFact]
    public void AClickOnKeyAAloneSavesNothing()
    {
        // A line of A with nothing in B yet is a match being made, not a line
        // that goes — saving it would fade the line out of the inbetweens.
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        var before = vm.PaintLayer().Cels[1].Frame;

        vm.LineMatchPress(100, 150, shift: false, Reach);

        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
        Assert.Same(before, vm.PaintLayer().Cels[1].Frame);
        Assert.Contains(vm.LineMatchView!.Lines, l => l.Pending);
    }

    [AvaloniaFact]
    public void EachClickThatChangesAMatchIsOneUndoStep()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);

        vm.UndoCommand.Execute(null);   // takes back B's upright only

        var match = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!);
        Assert.Single(match.B);
    }

    [AvaloniaFact]
    public void ClickingAMatchedLineOfBTakesItOutOfItsMatch()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);
        vm.LineMatchPress(500, 500, shift: false, Reach);   // empty canvas: the match is no longer open

        vm.LineMatchPress(140, 150, shift: false, Reach);

        var match = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!);
        Assert.Single(match.B);
        Assert.Contains(vm.LineMatchView!.Lines, l => l.Key == 1 && l.Match is null);
    }

    [AvaloniaFact]
    public void UnmatchingTheLastLineLeavesNoMatchesInTheFile()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        vm.LineMatchPress(100, 150, shift: false, Reach);
        vm.LineMatchPress(140, 150, shift: false, Reach);
        vm.LineMatchPress(500, 500, shift: false, Reach);

        vm.LineMatchPress(100, 150, shift: false, Reach);   // A's upright: out of its match, and the match is empty

        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
    }

    [AvaloniaFact]
    public void WithNoGapUnderThePlayheadTheModeDoesNotOpenAndSaysWhy()
    {
        var vm = new MainViewModel();
        Draw(vm, (10, 10), (60, 10));

        vm.LineMatchMode = true;

        Assert.False(vm.LineMatchMode);
        Assert.Contains("key", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void LeavingTheModeClearsTheOverlay()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;

        vm.LineMatchMode = false;

        Assert.Null(vm.LineMatchView);
    }

    [AvaloniaFact]
    public void AnUndoShowsInTheOverlay()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);
        Assert.All(vm.LineMatchView!.Lines, l => Assert.Equal(0, l.Match));

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.LineMatchView!.Lines, l => l.Key == 1 && l.Match is null);
    }

    [AvaloniaFact]
    public void AnInbetweenTheArtistChangedIsLeftAloneAndTheStatusSaysSo()
    {
        // The artist touched up the inbetween: one extra line. A match must
        // not redraw over it (the sensitivity review's W4, 2026-10-14).
        var vm = OnTheInbetween();
        Draw(vm, (300, 300), (340, 320));
        var touched = vm.PaintLayer().Cels[1].Frame;
        vm.LineMatchMode = true;

        MatchTheL(vm);

        Assert.Same(touched, vm.PaintLayer().Cels[1].Frame);
        Assert.NotNull(vm.PaintLayer().Cels[0].Frame!.Matches);   // the match itself is saved
        Assert.Contains("left as", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AnInbetweenTheEngineDrewAndNobodyChangedIsRedrawn()
    {
        var vm = OnTheInbetween();
        var drawn = vm.PaintLayer().Cels[1].Frame;
        vm.LineMatchMode = true;

        MatchTheL(vm);

        Assert.NotSame(drawn, vm.PaintLayer().Cels[1].Frame);
        Assert.DoesNotContain("left as", vm.AiStatus);
    }

    [AvaloniaFact]
    public void RightClickingAMatchedLineSwitchesItsMatchBetweenSolidAndBending()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);
        var match = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!);
        Assert.Null(match.Solid);   // left to the engine

        vm.LineMatchToggleSolid(100, 150, Reach);
        var flipped = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!).Solid;
        vm.LineMatchToggleSolid(140, 150, Reach);   // either side of the match will do
        var back = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!).Solid;

        Assert.NotNull(flipped);
        Assert.Equal(!flipped, back);
        Assert.Contains(vm.LineMatchView!.Lines, l => l.Match == 0 && l.Solid == back);
    }

    [AvaloniaFact]
    public void RightClickingAnUnmatchedLineDoesNothing()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;

        vm.LineMatchToggleSolid(100, 150, Reach);

        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
    }

    [AvaloniaFact]
    public void ACelInsertedWhileMatchingDoesNotMoveTheMatchOntoAnotherDrawing()
    {
        // The gap is held by its drawings, not by where they sat on the sheet.
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        vm.CurrentFrameIndex = 0;
        vm.AddFrameCommand.Execute(null);   // a new cel after key A: everything after it moves along

        MatchTheL(vm);

        var layer = vm.PaintLayer();
        var withMatches = layer.Cels.Where(c => c.Frame?.Matches is { Count: > 0 }).ToList();
        var holder = Assert.Single(withMatches).Frame!;
        Assert.Equal(FrameRole.Key, holder.Role);
        Assert.Equal(100, holder.Strokes[0].Points[0].X);
    }

    [AvaloniaFact]
    public void AnotherDocumentEndsTheMode()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;

        vm.ReplaceDocument(Lightbox.Core.Documents.DocumentFactory.CreateDoc(320, 240, 12));

        Assert.False(vm.LineMatchMode);
        Assert.Null(vm.LineMatchView);
    }

    [AvaloniaFact]
    public void ClickingALineOfTheOpenMatchAgainTakesItOutRatherThanMatchingItTwice()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);

        vm.LineMatchPress(100, 150, shift: false, Reach);   // A's upright, already in the open match
        vm.LineMatchPress(140, 150, shift: false, Reach);   // would join a second match if one had opened

        var matches = vm.PaintLayer().Cels[0].Frame!.Matches ?? [];
        var all = matches.SelectMany(m => m.A).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [AvaloniaFact]
    public void AClickThatChangesNothingAddsNoUndoStep()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        MatchTheL(vm);

        vm.LineMatchPress(140, 150, shift: false, Reach);   // B's upright: already in the open match
        vm.UndoCommand.Execute(null);

        // The undo took back the last real change, B's upright, not a no-op.
        var match = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!);
        Assert.Single(match.B);
    }

    [AvaloniaFact]
    public void AnUndoClosesTheOpenMatch()
    {
        var vm = OnTheInbetween();
        vm.LineMatchMode = true;
        vm.LineMatchPress(100, 150, shift: false, Reach);
        vm.LineMatchPress(140, 150, shift: false, Reach);

        vm.UndoCommand.Execute(null);
        vm.LineMatchPress(140, 150, shift: false, Reach);   // with nothing open, a line of B matches nothing

        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
    }

    [AvaloniaFact]
    public void ALockedLayerCannotBeMatchedOrRedrawn()
    {
        var vm = OnTheInbetween();
        vm.PaintLayer().Locked = true;
        var before = vm.PaintLayer().Cels.Select(c => c.Frame).ToList();

        vm.LineMatchMode = true;
        vm.RedrawInbetweensCommand.Execute(null);

        Assert.False(vm.LineMatchMode);
        Assert.Contains("locked", vm.AiStatus);
        Assert.Equal(before, vm.PaintLayer().Cels.Select(c => c.Frame).ToList());
    }
}
