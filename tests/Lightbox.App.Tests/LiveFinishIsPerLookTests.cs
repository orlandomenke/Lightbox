using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The live mark's finish and its unsettled tail are done when the mark is
/// looked at - a frame composed, the pen lifted - and not on every pointer
/// event. A pen reports about twice as often as a screen refreshes, and when
/// the app is behind the events queue; done per event, the finish was most of
/// what put a broad Watercolor a second and a half behind the pen in the perf
/// lab, and each queued event made the queue longer.
/// </summary>
[Collection("BrushState")]
public class LiveFinishIsPerLookTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm(string presetId, int size)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = true;
        vm.ColorHex = "#203060";
        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == presetId));
        vm.BrushSize = size;
        return vm;
    }

    private static int Differing(SKBitmap a, SKBitmap b)
    {
        var x = a.GetPixelSpan(); var y = b.GetPixelSpan(); var n = 0;
        for (var i = 0; i < x.Length; i += 4)
        {
            if (x[i] != y[i] || x[i + 1] != y[i + 1] || x[i + 2] != y[i + 2] || x[i + 3] != y[i + 3]) n++;
        }
        return n;
    }

    /// <summary>
    /// Sixty pointer events with no frame composed between them finish the
    /// mark once or twice, not sixty times - and it is still, to the byte, the mark its
    /// record renders, and still kept.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("builtin-watercolor-wet", 120)]
    [InlineData("builtin-oil", 120)]
    [InlineData("builtin-soft-round", 120)]
    [InlineData("builtin-pencil", 40)]
    public void EventsThatNobodyLookedAtShareOneFinish(string presetId, int size)
    {
        var vm = Vm(presetId, size);
        var scene = vm.Doc.Scene;
        var before = vm.LiveFinishes;

        vm.BeginStroke(150, 300, 0.5);
        for (var i = 1; i <= 60; i++)
        {
            // No RunJobs: nothing composes a frame between these.
            vm.MoveStroke(150 + (i * 12.0), 300 + (Math.Sin(i * 0.2) * 80), 0.5 + (0.4 * i / 60.0));
        }
        var during = vm.LiveFinishes - before;
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        var total = vm.LiveFinishes - before;

        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var differing = Differing(kept, replay);
        output.WriteLine($"{presetId}: {during} finishes during 60 events, {total} by pen-up; kept {vm.PromotedStrokes == 1}; differing {differing}");

        // One, not none: the first frame of a stroke is composed where it is
        // asked for. Sixty would be the finish back on every event.
        Assert.InRange(during, 0, 2);
        Assert.InRange(total, 1, 3);
        Assert.Equal(0, differing);
        Assert.True(vm.PromotedStrokes == 1, $"{presetId}: rendered again at pen-up - {vm.WhyReplayed}");
    }

    /// <summary>
    /// And a frame composed mid-stroke sees the finished mark, tail and all:
    /// what is looked at is never behind the pen.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("builtin-watercolor-wet", 120)]
    [InlineData("builtin-oil", 120)]
    public void WhatIsLookedAtIsFinishedToTheLastDab(string presetId, int size)
    {
        var vm = Vm(presetId, size);
        vm.BeginStroke(150, 300, 0.5);
        for (var i = 1; i <= 30; i++) vm.MoveStroke(150 + (i * 12.0), 300 + (Math.Sin(i * 0.2) * 80), 0.7);

        // Looking: the accessor every reader of the live mark goes through.
        var finishes = vm.LiveFinishes;
        using var looked = vm.LiveMarkSource!.Copy();
        Assert.Equal(1, vm.LiveFinishes - finishes);

        // The same stroke, looked at after every single event.
        var each = Vm(presetId, size);
        each.BeginStroke(150, 300, 0.5);
        for (var i = 1; i <= 30; i++)
        {
            each.MoveStroke(150 + (i * 12.0), 300 + (Math.Sin(i * 0.2) * 80), 0.7);
            _ = each.LiveMarkSource;
        }
        using var stepwise = each.LiveMarkSource!.Copy();
        var differing = Differing(looked, stepwise);
        output.WriteLine($"{presetId}: one look after 30 events against a look after each: {differing} differing pixels");
        Assert.Equal(0, differing);

        vm.EndStroke();
        each.EndStroke();
    }
}
