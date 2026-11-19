using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The line cleaner as the artist reaches it (Q292–Q295): a preview that writes
/// nothing, Apply as one undo step, Cancel leaving the drawing alone, and the
/// opt-in that cleans generated inbetweens as they are made.
/// </summary>
[Collection("BrushState")]
public sealed class LineCleanupVmTests : BrushStateIsolated
{
    private static void Draw(MainViewModel vm, IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        vm.BeginStroke(list[0].X, list[0].Y, 0.5);
        foreach (var p in list.Skip(1)) vm.MoveStroke(p.X, p.Y, 0.5);
        vm.EndStroke();
    }

    /// <summary>A shaky horizontal line, one sample per pixel.</summary>
    private static IEnumerable<(double, double)> Shaky(double y) =>
        Enumerable.Range(0, 201).Select(i => (100.0 + i, y + 1.5 * Math.Sin(i / 2.0)));

    private static MainViewModel OneShakyLine()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, Shaky(200));
        return vm;
    }

    private static List<StrokePoint> PointsOf(MainViewModel vm, int cel = 0) =>
        [.. vm.PaintLayer().Cels[cel].Frame!.Strokes.SelectMany(s => s.Points)];

    [AvaloniaFact]
    public void APreviewShowsTheCleanupAndWritesNothing()
    {
        var vm = OneShakyLine();
        var before = PointsOf(vm);

        var drawings = vm.PreviewCleanup(guided: false);

        Assert.Equal(1, drawings);
        Assert.True(vm.IsCleanupPending);
        Assert.Equal(before, PointsOf(vm));
        var line = Assert.Single(vm.CleanupView!.Lines);
        Assert.Equal(CleanupKind.Smoothed, line.Kind);
        Assert.NotEqual(before, line.Points);
        Assert.Contains("Smoothed 1 line", vm.CleanupSummary);
    }

    [AvaloniaFact]
    public void ApplyIsOneUndoStepThatPutsTheLineBackExactly()
    {
        var vm = OneShakyLine();
        var before = PointsOf(vm);
        vm.PreviewCleanup(guided: false);
        var proposed = vm.CleanupView!.Lines[0].Points.ToList();

        Assert.True(vm.TryApplyCleanup());

        Assert.Equal(proposed, PointsOf(vm));
        Assert.False(vm.IsCleanupPending);
        Assert.Null(vm.CleanupView);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, PointsOf(vm));
        vm.RedoCommand.Execute(null);
        Assert.Equal(proposed, PointsOf(vm));
    }

    [AvaloniaFact]
    public void CancelLeavesTheDrawingExactlyAsItWas()
    {
        var vm = OneShakyLine();
        var before = PointsOf(vm);
        vm.PreviewCleanup(guided: false);

        vm.CancelCleanupCommand.Execute(null);

        Assert.False(vm.IsCleanupPending);
        Assert.Null(vm.CleanupView);
        Assert.Equal(before, PointsOf(vm));
        // No step was pushed: one undo takes away the line itself.
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.PaintLayer().Cels[0].Frame?.Strokes ?? []);
    }

    [AvaloniaFact]
    public void APreviewOfADrawingThatHasSinceChangedIsNotApplied()
    {
        var vm = OneShakyLine();
        vm.PreviewCleanup(guided: false);
        Draw(vm, Shaky(300));
        var now = PointsOf(vm);

        Assert.False(vm.TryApplyCleanup());

        Assert.Equal(now, PointsOf(vm));
        Assert.Contains("changed after the preview", vm.AiStatus);
    }

    [AvaloniaFact]
    public void CleaningTowardKeysWithNoKeysAroundSaysSoAndChangesNothing()
    {
        var vm = OneShakyLine();
        var before = PointsOf(vm);

        Assert.Equal(0, vm.PreviewCleanup(guided: true));

        Assert.False(vm.IsCleanupPending);
        Assert.Equal(before, PointsOf(vm));
        Assert.Contains("no hand-drawn key", vm.AiStatus);
    }

    /// <summary>Two keys a frame apart: a shaky line, and the same line moved down.</summary>
    private static MainViewModel TwoKeys()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, Shaky(200));
        vm.AddFrameCommand.Execute(null);
        Draw(vm, Shaky(240));
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        return vm;
    }

    [AvaloniaFact]
    public void NewInbetweensAreNotCleanedUnlessTheArtistAsks()
    {
        var vm = TwoKeys();
        Assert.False(vm.CleanNewInbetweens);
        var keys = vm.PaintLayer().Cels.Select(c => c.Frame).OfType<Frame>().Select(f => f.Strokes).ToList();
        var plain = Inbetweener.Inbetween(keys[0], keys[1], 0.5, Easing.Linear);

        vm.InsertInbetweensCommand.Execute(null);

        Assert.Equal(plain.SelectMany(s => s.Points), PointsOf(vm, 1));
    }

    /// <summary>
    /// Two keys where only the first has a short tick: across four inbetweens
    /// the tick fades out, and by the second it is a faint orphan mark.
    /// </summary>
    private static MainViewModel KeysWithATickThatGoes()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, Shaky(200));
        Draw(vm, [(150, 100), (160, 100)]);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, Shaky(240));
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 4;
        vm.TweenEasing = Easing.Linear;
        return vm;
    }

    [AvaloniaFact]
    public void WhenAskedEachNewInbetweenIsCleanedTowardItsKeysInTheSameStep()
    {
        var vm = KeysWithATickThatGoes();
        vm.InsertInbetweensCommand.Execute(null);
        var plain = vm.PaintLayer().Cels[2].Frame!.Strokes.Count;    // t = 0.4: line and faint tick
        vm.UndoCommand.Execute(null);
        var keyAPoints = vm.PaintLayer().Cels[0].Frame!.Strokes.Select(s => s.Points.ToList()).ToList();

        vm.CleanNewInbetweens = true;
        vm.CurrentFrameIndex = 0;
        vm.InsertInbetweensCommand.Execute(null);

        var layer = vm.PaintLayer();
        Assert.Equal(2, plain);
        Assert.Single(layer.Cels[2].Frame!.Strokes);                                 // the orphan went
        Assert.Equal(FrameRole.Inbetween, layer.Cels[2].Frame!.Role);
        Assert.Equal(keyAPoints, layer.Cels[0].Frame!.Strokes.Select(s => s.Points.ToList()).ToList()); // the key is never touched
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.PaintLayer().Cels.Count(c => c.Frame is not null));      // one step took the insert and the clean
    }

    [AvaloniaFact]
    public void TheOptInIsRememberedAndADocumentThatNeverUsesTheCleanerWritesNothingNew()
    {
        var vm = OneShakyLine();
        vm.CleanNewInbetweens = true;
        vm.CurrentFrameIndex = 0;
        Assert.True(vm.Settings.CleanNewInbetweens);

        var json = DocJson.Serialize(vm.Doc);
        Assert.DoesNotContain("clean", json, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheCommandsAreRegisteredWhereTheConfigurationWindowCanSeeThem()
    {
        var ids = new Lightbox.App.Services.ShortcutMap().Definitions.Select(d => d.Id).ToList();

        Assert.Contains("timeline.cleanLines", ids);
        Assert.Contains("timeline.cleanLinesTowardKeys", ids);
        Assert.Contains("timeline.applyCleanup", ids);
        Assert.Contains("timeline.cancelCleanup", ids);
    }
}
