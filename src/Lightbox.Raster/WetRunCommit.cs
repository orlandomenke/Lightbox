using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Commits the strokes of a wet run onto a cached layer bitmap so that the
/// pixels match what <see cref="FrameRasterizer.Rasterize"/> would produce for
/// the same record.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this exists for.</b> A run is simulated as one wash, so
/// committing its second stroke is not "stamp one more mark" — the wash has to
/// be re-simulated with both strokes in it, and the first stroke's separate
/// pixels have to come back off the layer first. Appending incrementally, as
/// every other brush does, would leave the artist looking at a stack of
/// separately dried strokes until something re-rendered the frame; the seams
/// would appear while painting and vanish on reload, which is worse than
/// either.
/// </para>
/// <para>
/// <b>Why a baseline and not a re-render.</b> Dropping the frame's cached
/// pixels is correct and costs a whole-frame rasterize per stroke — every
/// earlier medium stroke re-simulated, on the one workflow this feature is for.
/// Keeping the run's pre-run pixels instead makes the work proportional to the
/// run: restore, then re-simulate at most
/// <see cref="BrushSettings.MaxWetStrokes"/> + 1 strokes over what they reach.
/// That is invariant 6's shape.
/// </para>
/// <para>
/// <b>The baseline can only be taken before the run starts</b>, which is why
/// this is told about every wet stroke including the first: once stroke one is
/// on the layer, the pixels underneath it are gone and no later call can
/// recover them. A caller that loses the baseline mid-run (the frame left the
/// cache, an undo re-rendered it) gets <see cref="Outcome.Rerender"/> and must
/// fall back to rebuilding the frame from the record — slower, always correct.
/// </para>
/// </remarks>
public sealed class WetRunCommit : IDisposable
{
    private SKBitmap? _baseline;
    private SKRectI _rect;
    private string? _frameId;
    private List<Stroke> _run = [];
    private bool _lifted;

    /// <summary>What the caller still has to do after <see cref="Commit"/>.</summary>
    public enum Outcome
    {
        /// <summary>Not a wet stroke: append it the ordinary way.</summary>
        Append,

        /// <summary>The run is on the layer already; the caller does nothing more.</summary>
        Done,

        /// <summary>
        /// This stroke joins a run whose earlier pixels are not recoverable, so
        /// the frame's render has to be thrown away and rebuilt from the record.
        /// </summary>
        /// <remarks>
        /// Costly and bounded: every remaining stroke of that one run pays a
        /// frame re-render, because nothing after the loss can get back to the
        /// pre-run pixels either. It stops at the end of the run — the next one
        /// takes its baseline the ordinary way — so the worst case is
        /// <see cref="BrushSettings.MaxWetStrokes"/> re-renders, once, when a
        /// frame leaves the cache or an undo rebuilds it in the middle of a
        /// wash.
        /// </remarks>
        Rerender,
    }

    /// <summary>
    /// The frame whose render is currently missing an open run, because a live
    /// preview lifted it off — null when nothing is lifted.
    /// </summary>
    /// <remarks>
    /// A caller that abandons the stroke doing the previewing has to put the run
    /// back, and the cheapest thing that is certainly right is to throw the
    /// frame's render away and rebuild it from the record.
    /// </remarks>
    public string? LiftedFrameId => _lifted ? _frameId : null;

    /// <summary>
    /// Take the open run's pixels off <paramref name="layer"/> so a live
    /// preview can draw the whole wash itself, and return the strokes it now
    /// has to draw. Null when there is nothing to join.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the preview has to draw the earlier strokes too.</b> The overlay
    /// is laid <em>over</em> the layer, and a fused wash is lighter than the
    /// same strokes dried one at a time — nothing drawn on top can take paint
    /// away. So either the earlier members come off the layer and into the
    /// preview, or the mark jumps when the pen lifts: measured at 4.2/255 mean
    /// and <b>123 at worst</b>, which is a wash visibly going pale under the
    /// artist's hand.
    /// </para>
    /// <para>
    /// <b>Idempotent, and meant to be asked repeatedly.</b> Whether a stroke
    /// reaches the paint before it is not known at pen-down — the reach grows
    /// with the mark — so a caller asks on every event until it says yes. The
    /// answer only ever goes from no to yes, because a stroke's reach only
    /// grows, and the checks before the geometric one are all O(1).
    /// </para>
    /// </remarks>
    public IReadOnlyList<Stroke>? LiftOpenRun(SKBitmap layer, string frameId, Stroke live)
    {
        if (_lifted) return _frameId == frameId ? _run : null;
        if (_baseline is null || _frameId != frameId || _run.Count == 0) return null;
        if (_run.Count > _run[0].Brush.WetStrokeWindow) return null;
        if (!WetRun.CanFuse(_run[^1], live)) return null;

        Restore(layer, _rect);
        _lifted = true;
        return _run;
    }

