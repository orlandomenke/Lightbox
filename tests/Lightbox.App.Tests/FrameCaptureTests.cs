using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The frame capture: what it records, what it costs when nobody asked for it,
/// and that it writes something a person can actually read.
/// </summary>
[Collection("BrushState")]
public class FrameCaptureTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lightbox-capture-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Unarmed, drawing records nothing at all — no images kept, nothing to
    /// write. The point of the feature is that it is free until asked for.
    /// </summary>
    [AvaloniaFact]
    public void UnarmedItRecordsNothing()
    {
        var vm = DrawingHarness.Document();
        vm.BrushSize = 60;
        DrawingHarness.Scribble(vm, events: 20, seed: 7);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.Capture.Armed);
        Assert.Equal(0, vm.Capture.Recorded);
        var dir = Scratch();
        Assert.Null(vm.Capture.Write(dir));
        Assert.Empty(Directory.GetDirectories(dir));
    }

    /// <summary>
    /// Armed, a stroke leaves frames behind and each one carries the screen and
    /// the two buffers — which is the whole reason this exists rather than a
    /// screenshot key.
    /// </summary>
    [AvaloniaFact]
    public void ArmedAStrokeLeavesFramesWithBothBuffers()
    {
        var vm = DrawingHarness.Document();
        vm.ColorHex = "#101010";
        vm.BrushSize = 90;
        vm.BrushGranulation = 0.6;   // a brush with a pass, so PostScratch exists
        vm.BrushWetEdge = 0.9;
        var work = DrawingHarness.HoldThePasses(vm);

        vm.Capture.Arm(true);
        DrawingHarness.Scribble(vm, events: 30, seed: 3, afterEach: _ =>
        {
            if (work.Count > 0) { work.Dequeue()(); Dispatcher.UIThread.RunJobs(); }
        });

        output.WriteLine($"recorded {vm.Capture.Recorded} publishes, {vm.LivePostPasses} passes");
        Assert.True(vm.Capture.Recorded > 0, "drawing with the capture armed recorded nothing");

        var dir = Scratch();
        var folder = vm.Capture.Write(dir);
        Assert.NotNull(folder);

        var files = Directory.GetFiles(folder!);
        var index = Path.Combine(folder!, "index.txt");
        Assert.True(File.Exists(index), "the capture wrote images with nothing to read them by");
        var text = File.ReadAllText(index);
        output.WriteLine(text[..Math.Min(400, text.Length)]);

        Assert.Contains("-screen.png", string.Join(",", files.Select(Path.GetFileName)));
        Assert.Contains("-raw.png", string.Join(",", files.Select(Path.GetFileName)));
        Assert.Contains(
            "-processed.png", string.Join(",", files.Select(Path.GetFileName)));

        // The index has to say which publish each frame was, or a sequence of
        // near-identical pictures cannot be lined up against anything.
        Assert.Contains("points ", text);
        Assert.Contains("passRendered ", text);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The ring holds a bounded number of frames however long the stroke is —
    /// this runs on a per-pointer-event path and an unbounded one would be a
    /// memory leak with a menu item attached.
    /// </summary>
    [AvaloniaFact]
    public void TheRingIsBounded()
    {
        var vm = DrawingHarness.Document();
        vm.BrushSize = 60;
        vm.Capture.Arm(true);
        DrawingHarness.Scribble(vm, events: 120, seed: 11);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        var dir = Scratch();
        var folder = vm.Capture.Write(dir);
        Assert.NotNull(folder);
        var screens = Directory.GetFiles(folder!, "*-screen.png").Length;
        output.WriteLine($"{vm.Capture.Recorded} publishes recorded, {screens} frames kept");
        Assert.True(vm.Capture.Recorded > screens,
            "the stroke was too short to prove the ring drops anything");
        Assert.True(screens <= 24, $"the ring kept {screens} frames and is supposed to cap at 24");
    }

    /// <summary>
    /// Re-arming throws away the previous recording, so a second attempt at
    /// catching something is not read through the first one.
    /// </summary>
    [AvaloniaFact]
    public void ReArmingStartsClean()
    {
        var vm = DrawingHarness.Document();
        vm.BrushSize = 60;
        vm.Capture.Arm(true);
        DrawingHarness.Scribble(vm, events: 15, seed: 5);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Capture.Recorded > 0);

        vm.Capture.Arm(false);
        vm.Capture.Arm(true);
        Assert.Equal(0, vm.Capture.Recorded);
    }
}
