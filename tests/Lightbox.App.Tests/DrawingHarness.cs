using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A document and a hand to draw on it with, set up the way the application
/// actually runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because two throwaway harnesses were wrong on the same day
/// and each one cost a wrong diagnosis.</b> The first drew a stroke from
/// x=150 to x=1450 on a document nobody had resized — the default is
/// 960&#215;540 — so most of the mark was off the canvas, the crop fell back to
/// the whole bitmap, and the resulting white shapes were read as an artifact
/// in the compositor. The second kept its scribble in bounds with
/// <c>Math.Clamp</c> on the position, so the pen slid along the margin drawing
/// a dead-straight line, and a straight-edge detector duly reported a 653 px
/// seam in every build including the one with the feature switched off.
/// </para>
/// <para>
/// Both mistakes are invisible in the code that makes them and obvious in a
/// picture, which is the worst combination. So the fixtures live here, once,
/// where they can be read and argued with — rather than being retyped by the
/// next session under time pressure.
/// </para>
/// </remarks>
internal static class DrawingHarness
{
    /// <summary>The owner's document size, which is where the reports come from.</summary>
    public const int Width = 3840, Height = 2160;

    /// <summary>
    /// A view model with a real document, a viewport, and the compose scale a
    /// fit-to-window 4K page actually renders at.
    /// </summary>
    /// <remarks>
    /// The viewport and the display scale are not decoration: without them the
    /// publish takes the whole-document route at scale 1, which is not the
    /// route an artist's session takes and not the one the ring's dirty-region
    /// repaint runs on.
    /// </remarks>
    public static MainViewModel Document(string name = "harness", double displayScale = 0.375)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings(name, Width, Height, 12, 72, "#ffffff", false));
        vm.SetViewport(SKRectI.Create(0, 0, Width, Height));
        vm.SetDisplayScale(displayScale);
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// Stand in for the thread pool, so a live post-process pass lands exactly
    /// when the test says it does. <c>LivePostAsyncTests</c>' seam, shared.
    /// </summary>
    public static Queue<Action> HoldThePasses(MainViewModel vm)
    {
        var work = new Queue<Action>();
        vm.LivePostRunner = w => { work.Enqueue(w); return Task.CompletedTask; };
        Dispatcher.UIThread.RunJobs();
        return work;
    }

    /// <summary>
    /// A wandering stroke that stays on the page by <em>turning away</em> from
    /// the edge rather than by clamping to it.
    /// </summary>
    /// <remarks>
    /// <b>The distinction is the whole reason this method exists.</b> Clamping
    /// the position pins the pen against the margin and draws a perfectly
    /// straight line along it — which is itself an axis-aligned edge hundreds
    /// of pixels long, and drowns any measurement that is looking for one.
    /// Reflecting the heading keeps the mark inside the page and keeps it
    /// curved, which is what a hand draws.
    /// </remarks>
    public static void Scribble(
        MainViewModel vm, int events, int seed,
        Action<int>? afterEach = null, double margin = 300)
    {
        var rng = new Random(seed);
        var x = Width / 2.0;
        var y = Height / 2.0;
        var angle = rng.NextDouble() * Math.PI * 2;
        vm.BeginStroke(x, y, 1);
        for (var i = 1; i <= events; i++)
        {
            angle += (rng.NextDouble() - 0.5) * 1.1;
            var step = 40 + rng.NextDouble() * 70;
            var nx = x + Math.Cos(angle) * step;
            var ny = y + Math.Sin(angle) * step;
            if (nx < margin || nx > Width - margin)
            {
                angle = Math.PI - angle;
                nx = x + Math.Cos(angle) * step;
            }
            if (ny < margin || ny > Height - margin)
            {
                angle = -angle;
                ny = y + Math.Sin(angle) * step;
            }
            x = Math.Clamp(nx, margin, Width - margin);
            y = Math.Clamp(ny, margin, Height - margin);
            vm.MoveStroke(x, y, 1);
            Dispatcher.UIThread.RunJobs();
            afterEach?.Invoke(i);
        }
    }
}
