using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// What posing the rig writes to the performance log, for the lab's
/// pose-drag scenario: the cost of each pointer event on the UI thread, the
/// geometry of each posed drawing, and the key written on release.
/// </summary>
/// <remarks>
/// <para>
/// The rig had no span at all, so the lab could time an undo and a brush
/// stroke and say nothing about a pose drag — the one gesture where every
/// pointer event re-renders a whole drawing. Each span here is one question a
/// slow drag raises: is it the hand-off on the UI thread, the posing, or the
/// re-stamp (which the cache's own miss span already answers)?
/// </para>
/// <para>
/// In the <c>BrushState</c> collection for the reason
/// <c>PaintStrokePerfLogTests</c> is: the log is process-wide, so two classes
/// starting and stopping it must not run side by side.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PoseDragPerfLogTests : BrushStateIsolated
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"lightbox-poselog-{Guid.NewGuid():N}")).FullName;

    public override void Dispose()
    {
        PerfLog.StopForTests();
        Directory.Delete(_dir, recursive: true);
        base.Dispose();
    }

    /// <summary>One bone along the middle of a small document, and a line bound to it.</summary>
    private static (MainViewModel Vm, string Bone, string Drawing) Rigged(bool bound = true)
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        var bone = vm.Doc.Armature!.Bones[0].Id;
        var frame = vm.Doc.Scene.Layers.First(l => !l.IsBackground).Cels[0].Frame!;
        if (bound)
        {
            var stroke = new Stroke
            {
                Points = [new StrokePoint(105, 100, 1), new StrokePoint(140, 96, 1), new StrokePoint(175, 100, 1)],
                Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            };
            Skinning.AssignAll(stroke, bone);
            frame.Strokes.Add(stroke);
        }
        vm.PosingMode = true;
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        return (vm, bone, frame.Id);
    }

    private List<JsonElement> Record(MainViewModel vm, Action gesture)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.jsonl");
        PerfLog.StartForTests(path);
        try
        {
            gesture();
        }
        finally
        {
            PerfLog.StopForTests();
        }
        return File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement).ToList();
    }

    private static void Swing(MainViewModel vm, string bone)
    {
        for (var i = 1; i <= 3; i++)
        {
            vm.PreviewBoneGesture(bone, BoneGrab.Tip, 180, 100, 180 - i * 10, 100 + i * 20, extruding: false);
            for (var k = 0; k < 4; k++) Dispatcher.UIThread.RunJobs();
        }
        vm.EndBoneGesture(bone, BoneGrab.Tip, 180, 100, 150, 160, extruding: false);
        for (var k = 0; k < 4; k++) Dispatcher.UIThread.RunJobs();
    }

    private static int Count(List<JsonElement> lines, string ev) =>
        lines.Count(l => l.GetProperty("ev").GetString() == ev);

    [AvaloniaFact]
    public void APoseDragLogsEachPointerEventEachPosedDrawingAndItsCommit()
    {
        var (vm, bone, drawing) = Rigged();

        var lines = Record(vm, () => Swing(vm, bone));

        // One span per pointer event, however the app coalesces the redraws:
        // this is the number that says whether the hand-off kept up.
        Assert.Equal(3, Count(lines, "pose.drag"));
        Assert.Equal(1, Count(lines, "pose.commit"));

        // The posed drawing is named, so a slow one can be found in a
        // document with a dozen rigged layers.
        var posed = lines.Where(l => l.GetProperty("ev").GetString() == "pose.frame").ToList();
        Assert.NotEmpty(posed);
        Assert.All(posed, l => Assert.Equal(drawing, l.GetProperty("d").GetString()));

        // The commit begins after the last drag began: spans carry their
        // start, which is what lets the lab lay them against stalls.
        var lastDrag = lines.Last(l => l.GetProperty("ev").GetString() == "pose.drag").GetProperty("t").GetDouble();
        var commit = lines.First(l => l.GetProperty("ev").GetString() == "pose.commit").GetProperty("t").GetDouble();
        Assert.True(commit >= lastDrag, "the commit is logged as starting before the drag that led to it");
    }

    [AvaloniaFact]
    public void ADrawingNothingIsBoundOnWritesNoPosedFrameSpan()
    {
        // Every cache miss goes through the pose resolver, rigged or not. A
        // span for each would bury the rigged drawings under the rest of the
        // document — and would be a log line per frame in a file with no rig.
        var (vm, bone, _) = Rigged(bound: false);

        var lines = Record(vm, () => Swing(vm, bone));

        Assert.Equal(3, Count(lines, "pose.drag"));
        Assert.Equal(0, Count(lines, "pose.frame"));
    }

    [AvaloniaFact]
    public void WithTheLogOffAPoseDragWritesNothing()
    {
        var (vm, bone, _) = Rigged();
        Assert.False(PerfLog.On);
        Swing(vm, bone);
        Assert.Empty(Directory.GetFiles(_dir));
        Assert.NotNull(vm.Doc.Scene.PoseTrack);
    }
}
