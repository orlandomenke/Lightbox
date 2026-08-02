using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The escape hatch that keeps invariant 1 honest.
///
/// Inside a project, strokes reference shared swatches and gradients by id, so
/// the <em>project</em> is what re-renders — a lone animation file does not.
/// <c>ProjectIo.Flatten</c> is the boundary where that stops being true: an
/// exported document must carry everything it needs and render identically
/// with the project gone.
///
/// These are pixel comparisons rather than shape checks on purpose. A test
/// that only asserts "the swatch was copied" passes for a flatten that copies
/// the wrong swatch, and the failure would surface as art that quietly changed
/// colour after export.
/// </summary>
[Collection("Registries")]
public class ProjectFlattenTests : IDisposable
{
    private const int W = 120, H = 80;

    public ProjectFlattenTests() => PaletteRegistry.Clear();

    public void Dispose() => PaletteRegistry.Clear();

    private static Stroke Bar(string? swatchId, string literal) => new()
    {
        Tool = ToolKind.Brush,
        Color = literal,
        SwatchId = swatchId,
        Points = [new StrokePoint(20, 40, 1), new StrokePoint(100, 40, 1)],
        Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static byte[] Render(Doc doc)
    {
        var strokes = doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<PaintedFrame>()
            .SelectMany(f => f.Strokes)
            .ToList();
        using var bmp = FrameRasterizer.Rasterize(strokes, W, H);
        return bmp.GetPixelSpan().ToArray();
    }

    private static Doc DocWith(params Stroke[] strokes)
    {
        var doc = DocumentFactory.CreateDoc(W, H, 12);
        // What flatten does with the PROJECT's palettes is the subject here.
        // A document's own starting black-and-white would be carried across
        // by definition, and saying so in every assertion adds nothing.
        doc.Palettes.Clear();
        ((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.AddRange(strokes);
        return doc;
    }

    [Fact]
    public void AFlattenedDocumentRendersIdenticallyWithTheProjectGone()
    {
        // The literal on the stroke is deliberately WRONG, so anything that
        // falls back to it instead of resolving the swatch produces a
        // different image and this test fails.
        var swatch = new Swatch { Color = "#20c040" };
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        project.Palettes.Add(new Palette { Swatches = [swatch] });
        var doc = DocWith(Bar(swatch.Id, "#000000"));

        // In the project: the shared palette is what the engine resolves.
        PaletteRegistry.Reset(project.Palettes, project.Gradients);
        var inProject = Render(doc);
        Assert.Equal(new SKColor(0x20, 0xc0, 0x40), ColourAt(inProject, 60, 40));

        var exported = ProjectIo.Flatten(doc, project);

        // Out of it: the project is gone entirely, and the exported file has to
        // stand on its own.
        PaletteRegistry.Clear();
        PaletteRegistry.Register(exported.Palettes);
        Assert.Equal(inProject, Render(exported));
    }

    [Fact]
    public void WithoutFlatteningTheSameExportWouldRenderDifferently()
    {
        // The counter-test. Without it, the one above could pass because the
        // stroke's literal happened to match — it does not, and this proves it.
        var swatch = new Swatch { Color = "#20c040" };
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        project.Palettes.Add(new Palette { Swatches = [swatch] });
        var doc = DocWith(Bar(swatch.Id, "#000000"));

        PaletteRegistry.Reset(project.Palettes, project.Gradients);
        var inProject = Render(doc);

        PaletteRegistry.Clear(); // the project is gone and nothing was inlined
        Assert.NotEqual(inProject, Render(doc));
        Assert.Equal(SKColors.Black, ColourAt(Render(doc), 60, 40));
    }

    [Fact]
    public void AFlattenedGradientRendersIdenticallyToo()
    {
        var gradient = new Gradient
        {
            Stops =
            [
                new GradientStop { Position = 0, Color = "#ff0000" },
                new GradientStop { Position = 1, Color = "#0000ff" },
            ],
        };
        var project = ProjectIo.Create("P", "/tmp/unused.lbproj");
        project.Gradients[gradient.Id] = gradient;

        var doc = DocWith(new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Points = [new StrokePoint(0, 40, 1), new StrokePoint(W, 40, 1)],
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
        });

        PaletteRegistry.Reset(project.Palettes, project.Gradients);
        var inProject = Render(doc);

        var exported = ProjectIo.Flatten(doc, project);
        PaletteRegistry.Clear();
        PaletteRegistry.Register(exported.Gradients);
        Assert.Equal(inProject, Render(exported));
    }

    [Fact]
    public void ADocumentThatReferencesNothingSharedFlattensToItself()
    {
        // A document painted with literal colours must not gain keys it never
        // needed — the same "optional means absent" discipline the camera has.
        var project = ProjectIo.Create("P", "/tmp/unused.lbproj");
        project.Palettes.Add(new Palette { Swatches = [new Swatch { Color = "#ff0000" }] });
        var doc = DocWith(Bar(null, "#3070b0"));

        var exported = ProjectIo.Flatten(doc, project);

        Assert.Empty(exported.Palettes);
        Assert.Empty(exported.Gradients);
        Assert.Equal(Render(doc), Render(exported));
    }

    private static SKColor ColourAt(byte[] pixels, int x, int y)
    {
        var i = (y * W + x) * 4;
        return new SKColor(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }
}
