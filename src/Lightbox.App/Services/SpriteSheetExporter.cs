using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>How tightly each exported cell hugs the drawing.</summary>
public enum SpriteTrim
{
    /// <summary>Every cell is the full canvas. Simple, and often wasteful.</summary>
    None,

    /// <summary>
    /// One rectangle for the whole sequence: the union of every frame's ink.
    ///
    /// This is the default, and the reason is that the obvious alternative is
    /// wrong. Trimming each frame to its own tight box makes the trim follow
    /// the drawing rather than the rig, so the character <em>jitters</em> in
    /// the engine — every frame a different size, every frame a different
    /// offset, and nothing moved that the animator moved.
    /// </summary>
    Union,

    /// <summary>
    /// Each frame trimmed to its own ink, with the offset recorded so an
    /// importer can put it back. Tighter, and only safe because the offsets
    /// are measured from the pivot.
    /// </summary>
    PerFrame,
}

/// <summary>How the cells are arranged on the sheet.</summary>
public enum SpritePack
{
    /// <summary>
    /// A uniform grid. The default, and it stays the default.
    /// </summary>
    /// <remarks>
    /// Equal cells are what union bounds produce anyway, and every engine
    /// importer in existence reads a grid — including ones that ignore the
    /// sidecar entirely.
    /// </remarks>
    Grid,

    /// <summary>
    /// Bottom-left skyline packing: each sprite at its own size, tighter.
    /// </summary>
    /// <remarks>
    /// Worth reaching for when the frames are <b>ragged</b> — per-frame trimming,
    /// or a sheet holding several animations. It is only usable with the
    /// per-sprite rects in the sidecar, so an importer that reads
    /// <c>meta.columns</c> and divides will get this wrong; that is why a packed
    /// sheet reports <c>columns</c> and <c>rows</c> as zero rather than a number
    /// that would look plausible and be false.
    /// </remarks>
    Skyline,
}

public sealed record SpriteSheetOptions
{
    public SpriteTrim Trim { get; init; } = SpriteTrim.Union;

    /// <summary>Columns in the grid; null picks a near-square layout.</summary>
    public int? Columns { get; init; }

    /// <summary>Transparent gutter around each cell, to stop bilinear bleed in an engine.</summary>
    public int Padding { get; init; }

    /// <summary>
    /// Grid or skyline. Grid by default, so an existing export is byte-identical.
    /// </summary>
    public SpritePack Pack { get; init; } = SpritePack.Grid;
}

/// <param name="CellWidth">
/// The widest cell. Under <see cref="SpritePack.Skyline"/> cells differ, so this
/// is the maximum rather than the size of every one — read the sidecar's
/// per-frame rects for the truth.
/// </param>
/// <param name="Columns">Grid columns, or 0 for a packed sheet, which has no grid.</param>
/// <param name="SheetWidth">The image's own size, which a packed sheet does not imply.</param>
/// <param name="UsedArea">
/// Pixels the sprites occupy, against <paramref name="SheetWidth"/> ×
/// <paramref name="SheetHeight"/>. Reported so the packer's win can be *measured*
/// rather than claimed — "atlas optimisation" with no number attached is a
/// feeling.
/// </param>
public sealed record SpriteSheetResult(
    string SheetPath,
    string MetadataPath,
    int CellWidth,
    int CellHeight,
    int Columns,
    int Rows,
    int FrameCount,
    SpritePack Pack = SpritePack.Grid,
    int SheetWidth = 0,
    int SheetHeight = 0,
    long UsedArea = 0)
{
    /// <summary>How much of the sheet is sprite, 0 to 1.</summary>
    public double Occupancy =>
        SheetWidth > 0 && SheetHeight > 0 ? UsedArea / (double)SheetWidth / SheetHeight : 0;
}

