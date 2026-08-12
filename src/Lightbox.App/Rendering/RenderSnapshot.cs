using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The immutable hand-off between the UI thread (which composites the scene)
/// and Avalonia's render thread (which just blits it). The render thread must
/// never touch the mutable document — this object is the only thing that
/// crosses the boundary.
///
/// <see cref="Seq"/> orders snapshots so the canvas can free old images as
/// soon as a newer one has actually been rendered: holding them longer forces
/// the compositor's back-buffers into copy-on-write duplication, which on a
/// 4K document costs more than the drawing itself.
///
/// <see cref="DocViewport"/> carries the visible document rectangle (in document space),
/// enabling culled compositing. The viewport is
/// the rectangle the view can show at the current zoom/pan/rotation state — passed from
/// CanvasControl to enable TileCompositor to cull its work to what is visible.
/// When compositing only a region, this prevents allocating
/// and compositing pixels the user cannot see. Null means the whole document is visible
/// (ordinary case when compose surface is full document size).
/// </summary>
public sealed class RenderSnapshot(
    SKImage? image,
    int docWidth,
    int docHeight,
    long seq = 0,
    SKRectI? docViewport = null,
    SKRectI? changedInImage = null,
    IReadOnlyList<RenderPass>? passes = null,
    Action? release = null,
    DeferredCompose? deferred = null) : IDisposable
{
    private SKImage? _image = image;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Whether this snapshot has been freed.</summary>
    /// <remarks>
    /// <b>Tracked explicitly rather than inferred from a null image</b>, because
    /// since stage 3b null means two different things — "freed" and "not composed
    /// yet" — and the one place that has to tell them apart is
    /// <c>CanvasControl.HeldImagesAlive</c>, the guard that exists because B130
    /// drew a snapshot the canvas had already freed. Conflating them would make
    /// that guard read "alive" for a dangling frame.
    /// </remarks>
    public bool IsDisposed
    {
        get { lock (_gate) return _disposed; }
    }

    /// <summary>
    /// The composed frame, or null when it has not been composed yet.
    /// </summary>
    /// <remarks>
    /// <b>Nullable since B125 stage 3b.</b> A snapshot may describe a composite
    /// rather than carry one — see <see cref="Deferred"/> — in which case the
    /// pixels do not exist until <see cref="Materialise"/> runs on the render
    /// thread. Consumers that only need to know whether there is anything to
    /// free, or to draw, must handle null rather than assume a frame.
    /// </remarks>
    public SKImage? Image
    {
        get { lock (_gate) return _image; }
    }

    /// <summary>
    /// The composite the publisher described and did not perform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The inversion B125 stage 3b makes.</b> The publisher composited on the
    /// UI thread and handed over a finished image; the graphics context exists
    /// only inside the draw op, on the render thread. So the work moves to where
    /// the context is, and this is the description that travels instead of the
    /// pixels.
    /// </para>
    /// <para>
    /// Set on the culled route only — the one that already built a fresh surface
    /// every publish, so nothing is lost by building it elsewhere. See
    /// <see cref="DeferredCompose"/> for why the other two routes stay put.
    /// </para>
    /// </remarks>
    public DeferredCompose? Deferred { get; } = deferred;

    /// <summary>Whether this composite has run on a GPU-backed surface.</summary>
    public bool GpuBacked { get; private set; }

    /// <summary>
    /// Pretend this snapshot's image is GPU-backed, for tests. The real flag is
    /// only ever set by <see cref="Materialise(GRContext?, LayerTextureCache?)"/>
    /// on a machine with a graphics context, and this repository has none — the
    /// same constraint that shaped <c>GpuComposite.CountCompositeForTests</c>.
    /// </summary>
    internal void MarkGpuBackedForTests() => GpuBacked = true;

    /// <summary>
    /// The frame, composing it first if the publisher left that to us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called from the draw op, which is the whole point</b> — that is where
    /// the lease's <c>GRContext</c> lives, and passing it here is how a composite
    /// becomes GPU-backed at all (B125 stage 4).
    /// </para>
    /// <para>
    /// Composed once and kept: a snapshot may be drawn several times before a
    /// newer one replaces it (a resize, a view change, a compositor repaint), and
    /// recomposing per draw would turn one publish into many composites.
    /// </para>
    /// <para>
    /// The lock is not for the common case, where only the render thread touches
    /// this. It is for the seam with <see cref="Dispose"/>, which the UI thread
    /// calls from the retirement queue: the queue only frees snapshots older than
    /// the last one rendered, so the two should never meet — and "should never"
    /// on a path whose failure mode is a native access violation is worth a
    /// lock rather than a comment.
    /// </para>
    /// </remarks>
    public SKImage Materialise(GRContext? gpu) => Materialise(gpu, null);

    /// <inheritdoc cref="Materialise(GRContext?)"/>
    /// <param name="textures">
    /// Resident layer textures (B125 stage 5), so a layer showing the same
    /// drawing as the last frame is not uploaded again. Ignored unless the
    /// surface is GPU-backed.
    /// </param>
    public SKImage Materialise(GRContext? gpu, LayerTextureCache? textures)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_image is not null) return _image;
            if (Deferred is not { } work)
            {
                throw new InvalidOperationException(
                    "This snapshot has neither an image nor a composite to perform.");
            }
            _image = work.Compose(gpu, textures, out var onGpu);
            GpuBacked = onGpu;
            return _image;
        }
    }

    /// <summary>
    /// The passes this snapshot was composed from, when the publisher sent them
    /// along — null otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B125 stage 3: the pass list beside the image, before the pass list
    /// instead of it.</b> The end state is that the canvas composites from these
    /// inside the draw op and the intermediate full-canvas <see cref="SKImage"/>
    /// disappears; carrying both first is what makes that switchable one route
    /// at a time and comparable against <c>ComposeIdentityTests</c>.
    /// </para>
    /// <para>
    /// <b>Every bitmap in here is borrowed, not owned.</b> They belong to
    /// <see cref="FrameBitmapCache"/>, whose eviction disposes — so they are
    /// pinned for as long as this snapshot lives and released by
    /// <see cref="Dispose"/>. That is not a hypothetical: B130 was the same
    /// shape one level up, a snapshot freed while the compositor still held it,
    /// an access violation inside <c>sk_canvas_draw_image_rect</c>, and — because
    /// a native crash bypasses the reporter's managed channels — an empty log
    /// and "Lightbox dies as soon as I touch anything".
    /// </para>
    /// </remarks>
    public IReadOnlyList<RenderPass>? Passes { get; } = passes;

    private Action? _release = release;

    /// <summary>
    /// Free the image and let go of whatever the passes were holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent, and it has to be: the retirement queue frees a snapshot from
    /// three different places — an incoming frame dropped under back-pressure,
    /// the ordinary keep-window, and the hard cap — and a release that ran twice
    /// would unpin a bitmap somebody else is still reading.
    /// </para>
    /// <para>
    /// <b>Always on the UI thread.</b> Every one of those three sites is inside
    /// <c>UpdateSnapshot</c>, which the publisher calls directly, and the frame
    /// cache's pin table is a plain dictionary that assumes exactly this. The
    /// render thread reads pixels and disposes nothing.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // B179: a GPU-backed image must not be freed here. This runs on the UI
        // thread, the GRContext lives on the render thread, and a GPU resource
        // released off the context's thread is parked rather than freed — which
        // leaked one render target per publish, invisible to every cache,
        // until the process hit 12 GB. The reaper frees it inside the draw op,
        // where the context is current. CPU images gain nothing from the detour
        // and dispose here exactly as they always did.
        SKImage? toReap = null;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_image is not null)
            {
                if (GpuBacked) toReap = _image;
                else _image.Dispose();
                _image = null;
            }
        }
        if (toReap is not null) GpuImageReaper.Enqueue(toReap);
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    /// <summary>
    /// The region of <see cref="Image"/> this publish actually repainted, in
    /// <em>image pixel</em> space, or null when the whole image was repainted.
    /// </summary>
    /// <remarks>
    /// This is what lets <see cref="PresentedFrame"/> upload a dab instead of a
    /// canvas (B122). Image space rather than document space on purpose: the
    /// consumer copies pixels, and asking every consumer to redo the scale and
    /// the viewport offset is how the two get out of step. Null is the safe
    /// value — it costs a full repaint and can never show stale pixels — so
    /// anything unsure should leave it null rather than guess a rectangle.
    /// </remarks>
    public SKRectI? ChangedInImage { get; } = changedInImage;

    /// <summary>
    /// The document's size — <b>always</b>, whatever the compositor chose to
    /// render. The canvas derives its fit scale and its pointer mapping from
    /// these two numbers, so reporting the image's size here instead moves the
    /// cursor off the mark it makes. <c>CursorAlignmentTests</c> holds the
    /// numbers from when it did. Use <c>Image.Width</c> for the image's size.
    /// </summary>
    public int DocWidth { get; } = docWidth;

    /// <inheritdoc cref="DocWidth"/>
    public int DocHeight { get; } = docHeight;

    /// <summary>Monotonic publish counter (0 for snapshots made outside the publish loop).</summary>
    public long Seq { get; } = seq;

    /// <summary>
    /// The document rectangle <see cref="Image"/> covers, or null when it covers
    /// the whole document.
    /// </summary>
    /// <remarks>
    /// This describes the <em>image</em>, not the canvas's current view — the
    /// painter needs to know where to put these pixels, and by the time a frame
    /// is drawn the view may already have moved on. It is deliberately not an
    /// input to the pointer mapping; see the remarks on
    /// <c>CanvasControl.ViewMatrix</c> for why that is a cycle rather than a
    /// convenience.
    /// </remarks>
    public SKRectI? DocViewport { get; } = docViewport;
}
