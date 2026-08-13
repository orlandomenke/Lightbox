using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Services;



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

    /// <summary>
    /// What to do about background layers.
    /// </summary>
    /// <remarks>
    /// <see cref="BackgroundHandling.PaperOnly"/> by default, which is what the
    /// exporter has always done — so an existing export is byte-identical, and the
    /// stronger detection is something an artist turns on rather than something that
    /// starts quietly removing layers from sheets that were fine yesterday.
    /// </remarks>
    public BackgroundHandling Background { get; init; } = BackgroundHandling.PaperOnly;
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
/// <summary>Which document a sheet frame came from, and which of its frames it is.</summary>
/// <remarks>
/// What lets an engine block ask a frame's <em>own</em> document for its
/// pivot, anchors and clock after several documents share one sheet. Null on
/// a single-document result, where the answer is always (0, i).
/// </remarks>
public readonly record struct FrameOwner(int Document, int Frame);

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
    long UsedArea = 0,
    IReadOnlyList<OmittedLayer>? Omitted = null,
    IReadOnlyList<SuspectedBackground>? Suspected = null,
    IReadOnlyList<FrameOwner>? FrameOwners = null)
{
    /// <summary>How much of the sheet is sprite, 0 to 1.</summary>
    public double Occupancy =>
        SheetWidth > 0 && SheetHeight > 0 ? UsedArea / (double)SheetWidth / SheetHeight : 0;

    /// <summary>
    /// Layers this export left out, with the reason for each.
    /// </summary>
    /// <remarks>
    /// Reported rather than silent, and that is the load-bearing part of background
    /// omission. The failure being designed against is not "the background got
    /// exported" — that one is visible in the sheet. It is "a layer the artist wanted
    /// is missing", which is invisible here and shows up in the engine, on a build,
    /// days later.
    /// </remarks>
    public IReadOnlyList<OmittedLayer> OmittedLayers => Omitted ?? [];

    /// <summary>Layers that were exported but might not have been meant to be.</summary>
    public IReadOnlyList<SuspectedBackground> SuspectedBackgrounds => Suspected ?? [];
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
    /// The document's tags, clamped to frames that exist.
    /// </summary>
    /// <remarks>
    /// Clamped rather than dropped: a tag whose end ran past the sheet after
    /// somebody shortened the animation still names a real range, and losing the
    /// clip entirely would be a worse answer than shortening it. A tag that starts
    /// beyond the end has nothing to name and is dropped.
    /// </remarks>
    private static List<SheetTag>? TagsFor(Scene scene)
    {
        if (scene.Tags is not { Count: > 0 } tags) return null;
        var last = Math.Max(0, Math.Max(1, scene.FrameCount) - 1);

        var written = new List<SheetTag>();
        foreach (var tag in tags)
        {
            var from = Math.Clamp(Math.Min(tag.Start, tag.End), 0, last);
            var to = Math.Clamp(Math.Max(tag.Start, tag.End), 0, last);
            if (Math.Min(tag.Start, tag.End) > last) continue;

            written.Add(new SheetTag
            {
                Name = string.IsNullOrWhiteSpace(tag.Name) ? tag.Id : tag.Name.Trim(),
                From = from,
                To = to,
                Direction = tag.Direction switch
                {
                    TagDirection.Reverse => "reverse",
                    TagDirection.PingPong => "pingpong",
                    _ => "forward",
                },
                Loop = tag.Loop,
            });
        }
        return written.Count > 0 ? written : null;
    }

    /// <summary>
    /// Markers the artist marked as engine events, and only those.
    /// </summary>
    /// <remarks>
    /// Opt-in, because most markers are notes to the animator — "contact", "check
    /// the hand" — and exporting those would fill an AnimationClip with callbacks
    /// nothing handles.
    /// </remarks>
    private static List<SheetEvent>? EventsFor(Scene scene)
    {
        var last = Math.Max(0, Math.Max(1, scene.FrameCount) - 1);
        var events = scene.Markers
            .Where(m => m.ExportsAsEvent && m.Frame >= 0 && m.Frame <= last)
            .OrderBy(m => m.Frame)
            .Select(m => new SheetEvent
            {
                Frame = m.Frame,
                Name = string.IsNullOrWhiteSpace(m.Label) ? "event" : m.Label.Trim(),
            })
            .ToList();
        return events.Count > 0 ? events : null;
    }

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

    /// <summary>
    /// This frame's collision rectangles, measured inside its cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list rather than a dictionary keyed by name, unlike <see cref="AnchorsFor"/>,
    /// and for two reasons: each entry carries a role an importer wants to filter
    /// by, and the declaration order is meaningful to a person reading the file.
    /// The list follows <see cref="Scene.Shapes"/> order, so the same document
    /// exports the same order every time.
    /// </para>
    /// <para>
    /// Inside the cell like the pivot and the anchors, so trimming cannot move a
    /// hurtbox. That is not cosmetic: a collider that shifts because a frame's ink
    /// happened to be tighter is a gameplay bug with no visible cause.
    /// </para>
    /// </remarks>
    private static List<SheetShape>? ShapesFor(Scene scene, int index, SKRectI cell)
    {
        if (scene.Shapes is not { Count: > 0 } declared) return null;
        var resolved = CollisionShapes.ResolvedAt(scene, index);
        if (resolved.Count == 0) return null;

        var written = new List<SheetShape>();
        foreach (var shape in declared)
        {
            if (!resolved.TryGetValue(shape.Id, out var box)) continue;
            written.Add(new SheetShape
            {
                Name = string.IsNullOrWhiteSpace(shape.Name) ? shape.Id : shape.Name.Trim(),
                Role = shape.Role switch
                {
                    ShapeRole.Hitbox => "hitbox",
                    ShapeRole.Physics => "physics",
                    _ => "hurtbox",
                },
                X = box.X - cell.Left,
                Y = box.Y - cell.Top,
                W = box.W,
                H = box.H,
            });
        }
        return written.Count > 0 ? written : null;
    }

    /// <summary>
    /// Several documents into one sheet — a scope declared
    /// <see cref="ExportGrouping.OneArtifact"/> made runnable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules are per document, because that is where each answer lives: a
    /// frame's duration comes from its own document's fps, its pivot from its
    /// own scene, background layers are decided per stack (a layer covering
    /// one document says nothing about another's), and trim unions within a
    /// document so each character holds still without being cropped by a
    /// neighbour. The one thing decided across all of them is the cell — the
    /// largest canvas wins, so nothing is cropped by whoever came first.
    /// </para>
    /// <para>
    /// Every document becomes a <c>frameTags</c> entry naming its frame range
    /// — without that, a three-cycle sheet is a wall of frames with nothing
    /// saying where the walk ends and the run begins. A document's own tags
    /// and events travel too, shifted to where its frames landed.
    /// </para>
    /// </remarks>
    public static SpriteSheetResult Export(
        IReadOnlyList<Doc> docs, string sheetPath, SpriteSheetOptions? options = null, IReadOnlyList<string>? names = null)
    {
        if (docs.Count == 0) throw new ArgumentException("An export needs at least one document.", nameof(docs));
        if (docs.Count == 1)
            return Export(docs[0], sheetPath, options);

        var opts = options ?? new SpriteSheetOptions();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sheetPath))!);

        // The composite canvas: the largest document decides the untrimmed
        // cell, and the sidecar reports the same extent the cell was measured
        // against.
        var wholeW = docs.Max(d => d.Scene.Width);
        var wholeH = docs.Max(d => d.Scene.Height);

        using var cache = new FrameBitmapCache();
        var frames = new List<SKImage>();
        try
        {
            var cells = new List<SKRectI>();
            var owners = new List<FrameOwner>();     // each frame's own document
            var starts = new List<int>();            // where each document begins
            var allOmitted = new List<OmittedLayer>();
            var allSuspected = new List<SuspectedBackground>();

            for (var d = 0; d < docs.Count; d++)
            {
                var scene = docs[d].Scene;
                var count = Math.Max(1, scene.FrameCount);
                starts.Add(frames.Count);

                // Layer decisions are per stack — one document's covering
                // layer must not silence another's art.
                var (omitted, suspected) = DecideLayers(scene, cache, opts.Background, count);
                allOmitted.AddRange(omitted);
                allSuspected.AddRange(suspected);
                var skip = omitted.Select(o => o.LayerId).ToHashSet(StringComparer.Ordinal);

                var ink = new List<SKRectI>(count);
                for (var i = 0; i < count; i++)
                {
                    var image = ComposeFrame(scene, cache, i, skip);
                    frames.Add(image);
                    ink.Add(InkBoundsOf(image));
                    owners.Add(new FrameOwner(d, i));
                }
                // Trim unions within the document, so its character holds
                // still; None takes the composite canvas, so a small document
                // is not the crop of a large one.
                cells.AddRange(opts.Trim == SpriteTrim.None
                    ? ink.Select(_ => new SKRectI(0, 0, wholeW, wholeH))
                    : CellsFor(opts.Trim, ink, scene));
            }

            var total = frames.Count;
            var cellW = Math.Max(1, cells.Max(c => c.Width));
            var cellH = Math.Max(1, cells.Max(c => c.Height));
            var stride = opts.Padding;

            var slots = new (int X, int Y, int W, int H)[total];
            int sheetW, sheetH, columns, rows;
            if (opts.Pack == SpritePack.Skyline)
            {
                var packed = SkylinePacker.Pack(
                    cells.Select(c => (c.Width, c.Height)).ToList(), stride);
                sheetW = packed.Width;
                sheetH = packed.Height;
                columns = 0;
                rows = 0;
                for (var i = 0; i < total; i++)
                {
                    var r = packed.Rects[i];
                    slots[i] = (r.X, r.Y, r.Width, r.Height);
                }
            }
            else
            {
                columns = opts.Columns is > 0
                    ? opts.Columns.Value
                    : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(total)));
                rows = (int)Math.Ceiling(total / (double)columns);
                sheetW = columns * (cellW + stride * 2);
                sheetH = rows * (cellH + stride * 2);
                for (var i = 0; i < total; i++)
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

            var entries = new List<SheetFrame>(total);
            for (var i = 0; i < total; i++)
            {
                var scene = docs[owners[i].Document].Scene;
                var local = owners[i].Frame;
                var cell = cells[i];
                var (x, y, w, h) = slots[i];

                surface.Canvas.Save();
                surface.Canvas.ClipRect(SKRect.Create(x, y, w, h));
                surface.Canvas.DrawImage(frames[i], x - cell.Left, y - cell.Top);
                surface.Canvas.Restore();

                var pivot = scene.Pivot;
                entries.Add(new SheetFrame
                {
                    Filename = $"{Path.GetFileNameWithoutExtension(sheetPath)} {i}.png",
                    Frame = new Box(x, y, w, h),
                    Rotated = false,
                    Trimmed = opts.Trim != SpriteTrim.None,
                    SpriteSourceSize = new Box(cell.Left, cell.Top, w, h),
                    // The composite extent, not the frame's own canvas — the
                    // same one the untrimmed cell was measured against, so an
                    // importer reconstructing the canvas is told one story.
                    SourceSize = new Size(wholeW, wholeH),
                    // Its own document's clock: one sheet can hold a 12 fps
                    // cycle and a 24 fps one, and a header number cannot.
                    Duration = (int)Math.Round(1000.0 / Math.Max(1, scene.Fps)),
                    PivotOffset = pivot is null
                        ? null
                        : new Point(pivot.X - cell.Left, pivot.Y - cell.Top),
                    Anchors = AnchorsFor(scene, local, cell),
                    Shapes = ShapesFor(scene, local, cell),
                });
            }

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)
                   ?? throw new InvalidOperationException("PNG encode failed."))
            using (var file = File.Create(sheetPath))
            {
                data.SaveTo(file);
            }

            // Every document is a clip: a tag naming its range, then its own
            // tags and events moved to where its frames landed.
            var tags = new List<SheetTag>();
            var events = new List<SheetEvent>();
            for (var d = 0; d < docs.Count; d++)
            {
                var scene = docs[d].Scene;
                var start = starts[d];
                var end = d + 1 < docs.Count ? starts[d + 1] - 1 : total - 1;
                tags.Add(new SheetTag
                {
                    Name = ClipName(scene, names, d),
                    From = start,
                    To = end,
                });
                foreach (var tag in TagsFor(scene) ?? [])
                {
                    tag.From += start;
                    tag.To += start;
                    tags.Add(tag);
                }
                foreach (var evt in EventsFor(scene) ?? [])
                {
                    evt.Frame += start;
                    events.Add(evt);
                }
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
                    Pack = opts.Pack == SpritePack.Skyline ? "skyline" : "grid",
                    // The first document's, for a reader that wants one number;
                    // the truth is per frame, where each document's own clock
                    // was written.
                    Fps = docs[0].Scene.Fps,
                    // No single pivot can speak for several documents — each
                    // frame carries its own.
                    Pivot = null,
                    FrameTags = tags,
                    Events = events.Count > 0 ? events : null,
                },
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(document, JsonOptions));

            return new SpriteSheetResult(
                sheetPath, metaPath, cellW, cellH, columns, rows, total,
                opts.Pack, sheetW, sheetH, entries.Sum(e => (long)e.Frame.W * e.Frame.H),
                allOmitted, allSuspected, owners);
        }
        finally
        {
            foreach (var frame in frames) frame.Dispose();
        }
    }

    /// <summary>What a document's clip tag is called.</summary>
    /// <remarks>
    /// The caller's name first — an export plan passes the project's document
    /// names, which are what the artist calls these — then the scene's own,
    /// then a stable fallback so no clip is nameless.
    /// </remarks>
    private static string ClipName(Scene scene, IReadOnlyList<string>? names, int index) =>
        names is not null && index < names.Count && !string.IsNullOrWhiteSpace(names[index])
            ? names[index].Trim()
        : !string.IsNullOrWhiteSpace(scene.Name) ? scene.Name.Trim()
        : $"clip {index + 1}";

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
            // Once for the whole export, before any frame is composed: a layer that
            // were a background on one frame and not the next is not a background.
            var (omitted, suspected) = DecideLayers(scene, cache, opts.Background, count);
            var skip = omitted.Select(o => o.LayerId).ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < count; i++)
            {
                var image = ComposeFrame(scene, cache, i, skip);
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
                    // Collision rectangles, in the cell's coordinates like
                    // everything else positional here. Absent when the document
                    // declares none, so an ordinary sheet is byte-identical to one
                    // exported before shapes existed.
                    Shapes = ShapesFor(scene, i, cell),
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
                    // Aseprite's own key and shape, because engine importers
                    // already read it. Absent when nothing is tagged.
                    FrameTags = TagsFor(scene),
                    Events = EventsFor(scene),
                },
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(document, JsonOptions));

            return new SpriteSheetResult(
                sheetPath, metaPath, cellW, cellH, columns, rows, count,
                opts.Pack, sheetW, sheetH, entries.Sum(e => (long)e.Frame.W * e.Frame.H),
                omitted, suspected);
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
    private static SKImage ComposeFrame(
        Scene scene, FrameBitmapCache cache, int index, HashSet<string> skipLayerIds)
    {
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (skipLayerIds.Contains(layer.Id)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, index);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                cache.Get(frame, scene.Width, scene.Height, celIndex: index), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode)));
        }
        return SceneRenderer.Compose(scene.Width, scene.Height, passes, SKColors.Transparent);
    }

    /// <summary>
    /// Which layers this export leaves out, and which kept ones are worth a word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decided once for the whole export rather than per frame, because a layer that
    /// is a background on frame 3 and not on frame 4 is not a background — it is an
    /// animation, and dropping it on some frames would be the worst possible answer.
    /// </para>
    /// <para>
    /// The measurement is arranged to be cheap in the case that dominates. Coverage is
    /// only computed under <see cref="BackgroundHandling.Detected"/>; it is computed
    /// per <em>drawing</em> so a hold is free; it stops at the first drawing that
    /// fails; and each drawing is first checked at five points — the corners and the
    /// centre — so an ordinary layer with a transparent corner costs five reads rather
    /// than a full scan. Invariant 6's spirit: an ordinary character sequence pays
    /// almost nothing for a feature aimed at the layer that covers everything.
    /// </para>
    /// </remarks>
    private static (List<OmittedLayer> Omitted, List<SuspectedBackground> Suspected) DecideLayers(
        Scene scene, FrameBitmapCache cache, BackgroundHandling mode, int frameCount)
    {
        var omitted = new List<OmittedLayer>();
        var suspected = new List<SuspectedBackground>();

        foreach (var layer in scene.Layers)
        {
            var visible = scene.IsLayerVisible(layer);

            // Only measured when the answer can matter — the mode has to be Detected,
            // the layer has to be visible, and it must not already be settled by the
            // artist's own pin or by being the paper layer.
            var needsCoverage =
                mode == BackgroundHandling.Detected
                && visible
                && !layer.IsOmittedFromExport
                && !layer.IsKeptInExport
                && !layer.IsBackground;

            var covers = needsCoverage && CoversCanvasThroughout(layer, scene, cache, frameCount);

            var signal = BackgroundRules.OmissionFor(layer, visible, mode, covers);
            if (signal is { } reason)
            {
                omitted.Add(new OmittedLayer(layer.Id, layer.Name, reason));
            }
            else if (BackgroundRules.SuspicionFor(layer, signal) is { } suspicion)
            {
                suspected.Add(suspicion);
            }
        }
        return (omitted, suspected);
    }

    /// <summary>
    /// Whether every drawing this layer shows fills the canvas.
    /// </summary>
    /// <remarks>
    /// <b>Every</b> drawing, not the first. A layer whose opening frame happens to be
    /// a full-bleed shape and which then animates is art, and requiring all of them
    /// is what keeps detection from eating it. A layer that shows nothing at all is
    /// not a background either — it is empty, and it costs nothing to export.
    /// </remarks>
    private static bool CoversCanvasThroughout(
        Layer layer, Scene scene, FrameBitmapCache cache, int frameCount)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var any = false;

        for (var i = 0; i < frameCount; i++)
        {
            if (ExposureSheet.ExposedFrame(layer, i) is not { } frame) continue;
            if (!seen.Add(frame.Id)) continue;   // a hold is the same drawing

            any = true;
            var image = cache.Get(frame, scene.Width, scene.Height, celIndex: i);
            if (!FillsCanvas(image)) return false;
        }
        return any;
    }

    /// <summary>
    /// Whether an image covers at least <see cref="BackgroundRules.FullCanvasCoverage"/>
    /// of itself.
    /// </summary>
    private static bool FillsCanvas(SKBitmap source)
    {
        using var pixels = source.PeekPixels();
        if (pixels is null) return false;

        var span = pixels.GetPixelSpan();
        var stride = pixels.RowBytes;
        var bytesPerPixel = pixels.BytesPerPixel;
        // Alpha is the last byte of the pixel for both Rgba8888 and Bgra8888, which is
        // every colour type the cache produces. Anything else is not something to
        // guess at.
        if (bytesPerPixel != 4) return false;

        int w = source.Width, h = source.Height;
        if (w <= 0 || h <= 0) return false;

        // Five reads before any scan. A drawing is overwhelmingly likely not to be a
        // full-canvas fill, and one transparent corner settles it.
        if (span[0 * stride + 0 * 4 + 3] < InkAlpha
            || span[0 * stride + (w - 1) * 4 + 3] < InkAlpha
            || span[(h - 1) * stride + 0 * 4 + 3] < InkAlpha
            || span[(h - 1) * stride + (w - 1) * 4 + 3] < InkAlpha
            || span[h / 2 * stride + w / 2 * 4 + 3] < InkAlpha)
        {
            return false;
        }

        // The corners passed, so count properly. The threshold is a fraction rather
        // than "no transparent pixel" because a flood fill on an antialiased canvas
        // leaves a handful of soft pixels at the edges.
        var needed = (long)Math.Ceiling(BackgroundRules.FullCanvasCoverage * w * h);
        var allowed = (long)w * h - needed;
        var missing = 0L;

        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            for (var x = 0; x < w; x++)
            {
                if (span[row + x * 4 + 3] >= InkAlpha) continue;
                if (++missing > allowed) return false;
            }
        }
        return true;
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

        /// <summary>
        /// Lightbox extension: collision rectangles on this cell.
        /// </summary>
        /// <remarks>
        /// A list, not a map, because each entry carries a role and an importer's
        /// first move is to filter by it.
        /// </remarks>
        [JsonPropertyName("shapes")] public List<SheetShape>? Shapes { get; set; }
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

        /// <summary>
        /// Named frame ranges, in Aseprite's <c>frameTags</c> shape.
        /// </summary>
        /// <remarks>
        /// Matching the established key rather than inventing one: every engine
        /// importer that reads a sprite-sheet sidecar already looks here, and an
        /// animation clip *is* a named frame range.
        /// </remarks>
        [JsonPropertyName("frameTags")] public List<SheetTag>? FrameTags { get; set; }

        /// <summary>Lightbox extension: markers the artist marked as engine events.</summary>
        [JsonPropertyName("events")] public List<SheetEvent>? Events { get; set; }
    }

    private sealed class SheetTag
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        [JsonPropertyName("from")] public int From { get; set; }

        [JsonPropertyName("to")] public int To { get; set; }

        [JsonPropertyName("direction")] public string Direction { get; set; } = "forward";

        /// <summary>Lightbox extension: Aseprite has no loop flag and engines need one.</summary>
        [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    }

    private sealed class SheetEvent
    {
        [JsonPropertyName("frame")] public int Frame { get; set; }

        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    /// <summary>
    /// Lightbox extension: one collision rectangle, in its cell's coordinates.
    /// </summary>
    /// <remarks>
    /// Pixels rather than anything normalised, and the cell's own origin rather
    /// than the canvas's — the same convention the pivot and the anchors use, so
    /// an importer that has already worked out one has worked out all three.
    /// Per-engine figures (pivot-relative offsets, world units) are computed in
    /// the engine block that needs them, not here, because two engines want two
    /// different conversions of the same rectangle.
    /// </remarks>
    private sealed class SheetShape
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        /// <summary>"hurtbox", "hitbox" or "physics".</summary>
        [JsonPropertyName("role")] public string Role { get; set; } = "hurtbox";

        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
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
