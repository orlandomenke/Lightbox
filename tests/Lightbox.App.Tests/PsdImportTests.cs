using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Import;
using Lightbox.Raster.Tests;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A PSD becoming a document: one frame deep, one layer per Photoshop layer,
/// pixels on the baseline that has always been there for imported content.
/// </summary>
public class PsdImportTests(ITestOutputHelper output)
{
    private static SKBitmap Baseline(Layer layer) =>
        Lightbox.Raster.PngCodec.Decode(layer.Cels[0].Frame!.PngBase64!);

    // ---- the shape of the document --------------------------------------------

    [Fact]
    public void TheCanvasAndOneCelComeFromThePsd()
    {
        var bytes = new PsdFixture
        {
            Width = 12,
            Height = 9,
            Layers = { PsdLayerFixture.Solid("Art", 30, 60, 90, a: 255, right: 12, bottom: 9) },
        }.Build();

        var result = PsdDocumentImport.Open(bytes, "Poster");

        Assert.Equal(12, result.Document.Scene.Width);
        Assert.Equal(9, result.Document.Scene.Height);
        Assert.Equal(1, result.Document.Scene.FrameCount);
        Assert.Equal("Poster", result.Document.Scene.Name);
        var layer = Assert.Single(result.Document.Scene.Layers);
        Assert.Single(layer.Cels);
    }

