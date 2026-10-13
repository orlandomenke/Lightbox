using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The art under a line that is open for reshaping, held so that letting go of
/// a point redraws the line and what is over it instead of the drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> A reshape changes one stroke, and the strokes under it have to
/// be under it again afterwards. Replaying them is what a region repaint does,
/// and for a line across a busy drawing "the strokes its rectangle touches" is
/// most of the drawing: about 200 ms from release to screen on the owner-shaped
/// document (perf lab, 2026-10-12), and no better with B450, because nothing in
/// the engine makes that rectangle small. But while the line is open those
/// strokes do not change. So their picture is rendered once, off the UI
/// thread, when the line is opened — and each reshape copies the rectangle out
/// of it, stamps the line, and replays only the strokes that sit over it.
/// </para>
/// <para>
/// <b>It is a cache of a state the record already describes</b>, never a
/// second source: the frame as it stood before the open stroke, which is the
/// frame's own first strokes rendered by the same rasterizer. Invariant 1 is
/// untouched, and the tests that hold this compare the patched drawing with a
/// render of the record byte for byte.
/// </para>
/// <para>
/// <b>It is trusted for exactly one thing: this line's own reshape.</b> Any
/// other edit — an undo, a change from the MCP surface, a layer operation —
/// may have changed what is under the line, and this cannot tell cheaply
/// whether it did. So any edit that is not the reshape lets the held art go,
/// and it is rendered afresh behind the artist's back; until that lands, a
/// reshape takes the road it always took. Stale art put back under an edit
/// would be wrong ink on the page, which is worse than a slow release.
/// </para>
/// <para>
/// <b>What it costs:</b> one bitmap the size of the document for as long as a
/// line is open — 8 MB at 1080p, 33 MB at 4K — and one render of the strokes
/// under the line per session, on a worker.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The held picture and what it is a picture of.</summary>
    private sealed class IsolatedLineBackdrop(string frameId, string strokeId, int fromStroke, int width, int height)
    {
        private readonly object _gate = new();
        private SKBitmap? _below;
        private bool _letGo;

        public string FrameId { get; } = frameId;
        public string StrokeId { get; } = strokeId;

        /// <summary>The open stroke's place in the drawing: everything before it is in the picture.</summary>
        public int FromStroke { get; } = fromStroke;

        public int Width { get; } = width;
        public int Height { get; } = height;

        public Task? Build { get; set; }

        /// <summary>The picture, once the worker has delivered it; null until then and after it is let go.</summary>
        public SKBitmap? Below
        {
            get { lock (_gate) return _letGo ? null : _below; }
        }

        /// <summary>The worker's hand-over. False when nobody is waiting any more, and the picture is the caller's to free.</summary>
        public bool Deliver(SKBitmap picture)
        {
            lock (_gate)
            {
                if (_letGo) return false;
                _below = picture;
                return true;
            }
        }

        /// <summary>Only ever from the UI thread, which is also the only reader — so nothing is drawing from it.</summary>
        public void LetGo()
        {
            lock (_gate)
            {
                _letGo = true;
                _below?.Dispose();
                _below = null;
            }
        }
    }

    private IsolatedLineBackdrop? _lineBackdrop;

    /// <summary>True only inside <see cref="CommitPathEdit"/>'s own edit: the one edit the held art is trusted for.</summary>
    private bool _reshapeInFlight;

    /// <summary>Whether art is held, and ready, under the open line.</summary>
    internal bool HasLineBackdrop => _lineBackdrop?.Below is not null;

    /// <summary>The render of the art under the open line, for a test to wait on.</summary>
    internal Task? LineBackdropBuild => _lineBackdrop?.Build;

    /// <summary>How many reshapes were repainted over held art rather than by replaying the drawing.</summary>
    internal int LineBackdropRepaints { get; private set; }

    /// <summary>How many times held art was let go because of an edit it could not vouch for.</summary>
    internal int LineBackdropsLetGo { get; private set; }

    /// <summary>
    /// Start holding the art under a stroke: called when a line is opened.
    /// </summary>
    /// <remarks>
    /// Returns having held nothing wherever a region repaint would be refused
    /// anyway — paper that has moved, a posed drawing, stored pixels, a placed
    /// symbol — because there the reshape rebuilds the drawing and has no use
    /// for it.
    /// </remarks>
    private void HoldArtUnder(string strokeId)
    {
        ForgetLineBackdrop();
        if (PaperHasMoved || PaintTarget() is not { } frame) return;
        if (!FrameRasterizer.CanRepaintRegion(frame) || _cache.Rig.IsPosed(frame)) return;
        var index = frame.Strokes.FindIndex(s => s.Id == strokeId);
        if (index < 0) return;

        // The strokes themselves, by reference, in a frame of their own. They
        // are not copied: an edit to any of them lets this go before its
        // picture can be used, and until then nothing writes to them.
        var under = new Frame { Id = frame.Id + "/under", Strokes = [.. frame.Strokes.Take(index)] };
        var (width, height) = (Scene.Width, Scene.Height);
        var held = new IsolatedLineBackdrop(frame.Id, strokeId, index, width, height);
        held.Build = Task.Run(() =>
        {
            var picture = FrameRasterizer.Materialize(under, width, height);
            if (!held.Deliver(picture)) picture.Dispose();
        });
        _lineBackdrop = held;
    }

    /// <summary>Let the held art go: the line was closed, or it can no longer be vouched for.</summary>
    internal void ForgetLineBackdrop()
    {
        _lineBackdrop?.LetGo();
        _lineBackdrop = null;
    }

    /// <summary>
    /// An edit has landed. Unless it is the open line's own reshape, the held
    /// art is let go and rendered afresh.
    /// </summary>
    private void LineBackdropSawAnEdit()
    {
        if (_reshapeInFlight || _lineBackdrop is null) return;
        ForgetLineBackdrop();
        LineBackdropsLetGo++;
        if (_pathEdit is { } session) HoldArtUnder(session.StrokeId);
    }

    /// <summary>
    /// The held art, if it is ready and is the art under this stroke of this
    /// drawing as the drawing now stands.
    /// </summary>
    private IsolatedLineBackdrop? ReadyArtUnder(Frame frame, string strokeId)
    {
        if (_lineBackdrop is not { } held || held.Below is null) return null;
        if (held.FrameId != frame.Id || held.StrokeId != strokeId) return null;
        if (held.Width != Scene.Width || held.Height != Scene.Height) return null;
        if (held.FromStroke >= frame.Strokes.Count || frame.Strokes[held.FromStroke].Id != strokeId) return null;
        return held;
    }

    /// <summary>
    /// Repaint a rectangle of a drawing over the held art, when this is the
    /// open line's own reshape and the art is ready. False otherwise, and the
    /// caller goes on to the ordinary replay.
    /// </summary>
    private bool TryRepaintOverHeldArt(Frame frame, SKRectI region)
    {
        if (!_reshapeInFlight || _pathEdit is not { } session) return false;
        if (ReadyArtUnder(frame, session.StrokeId) is not { } held || held.Below is not { } below) return false;
        if (!_cache.RepaintRegion(frame, region, below, held.FromStroke)) return false;
        LineBackdropRepaints++;
        return true;
    }
}
