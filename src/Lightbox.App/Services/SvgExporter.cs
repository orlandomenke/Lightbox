using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>How one layer went into the SVG.</summary>
public enum SvgLayerForm
{
    /// <summary>Every mark on it is an outline, and the file holds those outlines.</summary>
    Paths,

    /// <summary>It holds something that is not an outline, so the file holds its pixels.</summary>
    Pixels,
}

public sealed record SvgLayerReport(string Name, SvgLayerForm Form);

/// <summary>
/// What an SVG save did with each layer — the half of Q220's answer that says
/// "and reported".
/// </summary>
/// <param name="Layers">Bottom to top, one entry per layer that wrote anything.</param>
/// <param name="FlattenedBecause">
/// Why the whole picture went in as one image, or null when it went in layer
/// by layer. Written to finish the sentence "…because the document has ___".
/// </param>
public sealed record SvgReport(IReadOnlyList<SvgLayerReport> Layers, string? FlattenedBecause)
{
    public int PathLayers => Layers.Count(l => l.Form == SvgLayerForm.Paths);

    public int PixelLayers => Layers.Count(l => l.Form == SvgLayerForm.Pixels);

    public bool Flattened => FlattenedBecause is not null;

    /// <summary>
    /// The one sentence for the artist, or null when every layer is paths and
    /// there is nothing to say.
    /// </summary>
    public string? Notice
    {
        get
        {
            if (FlattenedBecause is { } why)
            {
                return $"The whole picture is saved as pixels inside the SVG, because the document has {why}.";
            }
            if (PixelLayers == 0) return null;
            var names = string.Join(", ", Layers.Where(l => l.Form == SvgLayerForm.Pixels).Select(l => l.Name));
            var count = PixelLayers == 1 ? "1 of" : $"{PixelLayers} of";
            var total = Layers.Count == 1 ? "1 layer is" : $"{Layers.Count} layers are";
            return $"{count} {total} saved as pixels inside the SVG ({names}): "
                + "they hold marks that are not outlines.";
        }
    }
}

public sealed record SvgDocument(string Xml, SvgReport Report);

