using System.Runtime.InteropServices;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The pixels under a mark as they stood before it was stamped, so undo can put
/// them back instead of rebuilding them from the stroke record (Q167).
/// </summary>
/// <remarks>
/// <para>
/// <b>B327 made undo repaint a mark's footprint instead of the whole drawing,
/// and this is the half it could not reach.</b> That fix rebuilds the footprint
/// by <em>replaying the record</em> — clear the rectangle, re-stamp every stroke
/// that reaches it — so it still costs whatever ink happens to be in the way. On
/// a model sheet, which is one dense finished drawing, that is most of the
/// drawing: measured at 7 497 ms for a hatched 800-stroke band where restoring a
/// saved copy of the same patch costs 0.045 ms.
/// </para>
/// <para>
/// <b>It does not touch invariant 1.</b> The stroke record is still the
/// document; this is a cache of a state the record can already describe, and a
/// snapshot that disagreed with the record would be a bug rather than a second
/// source of truth. <c>UndoRegionRepaintTests</c> and
/// <c>UndoMarkSnapshotTests</c> both compare against a from-nothing
/// <c>Materialize</c> at bit-identity, which is what says so.
/// </para>
/// <para>
/// <b>Rectangles, not tiles, and that is a deviation from Q167 worth naming.</b>
/// The answer said to reuse <c>TileGrid</c> rather than invent a second set of
/// squares, on the model of Krita, whose canvas <em>is</em> tiles. Lightbox's is
/// not: <see cref="FrameBitmapCache"/> holds one flat bitmap per frame, so
/// rounding a 42x34 mark out to 256-pixel tiles would store between 256 KB and
/// 1 MB where the exact rectangle stores 6 KB — and there is no copy-on-write
/// sharing to win it back. The 6 KB figure is the one Q167's own budget argument
/// rests on, so the rectangle is what keeps that argument true.
/// </para>
/// <para>
/// <b>Every refusal falls back to B327's replay</b>, which is what every undo
/// did before this existed. Nothing here can produce a wrong picture by being
/// unavailable — only a slower one — which is why the verification below is
/// deliberately strict and the failure is always <c>false</c>.
/// </para>
/// </remarks>
public sealed class MarkSnapshot : IDisposable
{
    /// <summary>
    /// Bytes of saved patches held across the whole history.
    /// </summary>
    /// <inheritdoc cref="MemoryBudget.UndoSnapshots" path="/remarks"/>
    public static long ByteBudget { get; set; } = MemoryBudget.UndoSnapshots();

    /// <summary>
    /// How many steps' pixels to keep, mirroring the editor's undo depth.
    /// </summary>
    /// <remarks>
    /// <b>The budget alone is not enough, and the reason is that the two limits
    /// count different things.</b> <c>DocumentEditor.MaxUndo</c> trims the undo
    /// stack, so a step older than the depth can never be undone again — but its
    /// saved pixels are still held, because nothing tells this store that the
    /// step has gone. Measured on a hatched 2 400-stroke drawing: <b>194 MB of
    /// patches for 64 reachable steps</b>, waiting on a 501 MB budget to notice.
    /// The artist pays for that in the memory readout and gets nothing back.
    /// </remarks>
    public int MaxSteps
    {
        get;
        set
        {
            field = Math.Max(1, value);
            Evict();
        }
    } = 64;

    /// <summary>
    /// One cached rendering's pixels under the mark. Mutable because undo and
    /// redo <b>exchange</b> it with what is on the bitmap rather than replacing
    /// it — see <see cref="Swap"/>.
    /// </summary>
    private sealed class Patch
    {
        public required string Key { get; init; }

        /// <summary>Where it came from, in that bitmap's own surface pixels.</summary>
        public required SKRectI Rect { get; init; }

        public required byte[] Bytes { get; set; }
    }

    /// <summary>Everything saved for one step.</summary>
    private sealed class Step
    {
        public required string FrameId { get; init; }

        /// <summary>The mark's footprint in document coordinates, as the caller gave it.</summary>
        public required SKRectI Region { get; init; }

        public required List<Patch> Patches { get; init; }

        public required long Bytes { get; init; }
    }

    private readonly Dictionary<long, Step> _steps = [];

    /// <summary>Revisions oldest first — what eviction takes from.</summary>
    private readonly List<long> _order = [];

    /// <summary>
    /// Saved, but not yet attached to a step.
    /// </summary>
    /// <remarks>
    /// <b>The revision cannot be known when the pixels have to be read.</b> The
    /// commit paths stamp the mark onto the cached bitmap <em>before</em> they
    /// push the undo step, so the only moment the old pixels still exist is one
    /// where the step has no number yet. Reading <c>NextRevision</c> and hoping
    /// would be a guess that fails silently — anything pushing a step in between
    /// would file these pixels under somebody else's edit, and restoring them
    /// later would paint a picture the document does not describe. So the pixels
    /// wait here and <see cref="Promote"/> files them once the editor has
    /// actually issued the number.
    /// </remarks>
    private Step? _pending;

