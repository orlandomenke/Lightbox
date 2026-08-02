using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
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

public sealed record SpriteSheetOptions
{
    public SpriteTrim Trim { get; init; } = SpriteTrim.Union;

    /// <summary>Columns in the grid; null picks a near-square layout.</summary>
    public int? Columns { get; init; }

    /// <summary>Transparent gutter around each cell, to stop bilinear bleed in an engine.</summary>
    public int Padding { get; init; }
}

public sealed record SpriteSheetResult(
    string SheetPath,
    string MetadataPath,
    int CellWidth,
    int CellHeight,
    int Columns,
    int Rows,
    int FrameCount);

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

            var columns = opts.Columns is > 0
                ? opts.Columns.Value
                : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            var rows = (int)Math.Ceiling(count / (double)columns);

            var stride = opts.Padding;
            var sheetW = columns * (cellW + stride * 2);
            var sheetH = rows * (cellH + stride * 2);

            var info = new SKImageInfo(sheetW, sheetH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info)
                ?? throw new InvalidOperationException("Could not create sprite sheet surface.");
            surface.Canvas.Clear(SKColors.Transparent);

            var entries = new List<SheetFrame>(count);
            var pivot = scene.Pivot;
            for (var i = 0; i < count; i++)
            {
                var cell = cells[i];
                var col = i % columns;
                var row = i / columns;
                var x = col * (cellW + stride * 2) + stride;
                var y = row * (cellH + stride * 2) + stride;

                // Draw the cell's slice of the frame at the cell's origin.
                surface.Canvas.Save();
                surface.Canvas.ClipRect(SKRect.Create(x, y, cellW, cellH));
                surface.Canvas.DrawImage(frames[i], x - cell.Left, y - cell.Top);
                surface.Canvas.Restore();

                entries.Add(new SheetFrame
                {
                    Filename = $"{Path.GetFileNameWithoutExtension(sheetPath)} {i}.png",
                    Frame = new Box(x, y, cellW, cellH),
                    Rotated = false,
                    Trimmed = opts.Trim != SpriteTrim.None,
                    // Aseprite's spriteSourceSize is where the trimmed cell sat
                    // in the untrimmed canvas, which is exactly the offset an
                    // importer needs to put the drawing back.
                    SpriteSourceSize = new Box(cell.Left, cell.Top, cellW, cellH),
                    SourceSize = new Size(scene.Width, scene.Height),
                    Duration = (int)Math.Round(1000.0 / Math.Max(1, scene.Fps)),
                    // The offset that actually matters to an engine: where the
                    // pivot sits inside this cell. Measured from the pivot, so
                    // trimming cannot move the character.
                    PivotOffset = pivot is null
                        ? null
                        : new Point(pivot.X - cell.Left, pivot.Y - cell.Top),
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
                    Fps = scene.Fps,
                    Pivot = pivot is null ? null : new Point(pivot.X, pivot.Y),
                },
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(document, JsonOptions));

            return new SpriteSheetResult(sheetPath, metaPath, cellW, cellH, columns, rows, count);
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
                cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity,
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
