using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback phase 2b (docs/DESIGN-playback-first-loop.md, Q214): idle time
/// renders the playback range, so pressing play finds it ready.
/// </summary>
/// <remarks>
/// The app hands results back through <see cref="ThumbnailWorker.Post"/>; here
/// that is a queue the test drains itself, as <c>ThumbnailWorkerTests</c> does,
/// so when a warm is taken in is the test's decision rather than the scheduler's.
/// </remarks>
[Collection("BrushState")]
public sealed class IdlePlaybackWarmTests : BrushStateIsolated
{
    private readonly ITestOutputHelper _output;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly long _tileBudget = TileFrameCache.ByteBudget;

    public IdlePlaybackWarmTests(ITestOutputHelper output)
    {
        _output = output;
        ThumbnailWorker.Post = _posted.Enqueue;
    }

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        TileFrameCache.ByteBudget = _tileBudget;
        base.Dispose();
    }

    /// <summary>Six frames, each with its own drawing, and a viewport so playback would take tiles.</summary>
    private static MainViewModel Vm(bool warm = true)
    {
        var vm = VmLayers.PaperVm();
        // Before the first stroke: building the document publishes at idle too.
        vm.WarmPlaybackAtIdle = warm;
        vm.SmoothStrokes = false;
        vm.BrushSize = 12;
        vm.BrushHardness = 0.6;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var i = 0; i < 6; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 20, 100, 1);
            vm.MoveStroke(300 - i * 20, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>Run what the warm posts until it has been quiet for half a second.</summary>
    private void Settle(MainViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var quietSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            if (_posted.TryDequeue(out var run))
            {
                run();
                quietSince = DateTime.UtcNow;
            }
            else if (!vm.Prewarm.IsBusy && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(500))
            {
                return;
            }
            else
            {
                Thread.Sleep(5);
            }
        }
        throw new TimeoutException("the idle warm never settled");
    }

    private static List<Frame> Drawings(MainViewModel vm) =>
        vm.Doc.Scene.Layers.Where(l => !l.IsBackground)
            .SelectMany(l => Enumerable.Range(0, vm.Doc.Scene.FrameCount).Select(i => ExposureSheet.ExposedFrame(l, i)))
            .OfType<Frame>().DistinctBy(f => f.Id).ToList();

    [AvaloniaFact]
    public void APausedCanvasPreparesThePlaybackRange()
    {
        var vm = Vm();
        vm.PublishSnapshot();
        Settle(vm);

        var drawings = Drawings(vm);
        _output.WriteLine($"rendered {vm.Prewarm.Rendered}, installed {vm.Prewarm.Installed}, " +
                          $"tiles held {drawings.Count(f => vm.TileFrames.Holds(f.Id))} of {drawings.Count}");
        Assert.All(drawings, f => Assert.True(vm.TileFrames.Holds(f.Id), $"{f.Id} was not prepared"));
    }

    [AvaloniaFact]
    public void WithTheSwitchOffNothingIsPrepared()
    {
        var vm = Vm(warm: false);
        vm.PublishSnapshot();
        Settle(vm);

        Assert.DoesNotContain(Drawings(vm), f => vm.TileFrames.Holds(f.Id));
    }

    /// <summary>The cores are the stroke's the moment it begins.</summary>
    [AvaloniaFact]
    public void AStrokeStartingStopsTheWarm()
    {
        var vm = Vm();
        vm.PublishSnapshot(); // starts the warm
        vm.BeginStroke(500, 300, 1);

        // Nothing more is queued; at most the renders in hand finish.
        Assert.True(vm.Prewarm.WaitForIdle(TimeSpan.FromSeconds(10)), "the warm kept going under a stroke");
        vm.EndStroke();
    }

    /// <summary>An edit throws the warm's copy of a drawing away, and the next idle prepares it again.</summary>
    [AvaloniaFact]
    public void AnEditedDrawingIsPreparedAgain()
    {
        var vm = Vm();
        vm.PublishSnapshot();
        Settle(vm);

        vm.CurrentFrameIndex = 3;
        var edited = vm.PaintedCel(3);
        vm.BeginStroke(600, 400, 1);
        vm.MoveStroke(700, 450, 1);
        vm.EndStroke();
        vm.PublishSnapshot();
        Settle(vm);

        Assert.True(vm.TileFrames.Holds(edited.Id), "the edited drawing was not prepared again");
    }

    /// <summary>
    /// A cache with no room stops the warm instead of rendering the rest of the
    /// range only to throw it away — and asking again after every publish.
    /// </summary>
    [AvaloniaFact]
    public void AFullCacheStopsTheWarmRatherThanChurning()
    {
        TileFrameCache.ByteBudget = 1; // nothing fits
        var vm = Vm();
        vm.PublishSnapshot();
        Settle(vm);
        var rendered = vm.Prewarm.Rendered;

        for (var i = 0; i < 3; i++) vm.PublishSnapshot();
        Settle(vm);

        _output.WriteLine($"rendered {rendered} before, {vm.Prewarm.Rendered} after three more publishes");
        Assert.Equal(rendered, vm.Prewarm.Rendered);
    }
}
