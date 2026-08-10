using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A published snapshot holds the bitmaps its passes borrow, and lets go of
/// them exactly once.
/// </summary>
/// <remarks>
/// <para>
/// <b>B125 stage 3, first half: the lifetime, wired.</b> Stage 1 built
/// <see cref="FrameBitmapCache"/>'s pin protocol and nothing used it. Now a
/// published <see cref="RenderSnapshot"/> carries the pass list, so the borrow
/// is real and the release has to fire on every path that frees a snapshot.
/// </para>
/// <para>
/// <b>There are three such paths and they are easy to miss one of</b> — a frame
/// dropped under back-pressure, the ordinary keep-window, and the hard cap.
/// A missed release is a leak; a doubled one frees a bitmap another reader still
/// holds, which is B130: a native access violation, an empty crash log, and
/// "Lightbox dies as soon as I touch anything". So the release lives on
/// <see cref="RenderSnapshot.Dispose"/> and every site calls that, rather than
/// each site remembering.
/// </para>
/// </remarks>
public class PublishedPassLifetimeTests(ITestOutputHelper output)
{
    private static bool Alive(SKBitmap bmp) => bmp.Handle != nint.Zero;

    private static SKImage AnImage() =>
        SKImage.FromBitmap(new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul)))!;

    private static RenderSnapshot Snapshot(
        IReadOnlyList<RenderPass>? passes = null, Action? release = null, long seq = 1) =>
        new(AnImage(), 64, 48, seq, null, null, passes, release);

    /// <summary>
    /// The ordinary case: the snapshot is freed, and what it borrowed goes back.
    /// </summary>
    [Fact]
    public void DisposingASnapshotReleasesWhatItsPassesBorrowed()
    {
        using var cache = new FrameBitmapCache();
        var frame = new Frame();
        var bmp = cache.Get(frame, 64, 48);
        cache.Pin(bmp);

        var snapshot = Snapshot([new RenderPass(bmp, null, 1.0)], () => cache.Unpin(bmp));
        Assert.Equal(1, cache.PinnedCount);

        snapshot.Dispose();

        Assert.Equal(0, cache.PinnedCount);
    }

    /// <summary>
    /// <b>Idempotent, and it has to be.</b> The retirement queue frees snapshots
    /// from three places, and a release that ran twice would unpin a bitmap a
    /// second reader still holds — freeing pixels out from under a live render.
    /// </summary>
    [Fact]
    public void ReleasingTwiceOnlyReleasesOnce()
    {
        var releases = 0;
        var snapshot = Snapshot([], () => releases++);

        snapshot.Dispose();
        snapshot.Dispose();
        snapshot.Dispose();

        Assert.Equal(1, releases);
    }

    /// <summary>
    /// While the snapshot is alive, an eviction must not free the pixels under
    /// it. This is the whole point of the borrow — the render thread may be
    /// reading them at the moment the cache decides it needs the room.
    /// </summary>
    [Fact]
    public void AnEvictionWhileTheSnapshotIsLiveDoesNotFreeThePixels()
    {
        using var cache = new FrameBitmapCache();
        var frame = new Frame();
        var bmp = cache.Get(frame, 64, 48);
        cache.Pin(bmp);
        var snapshot = Snapshot([new RenderPass(bmp, null, 1.0)], () => cache.Unpin(bmp));

        cache.Invalidate(frame.Id);   // the cache lets go mid-flight

        output.WriteLine($"after eviction: alive={Alive(bmp)}, awaiting={cache.AwaitingUnpinBytes} bytes");
        Assert.True(Alive(bmp), "the pixels were freed while a published snapshot still referenced them");

        snapshot.Dispose();

        Assert.False(Alive(bmp), "the deferred disposal never happened — a frame leaks per eviction");
    }

    /// <summary>
    /// A snapshot with no passes is the headless and pre-stage-3 case, and must
    /// stay exactly as cheap as it was: an image, freed.
    /// </summary>
    [Fact]
    public void ASnapshotWithNoPassesStillDisposesItsImage()
    {
        var snapshot = Snapshot();
        var image = snapshot.Image!;

        snapshot.Dispose();

        Assert.Equal(nint.Zero, image.Handle);
        Assert.True(snapshot.IsDisposed);
        Assert.Null(snapshot.Image);
    }

    /// <summary>
    /// <b>Two snapshots can borrow the same bitmap</b>, which is the ordinary
    /// case rather than a corner: a publish overlaps the one before it, and both
    /// sit in the retirement queue at once. The first release must not free it.
    /// </summary>
    [Fact]
    public void TheSameBitmapInTwoLiveSnapshotsSurvivesTheFirstRelease()
    {
        using var cache = new FrameBitmapCache();
        var frame = new Frame();
        var bmp = cache.Get(frame, 64, 48);

        cache.Pin(bmp);
        var first = Snapshot([new RenderPass(bmp, null, 1.0)], () => cache.Unpin(bmp), seq: 1);
        cache.Pin(bmp);
        var second = Snapshot([new RenderPass(bmp, null, 1.0)], () => cache.Unpin(bmp), seq: 2);

        cache.Invalidate(frame.Id);
        first.Dispose();

        Assert.True(Alive(bmp),
            "freed on the first release while a second snapshot still held it — "
            + "this is the bug a boolean pin would have");

        second.Dispose();
        Assert.False(Alive(bmp));
    }
}