    [Fact]
    public void LayersKeepThePsdsBottomFirstOrder()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Solid("Bottom", 1, 1, 1, a: 255),
                PsdLayerFixture.Solid("Middle", 2, 2, 2, a: 255),
                PsdLayerFixture.Solid("Top", 3, 3, 3, a: 255),
            },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);

        Assert.Equal(
            ["Bottom", "Middle", "Top"],
            result.Document.Scene.Layers.ConvertAll(l => l.Name));
    }

    [Fact]
    public void NoPaperLayerIsInvented()
    {
        // A PSD carries its own background as an ordinary opaque layer. Adding
        // Lightbox's paper underneath would give the artist a layer they never had.
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Background", 255, 255, 255, a: 255) },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);

        Assert.Single(result.Document.Scene.Layers);
        Assert.DoesNotContain(result.Document.Scene.Layers, l => l.IsBackground);
        Assert.True(result.Document.Scene.TransparentBackground);
    }

    // ---- pixels ---------------------------------------------------------------

    [Fact]
    public void ALayersPixelsLandOnTheBaselineAtTheirCanvasPosition()
    {
        var bytes = new PsdFixture
        {
            Width = 10,
            Height = 10,
            Layers =
            {
                PsdLayerFixture.Solid("Patch", 200, 100, 50, a: 255,
                    left: 4, top: 2, right: 7, bottom: 5),
            },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);
        using var baseline = Baseline(result.Document.Scene.Layers[0]);

        // Canvas-sized, so the patch sits where the PSD put it rather than
        // being stretched over the whole frame.
        Assert.Equal(10, baseline.Width);
        Assert.Equal(10, baseline.Height);
        var inside = baseline.GetPixel(5, 3);
        output.WriteLine($"inside=({inside.Red},{inside.Green},{inside.Blue},{inside.Alpha}) "
            + $"outside alpha={baseline.GetPixel(0, 0).Alpha}");
        Assert.Equal(200, inside.Red);
        Assert.Equal(100, inside.Green);
        Assert.Equal(50, inside.Blue);
        Assert.Equal(255, inside.Alpha);
        Assert.Equal(0, baseline.GetPixel(0, 0).Alpha);
        Assert.Equal(0, baseline.GetPixel(9, 9).Alpha);
    }

    [Fact]
    public void ContentOutsideTheCanvasIsClippedRatherThanCrashing()
    {
        // Photoshop happily keeps pixels past the canvas edge.
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers =
            {
                PsdLayerFixture.Solid("Overhang", 10, 20, 30, a: 255,
                    left: 2, top: 2, right: 9, bottom: 9),
            },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);
        using var baseline = Baseline(result.Document.Scene.Layers[0]);

        Assert.Equal(4, baseline.Width);
        Assert.Equal(10, baseline.GetPixel(3, 3).Red);
    }

    [Fact]
    public void AFullyTransparentLayerStillProducesABaseline()
    {
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Empty", 0, 0, 0, a: 0) },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);
        var frame = result.Document.Scene.Layers[0].Cels[0].Frame!;

        Assert.True(frame.HasBaseline);
        Assert.Empty(frame.Strokes);
    }

    [Fact]
    public void ImportedPixelsAreABaselineAndNeverStrokes()
    {
        // Invariant 1's line: a baseline is pixels with no stroke provenance, and
        // nothing here may invent a stroke record for pixels nobody drew.
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Scan", 90, 90, 90, a: 255) },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);
        var frame = result.Document.Scene.Layers[0].Cels[0].Frame!;

        Assert.True(frame.HasBaseline);
        Assert.Empty(frame.Strokes);
        Assert.Null(frame.Placements);
    }

    // ---- layer metadata -------------------------------------------------------

    [Fact]
    public void NameVisibilityOpacityAndBlendModeAllReachTheLayer()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Solid("Shadow", 0, 0, 0, a: 255,
                    blend: "mul ", opacity: 128, visible: false),
            },
        }.Build();

        var layer = PsdDocumentImport.Open(bytes).Document.Scene.Layers[0];

        Assert.Equal("Shadow", layer.Name);
        Assert.False(layer.Visible);
        Assert.Equal(LayerBlendMode.Multiply, layer.BlendMode);
        Assert.InRange(layer.Opacity, 0.50, 0.51);
    }

    [Theory]
    [InlineData("norm", LayerBlendMode.Normal)]
    [InlineData("pass", LayerBlendMode.Normal)]
    [InlineData("scrn", LayerBlendMode.Screen)]
    [InlineData("over", LayerBlendMode.Overlay)]
    [InlineData("idiv", LayerBlendMode.ColorBurn)]
    [InlineData("smud", LayerBlendMode.Exclusion)]
    [InlineData("lum ", LayerBlendMode.Luminosity)]
    public void PhotoshopsBlendKeysBecomeTheMatchingLightboxMode(string key, LayerBlendMode expected)
    {
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Blended", 5, 5, 5, a: 255, blend: key) },
        }.Build();

        var layer = PsdDocumentImport.Open(bytes).Document.Scene.Layers[0];

        Assert.Equal(expected, layer.BlendMode);
    }

    [Fact]
    public void EveryBlendKeyTheReaderWillEmitHasAHomeInTheDocumentModel()
    {
        // The drift guard between the two halves of the mapping: the list of keys
        // lives in Lightbox.Import, which cannot see LayerBlendMode, and the
        // mapping lives in the app. A key added to one and not the other would
        // otherwise composite silently as Normal.
        foreach (var key in PsdBlend.SupportedKeys)
        {
            Assert.NotNull(PsdBlendMap.For(key));
        }
        output.WriteLine($"{PsdBlend.SupportedKeys.Length} keys, all mapped");
    }

    [Fact]
    public void AModeTheReaderRefusesHasNoMappingEither()
    {
        Assert.Null(PsdBlendMap.For("lbrn"));
        Assert.Null(PsdBlendMap.For("nope"));
    }

    // ---- folders --------------------------------------------------------------

    [Fact]
    public void APhotoshopFolderBecomesALayerFolder()
    {
        // Bottom-first, so the closing divider comes before the contents and the
        // header that names the folder comes last.
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Arm", 1, 1, 1, a: 255),
                PsdLayerFixture.Solid("Leg", 2, 2, 2, a: 255),
                PsdLayerFixture.Group("Character", 1),
            },
        }.Build();

        var scene = PsdDocumentImport.Open(bytes).Document.Scene;

        var group = Assert.Single(scene.LayerGroups);
        Assert.Equal("Character", group.Name);
        Assert.Equal(2, scene.Layers.Count);
        Assert.All(scene.Layers, l => Assert.Equal(group.Id, l.GroupId));
    }

    [Fact]
    public void AHiddenFolderHidesTheGroupRatherThanBeingLost()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Rough", 1, 1, 1, a: 255),
                new PsdLayerFixture { Name = "Sketches", SectionType = 1, Visible = false },
            },
        }.Build();

        var scene = PsdDocumentImport.Open(bytes).Document.Scene;

        Assert.False(Assert.Single(scene.LayerGroups).Visible);
    }

    [Fact]
    public void NestedFoldersFlattenAndKeepTheirPathInTheName()
    {
        // Lightbox folders are one level deep. Nesting is organisation rather
        // than image, so it flattens and the path survives in the name.
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</outer>", 3),
                PsdLayerFixture.Group("</inner>", 3),
                PsdLayerFixture.Solid("Eye", 1, 1, 1, a: 255),
                PsdLayerFixture.Group("Head", 1),
                PsdLayerFixture.Group("Character", 1),
            },
        }.Build();

        var scene = PsdDocumentImport.Open(bytes).Document.Scene;

        output.WriteLine(string.Join(", ", scene.LayerGroups.ConvertAll(g => g.Name)));
        Assert.Contains(scene.LayerGroups, g => g.Name.Contains("Head"));
        var eye = Assert.Single(scene.Layers);
        Assert.NotNull(eye.GroupId);
    }

    [Fact]
    public void AFolderThatBlendsAsAGroupIsRefused()
    {
        // Photoshop composites such a folder as one unit before blending it;
        // a Lightbox folder never does, so it would render as something else.
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Glow", 1, 1, 1, a: 255),
                new PsdLayerFixture { Name = "Lighting", SectionType = 1, BlendKey = "scrn" },
            },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdDocumentImport.Open(bytes));

        Assert.Contains(ex.Reasons, r => r.Feature.Contains("blends as a group"));
    }

    [Fact]
    public void AFadedFolderIsRefusedBecauseTheFadeWouldMoveToEachLayer()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Ink", 1, 1, 1, a: 255),
                new PsdLayerFixture { Name = "Faded", SectionType = 1, Opacity = 100 },
            },
        }.Build();

        Assert.Throws<PsdUnsupportedException>(() => PsdDocumentImport.Open(bytes));
    }

    [Fact]
    public void AnOrdinaryFolderAtFullOpacityIsNotRefused()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Ink", 1, 1, 1, a: 255),
                new PsdLayerFixture { Name = "Fine", SectionType = 1, BlendKey = "pass" },
            },
        }.Build();

        var scene = PsdDocumentImport.Open(bytes).Document.Scene;

        Assert.Single(scene.LayerGroups);
    }

    // ---- flattened files and notes --------------------------------------------

    [Fact]
    public void AFlattenedPsdBecomesOneLayer()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            CompositeChannels = 4,
            CompositeFill = [12, 34, 56, 255],
        }.Build();

        var scene = PsdDocumentImport.Open(bytes).Document.Scene;

        var layer = Assert.Single(scene.Layers);
        using var baseline = Baseline(layer);
        Assert.Equal(12, baseline.GetPixel(1, 1).Red);
    }

    [Fact]
    public void ASixteenBitFilesConversionIsReportedRatherThanSilent()
    {
        var bytes = new PsdFixture
        {
            Depth = 16,
            Layers = { PsdLayerFixture.Solid("Deep", 77, 88, 99, a: 255) },
        }.Build();

        var result = PsdDocumentImport.Open(bytes);

        Assert.Contains(result.Notes, n => n.Contains("16 bits"));
    }

    // ---- masks and clipping ---------------------------------------------------

    [Fact]
    public void APhotoshopMaskBecomesALightboxLayerMask()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Masked",
                    Red = Enumerable.Repeat((byte)200, 16).ToArray(),
                    Alpha = Enumerable.Repeat((byte)255, 16).ToArray(),
                    Mask = [0, 255, 255, 0],
                    MaskLeft = 1,
                    MaskTop = 1,
                    MaskRight = 3,
                    MaskBottom = 3,
                    MaskOutside = 255,
                },
            },
        }.Build();

        var layer = PsdDocumentImport.Open(bytes).Document.Scene.Layers[0];

        Assert.NotNull(layer.Mask);
        Assert.True(layer.IsMasked);
        using var coverage = Lightbox.Raster.PngCodec.Decode(layer.Mask!.Frame.PngBase64!);
        // Canvas-sized, the mask's own rect placed inside it, and the default
        // coverage everywhere else.
        Assert.Equal(4, coverage.Width);
        Assert.Equal(0, coverage.GetPixel(1, 1).Alpha);
        Assert.Equal(255, coverage.GetPixel(2, 1).Alpha);
        Assert.Equal(255, coverage.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void AMaskThatHidesEverythingOutsideItselfDoesSo()
    {
        // The half of the mask rectangle that is easy to get wrong: what applies
        // beyond it. Assuming "shows" here would reveal three quarters of a
        // drawing the artist had masked away.
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Keyhole",
                    Red = Enumerable.Repeat((byte)200, 16).ToArray(),
                    Mask = [255],
                    MaskRight = 1,
                    MaskBottom = 1,
                    MaskOutside = 0,
                },
            },
        }.Build();

        var layer = PsdDocumentImport.Open(bytes).Document.Scene.Layers[0];

        using var coverage = Lightbox.Raster.PngCodec.Decode(layer.Mask!.Frame.PngBase64!);
        output.WriteLine($"inside={coverage.GetPixel(0, 0).Alpha}, outside={coverage.GetPixel(3, 3).Alpha}");
        Assert.Equal(255, coverage.GetPixel(0, 0).Alpha);
        Assert.Equal(0, coverage.GetPixel(3, 3).Alpha);
    }

    [Fact]
    public void ADisabledMaskArrivesDisabledRatherThanMissing()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Off",
                    Red = Enumerable.Repeat((byte)9, 16).ToArray(),
                    Mask = [128],
                    MaskRight = 1,
                    MaskBottom = 1,
                    MaskDisabled = true,
                },
            },
        }.Build();

        var layer = PsdDocumentImport.Open(bytes).Document.Scene.Layers[0];

        Assert.NotNull(layer.Mask);
        Assert.True(layer.Mask!.Disabled);
        Assert.False(layer.IsMasked);
    }

    [Fact]
    public void AClippedPhotoshopLayerClipsToTheOneBelow()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Solid("Base", 10, 10, 10, a: 255),
                new PsdLayerFixture
                {
                    Name = "Paint over base",
                    Clipping = true,
                    Red = Enumerable.Repeat((byte)200, 16).ToArray(),
                },
            },
        }.Build();

        var layers = PsdDocumentImport.Open(bytes).Document.Scene.Layers;

        Assert.False(layers[0].IsClipped);
        Assert.True(layers[1].IsClipped);
    }

    [Fact]
    public void ADocumentWithNoMasksOrClippingWritesNeitherKey()
    {
        // Absent, not defaulted: a PSD that used neither must serialize exactly
        // as it did before either was supported.
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Plain", 1, 2, 3, a: 255) },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        var json = Lightbox.Core.Serialization.DocJson.Serialize(doc);

        Assert.DoesNotContain("\"mask\"", json);
        Assert.DoesNotContain("\"clipToBelow\"", json);
        Assert.DoesNotContain("\"disabled\"", json);
    }

    [Fact]
    public void AMaskSurvivesBeingSavedAndReopened()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Masked",
                    Red = Enumerable.Repeat((byte)200, 16).ToArray(),
                    Mask = [64, 128, 192, 255],
                    MaskRight = 2,
                    MaskBottom = 2,
                    Clipping = true,
                },
            },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(doc));

        var layer = reloaded.Scene.Layers[0];
        Assert.True(layer.IsClipped);
        Assert.NotNull(layer.Mask);
        using var before = Lightbox.Raster.PngCodec.Decode(doc.Scene.Layers[0].Mask!.Frame.PngBase64!);
        using var after = Lightbox.Raster.PngCodec.Decode(layer.Mask!.Frame.PngBase64!);
        Assert.Equal(before.GetPixel(1, 1), after.GetPixel(1, 1));
    }

    // ---- what it actually looks like ------------------------------------------

    /// <summary>
    /// Render the imported document the way export does, at frame 0.
    /// </summary>
    private static SKBitmap Rendered(Doc doc)
    {
        using var cache = new FrameBitmapCache();
        using var image = SequenceExporter.RenderFrame(doc, cache, 0);
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        image.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    [Fact]
    public void TheImportedStackRendersWithTheTopLayerOverTheBottomOne()
    {
        // Every other test here reads a layer's baseline directly. None of them
        // put the stack through the renderer, so a baseline that decodes
        // perfectly and composites in the wrong order — or not at all — would
        // pass the lot. This is the one that says the drawing looks right.
        var bytes = new PsdFixture
        {
            Width = 8,
            Height = 8,
            Layers =
            {
                PsdLayerFixture.Solid("Blue base", 0, 0, 255, a: 255, right: 8, bottom: 8),
                PsdLayerFixture.Solid("Red patch", 255, 0, 0, a: 255, right: 4, bottom: 4),
            },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        using var rendered = Rendered(doc);

        var covered = rendered.GetPixel(1, 1);
        var exposed = rendered.GetPixel(6, 6);
        output.WriteLine($"under the patch = {covered}, beside it = {exposed}");
        Assert.Equal(255, covered.Red);
        Assert.Equal(0, covered.Blue);
        Assert.Equal(255, exposed.Blue);
        Assert.Equal(0, exposed.Red);
        Assert.Equal(255, exposed.Alpha);
    }

    [Fact]
    public void AHiddenPhotoshopLayerDoesNotRender()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers =
            {
                PsdLayerFixture.Solid("Notes to self", 255, 0, 0, a: 255, visible: false),
            },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        using var rendered = Rendered(doc);

        Assert.Equal(0, rendered.GetPixel(1, 1).Alpha);
    }

    [Fact]
    public void ALayersOpacityReachesTheRenderedPixels()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers = { PsdLayerFixture.Solid("Half", 0, 0, 0, a: 255, opacity: 128) },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        using var rendered = Rendered(doc);

        var pixel = rendered.GetPixel(1, 1);
        output.WriteLine($"opacity 128/255 rendered as alpha {pixel.Alpha}");
        Assert.InRange(pixel.Alpha, 120, 136);
    }

    [Fact]
    public void ATranslucentPsdLayerKeepsItsAlphaThroughTheRender()
    {
        // The premultiply hand-off: the reader returns unpremultiplied pixels and
        // Skia multiplies when they are drawn onto the document surface. Get that
        // wrong and a half-transparent layer renders at the wrong strength.
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            Layers = { PsdLayerFixture.Solid("Wash", 255, 255, 255, a: 128) },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        using var rendered = Rendered(doc);

        var pixel = rendered.GetPixel(1, 1);
        output.WriteLine($"stored (255,255,255,128) rendered as {pixel}");
        Assert.InRange(pixel.Alpha, 120, 136);
        Assert.InRange(pixel.Red, 250, 255);
    }

    [Fact]
    public void AnImportedPsdCanBeSavedStraightBackOutAsAPng()
    {
        // The two halves of this branch meeting: open a Photoshop file, save it
        // as an ordinary picture, without drawing a stroke in between.
        var bytes = new PsdFixture
        {
            Width = 6,
            Height = 6,
            Layers = { PsdLayerFixture.Solid("Art", 12, 200, 90, a: 255, right: 6, bottom: 6) },
        }.Build();
        var doc = PsdDocumentImport.Open(bytes).Document;
        var dir = Directory.CreateTempSubdirectory("lightbox-psd-png");
        var path = System.IO.Path.Combine(dir.FullName, "out.png");

        try
        {
            SaveAsImage.Write(doc, path);

            using var saved = SKBitmap.Decode(path);
            Assert.Equal(6, saved.Width);
            var pixel = saved.GetPixel(2, 2);
            Assert.Equal(12, pixel.Red);
            Assert.Equal(200, pixel.Green);
            Assert.Equal(90, pixel.Blue);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADocumentFromAPsdSerializesAndComesBackTheSame()
    {
        var bytes = new PsdFixture
        {
            Width = 6,
            Height = 6,
            Layers =
            {
                PsdLayerFixture.Solid("Base", 10, 20, 30, a: 255, right: 6, bottom: 6),
                PsdLayerFixture.Solid("Over", 200, 0, 0, a: 128, left: 1, top: 1, right: 4, bottom: 4),
            },
        }.Build();

        var doc = PsdDocumentImport.Open(bytes).Document;
        var json = Lightbox.Core.Serialization.DocJson.Serialize(doc);
        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(json);

        Assert.Equal(2, reloaded.Scene.Layers.Count);
        using var before = Baseline(doc.Scene.Layers[1]);
        using var after = Baseline(reloaded.Scene.Layers[1]);
        Assert.Equal(before.GetPixel(2, 2), after.GetPixel(2, 2));
    }
}