/// <summary>
/// The drawing as SVG: paths where the marks are geometry, pixels where they
/// are not, and a report of which (Q220).
/// </summary>
/// <remarks>
/// <para>
/// <b>A layer is one or the other, never a mixture.</b> Marks on a layer act on
/// one another — an eraser takes a bite out of the fill before it, a smudge
/// drags it — so a path for the fill beside a bitmap for the eraser is a
/// picture of a drawing nobody made. A layer whose every mark is an outline is
/// written as outlines; any other layer is written as its pixels.
/// </para>
/// <para>
/// <b>What counts as an outline is two things the engine already says.</b> A
/// fill or a piece of set type is contours (<see cref="ToolKinds.FillsAContour"/>).
/// A brush stroke is an outline when <see cref="BrushEngine.DrawsAsOneSilhouette"/>
/// says the engine itself draws it as one — the hard round family — and then
/// the outline is the union of its dabs, each at the radius pressure gave it.
/// An outline, not a centreline with a width: a width could not carry the
/// pressure that thins one end of the line.
/// </para>
/// <para>
/// <b>And then it is measured, which is the part that makes it honest.</b> The
/// paths for a layer are drawn back and compared with the layer as the
/// application renders it; if they are not the same picture, the layer goes in
/// as pixels. So this never has to keep a list of every brush option, medium
/// and frame-level feature that would make an outline untrue — imported pixels
/// under the strokes, a pose, a symbol, an option added next year. Anything the
/// paths do not account for shows up as a difference, and a difference means
/// pixels. It costs one extra render per layer, on a save.
/// </para>
/// <para>
/// <b>Some documents cannot be said layer by layer at all</b> — a camera, a
/// mask or a clipped layer, an effect, an adjustment layer, a scene grade,
/// production footage. Those go in as one image of what export composes, and
/// the report says why.
/// </para>
/// <para>
/// The file follows the W3C's SVG 1.1 (paths, groups, images, <c>fill-rule</c>)
/// and Compositing and Blending Level 1 (<c>mix-blend-mode</c>). Those two
/// specifications are the source of every element and keyword written here.
/// </para>
/// </remarks>
public static class SvgExporter
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    /// <summary>The largest size the save dialog offers, held here too so every caller inherits it.</summary>
    private const double MaxScale = 16;

    /// <summary>
    /// What a save would do with each layer, without doing it: nothing is
    /// encoded and no file is built. This is what the dialog asks before the
    /// artist has chosen to save at all.
    /// </summary>
    public static SvgReport Survey(Doc doc, int frameIndex = 0) =>
        Compose(doc, frameIndex, 1.0, write: false).Report;

    public static SvgDocument Build(Doc doc, int frameIndex = 0, double scale = 1.0) =>
        Compose(doc, frameIndex, scale, write: true);

    private static SvgDocument Compose(Doc doc, int frameIndex, double scale, bool write)
    {
        var scene = doc.Scene;
        frameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, scene.FrameCount - 1));
        scale = scale > 0 && double.IsFinite(scale) ? Math.Min(scale, MaxScale) : 1.0;

        using var cache = new FrameBitmapCache();
        cache.Rig = RigIndex.For(doc);
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);

        var (width, height) = SequenceExporter.OutputSize(scene);
        var root = new XElement(Ns + "svg",
            new XAttribute("width", N(Math.Max(1, Math.Round(width * scale)))),
            new XAttribute("height", N(Math.Max(1, Math.Round(height * scale)))),
            new XAttribute("viewBox", $"0 0 {N(width)} {N(height)}"));

        SvgReport report;
        if (WhyNotLayerByLayer(scene, frameIndex) is { } why)
        {
            if (write)
            {
                using var image = SequenceExporter.RenderFrame(doc, cache, frameIndex, scale);
                root.Add(Image(image, width, height));
            }
            report = new SvgReport([], why);
        }
        else
        {
            report = new SvgReport(Layers(scene, cache, frameIndex, scale, root, write), null);
        }

        var xml = write
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root.ToString(SaveOptions.None) + "\n"
            : "";
        return new SvgDocument(xml, report);
    }

    /// <summary>
    /// The first thing about this frame that only a composite can show, as the
    /// end of "…because the document has ___", or null.
    /// </summary>
    private static string? WhyNotLayerByLayer(Scene scene, int frameIndex)
    {
        if (scene.Camera is not null) return "a camera, and the picture is what the camera saw";
        if (EffectPasses.SceneStackPass(scene, frameIndex) is not null) return "an effect on the whole scene";
        if (scene.References is { } strips && strips.Exists(s => s.RendersInExport && s.Visible))
        {
            return "footage that is part of the picture";
        }

        for (var i = 0; i < scene.Layers.Count; i++)
        {
            var layer = scene.Layers[i];
            if (!scene.IsLayerVisible(layer)) continue;
            if (layer.IsAdjustment) return "an adjustment layer";
            if (LayerShapes.For(scene, i, frameIndex) is not null) return "a mask or a clipped layer";
            // Asked of the record, not of what the stack resolves to this
            // frame: a live effect that happens to filter nothing here is still
            // not something to promise paths for.
            if (layer.HasLiveEffects) return "a layer effect";
        }
        return null;
    }

    private static List<SvgLayerReport> Layers(
        Scene scene, FrameBitmapCache cache, int frameIndex, double scale, XElement root, bool write)
    {
        // What SceneRenderer.BackgroundOf clears to, said as a shape: a document
        // from before background layers keeps its paper on the scene.
        if (!scene.TransparentBackground && !scene.Layers.Exists(l => l.IsBackground))
        {
            root.Add(new XElement(Ns + "rect",
                new XAttribute("width", N(scene.Width)),
                new XAttribute("height", N(scene.Height)),
                new XAttribute("fill", Hex(BrushEngine.ParseColor(scene.BackgroundColor)))));
        }

        var report = new List<SvgLayerReport>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            // Nothing of it reaches the canvas, and a layered file would carry
            // all of it: a sketch nobody can see, published by accident.
            if (layer.Opacity <= 0) continue;
            if (ExposureSheet.ExposedFrame(layer, frameIndex) is not { } frame) continue;
            if (frame.Strokes.Count == 0 && frame.PngBase64 is null && frame.Placements is not { Count: > 0 })
            {
                continue;
            }

            var name = XmlSafe(layer.Name);
            var group = new XElement(Ns + "g", new XAttribute("data-layer", name));
            if (layer.Opacity < 1) group.Add(new XAttribute("opacity", N(Math.Clamp(layer.Opacity, 0, 1))));
            if (layer.BlendMode != LayerBlendMode.Normal)
            {
                group.Add(new XAttribute("style", $"mix-blend-mode:{CssBlend(layer.BlendMode)}"));
            }

            // The layer as the application renders it: the reference the paths
            // are held to, and the pixels themselves when they fall short.
            var rendered = cache.Get(frame, scene.Width, scene.Height, celIndex: frameIndex);
            // A blender or an eraser on an empty layer is strokes that left
            // nothing: not a layer of pixels, and not worth a sentence.
            if (IsBlank(rendered)) continue;

            // What the record says outright is not left to the comparison:
            // imported pixels, placed symbols and correctives are all more
            // than the strokes, and a small one on a large layer is exactly
            // what a comparison is worst at seeing.
            var moreThanStrokes = frame.PngBase64 is not null
                || frame.Placements is { Count: > 0 }
                || frame.Correctives is { Count: > 0 };
            var outlines = moreThanStrokes ? null : Outlines(frame);
            if (outlines is not null && Matches(rendered, outlines))
            {
                foreach (var outline in outlines) group.Add(outline.Element());
                report.Add(new SvgLayerReport(name, SvgLayerForm.Paths));
            }
            else
            {
                if (write)
                {
                    var pixels = scale == 1.0
                        ? rendered
                        : cache.Get(frame, scene.Width, scene.Height, scale, celIndex: frameIndex);
                    using var image = SKImage.FromBitmap(pixels);
                    group.Add(Image(image, scene.Width, scene.Height));
                }
                report.Add(new SvgLayerReport(name, SvgLayerForm.Pixels));
            }

            foreach (var outline in outlines ?? []) outline.Dispose();
            root.Add(group);
        }
        return report;
    }

    // ---- outlines --------------------------------------------------------------

    /// <summary>One filled shape: the path, and how it is painted.</summary>
    private sealed record Outline(SKPath Path, string Data, SKColor Color, double Opacity, bool AntiAlias)
        : IDisposable
    {
        public XElement Element()
        {
            var e = new XElement(Ns + "path",
                new XAttribute("d", Data),
                new XAttribute("fill", Hex(Color)));
            if (Path.FillType == SKPathFillType.EvenOdd) e.Add(new XAttribute("fill-rule", "evenodd"));
            if (Opacity < 1) e.Add(new XAttribute("fill-opacity", N(Opacity)));
            if (!AntiAlias) e.Add(new XAttribute("shape-rendering", "crispEdges"));
            return e;
        }

        public void Dispose() => Path.Dispose();
    }

    /// <summary>
    /// Every stroke of the frame as an outline, or null as soon as one of them
    /// is not one.
    /// </summary>
    private static List<Outline>? Outlines(Frame frame)
    {
        var outlines = new List<Outline>(frame.Strokes.Count);
        foreach (var stroke in frame.Strokes)
        {
            if (OutlineOf(stroke) is { } outline)
            {
                outlines.Add(outline);
                continue;
            }
            foreach (var made in outlines) made.Dispose();
            return null;
        }
        return outlines;
    }

    private static Outline? OutlineOf(Stroke stroke)
    {
        // Each of these changes where a mark lands or what it lands on, and
        // none of them is in the contour: a selection, a locked alpha, a
        // mirrored or wrapped copy.
        if (stroke.ClipId is not null || stroke.AlphaLocked || stroke.Symmetry is not null || stroke.Wrap is not null
            // …and a stroke bound to a rig or a simulation is drawn somewhere
            // other than where its points say.
            || stroke.Weights is not null || stroke.SimId is not null)
        {
            return null;
        }

        var color = BrushEngine.StrokeColor(stroke);
        var opacity = Math.Clamp(stroke.Brush.Opacity, 0, 1) * (color.Alpha / 255.0);

        if (stroke.Tool is ToolKind.Fill or ToolKind.Text)
        {
            if (stroke.Points.Count < 3 || stroke.GradientId is not null) return null;
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            return new Outline(
                BrushEngine.PathFromContours(contours), ContourData(contours), color, opacity, stroke.Brush.AntiAlias);
        }

        if (stroke.Tool != ToolKind.Brush
            || stroke.Brush.Kind != BrushKind.Paint
            || stroke.Brush.Blend is not null
            || !BrushEngine.DrawsAsOneSilhouette(stroke.Brush)
            || CarvesTheMark(stroke.Brush))
        {
            return null;
        }

        // The mark the engine fills: every dab a circle at the radius its
        // pressure gave it, and the union of them. Winding fill is what makes
        // overlapping circles a union rather than a lattice of holes.
        var union = new SKPath { FillType = SKPathFillType.Winding };
        foreach (var dab in BrushEngine.WalkDabs(stroke))
        {
            var radius = (float)BrushEngine.RadiusAt(stroke.Brush, dab.Pressure);
            if (radius > 0) union.AddCircle(dab.Pos.X, dab.Pos.Y, radius);
        }
        if (union.IsEmpty)
        {
            union.Dispose();
            return null;
        }

        // One outline instead of a thousand circles, when Skia can find it. It
        // is the same region either way, so the unsimplified path is a correct
        // answer and only a bigger file.
        var simplified = union.Simplify();
        if (simplified is { IsEmpty: false })
        {
            union.Dispose();
            union = simplified;
            union.FillType = SKPathFillType.Winding;
        }
        else
        {
            simplified?.Dispose();
        }

        opacity *= Math.Clamp(stroke.Brush.Flow, 0, 1);
        return new Outline(union, union.ToSvgPathData(), color, opacity, AntiAlias: true);
    }

    /// <summary>
    /// Whether the brush works on the mark after its dabs are down.
    /// </summary>
    /// <remarks>
    /// <see cref="BrushEngine.DrawsAsOneSilhouette"/> answers a question about
    /// the dabs, and these passes come after them: granulation, a wet edge, a
    /// paper or brush texture and every simulated medium take paint back out of
    /// a silhouette that was solid when it was stamped. Found in review — Ink
    /// with a little granulation is a silhouette to the engine and is not a
    /// flat shape on the page. The comparison catches these too; naming them
    /// means a light grain on a thin line does not depend on it.
    /// </remarks>
    private static bool CarvesTheMark(BrushSettings brush) =>
        brush.Granulation > 0
        || brush.WetEdge > 0
        || brush.TextureId is not null
        || brush.TextureSurface is not null
        || brush.Medium.Kind != MediumKind.None;

    private static string ContourData(IEnumerable<IReadOnlyList<StrokePoint>> contours)
    {
        var d = new StringBuilder();
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            for (var i = 0; i < contour.Count; i++)
            {
                d.Append(i == 0 ? 'M' : 'L').Append(N(contour[i].X)).Append(' ').Append(N(contour[i].Y));
            }
            d.Append('Z');
        }
        return d.ToString();
    }

    // ---- the measurement -------------------------------------------------------

    /// <summary>Draw the outlines as a renderer would and hold them to the layer.</summary>
    /// <remarks>
    /// From the path <em>text</em>, parsed back, and not from the path it was
    /// made from: the text is what is in the file, and writing it rounds every
    /// number and turns every arc into curves. Measuring the object would be
    /// measuring something no reader of the SVG ever sees.
    /// </remarks>
    private static bool Matches(SKBitmap rendered, List<Outline> outlines)
    {
        using var drawn = new SKBitmap(new SKImageInfo(
            rendered.Width, rendered.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(drawn))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var outline in outlines)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = outline.AntiAlias,
                    Color = outline.Color.WithAlpha((byte)Math.Round(outline.Opacity * 255)),
                };
                using var written = SKPath.ParseSvgPathData(outline.Data);
                if (written is null) return false;
                written.FillType = outline.Path.FillType;
                canvas.DrawPath(written, paint);
            }
        }
        return LooksTheSame(rendered, drawn);
    }

    /// <summary>How far a channel may sit from its counterpart and still be the same paint.</summary>
    /// <remarks>
    /// Small on purpose. It only has to cover rounding — an edge is handled by
    /// the neighbourhood, not by this — and at 40, where it started, a wash at
    /// 15% opacity compared equal to nothing and a flow of 0.85 to a flow of 1.
    /// </remarks>
    private const int ChannelSlack = 12;

    /// <summary>
    /// The most pixels that may have no counterpart, however large the layer.
    /// </summary>
    /// <remarks>
    /// The allowance is proportional for a small mark and stops here for a
    /// large one. Unbounded, a full page of flats allowed a stray patch two
    /// hundred pixels across.
    /// </remarks>
    private const int MaxStray = 48;

    /// <summary>
    /// Whether two renders are the same picture, allowing an edge to fall a
    /// pixel to one side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pixel agrees when its value lies within what the other image holds
    /// <em>at that place or beside it</em>. That neighbourhood is the whole
    /// tolerance: a path's antialiasing and a dab's are different arithmetic,
    /// so the same edge legitimately lands half a pixel apart, and a per-pixel
    /// comparison would call every honest outline a mismatch. Anything bigger
    /// than an edge — a shape moved, a mark missing, a texture inside the mark —
    /// leaves pixels with no near neighbour, and those are counted.
    /// </para>
    /// <para>
    /// The allowance is a handful of pixels or one in two hundred of what is
    /// painted, whichever is larger. Set by breaking it, as the tests record: a
    /// square moved three pixels and a missing mark both fail by a wide margin,
    /// and the same square with and without antialiasing passes.
    /// </para>
    /// </remarks>
    internal static bool LooksTheSame(SKBitmap expected, SKBitmap actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height) return false;
        using var a = Rgba(expected);
        using var b = Rgba(actual);
        var (width, height) = (a.Width, a.Height);
        var pa = a.GetPixelSpan();
        var pb = b.GetPixelSpan();

        int painted = 0, stray = 0;
        Span<long> inkA = [0, 0, 0, 0];
        Span<long> inkB = [0, 0, 0, 0];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (pa[i + 3] != 0 || pb[i + 3] != 0) painted++;
                for (var c = 0; c < 4; c++)
                {
                    inkA[c] += pa[i + c];
                    inkB[c] += pb[i + c];
                }
                if (Close(pa, i, pb, i)) continue;
                if (!HasNeighbour(pa, i, pb, x, y, width, height)
                    || !HasNeighbour(pb, i, pa, x, y, width, height))
                {
                    stray++;
                }
            }
        }
        if (stray > Math.Max(4, Math.Min(painted / 200, MaxStray))) return false;

        // The amount of paint, as well as where it is. A line one or two
        // pixels wide is all edge, so every pixel of it has a neighbour that
        // brackets it and the test above passes almost anything; what a grain
        // or a thinner flow cannot hide is that there is less ink on the page.
        for (var c = 0; c < 4; c++)
        {
            var most = Math.Max(inkA[c], inkB[c]);
            if (Math.Abs(inkA[c] - inkB[c]) > Math.Max(255L * 8, most / 50)) return false;
        }
        return true;
    }

    private static bool Close(ReadOnlySpan<byte> a, int i, ReadOnlySpan<byte> b, int j) =>
        Math.Abs(a[i] - b[j]) <= ChannelSlack
        && Math.Abs(a[i + 1] - b[j + 1]) <= ChannelSlack
        && Math.Abs(a[i + 2] - b[j + 2]) <= ChannelSlack
        && Math.Abs(a[i + 3] - b[j + 3]) <= ChannelSlack;

    /// <summary>
    /// Whether each channel of one pixel lies between the lowest and highest
    /// the other image has at that place or beside it.
    /// </summary>
    /// <remarks>
    /// Between, not equal to one of them: a half-covered edge pixel sits
    /// between the empty pixel on one side and the full one on the other, and
    /// that is exactly what an edge landing half a pixel differently looks like.
    /// </remarks>
    private static bool HasNeighbour(
        ReadOnlySpan<byte> of, int i, ReadOnlySpan<byte> within, int x, int y, int width, int height)
    {
        Span<int> low = [255, 255, 255, 255];
        Span<int> high = [0, 0, 0, 0];
        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                if (nx < 0 || nx >= width) continue;
                var j = (ny * width + nx) * 4;
                for (var c = 0; c < 4; c++)
                {
                    low[c] = Math.Min(low[c], within[j + c]);
                    high[c] = Math.Max(high[c], within[j + c]);
                }
            }
        }
        for (var c = 0; c < 4; c++)
        {
            if (of[i + c] < low[c] - ChannelSlack || of[i + c] > high[c] + ChannelSlack) return false;
        }
        return true;
    }

    private static bool IsBlank(SKBitmap bitmap)
    {
        using var copy = Rgba(bitmap);
        var pixels = copy.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) return false;
        }
        return true;
    }

    /// <summary>A copy in one known layout, so the comparison reads bytes it understands.</summary>
    private static SKBitmap Rgba(SKBitmap source)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var copy = new SKBitmap(info);
        using var canvas = new SKCanvas(copy);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
        return copy;
    }

    // ---- writing ---------------------------------------------------------------

    private static XElement Image(SKImage image, int width, int height)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return new XElement(Ns + "image",
            new XAttribute("width", N(width)),
            new XAttribute("height", N(height)),
            new XAttribute("href", "data:image/png;base64," + Convert.ToBase64String(data.ToArray())));
    }

    /// <summary>
    /// A layer name with everything XML 1.0 cannot hold taken out.
    /// </summary>
    /// <remarks>
    /// A name is document text, and a document can come from anywhere: a
    /// Photoshop layer name arrives as whatever its bytes say. One control
    /// character in one name would otherwise stop the whole save, because the
    /// writer refuses the character rather than escaping it.
    /// </remarks>
    private static string XmlSafe(string? name)
    {
        var kept = new StringBuilder();
        var text = name ?? "";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                kept.Append(c).Append(text[++i]);
            }
            else if (!char.IsSurrogate(c) && System.Xml.XmlConvert.IsXmlChar(c))
            {
                kept.Append(c);
            }
        }
        return kept.Length == 0 ? "Layer" : kept.ToString();
    }

    /// <summary>A number as SVG wants it, whatever the machine's locale.</summary>
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Hex(SKColor color) => $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";

    private static string CssBlend(LayerBlendMode mode) => mode switch
    {
        LayerBlendMode.Multiply => "multiply",
        LayerBlendMode.Screen => "screen",
        LayerBlendMode.Overlay => "overlay",
        LayerBlendMode.Darken => "darken",
        LayerBlendMode.Lighten => "lighten",
        LayerBlendMode.ColorDodge => "color-dodge",
        LayerBlendMode.ColorBurn => "color-burn",
        LayerBlendMode.HardLight => "hard-light",
        LayerBlendMode.SoftLight => "soft-light",
        LayerBlendMode.Difference => "difference",
        LayerBlendMode.Exclusion => "exclusion",
        LayerBlendMode.Hue => "hue",
        LayerBlendMode.Saturation => "saturation",
        LayerBlendMode.Color => "color",
        LayerBlendMode.Luminosity => "luminosity",
        _ => "normal",
    };
}
