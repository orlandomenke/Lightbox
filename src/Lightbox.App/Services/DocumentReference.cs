using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Another document's frames, flattened: one picture per frame, stacked in a
/// column — or in several side by side, top to bottom then left to right, when
/// one would be taller than <see cref="DocumentReference.MaxSide"/>.
/// <see cref="Sheet"/> is owned and disposed with this.
/// </summary>
public sealed class FlattenedDocument : IDisposable
{
    public required SKBitmap Sheet { get; init; }

    /// <summary>One window per source frame, in frame order, all the same size.</summary>
    public required List<ReferenceCell> Cells { get; init; }

    /// <summary>Source-canvas pixel where every window's top-left sits.</summary>
    public int OriginX { get; init; }

    public int OriginY { get; init; }

    /// <summary>Sheet pixels per source pixel — 1, or less when the column had to shrink.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>The size of what the source renders — its canvas, or its camera's frame.</summary>
    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    /// <summary>The source file's last-write time when it was read, UTC ticks.</summary>
    public long Stamp { get; init; }

    public void Dispose() => Sheet.Dispose();
}

/// <summary>
/// Flatten a Lightbox document on disk into a reference (Q284–Q286): read it,
/// never write it, and render each frame the way an export would — visible
/// layers, without the paper.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read only, structurally.</b> The source goes through
/// <see cref="DocJson.Load(string)"/> and nothing else: no editor, no tab, no
/// project cache. There is no path from here to a save, which is what makes
/// "keep the file safe" a property of the code rather than of care.
/// </para>
/// <para>
/// <b>One window for every frame</b>, the union of what any frame draws. The
/// same rule an imported sheet follows — windows, not crops — so a runner who
/// travels still travels; and cropping to the union rather than keeping the
/// whole canvas is what keeps a 31-frame 1080p run a few megapixels instead of
/// sixty-four.
/// </para>
/// <para>
/// <b>Without the paper</b> (Q285): a reference sits over the paper, and a
/// frame carrying its own opaque background would be a wall at that position.
/// The background is <see cref="Layer.IsBackground"/>, the flag the export's
/// own "without paper" already reads — never a guess from a layer's name.
/// </para>
/// </remarks>
public static class DocumentReference
{
    /// <summary>The longest side a column may have. Past this it shrinks.</summary>
    public const int MaxSide = 16384;

    /// <summary>The most pixels a column may hold — about 160 MB decoded. Past this it shrinks.</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>
    /// The most frames a source may have. A reference is something to draw
    /// against; ten thousand frames of it is a file that was not meant for this.
    /// </summary>
    public const int MaxFrames = 2000;

    /// <summary>
    /// The most pixels one frame of the source may have — refused before any
    /// render, since every frame is composed whole before it is cropped.
    /// </summary>
    public const long MaxFramePixels = 64_000_000;

    /// <summary>
    /// The most drawn pixels held across frames while the window is found —
    /// about a gigabyte. A shot with a full-bleed card on every frame reaches
    /// this long before the output caps would, so it is checked as it grows.
    /// </summary>
    public const long MaxHeldPixels = 250_000_000;

    /// <summary>Breathing room round the drawn area, so a soft edge is not shaved.</summary>
    private const int Margin = 4;

