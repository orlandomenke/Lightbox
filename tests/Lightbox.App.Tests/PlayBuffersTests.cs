using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback phase 4 (docs/DESIGN-playback-first-loop.md, Q214 decision 1):
/// pressing play on a loop that is not ready buffers, then plays at full speed.
/// </summary>
/// <remarks>
/// <para>
/// Q214's answer was "buffer, then play": timing on screen is always true. It
/// was never built, so a cold first play rendered each drawing on the tick that
/// reached it — on the lab's 30-layer document, frames came late and some never
/// came. The owner, 2026-10-11: a steady loop matters more than how soon it
/// starts.
/// </para>
/// <para>
/// While buffering the playhead holds, the clock is not running, every core but
/// one is on the range, and pressing play again cancels. A range that can never
/// be ready — larger than the tile budget — plays as before rather than waiting
/// for ever.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PlayBuffersTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly long _tileBudget = TileFrameCache.ByteBudget;

    public PlayBuffersTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        TileFrameCache.ByteBudget = _tileBudget;
        base.Dispose();
    }

    /// <summary>Six frames, each its own drawing, with a viewport so playback takes tiles; nothing warmed.</summary>
    private static MainViewModel Vm(bool warm = false)
    {
        var vm = VmLayers.PaperVm();
        vm.WarmPlaybackAtIdle = warm;
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 12;
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));
        for (var i = 0; i < 6; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 20, 100, 1);
            vm.MoveStroke(300 - i * 20, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        vm.TileFrames.Clear();
        return vm;
    }

    /// <summary>Run what the workers hand back until <paramref name="done"/>, or fail after a while.</summary>
    private void Until(MainViewModel vm, Func<bool> done, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!done())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{what}: buffering={vm.IsBufferingPlay} playing={vm.IsPlaying} "
                    + $"progress={vm.PlayBufferProgress:0.00} busy={vm.Prewarm.IsBusy}");
            }
            if (_posted.TryDequeue(out var run)) run();
            else Thread.Sleep(5);
        }
    }

    private void Settle(MainViewModel vm)
    {
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(30);
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
    }

    private static IEnumerable<string> RangeDrawings(MainViewModel vm) =>
        Enumerable.Range(0, vm.Doc.Scene.FrameCount)
            .SelectMany(i => vm.Doc.Scene.Layers.Select(l => ExposureSheet.ExposedFrame(l, i)))
            .OfType<Lightbox.Core.Documents.Frame>()
            .Select(f => f.Id)
            .Distinct();

    [AvaloniaFact]
    public void PlayingAColdLoopBuffersWithThePlayheadHeld()
    {
        var vm = Vm();

        vm.TogglePlaybackCommand.Execute(null);

        Assert.True(vm.IsBufferingPlay, "play started on a loop with nothing prepared");
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void WhenTheLoopIsReadyItPlaysOnItsOwn()
    {
        var vm = Vm();
        vm.TogglePlaybackCommand.Execute(null);

        Until(vm, () => vm.IsPlaying, "the loop never started");

        Assert.False(vm.IsBufferingPlay);
        Assert.Equal(1.0, vm.PlayBufferProgress, 3);
        Assert.All(RangeDrawings(vm), id => Assert.True(vm.TileFrames.Holds(id), $"{id} was not prepared before play"));
        vm.TogglePlaybackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void PressingPlayAgainCancelsTheBuffering()
    {
        var vm = Vm();
        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsBufferingPlay);

        vm.TogglePlaybackCommand.Execute(null);
        Settle(vm);

        Assert.False(vm.IsBufferingPlay);
        Assert.False(vm.IsPlaying, "a cancelled buffer started playing anyway");
    }

    /// <summary>
    /// Prepared means the drawings as tiles and the first second of finished
    /// frames (prepared playback frames, step 6): with both, play starts at once.
    /// </summary>
    [AvaloniaFact]
    public void ALoopAlreadyPreparedPlaysAtOnce()
    {
        var vm = Vm(warm: true);
        vm.PlaybackComposites = new ComposeCache(256L * 1024 * 1024);
        vm.PublishSnapshot();
        Settle(vm);
        for (var i = 0; i < vm.Doc.Scene.FrameCount; i++) Assert.True(vm.PrepareFrameNow(i), $"frame {i} could not be prepared");

        vm.TogglePlaybackCommand.Execute(null);

        Assert.False(vm.IsBufferingPlay);
        Assert.True(vm.IsPlaying);
        vm.TogglePlaybackCommand.Execute(null);
    }

    /// <summary>
    /// With only the tiles ready — what idle time leaves behind — play waits for
    /// the first frames to be finished, and the first lap is then shown from them
    /// rather than built on its ticks: the large document's first lap dropped
    /// about forty frames in fifteen seconds when it was not (2026-10-12).
    /// </summary>
    [AvaloniaFact]
    public void WithOnlyItsTilesReadyALoopWaitsForItsFirstFramesThenShowsThemFinished()
    {
        var vm = Vm(warm: true);
        vm.PlaybackComposites = new ComposeCache(256L * 1024 * 1024);
        vm.PublishSnapshot();
        Settle(vm);

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsBufferingPlay, "play started with no frame of the loop finished");
        Until(vm, () => vm.IsPlaying, "the loop never started");

        var shown = new List<RenderSnapshot>();
        void Take(RenderSnapshot s) => shown.Add(s);
        vm.SnapshotChanged += Take;
        for (var i = 1; i < vm.Doc.Scene.FrameCount; i++) vm.CurrentFrameIndex = i;
        vm.SnapshotChanged -= Take;
        vm.TogglePlaybackCommand.Execute(null);

        Assert.NotEmpty(shown);
        Assert.All(shown, s => Assert.True(s.FromCache, "a frame of the first lap was built on its tick"));
    }

    [AvaloniaFact]
    public void ALoopThatCanNeverFitPlaysRatherThanWaiting()
    {
        var vm = Vm();
        TileFrameCache.ByteBudget = 1; // no drawing's tiles are ever taken in

        vm.TogglePlaybackCommand.Execute(null);

        Until(vm, () => vm.IsPlaying, "a loop that cannot fit buffered for ever");
        Assert.False(vm.IsBufferingPlay);
        vm.TogglePlaybackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void AStrokeCancelsTheBuffering()
    {
        var vm = Vm();
        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsBufferingPlay);

        vm.BeginStroke(500, 300, 1);
        vm.MoveStroke(520, 320, 1);
        vm.EndStroke();
        Settle(vm);

        Assert.False(vm.IsBufferingPlay);
        Assert.False(vm.IsPlaying);
    }

    /// <summary>The transport says so while it buffers, and only then.</summary>
    [AvaloniaFact]
    public void TheTransportShowsPreparingOnlyWhileBuffering()
    {
        var vm = Vm();
        var bar = new Lightbox.App.Views.TransportBar { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = bar, Width = 900, Height = 80 };
        window.Show();
        try
        {
            var preparing = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.StackPanel>(bar, "PlayBuffering")!;
            Assert.False(preparing.IsVisible);

            vm.TogglePlaybackCommand.Execute(null);
            Assert.True(preparing.IsVisible, "buffering showed nothing on the transport");

            Until(vm, () => vm.IsPlaying, "the loop never started");
            Assert.False(preparing.IsVisible);
            vm.TogglePlaybackCommand.Execute(null);
        }
        finally
        {
            window.Close();
        }
    }
}
