using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Decoded reference sheets, per document, keyed by their strip id (<c>ref_…</c>).
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="BrushTipRegistry"/>, and for the same
/// reason: the document carries base64 PNG, and decoding it on every composite
/// would cost more than the whole frame. One sheet is held per strip and the
/// cells are cut out of it at draw time, rather than one bitmap per cell —
/// a hundred-frame reference is one decode and one allocation, not a hundred.
/// </para>
/// <para>
/// <b>Per document, because a strip id is not unique (B490).</b> A copy, a
/// template and the same file opened twice all carry the same ids, and a file
/// reopened after closing it unsaved is a different document from the tab it
/// replaced. Keyed by id alone, the reopened file was served the edit nobody
/// saved, and an edit to one document's taped sheet drew on the other. The
/// document is <see cref="Scene.ResourceScope"/> — the editor that holds it —
/// which a clone keeps, so undo and a render snapshot see the same sheets; the
/// same scoping B483 gave the frame cache.
/// </para>
/// <para>
/// A sheet that will not decode is dropped rather than thrown: a reference is
/// an aid, and losing it must never be the reason a document fails to open.
/// </para>
/// </remarks>
public static class ReferenceStripRegistry
{
    /// <summary>
    /// Weak on the document, so one that goes away without passing through
    /// <see cref="ForgetScope"/> does not pin its sheets for the life of the
    /// process; they wait for the finaliser instead.
    /// </summary>
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, SKBitmap>> Scopes = new();

    /// <summary>How many exports are drawing right now; see <see cref="Reading"/>.</summary>
    private static int _readers;

    /// <summary>The sheets held for this scene's document, made on first use.</summary>
    /// <remarks>
    /// A scene no editor holds gets a scope of its own here, so a registration
    /// against it is found by the same scene and its clones and by nothing else.
    /// </remarks>
    private static ConcurrentDictionary<string, SKBitmap> SheetsOf(Scene scene) =>
        Scopes.GetValue(scene.ResourceScope ??= new object(), _ => new(StringComparer.Ordinal));

    /// <summary>Decode any sheet not already held for this document.</summary>
    public static void Register(Scene scene, IEnumerable<(string Id, string Png)> strips)
    {
        var sheets = SheetsOf(scene);
        foreach (var (id, png) in strips)
        {
            if (string.IsNullOrEmpty(png) || sheets.ContainsKey(id)) continue;
            try
            {
                sheets[id] = PngCodec.Decode(png);
            }
            catch
            {
                // Drawing against nothing is better than not opening the file.
            }
        }
    }

    /// <summary>
    /// Hold an already-decoded sheet — the video-reference path (Q56), where
    /// the pixels come from footage rather than a stored PNG. Takes ownership.
    /// </summary>
    public static void Register(Scene scene, string id, SKBitmap sheet)
    {
        if (!SheetsOf(scene).TryAdd(id, sheet))
        {
            sheet.Dispose();
        }
    }

    /// <summary>
    /// This document's sheet for the strip, or null. Called per composite, so
    /// it allocates nothing: a scene nothing was registered for answers null.
    /// </summary>
    public static SKBitmap? Resolve(Scene scene, string id) =>
        scene.ResourceScope is { } owner && Scopes.TryGetValue(owner, out var sheets)
            ? sheets.GetValueOrDefault(id)
            : null;

    /// <summary>
    /// Swap in new pixels for a sheet already held — a live reference whose
    /// source changed (Q284). Takes ownership; the sheet it displaces goes to
    /// <paramref name="retire"/>, see <see cref="Retire"/>.
    /// </summary>
    public static void Replace(Scene scene, string id, SKBitmap sheet, Action<SKBitmap>? retire = null)
    {
        var sheets = SheetsOf(scene);
        sheets.TryGetValue(id, out var old);
        sheets[id] = sheet;
        if (old is not null && !ReferenceEquals(old, sheet)) Retire(old, retire);
    }

    /// <summary>Forget one sheet — a reference the artist removed.</summary>
    public static void Forget(Scene scene, string id, Action<SKBitmap>? retire = null)
    {
        if (scene.ResourceScope is { } owner
            && Scopes.TryGetValue(owner, out var sheets)
            && sheets.TryRemove(id, out var sheet))
        {
            Retire(sheet, retire);
        }
    }

    /// <summary>
    /// Forget every sheet held for one document — a tab that closed. Nothing
    /// can ask for them again: a reopened file is held by a new editor.
    /// </summary>
    public static void ForgetScope(object owner, Action<SKBitmap>? retire = null)
    {
        if (!Scopes.TryGetValue(owner, out var sheets)) return;
        Scopes.Remove(owner);
        foreach (var id in sheets.Keys)
        {
            if (sheets.TryRemove(id, out var sheet)) Retire(sheet, retire);
        }
    }

    /// <summary>
    /// Held by an export for as long as it draws: sheets displaced meanwhile
    /// are left to the finaliser rather than freed under it.
    /// </summary>
    public static IDisposable Reading()
    {
        Interlocked.Increment(ref _readers);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Decrement(ref _readers);
        }
    }

    /// <summary>
    /// Free a sheet nothing can resolve any more, without freeing it under a draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disposing pixels a composite is drawing from is a native fault, not an
    /// exception (B130). The canvas pins every pass bitmap at publish, so the
    /// view model hands in its frame cache's pin-aware disposal as
    /// <paramref name="retire"/>; with none, the sheet is disposed now.
    /// </para>
    /// <para>
    /// An export draws on its own thread and pins nothing, so while one holds
    /// <see cref="Reading"/> the sheet is left to the finaliser — what every
    /// displaced sheet got before. The count is read after the sheet left the
    /// dictionary: an export that took its lease later cannot resolve it.
    /// </para>
    /// </remarks>
    private static void Retire(SKBitmap sheet, Action<SKBitmap>? retire)
    {
        if (Volatile.Read(ref _readers) > 0) return;
        if (retire is not null) retire(sheet);
        else sheet.Dispose();
    }
}
