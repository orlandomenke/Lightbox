using System.Globalization;
using System.Xml.Linq;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Saving the drawing as SVG (Q219, part 1): what is geometry goes in as
/// paths, what is not goes in as pixels, and the file says which.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters draws the SVG back.</b> <see cref="Draw"/> is a
/// reader for exactly the SVG this writes and nothing more — paths, groups,
/// images, a background rectangle — and the picture it makes is compared with
/// the one the canvas shows. An SVG that is well-formed, has the right number
/// of paths in it and does not look like the drawing passes every other test
/// here and fails that one.
/// </para>
/// <para>
/// Both numbers are printed, as the brush-measurement rule asks: how many
/// pixels differ, out of how many are inked. Edges are allowed to differ a
/// little — a path's antialiasing and a dab's are not the same arithmetic —
/// and the shape is not.
/// </para>
/// </remarks>
public class SvgSaveTests(Xunit.ITestOutputHelper output) : IDisposable
{
    private const int Size = 64;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private readonly string _dir = Directory.CreateTempSubdirectory("lightbox-svg").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- fixtures --------------------------------------------------------------

    private static Doc Blank(string? paper = null)
    {
        var doc = DocumentFactory.CreateDoc(Size, Size, fps: 12, paperColor: paper);
        if (paper is null) doc.Scene.TransparentBackground = true;
        return doc;
    }

    private static Layer Painted(string name, params Stroke[] strokes) => new()
    {
        Name = name,
        Kind = LayerKind.Painted,
        Cels = [new Cel { Frame = new Frame { Strokes = [.. strokes] } }],
    };

    private static Doc With(Doc doc, params Layer[] layers)
    {
        doc.Scene.Layers.RemoveAll(l => !l.IsBackground);
        doc.Scene.Layers.AddRange(layers);
        return doc;
    }

    private static Stroke Fill(string colour, double opacity, params (double X, double Y)[] corners) => new()
    {
        Tool = ToolKind.Fill,
        Color = colour,
        Brush = new BrushSettings { Opacity = opacity },
        Points = [.. corners.Select(c => new StrokePoint(c.X, c.Y, 1))],
    };

    private static Stroke Box(string colour, double x, double y, double w, double h, double opacity = 1) =>
        Fill(colour, opacity, (x, y), (x + w, y), (x + w, y + h), (x, y + h));

