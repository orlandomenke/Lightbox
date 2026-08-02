using System.Collections.Concurrent;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Decoded reference sheets, keyed by their document id (<c>ref_…</c>).
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
/// A sheet that will not decode is dropped rather than thrown: a reference is
/// an aid, and losing it must never be the reason a document fails to open.
/// </para>
/// </remarks>
public static class ReferenceStripRegistry
{
    private static readonly ConcurrentDictionary<string, SKBitmap> Sheets = new();

    /// <summary>Decode any sheet not already held. Ids are globally unique.</summary>
    public static void Register(IEnumerable<(string Id, string Png)> strips)
    {
        foreach (var (id, png) in strips)
        {
            if (string.IsNullOrEmpty(png) || Sheets.ContainsKey(id)) continue;
            try
            {
                Sheets[id] = PngCodec.Decode(png);
            }
            catch
            {
                // Drawing against nothing is better than not opening the file.
            }
        }
    }

    public static SKBitmap? Resolve(string id) => Sheets.GetValueOrDefault(id);

    /// <summary>Forget one sheet — a reference the artist removed.</summary>
    public static void Forget(string id)
    {
        if (Sheets.TryRemove(id, out var sheet)) sheet.Dispose();
    }
}
