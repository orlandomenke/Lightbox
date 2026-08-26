namespace Lightbox.App.Rendering;

/// <summary>
/// What the canvas can tell the publisher about its own progress (B321).
/// </summary>
/// <remarks>
/// A file of its own because <c>CanvasControl.cs</c> is on the monolith ratchet
/// and this is new surface rather than an edit to old — which is exactly the
/// move the ratchet exists to force.
/// </remarks>
public partial class CanvasControl
{
    /// <summary>
    /// The highest snapshot sequence the render thread has finished drawing,
    /// readable from the UI thread without waiting for
    /// <see cref="SnapshotPresented"/> to be delivered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The publish dam waits for a message describing something already
    /// true.</b> That event is posted to the UI thread from <c>NoteRendered</c>,
    /// so the pacing learns of a draw one dispatcher hop after it happened — and
    /// mid-stroke that hop queues behind the pointer events the artist is
    /// generating. Measured on the owner's machine: the pacing round trip was
    /// <b>97.55 ms</b> against a <c>publish -&gt; drawn</c> of <b>56.96 ms</b>,
    /// so roughly forty of it was the telling rather than the drawing.
    /// </para>
    /// <para>
    /// The event stays. It is what re-enters <c>RequestSnapshot</c> when no
    /// further pointer event arrives, and it keeps B73's ordering when one does.
    /// This exists only so a dam being <em>checked</em> can consult the truth
    /// instead of the last message about it.
    /// </para>
    /// </remarks>
    internal long LastRenderedSeq => System.Threading.Interlocked.Read(ref _lastRenderedSeq);

    private void NoteRendered(long seq)
    {
        // Before the early return below, which is about keeping the high-water
        // mark monotonic. A frame that arrived out of order was still drawn, and
        // dropping it here would flatter the average by counting only the
        // frames that behaved.
        _presentWait.Rendered(seq);
        StrokeToScreen.Shared.Rendered(seq);

        long current;
        do
        {
            current = Interlocked.Read(ref _lastRenderedSeq);
            if (seq <= current) return;
        }
        while (Interlocked.CompareExchange(ref _lastRenderedSeq, seq, current) != current);

        // Only when the high-water mark moved: a cursor repaint re-draws the
        // same snapshot many times a second, and the publisher only cares that
        // a NEW frame reached the screen. The deferral race is safe by
        // ordering — a publish can only be deferred while its draw's
        // notification has not been processed yet, so the post that releases
        // it is always already queued.
        if (SnapshotPresented is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => SnapshotPresented?.Invoke(seq),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Frame times arrive from the render thread; marshal to the UI thread to publish them.</summary>
    private void ReportFrameTime(double milliseconds)
    {
        _presentWait.Drew(milliseconds);   // B321: see PresentLatency.Drew.
        if (FrameRendered is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => FrameRendered?.Invoke(milliseconds),
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
