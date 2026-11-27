using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Import.Unity;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>Bounds and looks for opening a game clip.</summary>
public sealed record UnityClipImportOptions
{
    /// <summary>Empty canvas kept around the widest frame, in pixels.</summary>
    public int Margin { get; init; } = 16;

    /// <summary>How strongly the placeholder shows under the artist's drawing.</summary>
    public double PlaceholderOpacity { get; init; } = 0.4;

    /// <summary>A clip longer than this is refused before anything is allocated per frame (S1).</summary>
    public int MaxFrames { get; init; } = 5000;

    /// <summary>A canvas side above this is refused: it means a pivot far outside its cell.</summary>
    public int MaxCanvasSide { get; init; } = 8192;

    /// <summary>The cells a clip uses, all together, may hold no more pixels than this (S1).</summary>
    public long MaxCellPixels { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// A PNG the row reader does not handle is decoded whole by Skia, and only
    /// when it holds no more pixels than this (Q301).
    /// </summary>
    public long MaxWholeDecodePixels { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Decoded bytes a texture's rows may cost to reach its lowest needed cell.
    /// Memory is one row whatever this is; this bounds the CPU a small,
    /// highly compressible file can demand (1 GiB is a full 16 384² atlas,
    /// Unity's own maximum, at about three seconds).
    /// </summary>
    public long MaxDecodedBytes { get; init; } = 1L << 30;
}

/// <summary>A game clip as a document, with everything approximate said out loud.</summary>
public sealed record UnityClipImportResult(Doc Document, IReadOnlyList<string> Notes);

/// <summary>
/// Opens a sprite clip from a linked Unity project as a Lightbox document — step 2
/// of docs/DESIGN-unity-link.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Frames line up by pivot.</b> Cells on a hand-sliced atlas are all different
/// sizes, so placing them by their corners would make the character jump about.
/// Each cell is placed so its pivot lands on one canvas point — feet on one line,
/// the way the game plays them — and that point becomes the document's
/// <see cref="Scene.Pivot"/>. A cell's offset is a whole pixel, because a
/// fractional one would resample the art on the way in and again on the way out;
/// the record holds the offset actually used, which is what write-back inverts.
/// </para>
/// <para>
/// <b>The placeholder is reference, not art.</b> It lands on a locked, dimmed
/// layer pinned out of export (Q300), one per renderer the clip drives; the
/// artist draws on the empty layer above it, whose exposures match the
/// placeholder's so each drawing has an obvious sprite to replace. Soft edges
/// are not byte-exact there — the layer is stored premultiplied — and nothing
/// reads them back: write-back works from the file.
/// </para>
/// <para>
/// <b>An atlas is read a row at a time, and only as far down as its lowest
/// needed cell</b> (<see cref="PngRows"/>, Q301), keeping just the cells' spans:
/// one real character's atlas is 12 288 × 5 120, a quarter of a gigabyte decoded.
/// A PNG of a kind the row reader does not handle is decoded whole by Skia under
/// <see cref="UnityClipImportOptions.MaxWholeDecodePixels"/>; its frames open,
/// but carry no pixel hash, so write-back will not rewrite them.
/// </para>
/// <para>
/// <b>Every refusal comes before the cost it prevents.</b> The canvas and the
/// cells' total size are worked out from the slicing alone, in 64-bit arithmetic
/// with every pivot bounded, before a single pixel is decoded.
/// </para>
/// </remarks>
public static class UnityClipImport
{
    /// <exception cref="FormatException">The clip cannot be opened; the message says why.</exception>
    public static UnityClipImportResult Open(UnityProjectIndex index, UnityClipEntry entry, UnityClipImportOptions? options = null)
    {
        try
        {
            return OpenChecked(index, entry, options ?? new UnityClipImportOptions());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The texture was deleted, moved or locked since the scan.
            throw new FormatException($"{entry.RelativePath}: {e.Message}", e);
        }
    }

    private static UnityClipImportResult OpenChecked(UnityProjectIndex index, UnityClipEntry entry, UnityClipImportOptions options)
    {
        var clip = entry.Clip;
        if (clip.Tracks.Count == 0)
            throw new FormatException($"{entry.RelativePath}: the clip swaps no sprites.");

        var notes = new List<string>();
        var multi = clip.Tracks.Count > 1;

        // ---- timing ----------------------------------------------------------
        var sheets = clip.Tracks
            .Select(t => UnityClipTiming.Exposures(t, clip.SampleRate, clip.StopTime))
            .ToList();
        var frameCount = Math.Max(1, sheets.Max(s => s.FrameCount));
        if (frameCount > options.MaxFrames)
            throw new FormatException($"{entry.RelativePath}: {frameCount} frames is more than the {options.MaxFrames} a clip may open with.");
        for (var t = 0; t < sheets.Count; t++)
            foreach (var n in sheets[t].Notes)
                notes.Add(multi ? $"{TrackLabel(clip.Tracks[t])}: {n}" : n);

        var fps = (int)Math.Round(clip.SampleRate);
        if (Math.Abs(fps - clip.SampleRate) > 1e-6)
            notes.Add(FormattableString.Invariant($"the clip runs at {clip.SampleRate:0.###} fps; the document plays at {Math.Max(1, fps)}"));
        foreach (var s in clip.Skipped)
            notes.Add($"also in the clip, not shown here: {s}");
        if (multi)
            notes.Add("the clip drives more than one renderer; each is placed at the same pivot, because where a child renderer sits is in the prefab, not the clip");

        // ---- sprites ---------------------------------------------------------
        var refs = sheets.SelectMany(s => s.Exposures).Select(e => e.Sprite).OfType<UnitySpriteRef>().Distinct().ToList();
        var resolved = new Dictionary<UnitySpriteRef, UnitySpriteResolution>();
        foreach (var r in refs)
        {
            var res = index.Resolve(r);
            if (res.Resolved) resolved[r] = res;
            else notes.Add($"a frame's sprite ({res.TexturePath ?? r.ToString()}) could not be used: {res.Problem}; it shows blank");
        }
        if (resolved.Count == 0)
            throw new FormatException($"{entry.RelativePath}: none of the clip's sprites could be found.");

        // ---- canvas by pivot, from the slicing alone ------------------------------
        // Relative to the pivot, a cell spans [-px, w - px] across and [-py, h - py]
        // down; the canvas is the union of those, the pivot itself included, plus
        // the margin. Pivots are bounded before any arithmetic, and the arithmetic
        // is 64-bit, so no pivot in a .meta can wrap the bound it is checked against.
        var places = new Dictionary<UnitySpriteRef, (int PivotX, int PivotY)>();
        long minX = 0, maxX = 0, minY = 0, maxY = 0, cellPixels = 0;
        foreach (var (r, res) in resolved)
        {
            var s = res.Slice!;
            var px = BoundedPivot(s.PivotX * s.Width, s, options, entry);
            var py = BoundedPivot((1 - s.PivotY) * s.Height, s, options, entry);
            places[r] = (px, py);
            minX = Math.Min(minX, -px);
            maxX = Math.Max(maxX, (long)s.Width - px);
            minY = Math.Min(minY, -py);
            maxY = Math.Max(maxY, (long)s.Height - py);
            cellPixels += (long)s.Width * s.Height;
        }
        var width = maxX - minX + 2L * options.Margin;
        var height = maxY - minY + 2L * options.Margin;
        if (width <= 0 || height <= 0 || width > options.MaxCanvasSide || height > options.MaxCanvasSide)
            throw new FormatException($"{entry.RelativePath}: lining the frames up by pivot needs a {width}×{height} canvas, more than {options.MaxCanvasSide} a side — a pivot far outside its cell?");
        if (cellPixels > options.MaxCellPixels)
            throw new FormatException($"{entry.RelativePath}: the clip's cells hold {cellPixels:N0} pixels, more than the {options.MaxCellPixels:N0} a clip may open with.");
        var pivotX = (int)(-minX + options.Margin);
        var pivotY = (int)(-minY + options.Margin);

        // ---- pixels ------------------------------------------------------------------
        var cells = new Dictionary<UnitySpriteRef, Cell>();
        try
        {
            foreach (var group in resolved.GroupBy(kv => kv.Value.TexturePath!))
            {
                var full = UnityProjectScanner.Inside(index.Root, group.Key)
                    ?? throw new FormatException($"{group.Key}: the path leaves the project.");
                ReadCells(full, group.Key, group.ToList(), places, cells, options, notes);
            }

            // ---- the document ----------------------------------------------------
            var scene = new Scene
            {
                Name = string.IsNullOrWhiteSpace(clip.Name) ? Path.GetFileNameWithoutExtension(entry.RelativePath) : clip.Name,
                Width = (int)width,
                Height = (int)height,
                Fps = Math.Max(1, fps),
                FrameCount = frameCount,
                TransparentBackground = true,
                Pivot = new Pivot { X = pivotX, Y = pivotY },
            };
            var source = new GameSource { Clip = entry.RelativePath, CanvasWidth = (int)width, CanvasHeight = (int)height };
            var baselines = new Dictionary<UnitySpriteRef, string>();

            for (var t = 0; t < clip.Tracks.Count; t++)
            {
                var track = clip.Tracks[t];
                var placeholder = new Layer
                {
                    Name = multi ? $"Placeholder · {TrackLabel(track)}" : "Placeholder",
                    Kind = LayerKind.Painted,
                    Locked = true,
                    Opacity = options.PlaceholderOpacity,
                    OmitFromExport = true,
                };
                var record = new GameSourceTrack { Path = track.Path, ClassId = track.ClassId, PlaceholderLayerId = placeholder.Id };

                placeholder.Cels = Timeline(frameCount, sheets[t].Exposures, e =>
                {
                    if (e.Sprite is not { } r || !cells.TryGetValue(r, out var c)) return new Frame();
                    if (!baselines.TryGetValue(r, out var png))
                        baselines[r] = png = Baseline(c, pivotX - c.PivotX, pivotY - c.PivotY, (int)width, (int)height);
                    return new Frame { PngBase64 = png };
                });

                foreach (var e in sheets[t].Exposures)
                    record.Frames.Add(FrameRecord(e, resolved, cells, pivotX, pivotY));

                scene.Layers.Add(placeholder);
                source.Tracks.Add(record);
            }

            // The artist's layer, on top, with a drawing wherever the first
            // renderer shows a new sprite and holds in between.
            var drawing = new Layer { Name = "Drawing", Kind = LayerKind.Painted };
            drawing.Cels = Timeline(frameCount, sheets[0].Exposures, _ => new Frame());
            scene.Layers.Add(drawing);
            source.Tracks[0].DrawingLayerId = drawing.Id;

            var doc = new Doc
            {
                Scene = scene,
                Palettes = [DocumentFactory.DefaultPalette()],
                ActiveLayerId = drawing.Id,
                GameSource = source,
            };
            return new UnityClipImportResult(doc, notes);
        }
        finally
        {
            foreach (var c in cells.Values) c.Pixels.Dispose();
        }
    }

    private static string TrackLabel(UnitySpriteTrack t) => t.Path.Length == 0 ? "root" : t.Path;

    private static int BoundedPivot(double v, UnitySpriteSlice s, UnityClipImportOptions o, UnityClipEntry entry)
    {
        // A pivot more than a whole canvas outside its cell can only make a canvas
        // that is refused anyway; refusing it here keeps the arithmetic in range.
        if (!double.IsFinite(v) || Math.Abs(v) > (double)o.MaxCanvasSide + Math.Max(s.Width, s.Height))
            throw new FormatException($"{entry.RelativePath}: sprite '{s.Name}' has its pivot far outside its cell.");
        return PivotPixel(v);
    }

    /// <summary>
    /// One cel per timeline frame: a drawing where an exposure starts, a hold
    /// (<c>null</c>) while it lasts, an empty drawing before the first one, and
    /// an empty drawing where the last one ends if the document runs on — so a
    /// renderer whose keys stop early is not shown holding past its own end.
    /// </summary>
    private static List<Cel> Timeline(int frameCount, IReadOnlyList<UnityExposure> exposures, Func<UnityExposure, Frame> drawingFor)
    {
        var cels = new List<Cel>(frameCount);
        for (var f = 0; f < frameCount; f++) cels.Add(new Cel());
        cels[0].Frame = new Frame();
        foreach (var e in exposures)
            if (e.Start < frameCount) cels[e.Start].Frame = drawingFor(e);
        if (exposures.Count > 0)
        {
            var end = exposures[^1].Start + exposures[^1].Length;
            if (end < frameCount && cels[end].Frame is null) cels[end].Frame = new Frame();
        }
        return cels;
    }

    private static GameSourceFrame FrameRecord(UnityExposure e, Dictionary<UnitySpriteRef, UnitySpriteResolution> resolved,
        Dictionary<UnitySpriteRef, Cell> cells, int pivotX, int pivotY)
    {
        var record = new GameSourceFrame { Start = e.Start, Length = e.Length };
        if (e.Sprite is not { } r) return record;
        record.Texture = r.Guid;
        record.Sprite = r.FileId;
        if (!resolved.TryGetValue(r, out var res) || !cells.TryGetValue(r, out var c)) return record;
        var s = res.Slice!;
        record.Name = s.Name;
        record.TexturePath = res.TexturePath;
        record.Cell = [s.UnityX, s.Top, s.Width, s.Height];
        record.Pivot = [s.PivotX, s.PivotY];
        record.CanvasOffset = [pivotX - c.PivotX, pivotY - c.PivotY];
        record.SourceHash = c.Hash;
        return record;
    }

    /// <summary>
    /// A cell's pixels (unpremultiplied RGBA), its pivot in whole pixels from its
    /// top-left, and the hash of its stored bytes — null when the texture was not
    /// read byte-exact, which is what keeps write-back away from it.
    /// </summary>
    private sealed record Cell(SKBitmap Pixels, int PivotX, int PivotY, string? Hash)
    {
        public int Width => Pixels.Width;
        public int Height => Pixels.Height;
    }

    private static string Baseline(Cell c, int left, int top, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        // Unpremultiplied cell onto a premultiplied surface: Skia does the
        // multiply, as it does for a PSD layer. Whole-pixel offset, no filter.
        using var image = SKImage.FromBitmap(c.Pixels);
        canvas.DrawImage(image, left, top);
        canvas.Flush();
        return Lightbox.Raster.PngCodec.Encode(bitmap);
    }

    /// <summary>
    /// Reads one texture's cells into <paramref name="into"/>: row by row through
    /// <see cref="PngRows"/>, or — for a PNG it does not handle — one bounded
    /// whole decode. Bitmaps land in <paramref name="into"/> as they are made, so
    /// the caller's cleanup reaches every one of them, whatever fails.
    /// </summary>
    private static void ReadCells(string fullPath, string label, List<KeyValuePair<UnitySpriteRef, UnitySpriteResolution>> wanted,
        Dictionary<UnitySpriteRef, (int PivotX, int PivotY)> places, Dictionary<UnitySpriteRef, Cell> into,
        UnityClipImportOptions options, List<string> notes)
    {
        if (UnityProjectScanner.IsLink(fullPath))
            throw new FormatException($"{label}: the texture is now a link; links are not followed.");
        var texture = wanted[0].Value.Texture!;

        var targets = new List<(UnitySpriteRef Ref, UnitySpriteSlice Slice, SKBitmap Bitmap)>();
        foreach (var (r, res) in wanted)
        {
            var s = res.Slice!;
            var bmp = new SKBitmap(new SKImageInfo(s.Width, s.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            into[r] = new Cell(bmp, places[r].PivotX, places[r].PivotY, null);
            targets.Add((r, s, bmp));
        }
        var bottom = targets.Max(t => t.Slice.Top + t.Slice.Height);

        var exact = true;
        using (var stream = File.OpenRead(fullPath))
        {
            try
            {
                using var rows = PngRows.Open(stream, label);
                CheckSize(rows.Width, rows.Height, texture, label);
                if ((long)bottom * rows.Width * 4 > options.MaxDecodedBytes)
                    throw new FormatException($"{label}: reaching its lowest cell means decoding {(long)bottom * rows.Width * 4:N0} bytes, more than the {options.MaxDecodedBytes:N0} allowed.");
                var row = new byte[rows.Width * 4];
                for (var y = 0; y < bottom; y++)
                {
                    rows.ReadRow(row);
                    CopyRow(row, y, targets);
                }
                rows.VerifyChunk();
            }
            catch (PngUnsupportedException unsupported)
            {
                exact = false;
                notes.Add($"{unsupported.Message} Its frames open as reference, but write-back will not rewrite them.");
            }
        }

        if (!exact) WholeDecode(fullPath, label, texture, bottom, targets, options);

        foreach (var t in targets)
            if (exact) into[t.Ref] = into[t.Ref] with { Hash = Hash(t.Bitmap) };
    }

    private static void CheckSize(int w, int h, UnityTextureMeta texture, string label)
    {
        if (w != texture.TextureWidth || h != texture.TextureHeight)
            throw new FormatException($"{label}: the image is {w}×{h} but its slicing was read for {texture.TextureWidth}×{texture.TextureHeight}.");
    }

    private static void WholeDecode(string fullPath, string label, UnityTextureMeta texture, int bottom,
        List<(UnitySpriteRef Ref, UnitySpriteSlice Slice, SKBitmap Bitmap)> targets, UnityClipImportOptions options)
    {
        using var stream = File.OpenRead(fullPath);
        using var codec = SKCodec.Create(stream)
            ?? throw new FormatException($"{label}: not an image this build can read.");
        CheckSize(codec.Info.Width, codec.Info.Height, texture, label);
        if ((long)codec.Info.Width * codec.Info.Height > options.MaxWholeDecodePixels)
            throw new FormatException($"{label}: a {codec.Info.Width}×{codec.Info.Height} PNG of a kind that must be decoded whole is more than the {options.MaxWholeDecodePixels:N0} pixels allowed.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var whole = new SKBitmap(info);
        var result = codec.GetPixels(info, whole.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new FormatException($"{label}: could not be decoded ({result}).");
        var row = new byte[info.Width * 4];
        for (var y = 0; y < bottom; y++)
        {
            Marshal.Copy(whole.GetPixels() + y * whole.RowBytes, row, 0, row.Length);
            CopyRow(row, y, targets);
        }
    }

    /// <summary>
    /// A pivot as a whole pixel boundary, halves rounding <b>down</b>. A centred
    /// pivot on an odd-width cell sits on a pixel's middle (10.5 of 21); rounding
    /// it up would put that pixel left of the canvas pivot while an even-width
    /// cell's centre pixel sits right of it, and a run of mixed widths would
    /// shuffle a pixel left and right frame to frame. Down keeps them together.
    /// </summary>
    internal static int PivotPixel(double v) => (int)Math.Ceiling(v - 0.5 - 1e-9);

    private static void CopyRow(byte[] row, int y, List<(UnitySpriteRef Ref, UnitySpriteSlice Slice, SKBitmap Bitmap)> targets)
    {
        foreach (var t in targets)
        {
            var s = t.Slice;
            if (y < s.Top || y >= s.Top + s.Height) continue;
            Marshal.Copy(row, s.UnityX * 4, t.Bitmap.GetPixels() + (y - s.Top) * t.Bitmap.RowBytes, s.Width * 4);
        }
    }

    /// <summary>
    /// SHA-256 over the cell's RGBA rows — what write-back compares against the
    /// file before it writes, so it never overwrites pixels it did not read.
    /// </summary>
    internal static string Hash(SKBitmap cell)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var row = new byte[cell.Width * 4];
        for (var y = 0; y < cell.Height; y++)
        {
            Marshal.Copy(cell.GetPixels() + y * cell.RowBytes, row, 0, row.Length);
            sha.AppendData(row);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
