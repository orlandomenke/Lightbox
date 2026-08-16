using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The motion trail end to end: the painter puts pixels where the ticks are,
/// the view model recomputes on every hook that can move them, and the toggle
/// is registered so it can be found and rebound. The three assertions B58
/// taught — pixels appear, the canvas is handed the list, the switch is
/// reachable — asked of the new overlay from day one.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because the toggle persists through
/// <c>AppSettings</c>, which is a process-wide store redirected per class by
/// <see cref="BrushStateIsolated"/> — without it, one test's
/// <c>Settings.Save()</c> is the next test's surprising default.
/// </remarks>
[Collection("BrushState")]
public sealed class MotionTrailOverlayTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 200, H = 160;

    private static (int Painted, SKBitmap Image) PaintOf(IReadOnlyList<TrailPoint>? points, float scale = 1)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            MotionTrailPainter.Paint(canvas, points, scale);
            canvas.Flush();
        }
        var painted = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            if (bmp.GetPixel(x, y).Alpha > 8) painted++;
        }
        return (painted, bmp);
    }

    [Fact]
    public void TheTrailPaintsTicksAndTheLineBetweenThem()
    {
        var points = new List<TrailPoint>
        {
            new(0, 20, 80, Current: false, Before: true, Anchored: true, "A"),
            new(1, 100, 80, Current: true, Before: false, Anchored: true, "B"),
            new(2, 180, 80, Current: false, Before: false, Anchored: true, "C"),
        };

        var (painted, bmp) = PaintOf(points);
        output.WriteLine($"painted {painted} px");

        Assert.True(painted > 100, $"the trail painted almost nothing: {painted} px");
        // The line between ticks crosses the midpoint between A and B…
        Assert.True(bmp.GetPixel(60, 80).Alpha > 8, "no line between consecutive ticks");
        // …the before side is warm, the after side cool, the onion convention.
        var before = bmp.GetPixel(60, 80);
        var after = bmp.GetPixel(140, 80);
        output.WriteLine($"before ({before.Red},{before.Green},{before.Blue}) after ({after.Red},{after.Green},{after.Blue})");
        Assert.True(before.Red > before.Blue, "the before segment is not the warm tint");
        Assert.True(after.Blue > after.Red, "the after segment is not the cool tint");
    }

    [Fact]
    public void OneTickIsNotAMotionAndPaintsNothing()
    {
        var (painted, _) = PaintOf([new(0, 100, 80, true, false, true, "A")]);
        Assert.Equal(0, painted);
        Assert.Equal(0, PaintOf(null).Painted);
    }

    [Fact]
    public void ADerivedTickIsHollowAndAnAuthoredOneIsNot()
    {
        // The centre of a filled (anchored) tick carries ink; the centre of a
        // hollow (centroid) tick is bare — which is how the artist tells a
        // statement from a guess before trusting an arc.
        // Both ticks on one horizontal line, probed a pixel above the tick's
        // centre: off the polyline (which runs through every centre) but well
        // inside the tick's radius, so the probe reads the tick alone.
        var anchored = new List<TrailPoint>
        {
            new(0, 50, 80, false, Before: true, Anchored: true, "A"),
            new(1, 150, 80, false, Before: false, Anchored: true, "B"),
        };
        var derived = new List<TrailPoint>
        {
            new(0, 50, 80, false, Before: true, Anchored: false, "A"),
            new(1, 150, 80, false, Before: false, Anchored: false, "B"),
        };

        var (_, filled) = PaintOf(anchored);
        var (_, hollow) = PaintOf(derived);
        output.WriteLine(
            $"anchored probe alpha {filled.GetPixel(50, 78).Alpha}, derived probe alpha {hollow.GetPixel(50, 78).Alpha}");

        Assert.True(filled.GetPixel(50, 78).Alpha > 64, "an authored tick is not filled");
        Assert.True(hollow.GetPixel(50, 78).Alpha <= 8, "a derived tick is not hollow");
        // And the hollow tick's rim is still there — hollow, not missing.
        Assert.True(hollow.GetPixel(50, 77).Alpha > 8 || hollow.GetPixel(50, 83).Alpha > 8,
            "the derived tick has no rim at all");
    }

    [Fact]
    public void TheTintSplitSurvivesAPlayheadWithNoTickOfItsOwn()
    {
        // The adversary's repro: the playhead stands on an empty drawing, so
        // no point is Current. The first painter derived the split from a
        // current index it did not have, fell back to the last tick, and
        // painted the future red. The split is the record's Before flag now,
        // so the future stays blue with nothing to compare against.
        var points = new List<TrailPoint>
        {
            new(-2, 20, 80, false, Before: true, true, "A"),
            new(-1, 60, 80, false, Before: true, true, "B"),
            new(1, 140, 80, false, Before: false, true, "C"),
            new(2, 180, 80, false, Before: false, true, "D"),
        };

        var (_, bmp) = PaintOf(points);
        var past = bmp.GetPixel(40, 80);     // between A and B
        var future = bmp.GetPixel(160, 80);  // between C and D
        output.WriteLine($"past ({past.Red},{past.Green},{past.Blue}) future ({future.Red},{future.Green},{future.Blue})");

        Assert.True(past.Red > past.Blue, "the past is not the warm tint");
        Assert.True(future.Blue > future.Red, "a future segment painted in the past's colour");
    }

    [AvaloniaFact]
    public void TheViewModelHandsTheWindowTheTrailAndKeepsItCurrent()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Trail", W, H, 12, 72, "#ffffff", false));

        IReadOnlyList<TrailPoint>? latest = null;
        var pushes = 0;
        vm.MotionTrailChanged += p => { latest = p; pushes++; };

        // Two drawings in two places.
        vm.ColorHex = "#000000";
        vm.BrushSize = 6;
        vm.BeginStroke(30, 40, 1);
        vm.MoveStroke(50, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(120, 40, 1);
        vm.MoveStroke(140, 60, 1);
        vm.EndStroke();

        Assert.False(vm.MotionTrail, "the trail defaults on — it is an aid you reach for");
        vm.MotionTrail = true;
        Assert.NotNull(latest);
        Assert.Equal(2, latest!.Count);
        Assert.Equal(40, latest[0].X);   // centre of 30..50
        Assert.False(latest[0].Anchored);
        output.WriteLine($"ticks at {string.Join(", ", latest.Select(p => $"({p.X},{p.Y})"))}");

        // The playhead's tick moves with the playhead…
        vm.CurrentFrameIndex = 0;
        Assert.Equal(latest![0].FrameId, Assert.Single(latest, p => p.Current).FrameId);

        // …drawing moves the tick of the drawing it landed on…
        var before = latest[0].X;
        vm.BeginStroke(10, 40, 1);
        vm.MoveStroke(11, 41, 1);
        vm.EndStroke();
        Assert.NotEqual(before, latest![0].X);

        // …and switching off hands the window nothing, not a stale list.
        vm.MotionTrail = false;
        Assert.Null(latest);
        output.WriteLine($"pushes seen: {pushes}");
    }

    [AvaloniaFact]
    public void PlaybackShowsTheAnimationNotTheTrail()
    {
        // The ghosts' playback rule and B152's cost argument at once: playing
        // clears the trail, playhead ticks during the run never recompute it
        // (the bounds walk must not ride the tick), and stopping brings it
        // back with one refresh.
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Trail", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BeginStroke(30, 40, 1);
        vm.MoveStroke(50, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(120, 40, 1);
        vm.MoveStroke(140, 60, 1);
        vm.EndStroke();

        IReadOnlyList<TrailPoint>? latest = null;
        var pushes = 0;
        vm.MotionTrailChanged += p => { latest = p; pushes++; };
        vm.MotionTrail = true;
        Assert.NotNull(latest);

        vm.IsPlaying = true;
        Assert.Null(latest);
        var during = pushes;
        vm.CurrentFrameIndex = 0;
        vm.CurrentFrameIndex = 1;
        output.WriteLine($"pushes at play start {during}, after two ticks {pushes}");
        Assert.Equal(during, pushes);   // the tick path never even asked

        vm.IsPlaying = false;
        Assert.NotNull(latest);
    }

    [AvaloniaFact]
    public void TheToggleIsRegisteredSoItCanBeFoundAndRebound()
    {
        // B58's third assertion: a switch wired straight to a key handler is
        // invisible to the Configure window and an artist's remap skips it.
        var map = new Services.ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"trail-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "canvas.motionTrail");
    }
}
