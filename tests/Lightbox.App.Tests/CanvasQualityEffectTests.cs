using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// That turning the canvas quality down actually renders fewer pixels — and
/// that nothing which leaves the application is affected by it.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanvasReliefTests</c> covers the <i>decision</i> exhaustively: when the
/// setting flips to Half, that it happens once, never over a chosen default,
/// and never silently. It never checks that the setting does anything. Every
/// one of those tests would have stayed green if the wiring from
/// <c>CanvasQuality</c> through <c>ComposeScale</c> to the published surface
/// had been dropped, leaving a feature that was nothing but a status message.
/// </para>
/// <para>
/// The manual states both halves of the promise, so both are pinned here:
/// <i>"Half: half of what the screen shows… softer while you work, fastest"</i>
/// and <i>"the drawing, the exports and the thumbnails are always full
/// resolution, whatever this is set to"</i>.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class CanvasQualityEffectTests : BrushStateIsolated
{
    /// <summary>Grab the next published snapshot's pixel dimensions.</summary>
    private static (int W, int H) Publish(MainViewModel vm)
    {
        (int, int) size = (0, 0);
        void Capture(RenderSnapshot s) => size = (s.Image.Width, s.Image.Height);
        vm.SnapshotChanged += Capture;
        try
        {
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return size;
    }

    [AvaloniaFact]
    public void HalfQualityCompositesFewerPixelsThanFull()
    {
        // The promise the setting exists to keep. Asserted on the published
        // image rather than on ComposeScale, because the gap this closes was
        // between the two.
        var vm = new MainViewModel(null);
        vm.SetDisplayScale(1.0);

        vm.CanvasQuality = CanvasQuality.Full;
        var full = Publish(vm);
        vm.CanvasQuality = CanvasQuality.Half;
        var half = Publish(vm);

        Assert.True(full.W > 0 && half.W > 0, "nothing was published");
        Assert.True(half.W < full.W && half.H < full.H,
            $"Half published {half.W}x{half.H} against Full's {full.W}x{full.H}");
    }

    [AvaloniaFact]
    public void TheSnapshotStillDescribesTheDocumentItCameFrom()
    {
        // The canvas scales the image back up to fill the document's box, so a
        // smaller surface has to keep saying how big the document is. Getting
        // this wrong would shrink the artwork on screen rather than soften it.
        var vm = new MainViewModel(null);
        vm.SetDisplayScale(1.0);
        vm.CanvasQuality = CanvasQuality.Half;

        RenderSnapshot? seen = null;
        void Capture(RenderSnapshot s) => seen = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;

        Assert.NotNull(seen);
        Assert.Equal(vm.Doc.Scene.Width, seen!.DocWidth);
        Assert.Equal(vm.Doc.Scene.Height, seen.DocHeight);
        Assert.True(seen.Image.Width < seen.DocWidth);
    }

    [AvaloniaFact]
    public void AnExportIsFullResolutionWhateverTheCanvasIsSetTo()
    {
        // True by construction today — RenderFramePng composes at the scene's
        // own size and never reads ComposeScale — and pinned here because the
        // two paths both end up in SceneRenderer, so threading the compose
        // scale through the export is a plausible refactor away, and the
        // manual promises outright that it will not happen.
        var vm = new MainViewModel(null);
        vm.SetDisplayScale(1.0);
        vm.CanvasQuality = CanvasQuality.Half;

        var png = vm.RenderFramePng(0);

        using var decoded = SkiaSharp.SKBitmap.Decode(Convert.FromBase64String(png));
        Assert.Equal(vm.Doc.Scene.Width, decoded.Width);
        Assert.Equal(vm.Doc.Scene.Height, decoded.Height);
    }

    [AvaloniaFact]
    public void FullQualityIsUnaffectedByWhatTheScreenCanShow()
    {
        // Full means the document's own resolution, not the display's. A zoomed
        // -out canvas must not quietly become the export's resolution too, and
        // compositing ABOVE the document would invent detail the record does
        // not contain.
        var vm = new MainViewModel(null);
        vm.CanvasQuality = CanvasQuality.Full;

        vm.SetDisplayScale(0.25);
        var zoomedOut = Publish(vm);
        vm.SetDisplayScale(4.0);
        var zoomedIn = Publish(vm);

        Assert.Equal(vm.Doc.Scene.Width, zoomedOut.W);
        Assert.Equal(zoomedOut, zoomedIn);
    }
}