    /// <summary>Bytes currently held, pending included.</summary>
    public long Bytes { get; private set; }

    /// <summary>How many steps have pixels saved.</summary>
    public int Steps => _steps.Count;

    /// <summary>Undos and redos served from a saved patch rather than a replay.</summary>
    public int Restores { get; private set; }

    /// <summary>
    /// Times <see cref="Swap"/> refused and sent the caller back to the replay.
    /// </summary>
    /// <remarks>
    /// The counter that answers "how often does the fast path actually fire in
    /// real work", which Q167 left open. A number that climbs in ordinary
    /// drawing means the verification is too strict, not that undo is broken.
    /// </remarks>
    public int Fallbacks { get; private set; }

    /// <summary>Steps dropped because the history's pixels outgrew the budget.</summary>
    public int Evictions { get; private set; }

    /// <summary>
    /// Copy the pixels under <paramref name="region"/> aside, for every cached
    /// rendering of <paramref name="frame"/>. Call immediately before stamping
    /// the mark.
    /// </summary>
    /// <remarks>
    /// A frame nothing has cached saves nothing and is not an error: with no
    /// bitmap there is no repair for undo to make, and the next read renders the
    /// reverted record from nothing.
    /// </remarks>
    public void Hold(FrameBitmapCache cache, Frame frame, SKRectI region)
    {
        DropPending();
        if (region.Width <= 0 || region.Height <= 0) return;

        var patches = new List<Patch>();
        long bytes = 0;
        foreach (var (key, bmp, outputScale) in cache.EntriesFor(frame.Id))
        {
            var rect = FrameBitmapCache.ClampedRegion(region, outputScale, bmp.Width, bmp.Height);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (ReadRect(bmp, rect) is not { } saved) return;
            patches.Add(new Patch { Key = key, Rect = rect, Bytes = saved });
            bytes += saved.Length;
        }

        if (patches.Count == 0) return;
        // Refused here rather than after promotion: a mark too big to hold is
        // the case the budget exists for, and there is no point copying two
        // megabytes only to evict them on the next line.
        if (bytes > ByteBudget) return;
        _pending = new Step
        {
            FrameId = frame.Id, Region = region, Patches = patches, Bytes = bytes,
        };
        Bytes += bytes;
    }

    /// <summary>
    /// File the held pixels under the revision the editor issued for them.
    /// A no-op when nothing is held, which is every edit that stamps no mark.
    /// </summary>
    public void Promote(long revision)
    {
        if (_pending is not { } step) return;
        _pending = null;
        // A revision is never reused (DocumentEditor's counter only climbs), so
        // this only fires if a caller promoted the same one twice.
        if (_steps.ContainsKey(revision))
        {
            Bytes -= step.Bytes;
            return;
        }
        _steps[revision] = step;
        _order.Add(revision);
        Evict();
    }

    /// <summary>
    /// Throw away held pixels that never became a step.
    /// </summary>
    /// <remarks>
    /// The pending slot has to be emptied by whoever filled it, or a mark held
    /// during an edit that pushed no delta would still be sitting there when the
    /// next one commits — and would then be filed under that step's revision,
    /// which is a wrong picture rather than a slow one.
    /// </remarks>
    public void Discard() => DropPending();

