using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The transform session frees the bitmaps it rendered and never the ones it borrowed,
/// and it drops the whole gesture in one call.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>MainViewModel</c> under Q78's follow-up. The wiring is guarded by
/// the transform tests that drive a view model — a dropped field would take a gizmo or
/// a commit down with it. What those cannot see is the ownership rule, because both
/// ways of getting it wrong are silent: freeing a borrowed bitmap hands the compositor
/// a disposed one (a crash somewhere else, later), and not freeing a rendered one leaks
/// a full-canvas bitmap per in-scope frame per gesture, which on a 4K canvas is 33 MB a
/// time and shows up as memory climbing while an artist nudges a drawing around.
/// </para>
/// <para>
/// So the flag is asserted directly. This is the same argument as
/// <c>LivePaintSessionTests</c>: a class whose job is lifetime cannot be guarded by
/// tests that only check the picture.
/// </para>
/// </remarks>
public class TransformSessionTests(ITestOutputHelper output)
{
    private static SKBitmap Bitmap() =>
        new(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));

    /// <summary>
    /// Clearing the preview frees what the session rasterised and leaves the frame
    /// cache's own bitmap alone.
    /// </summary>
    [Fact]
    public void ClearingThePreviewFreesTheRenderedSplitAndNotTheBorrowedOne()
    {
        var session = new TransformSession();
        var moving = Bitmap();
        var staying = Bitmap();
        var borrowed = Bitmap();

        session.Remember("rendered", new TransformSession.Parts(moving, staying, Owned: true));
        session.Remember("borrowed", new TransformSession.Parts(borrowed, null, Owned: false));

        session.ClearPreview();

        output.WriteLine($"rendered moving {(moving.Handle == IntPtr.Zero ? "freed" : "ALIVE")}, "
                         + $"rendered static {(staying.Handle == IntPtr.Zero ? "freed" : "ALIVE")}, "
                         + $"borrowed {(borrowed.Handle == IntPtr.Zero ? "FREED" : "alive")}");

        Assert.True(moving.Handle == IntPtr.Zero, "the rendered moving half leaked");
        Assert.True(staying.Handle == IntPtr.Zero, "the rendered staying half leaked");
        Assert.False(borrowed.Handle == IntPtr.Zero, "the frame cache's own bitmap was disposed by the session");

        borrowed.Dispose();
    }

    /// <summary>
    /// A split is built once and reused for the rest of the drag — that reuse is the
    /// only reason a region-limited transform is affordable per pointer event.
    /// </summary>
    [Fact]
    public void ASplitIsReusedForTheGestureAndGoneAfterThePreviewIsCleared()
    {
        var session = new TransformSession();
        var parts = new TransformSession.Parts(Bitmap(), null, Owned: true);

        session.Remember("f1", parts);
        Assert.Same(parts, session.Cached("f1"));
        Assert.Null(session.Cached("f2"));

        session.ClearPreview();
        Assert.Null(session.Cached("f1"));
    }

    /// <summary>Ending drops the scope, the filter and the preview together.</summary>
    /// <remarks>
    /// They were four fields cleared by two methods across two partials. A gesture that
    /// ends with any one of them still set is the B39 shape — stale state quietly
    /// changing what a later action does.
    /// </remarks>
    [Fact]
    public void EndingASessionDropsEverythingItHeld()
    {
        var session = new TransformSession();
        var live = Bitmap();
        session.Begin([new Frame(), new Frame()], _ => true);
        session.Preview = SKMatrix.CreateTranslation(3, 4);
        session.Remember("f1", new TransformSession.Parts(live, null, Owned: true));

        session.End();

        output.WriteLine($"frames {session.Frames.Count}, filter {(session.Filter is null ? "null" : "SET")}, "
                         + $"preview {(session.Preview is null ? "null" : "SET")}");
        Assert.Empty(session.Frames);
        Assert.Null(session.Filter);
        Assert.Null(session.Preview);
        Assert.Null(session.Cached("f1"));
        Assert.True(live.Handle == IntPtr.Zero, "ending the session left a rendered split behind");
    }

    /// <summary>
    /// Beginning replaces the scope rather than adding to it, so a second transform
    /// does not silently include the first one's frames.
    /// </summary>
    [Fact]
    public void BeginningReplacesTheScopeRatherThanAddingToIt()
    {
        var session = new TransformSession();
        session.Begin([new Frame(), new Frame()], _ => true);
        session.Begin([new Frame()], null);

        output.WriteLine($"frames after the second begin: {session.Frames.Count}");
        Assert.Single(session.Frames);
        Assert.Null(session.Filter);
    }
}