    /// <summary>
    /// Whether a source path may be followed at all: a local file, never a
    /// network share or a device path.
    /// </summary>
    /// <remarks>
    /// A reference is re-read on its own — on open, on a tab switch, whenever
    /// the window comes forward — with no action from the artist. A UNC path
    /// in a received document would make Windows open a connection to a host
    /// the artist never named and offer it their credentials. So a share is
    /// refused at the door, picked or not: copy the document locally to draw
    /// against it.
    /// </remarks>
    public static bool IsLocal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            return !Path.IsPathRooted(path) || !new Uri(Path.GetFullPath(path)).IsUnc;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException
            or PathTooLongException)
        {
            return false;
        }
    }

    public static long StampOf(string path) => File.GetLastWriteTimeUtc(path).Ticks;

    /// <summary>
    /// The document at <paramref name="path"/>, flattened; null with a reason
    /// in <paramref name="error"/> when it cannot be read.
    /// </summary>
    public static FlattenedDocument? Flatten(string path, out string? error)
    {
        error = null;
        if (!IsLocal(path))
        {
            error = $"“{Path.GetFileName(path)}” is on a network share — copy it to this machine to draw against it.";
            return null;
        }
        if (!File.Exists(path))
        {
            error = $"“{Path.GetFileName(path)}” is not there any more.";
            return null;
        }

        Doc doc;
        long stamp;
        try
        {
            stamp = StampOf(path);
            doc = DocJson.Load(path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException
            or FormatException)
        {
            error = $"“{Path.GetFileName(path)}” could not be read as a Lightbox document — {ex.Message}";
            return null;
        }

        var count = Math.Max(1, doc.Scene.FrameCount);
        if (count > MaxFrames)
        {
            error = $"“{Path.GetFileName(path)}” has {count:N0} frames — more than the {MaxFrames:N0} a reference can hold.";
            return null;
        }
        if ((long)doc.Scene.Width * doc.Scene.Height > MaxFramePixels)
        {
            error = $"“{Path.GetFileName(path)}” is {doc.Scene.Width}×{doc.Scene.Height} — too large a canvas to flatten as a reference.";
            return null;
        }
        long held = 0;
        var crops = new List<(SKBitmap? Picture, SKRectI Bounds)>(count);
        int width = 0, height = 0;
        try
        {
            using (var cache = new FrameBitmapCache())
            {
                for (var i = 0; i < count; i++)
                {
                    using var image = SequenceExporter.RenderFrame(doc, cache, i, withoutPaper: true);
                    width = image.Width;
                    height = image.Height;
                    var crop = Crop(image);
                    crops.Add(crop);
                    held += (long)crop.Bounds.Width * crop.Bounds.Height;
                    if (held > MaxHeldPixels)
                    {
                        error = $"“{Path.GetFileName(path)}” draws too much to flatten as a reference — hide its full-frame layers and try again.";
                        return null;
                    }
                }
            }

            // The union of every frame's drawing, padded; the whole frame when
            // nothing was drawn at all, so an empty run still lays out frames.
            var union = SKRectI.Empty;
            foreach (var (_, bounds) in crops)
            {
                if (bounds.IsEmpty) continue;
                union = union.IsEmpty ? bounds : SKRectI.Union(union, bounds);
            }
            union = union.IsEmpty
                ? new SKRectI(0, 0, width, height)
                : SKRectI.Intersect(
                    SKRectI.Inflate(union, Margin, Margin), new SKRectI(0, 0, width, height));

            // Shrink only for the pixel budget, or for a single frame bigger
            // than a side may be. A run too long for one column wraps into the
            // next instead: 31 frames of a 1080p character is two columns at
            // full size, where one would have cost a third of the resolution.
            var scale = Math.Min(1.0, Math.Min(
                MaxSide / (double)Math.Max(union.Width, union.Height),
                Math.Sqrt(MaxPixels / ((double)union.Width * union.Height * count))));
            var cellW = Math.Max(1, (int)Math.Floor(union.Width * scale));
            var cellH = Math.Max(1, (int)Math.Floor(union.Height * scale));
            var perColumn = Math.Max(1, Math.Min(count, MaxSide / cellH));
            var columns = (count + perColumn - 1) / perColumn;

            var sheet = new SKBitmap(new SKImageInfo(
                cellW * columns, cellH * perColumn, SKColorType.Rgba8888, SKAlphaType.Premul));
            sheet.Erase(SKColors.Transparent);
            var cells = new List<ReferenceCell>(count);
            using (var canvas = new SKCanvas(sheet))
            using (var paint = new SKPaint())
            {
                var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
                for (var i = 0; i < count; i++)
                {
                    var (x, y) = (i / perColumn * cellW, i % perColumn * cellH);
                    cells.Add(new ReferenceCell { X = x, Y = y, Width = cellW, Height = cellH });
                    if (crops[i].Picture is not { } picture) continue;
                    var b = crops[i].Bounds;
                    canvas.Save();
                    canvas.ClipRect(SKRect.Create(x, y, cellW, cellH));
                    canvas.Translate(x, y);
                    canvas.Scale((float)scale);
                    canvas.Translate(b.Left - union.Left, b.Top - union.Top);
                    using var img = SKImage.FromBitmap(picture);
                    canvas.DrawImage(img, 0, 0, sampling, paint);
                    canvas.Restore();
                }
            }

            return new FlattenedDocument
            {
                Sheet = sheet,
                Cells = cells,
                OriginX = union.Left,
                OriginY = union.Top,
                Scale = scale,
                SourceWidth = width,
                SourceHeight = height,
                Stamp = stamp,
            };
        }
        finally
        {
            foreach (var (picture, _) in crops) picture?.Dispose();
        }
    }

    /// <summary>
    /// The drawn part of one frame and where it sat; a null picture for a frame
    /// with nothing on it. The tight copy is what lets every frame be held at
    /// once without holding every whole frame.
    /// </summary>
    private static (SKBitmap? Picture, SKRectI Bounds) Crop(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var full = new SKBitmap(info);
        if (!image.ReadPixels(info, full.GetPixels(), info.RowBytes, 0, 0)) return (null, SKRectI.Empty);

        var bounds = AlphaBounds(full);
        if (bounds.IsEmpty) return (null, SKRectI.Empty);
        var crop = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(full, -bounds.Left, -bounds.Top);
        }
        return (crop, bounds);
    }

    /// <summary>The smallest rectangle holding every pixel with any alpha.</summary>
    private static unsafe SKRectI AlphaBounds(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        int left = w, top = h, right = -1, bottom = -1;
        var stride = bitmap.RowBytes;
        var bytes = (byte*)bitmap.GetPixels();
        for (var y = 0; y < h; y++)
        {
            var row = (uint*)(bytes + (long)y * stride);
            int first = -1, last = -1;
            for (var x = 0; x < w; x++)
            {
                // RGBA8888, little-endian: alpha is the top byte.
                if ((row[x] & 0xFF000000u) == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first < 0) continue;
            if (first < left) left = first;
            if (last > right) right = last;
            if (y < top) top = y;
            bottom = y;
        }
        return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }
}