    /// <summary>
    /// Exchange the saved pixels with what is on the cached bitmap now, for
    /// every cached rendering of <paramref name="frame"/>. Returns false when
    /// anything at all has moved, in which case nothing is written and the
    /// caller must fall back to the replay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An exchange rather than a restore, which is what makes redo free
    /// too.</b> The patch always holds the pixels for the state the step is
    /// <em>not</em> in: after an undo it holds the marked ones, after a redo the
    /// unmarked ones. That invariant only survives while every transition goes
    /// through here, which is why the caller forgets a frame's snapshots on any
    /// path that rebuilds its bitmap another way.
    /// </para>
    /// <para>
    /// <b>Verified in full before a byte is written</b>, on B327's own
    /// precedent: a refusal has to leave the bitmap as it was rather than half
    /// swapped, because a half-swapped drawing is ink the record does not
    /// describe and it only shows where marks overlap.
    /// </para>
    /// </remarks>
    public bool Swap(long revision, FrameBitmapCache cache, Frame frame, SKRectI region)
    {
        if (!_steps.TryGetValue(revision, out var step)
            || step.FrameId != frame.Id
            || step.Region != region)
        {
            Fallbacks++;
            return false;
        }

        var live = cache.EntriesFor(frame.Id);
        if (live.Count != step.Patches.Count)
        {
            Fallbacks++;
            return false;
        }

        // Pair each cached rendering with its patch before writing anything. A
        // rendering that arrived since (an export at 2x) or one whose size moved
        // sends the whole step back to the replay rather than being skipped —
        // patching some of a frame's bitmaps and replaying none of the others
        // would leave the two disagreeing.
        var paired = new List<(SKBitmap Bmp, Patch Patch)>(live.Count);
        foreach (var (key, bmp, outputScale) in live)
        {
            var patch = step.Patches.FirstOrDefault(p => p.Key == key);
            if (patch is null)
            {
                Fallbacks++;
                return false;
            }
            var rect = FrameBitmapCache.ClampedRegion(region, outputScale, bmp.Width, bmp.Height);
            if (rect != patch.Rect
                || bmp.GetPixels() == IntPtr.Zero
                || patch.Bytes.Length != rect.Width * rect.Height * bmp.BytesPerPixel)
            {
                Fallbacks++;
                return false;
            }
            paired.Add((bmp, patch));
        }

        foreach (var (bmp, patch) in paired)
        {
            var now = ReadRect(bmp, patch.Rect);
            if (now is null)
            {
                // Cannot happen after the pairing above, and if it ever does the
                // honest answer is a rebuild rather than a partial write.
                Fallbacks++;
                return false;
            }
            WriteRect(bmp, patch.Rect, patch.Bytes);
            patch.Bytes = now;
            // The same announcement Append and RepaintRegion make: this bitmap is
            // the one the frame cache holds, and anything keyed on its identity —
            // a tile split, a baked layer stack — has to see the content move.
            BitmapVersion.Bump(bmp);
        }
        Restores++;
        return true;
    }

    /// <summary>
    /// Drop everything saved for one drawing — for any path that rebuilds its
    /// bitmap without going through <see cref="Swap"/>.
    /// </summary>
    public void Forget(string frameId)
    {
        if (_pending?.FrameId == frameId) DropPending();
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            if (!_steps.TryGetValue(_order[i], out var step) || step.FrameId != frameId) continue;
            Bytes -= step.Bytes;
            _steps.Remove(_order[i]);
            _order.RemoveAt(i);
        }
    }

    /// <summary>Drop everything — a document-wide change, a load, a tab switch.</summary>
    public void Clear()
    {
        DropPending();
        _steps.Clear();
        _order.Clear();
        Bytes = 0;
    }

    public void Dispose() => Clear();

    private void DropPending()
    {
        if (_pending is not { } step) return;
        Bytes -= step.Bytes;
        _pending = null;
    }

    /// <summary>
    /// Oldest first, because the oldest step is the one an artist is least
    /// likely to reach — and losing it costs a slower undo, never the edit.
    /// </summary>
    private void Evict()
    {
        while (_order.Count > 0 && (Bytes > ByteBudget || _order.Count > MaxSteps))
        {
            var oldest = _order[0];
            _order.RemoveAt(0);
            if (!_steps.Remove(oldest, out var step)) continue;
            Bytes -= step.Bytes;
            Evictions++;
        }
    }

    /// <summary>
    /// A rectangle of a bitmap as raw bytes, row by row.
    /// </summary>
    /// <remarks>
    /// <b>Raw bytes rather than a Skia draw, because the bar is bit-identity.</b>
    /// Blitting through a canvas invites a colour-space conversion or a resample
    /// that is invisible until two renders of the same record differ in the last
    /// bit — which is exactly the failure <c>UndoRegionRepaintTests</c> exists to
    /// catch. A row copy cannot do that.
    /// </remarks>
    private static byte[]? ReadRect(SKBitmap bmp, SKRectI rect)
    {
        var pixels = bmp.GetPixels();
        if (pixels == IntPtr.Zero) return null;
        var bpp = bmp.BytesPerPixel;
        var stride = rect.Width * bpp;
        var bytes = new byte[stride * rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            var row = pixels + (rect.Top + y) * bmp.RowBytes + rect.Left * bpp;
            Marshal.Copy(row, bytes, y * stride, stride);
        }
        return bytes;
    }

    /// <inheritdoc cref="ReadRect"/>
    private static void WriteRect(SKBitmap bmp, SKRectI rect, byte[] bytes)
    {
        var pixels = bmp.GetPixels();
        var bpp = bmp.BytesPerPixel;
        var stride = rect.Width * bpp;
        for (var y = 0; y < rect.Height; y++)
        {
            var row = pixels + (rect.Top + y) * bmp.RowBytes + rect.Left * bpp;
            Marshal.Copy(bytes, y * stride, row, stride);
        }
    }
}