    /// <summary>
    /// Put <paramref name="stroke"/> onto <paramref name="layer"/>, fusing it
    /// with the open run when the record says it belongs to one.
    /// </summary>
    /// <param name="committed">
    /// The frame's strokes <em>without</em> <paramref name="stroke"/> — the
    /// record as it stands at commit time.
    /// </param>
    public Outcome Commit(SKBitmap layer, string frameId, IReadOnlyList<Stroke> committed, Stroke stroke)
    {
        if (!WetRun.StaysWet(stroke))
        {
            Reset();
            return Outcome.Append;
        }

        if (WetRun.OpenRunFor(committed, stroke) is not { } run)
        {
            // A run the preview lifted that this stroke turns out not to join
            // after all — the record is the authority and it has just said no,
            // so the wash goes back exactly as it was and this stroke lands on
            // top of it. Rare: it takes a stroke whose reach was touching while
            // the pen was down and is not once the stabiliser has had it.
            if (_lifted) Replay(layer);

            // The first stroke of a possible run. Photograph what it is about
            // to cover, then let the caller stamp it as usual: on its own it
            // renders exactly as it always did, and if nothing follows, this
            // baseline is simply thrown away.
            Reset();
            if (Capture(layer, stroke) is null) return Outcome.Append;
            _frameId = frameId;
            _run = [stroke];
            return Outcome.Append;
        }

        // A run this instance did not watch from the start — a frame that left
        // the cache mid-wash, or an undo that rebuilt it. There is no way back
        // to the pre-run pixels from here.
        // Identity as well as count: the baseline was taken under *these*
        // strokes, and a run of the same length made of different ones would
        // restore pixels belonging to a wash nobody is painting.
        if (_baseline is null
            || _frameId != frameId
            || _run.Count != run.Count - 1
            || !ReferenceEquals(_run[^1], run[^2]))
        {
            Reset();
            return Outcome.Rerender;
        }

        if (Grow(layer, stroke) is not { } rect) { Reset(); return Outcome.Rerender; }

        // The pre-run pixels back — harmlessly repeated when a preview already
        // lifted them — and then the whole wash over them, this stroke in it.
        Restore(layer, rect);
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(layer))
        {
            BrushEngine.StampWetRun(canvas, run, info, layer);
            canvas.Flush();
        }

        // Same announcement `FrameRasterizer.Append` makes, and for the same
        // reason: anything keyed on this bitmap's identity has to see it move.
        BitmapVersion.Bump(layer);
        _run = run;
        _lifted = false;
        return Outcome.Done;
    }

    /// <summary>
    /// Forget the open run if it belongs to this frame — its pixels were just
    /// thrown away, so the baseline no longer describes anything.
    /// </summary>
    public void ForgetFrame(string frameId)
    {
        if (_frameId == frameId) Reset();
    }

    /// <summary>Forget the open run — the frame, layer or document moved on.</summary>
    public void Reset()
    {
        _baseline?.Dispose();
        _baseline = null;
        _frameId = null;
        _run = [];
        _rect = default;
        _lifted = false;
    }

    /// <summary>The pre-run pixels back over <paramref name="rect"/>.</summary>
    /// <remarks>
    /// <c>Src</c> rather than <c>SrcOver</c>: this is putting pixels back, not
    /// laying paint over them, and a wash restored by compositing would keep
    /// whatever it was meant to replace.
    /// </remarks>
    private void Restore(SKBitmap layer, SKRectI rect)
    {
        if (_baseline is null) return;
        using var canvas = new SKCanvas(layer);
        using var image = SKImage.FromBitmap(_baseline);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawImage(
            image, new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            new SKSamplingOptions(SKFilterMode.Nearest), paint);
        canvas.Flush();
    }

    /// <summary>The open run exactly as it was, over the pixels it was lifted from.</summary>
    private void Replay(SKBitmap layer)
    {
        if (_run.Count == 0) return;
        Restore(layer, _rect);
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampWetRun(canvas, _run, info, layer);
        canvas.Flush();
        BitmapVersion.Bump(layer);
        _lifted = false;
    }

    public void Dispose() => Reset();

    /// <summary>Bytes held for the open run, for the caller's memory reporting.</summary>
    public long AllocatedBytes => _baseline is null ? 0 : (long)_baseline.ByteCount;

    /// <summary>Take the pre-run copy of everything the first stroke can reach.</summary>
    private SKRectI? Capture(SKBitmap layer, Stroke stroke)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (BrushEngine.CommitBounds(stroke, info) is not { } rect) return null;
        _baseline = Crop(layer, rect);
        _rect = rect;
        return _baseline is null ? null : rect;
    }

    /// <summary>
    /// Widen the baseline to cover the new stroke as well, and return the rect
    /// the restore has to cover.
    /// </summary>
    /// <remarks>
    /// The paper the run has not touched yet still holds its pre-run pixels, so
    /// the widened area can simply be read off the layer. The part already
    /// covered cannot — that is what the old baseline is for, and it goes back
    /// on top.
    /// </remarks>
    private SKRectI? Grow(SKBitmap layer, Stroke stroke)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (BrushEngine.CommitBounds(stroke, info) is not { } reach) return _rect;
        if (_rect.Contains(reach)) return _rect;

        var union = new SKRectI(
            Math.Min(_rect.Left, reach.Left), Math.Min(_rect.Top, reach.Top),
            Math.Max(_rect.Right, reach.Right), Math.Max(_rect.Bottom, reach.Bottom));
        if (Crop(layer, union) is not { } widened) return null;
        using (var canvas = new SKCanvas(widened))
        using (var old = SKImage.FromBitmap(_baseline!))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            var into = new SKRect(
                _rect.Left - union.Left, _rect.Top - union.Top,
                _rect.Right - union.Left, _rect.Bottom - union.Top);
            canvas.DrawImage(old, into, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            canvas.Flush();
        }
        _baseline!.Dispose();
        _baseline = widened;
        _rect = union;
        return union;
    }

    /// <summary>
    /// An owned copy of a rectangle of the layer. <c>ExtractSubset</c> shares
    /// the source's pixels and the source is about to be painted on, so the
    /// copy is the whole point rather than a precaution.
    /// </summary>
    private static SKBitmap? Crop(SKBitmap layer, SKRectI rect)
    {
        using var view = new SKBitmap();
        return layer.ExtractSubset(view, rect) ? view.Copy() : null;
    }
}