/// <summary>
/// The asset target's export: one image holding every frame on a uniform
/// grid, plus a metadata sidecar.
///
/// A uniform grid first, and rect packing later if it is ever wanted. The grid
/// composes naturally with union bounds — equal cells are what union bounds
/// produce anyway — and every engine importer in existence reads it. A tighter
/// skyline or MaxRects pack needs per-sprite metadata to be usable at all, and
/// can be added without changing this path.
///
/// The sidecar is Aseprite's JSON shape rather than an invented one. Engine
/// importers and asset pipelines already read it, and matching it costs
/// nothing.
/// </summary>
public static class SpriteSheetExporter
{
    /// <summary>Below this a pixel is not ink. Antialiased edges sit well above it.</summary>
    private const byte InkAlpha = 8;

    /// <summary>
    /// This frame's anchors, named and measured inside its cell.
    /// </summary>
    /// <remarks>
    /// Two names for one anchor would make the sidecar ambiguous, so a duplicate
    /// takes the id as a suffix rather than overwriting silently — the artist gets
    /// a usable file and a visible oddity instead of one socket quietly missing.
    /// </remarks>
    private static Dictionary<string, Point>? AnchorsFor(Scene scene, int index, SKRectI cell)
    {
        if (scene.Anchors is not { Count: > 0 } declared) return null;
        var resolved = Anchors.ResolvedAt(scene, index);
        if (resolved.Count == 0) return null;

        var byName = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var anchor in declared)
        {
            if (!resolved.TryGetValue(anchor.Id, out var point)) continue;
            var name = string.IsNullOrWhiteSpace(anchor.Name) ? anchor.Id : anchor.Name.Trim();
            if (!byName.TryAdd(name, new Point(point.X - cell.Left, point.Y - cell.Top)))
            {
                byName[$"{name} ({anchor.Id})"] = new Point(point.X - cell.Left, point.Y - cell.Top);
            }
        }
        return byName.Count > 0 ? byName : null;
    }

    public static SpriteSheetResult Export(Doc doc, string sheetPath, SpriteSheetOptions? options = null)
    {
        var opts = options ?? new SpriteSheetOptions();
        var scene = doc.Scene;
        var count = Math.Max(1, scene.FrameCount);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sheetPath))!);

        using var cache = new FrameBitmapCache();
        var frames = new List<SKImage>(count);
        var inkBounds = new List<SKRectI>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var image = ComposeFrame(scene, cache, i);
                frames.Add(image);
                inkBounds.Add(InkBoundsOf(image));
            }

            var cells = CellsFor(opts.Trim, inkBounds, scene);
            var cellW = Math.Max(1, cells.Max(c => c.Width));
            var cellH = Math.Max(1, cells.Max(c => c.Height));

            var stride = opts.Padding;
            int columns, rows, sheetW, sheetH;

            // One placement list for both layouts, so everything downstream — the
            // draw loop, the sidecar, the pivot arithmetic — is written once and
            // cannot drift between the two modes.
            var slots = new (int X, int Y, int W, int H)[count];

            if (opts.Pack == SpritePack.Skyline)
            {
                // Each sprite at its own size, which is where per-frame trimming
                // finally pays: the grid takes the widest by the tallest for every
                // cell whatever the trim said.
                var packed = SkylinePacker.Pack(
                    cells.Select(c => (c.Width, c.Height)).ToList(), stride);
                sheetW = packed.Width;
                sheetH = packed.Height;
                // A packed sheet has no grid, and reporting a plausible number
                // here would be worse than reporting none.
                columns = 0;
                rows = 0;
                for (var i = 0; i < count; i++)
                {
                    var r = packed.Rects[i];
                    slots[i] = (r.X, r.Y, r.Width, r.Height);
                }
            }
            else
            {
                columns = opts.Columns is > 0
                    ? opts.Columns.Value
                    : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
                rows = (int)Math.Ceiling(count / (double)columns);
                sheetW = columns * (cellW + stride * 2);
                sheetH = rows * (cellH + stride * 2);
                for (var i = 0; i < count; i++)
                {
                    slots[i] = (
                        i % columns * (cellW + stride * 2) + stride,
                        i / columns * (cellH + stride * 2) + stride,
                        cellW,
                        cellH);
                }
            }

            var info = new SKImageInfo(sheetW, sheetH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info)
                ?? throw new InvalidOperationException("Could not create sprite sheet surface.");
            surface.Canvas.Clear(SKColors.Transparent);

            var entries = new List<SheetFrame>(count);
            var pivot = scene.Pivot;
            for (var i = 0; i < count; i++)
            {
                var cell = cells[i];
                var (x, y, w, h) = slots[i];

                // Draw the cell's slice of the frame at the cell's origin.
                surface.Canvas.Save();
                surface.Canvas.ClipRect(SKRect.Create(x, y, w, h));
                surface.Canvas.DrawImage(frames[i], x - cell.Left, y - cell.Top);
                surface.Canvas.Restore();

                entries.Add(new SheetFrame
                {
                    Filename = $"{Path.GetFileNameWithoutExtension(sheetPath)} {i}.png",
                    // The sprite's real rect. Under Skyline this is the only way
                    // to find it — there is no grid to divide.
                    Frame = new Box(x, y, w, h),
                    Rotated = false,
                    Trimmed = opts.Trim != SpriteTrim.None,
                    // Aseprite's spriteSourceSize is where the trimmed cell sat
                    // in the untrimmed canvas, which is exactly the offset an
                    // importer needs to put the drawing back.
                    SpriteSourceSize = new Box(cell.Left, cell.Top, w, h),
                    SourceSize = new Size(scene.Width, scene.Height),
                    Duration = (int)Math.Round(1000.0 / Math.Max(1, scene.Fps)),
                    // The offset that actually matters to an engine: where the
                    // pivot sits inside this cell. Measured from the pivot, so
                    // trimming cannot move the character.
                    PivotOffset = pivot is null
                        ? null
                        : new Point(pivot.X - cell.Left, pivot.Y - cell.Top),
                    // Named anchors, measured inside the cell like the pivot and
                    // for the same reason: trimming must not be able to move
                    // where a weapon attaches. Absent, not empty, when the
                    // document declares none.
                    Anchors = AnchorsFor(scene, i, cell),
                });
            }

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                   ?? throw new InvalidOperationException("PNG encode failed."))
            using (var file = File.Create(sheetPath))
            {
                data.SaveTo(file);
            }

            var metaPath = Path.ChangeExtension(sheetPath, ".json");
            var document = new SheetDocument
            {
                Frames = entries,
                Meta = new SheetMeta
                {
                    App = "Lightbox",
                    Image = Path.GetFileName(sheetPath),
                    Format = "RGBA8888",
                    Size = new Size(sheetW, sheetH),
                    Scale = "1",
                    Columns = columns,
                    Rows = rows,
                    // Named in the file, so an importer can tell that dividing by
                    // columns is not going to work rather than discovering it.
                    Pack = opts.Pack == SpritePack.Skyline ? "skyline" : "grid",
                    Fps = scene.Fps,
                    Pivot = pivot is null ? null : new Point(pivot.X, pivot.Y),
                },
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(document, JsonOptions));

            return new SpriteSheetResult(
                sheetPath, metaPath, cellW, cellH, columns, rows, count,
                opts.Pack, sheetW, sheetH, entries.Sum(e => (long)e.Frame.W * e.Frame.H));
        }
        finally
        {
            foreach (var frame in frames) frame.Dispose();
        }
    }

    /// <summary>
    /// The rectangle each frame contributes to the sheet.
    ///
    /// Union and None both give every frame the same rectangle, which is what
    /// keeps the character still. PerFrame gives each its own and relies on
    /// the recorded offsets.
    /// </summary>
    private static List<SKRectI> CellsFor(SpriteTrim trim, List<SKRectI> ink, Scene scene)
    {
        var whole = new SKRectI(0, 0, scene.Width, scene.Height);
        switch (trim)
        {
            case SpriteTrim.None:
                return ink.Select(_ => whole).ToList();

            case SpriteTrim.PerFrame:
                // An empty frame still needs a cell, and it must be the same
                // size as the others or the grid stops being a grid.
                return ink.Select(r => r.IsEmpty ? new SKRectI(0, 0, 1, 1) : r).ToList();

            default:
                var union = SKRectI.Empty;
                foreach (var r in ink)
                {
                    if (r.IsEmpty) continue;
                    union = union.IsEmpty ? r : Union(union, r);
                }
                if (union.IsEmpty) union = whole;
                return ink.Select(_ => union).ToList();
        }
    }

    private static SKRectI Union(SKRectI a, SKRectI b) => new(
        Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top),
        Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    /// <summary>
    /// Composite one frame onto transparency, skipping the Background layer.
    ///
    /// Trimming has to see the drawing, not the paper. An opaque background
    /// layer would make every frame's ink bounds the whole canvas and turn
    /// trimming into a no-op that looks like it worked.
    /// </summary>
    private static SKImage ComposeFrame(Scene scene, FrameBitmapCache cache, int index)
    {
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (layer.IsBackground || !scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, index);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                cache.Get(frame, scene.Width, scene.Height, celIndex: index), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode)));
        }
        return SceneRenderer.Compose(scene.Width, scene.Height, passes, SKColors.Transparent);
    }

    /// <summary>The tight box around everything with alpha in an image; empty when blank.</summary>
    internal static SKRectI InkBoundsOf(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) return SKRectI.Empty;

        using var pixels = bitmap.PeekPixels();
        var span = pixels.GetPixelSpan();
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
        {
            var row = y * pixels.RowBytes;
            for (var x = 0; x < image.Width; x++)
            {
                if (span[row + x * 4 + 3] < InkAlpha) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- the sidecar, in Aseprite's shape -------------------------------------

    private sealed class SheetDocument
    {
        [JsonPropertyName("frames")] public List<SheetFrame> Frames { get; set; } = [];
        [JsonPropertyName("meta")] public SheetMeta Meta { get; set; } = new();
    }

    private sealed class SheetFrame
    {
        [JsonPropertyName("filename")] public string Filename { get; set; } = "";
        [JsonPropertyName("frame")] public Box Frame { get; set; } = new(0, 0, 0, 0);
        [JsonPropertyName("rotated")] public bool Rotated { get; set; }
        [JsonPropertyName("trimmed")] public bool Trimmed { get; set; }
        [JsonPropertyName("spriteSourceSize")] public Box SpriteSourceSize { get; set; } = new(0, 0, 0, 0);
        [JsonPropertyName("sourceSize")] public Size SourceSize { get; set; } = new(0, 0);
        [JsonPropertyName("duration")] public int Duration { get; set; }

        /// <summary>Lightbox extension: the pivot's position within this cell.</summary>
        [JsonPropertyName("pivot")] public Point? PivotOffset { get; set; }

        /// <summary>
        /// Lightbox extension: named anchors on this cell, by name.
        /// </summary>
        /// <remarks>
        /// Keyed by <em>name</em> rather than by id, because the consumer is an
        /// engine importer and "leftHand" is what a script will look for. Ids are
        /// how the document keeps them straight; names are the contract.
        /// </remarks>
        [JsonPropertyName("anchors")] public Dictionary<string, Point>? Anchors { get; set; }
    }

    private sealed class SheetMeta
    {
        [JsonPropertyName("app")] public string App { get; set; } = "";
        [JsonPropertyName("image")] public string Image { get; set; } = "";
        [JsonPropertyName("format")] public string Format { get; set; } = "";
        [JsonPropertyName("size")] public Size Size { get; set; } = new(0, 0);
        [JsonPropertyName("scale")] public string Scale { get; set; } = "1";

        /// <summary>Lightbox extensions: what a grid importer needs and Aseprite does not record.</summary>
        [JsonPropertyName("columns")] public int Columns { get; set; }
        [JsonPropertyName("rows")] public int Rows { get; set; }

        /// <summary>"grid" or "skyline". A packed sheet has no grid to divide.</summary>
        [JsonPropertyName("pack")] public string Pack { get; set; } = "grid";
        [JsonPropertyName("fps")] public int Fps { get; set; }
        [JsonPropertyName("pivot")] public Point? Pivot { get; set; }
    }

    private sealed record Box(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("w")] int W,
        [property: JsonPropertyName("h")] int H);

    private sealed record Size(
        [property: JsonPropertyName("w")] int W,
        [property: JsonPropertyName("h")] int H);

    private sealed record Point(
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y);
}