    /// <summary>
    /// A curved line in the hard round family, thin at one end and full at the
    /// other — the mark the engine draws as one silhouette.
    /// </summary>
    private static Stroke InkLine(string colour = "#101010", double size = 8) => new()
    {
        Tool = ToolKind.Brush,
        Color = colour,
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1 },
        Points =
        [
            .. Enumerable.Range(0, 25).Select(i =>
            {
                var t = i / 24.0;
                return new StrokePoint(8 + 48 * t, 44 - 28 * Math.Sin(t * Math.PI), 0.25 + 0.75 * t);
            }),
        ],
    };

    /// <summary>The same line with a soft edge: dabs, not an outline.</summary>
    private static Stroke SoftLine()
    {
        var stroke = InkLine();
        stroke.Brush.Hardness = 0.3;
        return stroke;
    }

    // ---- reading the SVG back --------------------------------------------------

    private static XElement Root(string xml)
    {
        var root = XDocument.Parse(xml).Root!;
        Assert.Equal(Svg + "svg", root.Name);
        return root;
    }

    private static List<XElement> All(XElement root, string name) => [.. root.Descendants(Svg + name)];

    private static float Number(XElement e, string attribute, float fallback) =>
        e.Attribute(attribute) is { } a
            ? float.Parse(a.Value, CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>Draw the SVG this exporter writes, at the viewBox's own size.</summary>
    private static SKBitmap Draw(string xml)
    {
        var root = Root(xml);
        var box = root.Attribute("viewBox")!.Value.Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var bitmap = new SKBitmap(new SKImageInfo((int)box[2], (int)box[3], SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var child in root.Elements()) DrawElement(canvas, child);
        canvas.Flush();
        return bitmap;
    }

    private static void DrawElement(SKCanvas canvas, XElement e)
    {
        var opacity = Number(e, "opacity", 1);
        switch (e.Name.LocalName)
        {
            case "g":
                using (var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) })
                {
                    canvas.SaveLayer(layer);
                    foreach (var child in e.Elements()) DrawElement(canvas, child);
                    canvas.Restore();
                }
                break;
            case "rect":
                using (var paint = new SKPaint { Color = SKColor.Parse(e.Attribute("fill")!.Value) })
                {
                    canvas.DrawRect(Number(e, "x", 0), Number(e, "y", 0), Number(e, "width", 0), Number(e, "height", 0), paint);
                }
                break;
            case "path":
                using (var path = SKPath.ParseSvgPathData(e.Attribute("d")!.Value))
                using (var paint = new SKPaint { IsAntialias = true })
                {
                    path.FillType = e.Attribute("fill-rule")?.Value == "evenodd"
                        ? SKPathFillType.EvenOdd
                        : SKPathFillType.Winding;
                    var alpha = Number(e, "fill-opacity", 1) * opacity;
                    paint.Color = SKColor.Parse(e.Attribute("fill")!.Value).WithAlpha((byte)Math.Round(alpha * 255));
                    canvas.DrawPath(path, paint);
                }
                break;
            case "image":
                var href = (e.Attribute("href") ?? e.Attribute(XLink + "href"))!.Value;
                const string prefix = "data:image/png;base64,";
                Assert.StartsWith(prefix, href);
                using (var image = SKBitmap.Decode(Convert.FromBase64String(href[prefix.Length..])))
                using (var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) })
                {
                    canvas.DrawBitmap(
                        image,
                        SKRect.Create(Number(e, "x", 0), Number(e, "y", 0), Number(e, "width", image.Width), Number(e, "height", image.Height)),
                        paint);
                }
                break;
        }
    }

    /// <summary>What the canvas shows for frame 0, as export composes it.</summary>
    private static SKBitmap Canvas(Doc doc)
    {
        using var cache = new FrameBitmapCache();
        using var image = SequenceExporter.RenderFrame(doc, cache, 0);
        return SKBitmap.FromImage(image);
    }

    /// <summary>
    /// How many pixels are plainly different (any channel off by more than 48
    /// of 255), and how many are inked in the reference at all.
    /// </summary>
    private static (int Differ, int Inked) Compare(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        int differ = 0, inked = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.GetPixel(x, y);
                var b = actual.GetPixel(x, y);
                if (a.Alpha > 0) inked++;
                if (Math.Abs(a.Alpha - b.Alpha) > 48 || Math.Abs(a.Red - b.Red) > 48
                    || Math.Abs(a.Green - b.Green) > 48 || Math.Abs(a.Blue - b.Blue) > 48)
                {
                    differ++;
                }
            }
        }
        return (differ, inked);
    }

    private void AssertDrawsBackAsTheCanvas(Doc doc, SvgDocument svg, string what)
    {
        using var expected = Canvas(doc);
        using var actual = Draw(svg.Xml);
        var (differ, inked) = Compare(expected, actual);
        output.WriteLine($"{what}: {differ} px plainly different of {inked} inked, {svg.Xml.Length} chars of SVG");
        Assert.True(inked > 20, "the fixture drew nothing, so the comparison proves nothing");
        Assert.True(differ <= Math.Max(2, inked / 100), $"{what}: {differ} of {inked} px differ");
    }

    // ---- fills -----------------------------------------------------------------

    [Fact]
    public void AFillBecomesAPathInItsColourAndNoPixels()
    {
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)));

        var svg = SvgExporter.Build(doc);

        var root = Root(svg.Xml);
        var path = Assert.Single(All(root, "path"));
        Assert.Equal("#ff0000", path.Attribute("fill")!.Value);
        Assert.Empty(All(root, "image"));
        Assert.Equal(1, svg.Report.PathLayers);
        Assert.Equal(0, svg.Report.PixelLayers);
        Assert.False(svg.Report.Flattened);
        Assert.Null(svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "one fill");
    }

    [Fact]
    public void AFillWithAHoleKeepsTheHole()
    {
        var ring = Box("#0044cc", 8, 8, 40, 40);
        ring.Holes = [[new(20, 20, 1), new(36, 20, 1), new(36, 36, 1), new(20, 36, 1)]];
        var doc = With(Blank(), Painted("Ring", ring));

        var svg = SvgExporter.Build(doc);

        Assert.Equal("evenodd", Assert.Single(All(Root(svg.Xml), "path")).Attribute("fill-rule")!.Value);
        using var drawn = Draw(svg.Xml);
        Assert.Equal(0, drawn.GetPixel(28, 28).Alpha);
        Assert.Equal(255, drawn.GetPixel(12, 12).Alpha);
        AssertDrawsBackAsTheCanvas(doc, svg, "a fill with a hole");
    }

    [Fact]
    public void AHalfOpaqueFillOverAnotherKeepsBothAndTheirOrder()
    {
        var doc = With(Blank(), Painted("Flats",
            Box("#ff0000", 8, 8, 32, 32),
            Box("#0000ff", 24, 24, 32, 32, opacity: 0.5)));

        var svg = SvgExporter.Build(doc);

        var paths = All(Root(svg.Xml), "path");
        Assert.Equal(["#ff0000", "#0000ff"], paths.Select(p => p.Attribute("fill")!.Value).ToArray());
        Assert.Equal("0.5", paths[1].Attribute("fill-opacity")!.Value);
        Assert.Null(paths[0].Attribute("fill-opacity"));
        AssertDrawsBackAsTheCanvas(doc, svg, "two overlapping fills");
    }

    [Fact]
    public void ThePaperLayerIsAPathLikeAnyOtherFill()
    {
        var doc = With(Blank(paper: "#f4f0e6"), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)));

        var svg = SvgExporter.Build(doc);

        Assert.Equal(2, svg.Report.PathLayers);
        Assert.Empty(All(Root(svg.Xml), "image"));
        AssertDrawsBackAsTheCanvas(doc, svg, "a fill on paper");
    }

    [Fact]
    public void AnOldDocumentsPaperColourIsARectangle()
    {
        // Before background layers existed the paper was a colour on the scene.
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)));
        doc.Scene.TransparentBackground = false;
        doc.Scene.BackgroundColor = "#00ff00";

        var svg = SvgExporter.Build(doc);

        var rect = Assert.Single(All(Root(svg.Xml), "rect"));
        Assert.Equal("#00ff00", rect.Attribute("fill")!.Value);
        AssertDrawsBackAsTheCanvas(doc, svg, "a scene paper colour");
    }

    [Fact]
    public void TransparentPaperWritesNoBackgroundAtAll()
    {
        var svg = SvgExporter.Build(With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24))));

        Assert.Empty(All(Root(svg.Xml), "rect"));
    }

    // ---- lines -----------------------------------------------------------------

    [Fact]
    public void AHardRoundLineBecomesItsOutlineNotACentreline()
    {
        var doc = With(Blank(), Painted("Ink", InkLine()));

        var svg = SvgExporter.Build(doc);

        var path = Assert.Single(All(Root(svg.Xml), "path"));
        // An outline that is filled. A centreline with a stroke-width could not
        // carry the pressure that thins one end of this mark.
        Assert.Null(path.Attribute("stroke"));
        Assert.Null(path.Attribute("stroke-width"));
        Assert.Equal("#101010", path.Attribute("fill")!.Value);
        Assert.Equal(0, svg.Report.PixelLayers);
        AssertDrawsBackAsTheCanvas(doc, svg, "an ink line");
    }

    [Fact]
    public void ThePressureThatThinsTheLineIsInTheOutline()
    {
        var svg = SvgExporter.Build(With(Blank(), Painted("Ink", InkLine(size: 10))));

        using var drawn = Draw(svg.Xml);
        int Thickness(int x) => Enumerable.Range(0, Size).Count(y => drawn.GetPixel(x, y).Alpha > 127);
        var thin = Thickness(12);
        var full = Thickness(52);
        output.WriteLine($"thickness near the start {thin} px, near the end {full} px");
        Assert.True(thin >= 1);
        Assert.True(full >= thin + 3, $"{thin} → {full}");
    }

    [Fact]
    public void ASoftBrushLayerGoesInAsPixelsAndIsNamed()
    {
        var doc = With(Blank(), Painted("Shading", SoftLine()));

        var svg = SvgExporter.Build(doc);

        var root = Root(svg.Xml);
        Assert.Empty(All(root, "path"));
        Assert.Single(All(root, "image"));
        Assert.Equal(0, svg.Report.PathLayers);
        Assert.Equal(1, svg.Report.PixelLayers);
        var layer = Assert.Single(svg.Report.Layers);
        Assert.Equal("Shading", layer.Name);
        Assert.Equal(SvgLayerForm.Pixels, layer.Form);
        output.WriteLine(svg.Report.Notice ?? "(nothing to say)");
        Assert.Contains("Shading", svg.Report.Notice);
        Assert.Contains("pixels", svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "a soft line");
    }

    /// <summary>
    /// Every brush the application ships, drawn and saved. Nothing here says
    /// which of them ought to be paths — the claim is the one that has to hold
    /// for all of them: whatever form the layer took, the file draws back as
    /// the canvas.
    /// </summary>
    [Fact]
    public void EveryBuiltInBrushSavesAsSomethingThatLooksLikeItsMark()
    {
        var forms = new Dictionary<string, SvgLayerForm>();
        foreach (var preset in BuiltInPresets.Create())
        {
            var stroke = InkLine();
            stroke.Brush = preset.Settings;
            var doc = With(Blank(), Painted(preset.Name, stroke));

            var svg = SvgExporter.Build(doc);

            using var expected = Canvas(doc);
            using var actual = Draw(svg.Xml);
            var (differ, inked) = Compare(expected, actual);
            var form = svg.Report.Layers.Count == 0 ? "nothing" : svg.Report.Layers[0].Form.ToString();
            output.WriteLine($"{preset.Name,-22} {form,-7} {differ} px plainly different of {inked} inked");
            Assert.True(differ <= Math.Max(2, inked / 100), $"{preset.Name}: {differ} of {inked} px differ");
            if (svg.Report.Layers.Count > 0) forms[preset.Id] = svg.Report.Layers[0].Form;
        }

        // The line-art brush is the one an artist reaches for SVG with.
        Assert.Equal(SvgLayerForm.Paths, forms["builtin-ink"]);
        Assert.Contains(SvgLayerForm.Pixels, forms.Values);
    }

    // ---- a layer is one thing or the other -------------------------------------

    [Fact]
    public void OneMarkThatIsNotAnOutlineMakesItsWholeLayerPixels()
    {
        // An eraser after a fill changes what the fill looks like. Writing the
        // fill as a path and dropping the eraser would be a picture of a
        // drawing the artist did not make.
        var eraser = InkLine();
        eraser.Tool = ToolKind.Eraser;
        var doc = With(Blank(),
            Painted("Flats", Box("#ff0000", 4, 4, 56, 56), eraser),
            Painted("Clean", Box("#0000ff", 40, 40, 16, 16)));

        var svg = SvgExporter.Build(doc);

        var root = Root(svg.Xml);
        Assert.Single(All(root, "image"));
        Assert.Equal("#0000ff", Assert.Single(All(root, "path")).Attribute("fill")!.Value);
        Assert.Equal(
            [("Flats", SvgLayerForm.Pixels), ("Clean", SvgLayerForm.Paths)],
            svg.Report.Layers.Select(l => (l.Name, l.Form)).ToArray());
        Assert.Contains("1 of 2 layers", svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "an erased fill under a clean one");
    }

    [Fact]
    public void LayersStayInTheOrderTheyAreStacked()
    {
        var doc = With(Blank(),
            Painted("Under", Box("#ff0000", 4, 4, 40, 40)),
            Painted("Middle", SoftLine()),
            Painted("Over", Box("#0000ff", 30, 30, 30, 30)));

        var svg = SvgExporter.Build(doc);

        var order = Root(svg.Xml).Descendants()
            .Where(e => e.Name.LocalName is "path" or "image")
            .Select(e => e.Name.LocalName)
            .ToArray();
        Assert.Equal(["path", "image", "path"], order);
        AssertDrawsBackAsTheCanvas(doc, svg, "paths, pixels, paths");
    }

    [Fact]
    public void AHiddenLayerIsNotInTheFileOrTheCount()
    {
        var hidden = Painted("Hidden", Box("#00ff00", 0, 0, 64, 64));
        hidden.Visible = false;
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)), hidden);

        var svg = SvgExporter.Build(doc);

        Assert.DoesNotContain("#00ff00", svg.Xml);
        Assert.Equal("Flats", Assert.Single(svg.Report.Layers).Name);
    }

    [Fact]
    public void AnEmptyLayerWritesNothingAndIsNotCountedAsPixels()
    {
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)), Painted("Empty"));

        var svg = SvgExporter.Build(doc);

        Assert.Equal(0, svg.Report.PixelLayers);
        Assert.Null(svg.Report.Notice);
    }

    [Fact]
    public void ALayerWhoseStrokesLeftNothingIsNotALayerOfPixels()
    {
        // An eraser on an empty layer: strokes in the record, nothing on the page.
        var eraser = InkLine();
        eraser.Tool = ToolKind.Eraser;
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)), Painted("Rubbed out", eraser));

        var svg = SvgExporter.Build(doc);

        Assert.Empty(All(Root(svg.Xml), "image"));
        Assert.Equal("Flats", Assert.Single(svg.Report.Layers).Name);
        Assert.Null(svg.Report.Notice);
    }

    [Fact]
    public void ALayersOpacityIsOnItsGroup()
    {
        var layer = Painted("Glaze", Box("#ff0000", 8, 8, 40, 40), Box("#ff0000", 20, 20, 40, 40));
        layer.Opacity = 0.5;
        var doc = With(Blank(), layer);

        var svg = SvgExporter.Build(doc);

        // On the group, not on each path: two overlapping fills at half
        // opacity each would be darker where they cross, and the layer is not.
        var group = Assert.Single(All(Root(svg.Xml), "g"));
        Assert.Equal("0.5", group.Attribute("opacity")!.Value);
        Assert.All(All(Root(svg.Xml), "path"), p => Assert.Null(p.Attribute("fill-opacity")));
        AssertDrawsBackAsTheCanvas(doc, svg, "a half-opaque layer");
    }

    [Fact]
    public void ALayerIsNamedInTheFileSoItCanBeFoundInAnEditor()
    {
        var svg = SvgExporter.Build(With(Blank(), Painted("Line art <final> & \"clean\"", Box("#ff0000", 8, 8, 24, 24))));

        // Parsed, so the awkward characters were escaped rather than pasted.
        var group = Assert.Single(All(Root(svg.Xml), "g"));
        Assert.Equal("Line art <final> & \"clean\"", group.Attribute("data-layer")!.Value);
    }

    [Fact]
    public void AMultiplyLayerSaysSoInTheOnlyWaySvgCan()
    {
        var layer = Painted("Shadow", Box("#808080", 8, 8, 40, 40));
        layer.BlendMode = LayerBlendMode.Multiply;

        var svg = SvgExporter.Build(With(Blank(paper: "#ffffff"), layer));

        var group = All(Root(svg.Xml), "g").Single(g => g.Attribute("data-layer")!.Value == "Shadow");
        Assert.Contains("mix-blend-mode:multiply", group.Attribute("style")!.Value);
    }

    // ---- what cannot be said layer by layer ------------------------------------

    [Fact]
    public void ADocumentWithACameraIsOnePictureOfWhatTheCameraSaw()
    {
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)));
        doc.Scene.Camera = new Camera { OutputWidth = 32, OutputHeight = 20 };

        var svg = SvgExporter.Build(doc);

        var root = Root(svg.Xml);
        Assert.Equal("0 0 32 20", root.Attribute("viewBox")!.Value);
        Assert.Single(All(root, "image"));
        Assert.Empty(All(root, "path"));
        Assert.True(svg.Report.Flattened);
        output.WriteLine(svg.Report.Notice ?? "(nothing to say)");
        Assert.Contains("camera", svg.Report.Notice);
        Assert.Contains("whole picture", svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "through a camera");
    }

    [Fact]
    public void AMaskedLayerMakesTheWholePictureOneImage()
    {
        var masked = Painted("Masked", Box("#ff0000", 8, 8, 40, 40));
        masked.Mask = new LayerMask { Frame = new Frame { Strokes = [Box("#000000", 8, 8, 20, 40)] } };
        var doc = With(Blank(), masked);

        var svg = SvgExporter.Build(doc);

        Assert.True(svg.Report.Flattened);
        Assert.Contains("mask", svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "a masked layer");
    }

    // ---- the measurement behind "paths" ----------------------------------------

    [Fact]
    public void WhatTheStrokesDoNotAccountForSendsTheLayerToPixels()
    {
        // Imported pixels under the strokes: every stroke here is an honest
        // outline and the layer still is not, because the strokes are not all
        // of it. Nothing in the stroke list says so — the comparison does.
        using var baseline = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(baseline))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Green };
            canvas.DrawRect(40, 4, 20, 20, paint);
        }
        using var png = baseline.Encode(SKEncodedImageFormat.Png, 100);
        var layer = Painted("Imported", Box("#ff0000", 4, 40, 20, 20));
        layer.Cels[0].Frame!.PngBase64 = Convert.ToBase64String(png.ToArray());
        var doc = With(Blank(), layer);

        var svg = SvgExporter.Build(doc);

        Assert.Equal(SvgLayerForm.Pixels, Assert.Single(svg.Report.Layers).Form);
        AssertDrawsBackAsTheCanvas(doc, svg, "strokes over imported pixels");
    }

    [Fact]
    public void TheSamePictureWithSofterEdgesStillCountsAsTheSame()
    {
        using var a = Square(at: 10, antialias: true);
        using var b = Square(at: 10, antialias: false);

        Assert.True(SvgExporter.LooksTheSame(a, b));
    }

    [Theory]
    [InlineData(13)] // moved three pixels
    [InlineData(40)] // somewhere else entirely
    public void AShapeInADifferentPlaceDoesNot(int at)
    {
        using var a = Square(at: 10, antialias: true);
        using var b = Square(at, antialias: true);

        Assert.False(SvgExporter.LooksTheSame(a, b));
    }

    [Fact]
    public void AMissingMarkDoesNot()
    {
        using var a = Square(at: 10, antialias: true);
        using var b = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));

        Assert.False(SvgExporter.LooksTheSame(a, b));
    }

    private static SKBitmap Square(float at, bool antialias)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = antialias };
        canvas.DrawRect(at + 0.5f, at + 0.5f, 12, 12, paint);
        return bitmap;
    }

    // ---- what the review found (sensitivity-guardian, 2026-10-08) -------------

    /// <summary>
    /// A big layer of flats with a small imported patch on it. The allowance
    /// used to be one pixel in two hundred of what is painted, so a large
    /// enough fill bought a hole big enough to lose the patch through.
    /// </summary>
    [Fact]
    public void ALargeLayerDoesNotBuyALargeBlindSpot()
    {
        const int big = 256;
        using var flats = Patch(big, (8, 8, 240, 240, SKColors.Red));
        using var withPatch = Patch(big, (8, 8, 240, 240, SKColors.Red), (100, 100, 12, 12, SKColors.Blue));

        // 57,600 px painted; the patch is 144 of them.
        Assert.False(SvgExporter.LooksTheSame(withPatch, flats));
    }

    [Fact]
    public void AFaintWashIsNotNothing()
    {
        // Alpha 30 of 255 across the page: under the old slack it compared
        // equal to an empty layer.
        using var wash = Patch(Size, (0, 0, Size, Size, new SKColor(0, 0, 0, 30)));
        using var empty = Patch(Size);

        Assert.False(SvgExporter.LooksTheSame(wash, empty));
    }

    [Fact]
    public void ASlightlyThinnerPaintIsNotTheSamePaint()
    {
        // Flow 0.85 against 1.0 is 38 of 255 — the size of error a wrong
        // opacity would make, and it has to be seen.
        using var full = Patch(Size, (8, 8, 40, 40, SKColors.Black));
        using var thin = Patch(Size, (8, 8, 40, 40, new SKColor(0, 0, 0, 217)));

        Assert.False(SvgExporter.LooksTheSame(full, thin));
    }

    private static SKBitmap Patch(int size, params (int X, int Y, int W, int H, SKColor Color)[] rects)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var (x, y, w, h, color) in rects)
        {
            using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.Src };
            canvas.DrawRect(x, y, w, h, paint);
        }
        return bitmap;
    }

    [Fact]
    public void ImportedPixelsSendALayerToPixelsWithoutBeingMeasured()
    {
        // The record says outright that this layer is more than its strokes,
        // so it is not left to a comparison to notice — a small import on a
        // large fill is exactly what a comparison is worst at.
        using var baseline = Patch(Size, (30, 30, 3, 3, SKColors.Green));
        using var png = baseline.Encode(SKEncodedImageFormat.Png, 100);
        var layer = Painted("Flats", Box("#ff0000", 0, 0, Size, Size));
        layer.Cels[0].Frame!.PngBase64 = Convert.ToBase64String(png.ToArray());

        var svg = SvgExporter.Build(With(Blank(), layer));

        Assert.Equal(SvgLayerForm.Pixels, Assert.Single(svg.Report.Layers).Form);
    }

    [Theory]
    [InlineData("Line\u0001art")] // a control character, as a PSD layer name can carry
    [InlineData("Line\uFFFEart")] // not a character at all
    [InlineData("Line\uD800art")] // half of a surrogate pair
    [InlineData("\u0000")] // nothing left once it is cleaned
    public void ALayerNameXmlCannotHoldDoesNotStopTheSave(string escaped)
    {
        var name = System.Text.RegularExpressions.Regex.Unescape(escaped);
        var doc = With(Blank(), Painted(name, Box("#ff0000", 8, 8, 24, 24)));

        var svg = SvgExporter.Build(doc);

        var group = Assert.Single(All(Root(svg.Xml), "g"));
        output.WriteLine($"\"{escaped}\" → \"{group.Attribute("data-layer")!.Value}\"");
        Assert.DoesNotContain('\u0001', group.Attribute("data-layer")!.Value);
        Assert.Single(All(Root(svg.Xml), "path"));
    }

    [Fact]
    public void ALayerAtNoOpacityIsNotWritten()
    {
        // The canvas shows nothing of it, and a layered file would carry all
        // of it — a hidden sketch published by accident.
        var ghost = Painted("Sketch", Box("#00ff00", 0, 0, Size, Size));
        ghost.Opacity = 0;
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)), ghost);

        var svg = SvgExporter.Build(doc);

        Assert.DoesNotContain("#00ff00", svg.Xml);
        Assert.Equal("Flats", Assert.Single(svg.Report.Layers).Name);
    }

    [Theory]
    [InlineData(1000.0, "1024")] // held to the dialog's own ceiling of 16x
    [InlineData(0.0, "64")]
    [InlineData(double.NaN, "64")]
    public void AnAbsurdSizeIsHeldToWhatTheDialogAllows(double scale, string width)
    {
        var svg = SvgExporter.Build(With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24))), scale: scale);

        Assert.Equal(width, Root(svg.Xml).Attribute("width")!.Value);
    }

    [Fact]
    public void TheSurveyAnswersWithoutWritingTheFile()
    {
        var doc = With(Blank(), Painted("Ink", InkLine()), Painted("Shading", SoftLine()));

        var report = SvgExporter.Survey(doc);

        Assert.Equal(
            SvgExporter.Build(doc).Report.Layers.Select(l => (l.Name, l.Form)),
            report.Layers.Select(l => (l.Name, l.Form)));
    }

    [Fact]
    public void ASurveyThatFailsIsASentenceNotAnException()
    {
        // Read by a binding, on the UI thread, where an exception is not
        // marked handled: it has to come back as words.
        var asked = 0;
        var vm = new ViewModels.SaveImageDialogViewModel(Blank().Scene, () =>
        {
            asked++;
            throw new InvalidOperationException("boom");
        })
        {
            Format = ImageSaveFormat.Svg,
        };

        var notice = vm.SvgNotice;
        _ = vm.SvgNotice;
        _ = vm.HasSvgNotice;

        output.WriteLine(notice ?? "(nothing)");
        Assert.NotNull(notice);
        Assert.Contains("could not be checked", notice);
        // Asked once: a survey that failed is not re-run on every read.
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ALongInkLineIsStillSavedInGoodTime()
    {
        // Line art is what SVG is for, and a long line is thousands of dabs
        // into a path union. Printed rather than budgeted tightly: the bound
        // is "a save, not a wait".
        var stroke = InkLine(size: 12);
        stroke.Points =
        [
            .. Enumerable.Range(0, 4000).Select(i =>
            {
                var t = i / 4000.0;
                return new StrokePoint(
                    256 + 200 * t * Math.Cos(t * 40), 256 + 200 * t * Math.Sin(t * 40), 0.4 + 0.6 * t);
            }),
        ];
        var doc = DocumentFactory.CreateDoc(512, 512, fps: 12);
        doc.Scene.TransparentBackground = true;
        With(doc, Painted("Ink", stroke));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var svg = SvgExporter.Build(doc);
        clock.Stop();

        output.WriteLine(
            $"{BrushEngine.WalkDabs(stroke).Count} dabs → {svg.Report.Layers[0].Form}, "
            + $"{svg.Xml.Length / 1024} KB, {clock.ElapsedMilliseconds} ms");
        Assert.True(clock.ElapsedMilliseconds < 10_000, $"{clock.ElapsedMilliseconds} ms");
        using var expected = Canvas(doc);
        using var actual = Draw(svg.Xml);
        var (differ, inked) = Compare(expected, actual);
        output.WriteLine($"{differ} px plainly different of {inked} inked");
        Assert.True(differ <= Math.Max(2, inked / 100));
    }

    [Fact]
    public void InkWithGrainIsNotAFlatShape()
    {
        // A silhouette to the engine, and granulation then takes paint back out
        // of it. At 0.15 the loss is 38 of 255 — under the comparison's first
        // slack, which is how the review found it.
        var grainy = InkLine();
        grainy.Brush.Granulation = 0.15;

        var svg = SvgExporter.Build(With(Blank(), Painted("Grain", grainy)));

        Assert.Equal(SvgLayerForm.Pixels, Assert.Single(svg.Report.Layers).Form);
    }

    [Fact]
    public void LessInkOnAThinLineIsSeenEvenThoughEveryPixelOfItIsEdge()
    {
        // The comparison on its own, with no setting to go by: a 2 px line
        // against the same line at two thirds of the paint.
        using var full = Patch(Size, (4, 30, 56, 2, SKColors.Black));
        using var thin = Patch(Size, (4, 30, 56, 2, new SKColor(0, 0, 0, 170)));
        using var moved = Patch(Size, (4, 31, 56, 2, SKColors.Black));

        Assert.False(SvgExporter.LooksTheSame(full, thin));
        // One pixel to the side is an edge falling differently, and is the same.
        Assert.True(SvgExporter.LooksTheSame(full, moved));
    }

    [Fact]
    public void AnAdjustmentLayerMakesTheWholePictureOneImage()
    {
        var adjust = Painted("Grade");
        adjust.Adjusts = true;
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)), adjust);

        var svg = SvgExporter.Build(doc);

        Assert.True(svg.Report.Flattened);
        Assert.Contains("adjustment layer", svg.Report.Notice);
        Assert.Empty(All(Root(svg.Xml), "path"));
    }

    [Fact]
    public void ALayerEffectMakesTheWholePictureOneImage()
    {
        // Paths for a desaturated layer would be the layer in full colour.
        var graded = Painted("Graded", Box("#ff0000", 8, 8, 40, 40));
        graded.Effects = new Lightbox.Core.Effects.EffectStack
        {
            Uses =
            [
                new Lightbox.Core.Effects.EffectUse
                {
                    Kind = "grade.hsl",
                    Params = { ["saturation"] = new Lightbox.Core.Effects.EffectParam(-100) },
                },
            ],
        };
        var doc = With(Blank(), graded);

        var svg = SvgExporter.Build(doc);

        Assert.True(svg.Report.Flattened);
        Assert.Contains("effect", svg.Report.Notice);
        AssertDrawsBackAsTheCanvas(doc, svg, "a graded layer");
    }

    // ---- size, and the file ----------------------------------------------------

    [Fact]
    public void SizeChangesHowBigItOpensNeverTheCoordinates()
    {
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8, 8, 24, 24)));

        var once = SvgExporter.Build(doc);
        var twice = SvgExporter.Build(doc, scale: 2);

        var root = Root(twice.Xml);
        Assert.Equal("0 0 64 64", root.Attribute("viewBox")!.Value);
        Assert.Equal("128", root.Attribute("width")!.Value);
        Assert.Equal("128", root.Attribute("height")!.Value);
        // Invariant 7, in a format where it is free: the path is the same path.
        Assert.Equal(
            Assert.Single(All(Root(once.Xml), "path")).Attribute("d")!.Value,
            Assert.Single(All(root, "path")).Attribute("d")!.Value);
    }

    [Fact]
    public void TheSameDrawingWritesTheSameFileEveryTime()
    {
        var doc = With(Blank(paper: "#ffffff"),
            Painted("Ink", InkLine()), Painted("Shading", SoftLine()));

        Assert.Equal(SvgExporter.Build(doc).Xml, SvgExporter.Build(doc).Xml);
    }

    [Fact]
    public void NumbersAreWrittenTheSameInEveryLocale()
    {
        var doc = With(Blank(), Painted("Flats", Box("#ff0000", 8.5, 8.25, 24, 24, opacity: 0.5)));
        var before = CultureInfo.CurrentCulture;
        try
        {
            // A comma for a decimal point inside a path is a different path.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var svg = SvgExporter.Build(doc);

            var path = Assert.Single(All(Root(svg.Xml), "path"));
            Assert.Equal("0.5", path.Attribute("fill-opacity")!.Value);
            Assert.Contains("8.5", path.Attribute("d")!.Value);
            Assert.DoesNotContain("8,5", path.Attribute("d")!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void AChosenFrameIsTheOneWritten()
    {
        var layer = Painted("Flats", Box("#ff0000", 8, 8, 24, 24));
        layer.Cels.Add(new Cel { Frame = new Frame { Strokes = [Box("#0000ff", 8, 8, 24, 24)] } });
        var doc = With(Blank(), layer);
        doc.Scene.FrameCount = 2;

        var svg = SvgExporter.Build(doc, frameIndex: 1);

        Assert.Equal("#0000ff", Assert.Single(All(Root(svg.Xml), "path")).Attribute("fill")!.Value);
    }

    // ---- through Save as image -------------------------------------------------

    [Fact]
    public void SaveAsImageWritesAnSvgFileAndReportsWhatWentInAsPixels()
    {
        var doc = With(Blank(), Painted("Ink", InkLine()), Painted("Shading", SoftLine()));
        var path = Path.Combine(_dir, "character.svg");

        var result = SaveAsImage.Write(doc, path, new ImageSaveOptions(ImageSaveFormat.Svg));

        Assert.Equal([path], result.Paths);
        Root(File.ReadAllText(path));
        Assert.False(result.LostTransparency);
        Assert.NotNull(result.Svg);
        Assert.Equal(1, result.Svg!.PixelLayers);
        output.WriteLine(result.Warning ?? "(nothing to say)");
        Assert.Contains("Shading", result.Warning);
    }

    [Fact]
    public void AnSvgThatIsAllPathsWarnsAboutNothing()
    {
        var doc = With(Blank(), Painted("Ink", InkLine()));

        var path = Path.Combine(_dir, "ink.svg");

        var result = SaveAsImage.Write(doc, path, new ImageSaveOptions(ImageSaveFormat.Svg));

        // Read back as SVG: against the stubs this passed on a PNG named .svg.
        var root = Root(File.ReadAllText(path));
        Assert.Single(All(root, "path"));
        Assert.Empty(All(root, "image"));
        Assert.Equal(0, result.Svg!.PixelLayers);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void TypingDotSvgOverAPngDefaultMeansSvg()
    {
        var options = SaveAsImage.Reconcile(new ImageSaveOptions(), "cover.svg");

        Assert.Equal(ImageSaveFormat.Svg, options.Format);
        Assert.Equal(ImageSaveFormat.Svg, ImageSaveFormats.FromExtension("x.SVG"));
        Assert.Equal(".svg", ImageSaveFormats.Extension(ImageSaveFormat.Svg));
    }

    [Fact]
    public void SvgIsOfferedForAPictureAndNotForASequence()
    {
        // A run of SVG frames is a reasonable thing to want and is not built;
        // offering it would be a menu entry that writes PNGs named .svg.
        Assert.Contains(ImageSaveFormat.Svg, ImageSaveFormats.All);
        Assert.DoesNotContain(ImageSaveFormat.Svg, ImageSaveFormats.Raster);
        Assert.DoesNotContain(ImageSaveFormat.Svg, new ViewModels.ImageSequenceDialogViewModel(Blank().Scene).Formats);
        Assert.Contains(ImageSaveFormat.Svg, new ViewModels.SaveImageDialogViewModel(Blank().Scene).Formats);
    }

    [Fact]
    public void TheDialogSaysBeforeTheSaveWhichLayersWillBePixels()
    {
        var doc = With(Blank(), Painted("Ink", InkLine()), Painted("Shading", SoftLine()));
        var asked = 0;
        var vm = new ViewModels.SaveImageDialogViewModel(doc.Scene, () =>
        {
            asked++;
            return SvgExporter.Build(doc).Report;
        });

        // Not measured until SVG is actually chosen: the survey renders every
        // layer, and a PNG save should not pay for it.
        Assert.Null(vm.SvgNotice);
        Assert.Equal(0, asked);

        vm.Format = ImageSaveFormat.Svg;

        output.WriteLine(vm.SvgNotice ?? "(nothing to say)");
        Assert.Contains("Shading", vm.SvgNotice);
        Assert.Equal(1, asked);
        Assert.False(vm.HasQuality);
        Assert.False(vm.MayLoseTransparency);
        Assert.Contains("SVG", vm.Summary);

        vm.Format = ImageSaveFormat.Png;
        Assert.Null(vm.SvgNotice);
    }
}
