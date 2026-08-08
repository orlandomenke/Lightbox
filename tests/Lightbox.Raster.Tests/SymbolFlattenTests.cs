using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Pillar 3, step three: an exported document that places a symbol has to
/// stand on its own.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece the design flagged as the one that rots silently, so the
/// tests are pixel comparisons rather than shape checks: "the symbol was
/// copied" passes for a flatten that copies the wrong symbol, and the failure
/// would only surface as a file somebody had already sent to a client.
/// </para>
/// <para>
/// <b>The design changed here, and it is worth saying why.</b> It originally
/// said flatten should inline a placement "as ordinary strokes". It cannot:
/// baking the placement's transform into the strokes means multiplying their
/// coordinates, and every dab dynamic is seeded by <c>Hash01</c> from the bits
/// of a dab position — so the flattened sword would come out with different
/// scatter, size and colour jitter from the one the artist approved. The
/// symbols travel with the document instead, which makes it self-contained
/// without making it a different drawing. Invariant 2 outranks the pillar, and
/// the design said so in advance.
/// </para>
/// </remarks>
[Collection("Registries")]
public class SymbolFlattenTests : IDisposable
{
    private const int W = 160, H = 120;

    public SymbolFlattenTests()
    {
        SymbolRegistry.Clear();
        PaletteRegistry.Clear();
    }

    public void Dispose()
    {
        SymbolRegistry.Clear();
        PaletteRegistry.Clear();
    }

    private static Stroke Jittery(double x, double y, string? swatchId = null, string literal = "#c02040") => new()
    {
        Tool = ToolKind.Brush,
        Color = literal,
        SwatchId = swatchId,
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 30, y + 20, 1)],
        Brush = new BrushSettings
        {
            Size = 12, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.15,
            Scatter = 0.6, SizeJitter = 0.5, HueJitter = 0.3,
        },
    };

    private static Symbol Sword(string? swatchId = null) => new()
    {
        Name = "Sword",
        PivotX = 10,
        PivotY = 10,
        Frames = [new Frame { Strokes = [Jittery(10, 10, swatchId, "#000000")] }],
    };

    private static Doc DocPlacing(Symbol symbol, double x = 70, double y = 50)
    {
        var doc = DocumentFactory.CreateDoc(W, H, 12);
        doc.Palettes.Clear();
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Placements = [new SymbolPlacement { SymbolId = symbol.Id, X = x, Y = y }];
        return doc;
    }

    /// <summary>Render exactly as the app does: through the frame materializer.</summary>
    private static byte[] Render(Doc doc)
    {
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        using var bmp = FrameRasterizer.Materialize(frame, W, H);
        return bmp.GetPixelSpan().ToArray();
    }

    private static bool AnyInk(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) return true;
        }
        return false;
    }

    [Fact]
    public void AFlattenedDocumentRendersItsPlacementsWithTheProjectGone()
    {
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        var doc = DocPlacing(sword);

        SymbolRegistry.Reset(project.Symbols);
        var inProject = Render(doc);
        Assert.True(AnyInk(inProject));

        var exported = ProjectIo.Flatten(doc, project);

        // The project is gone entirely — the file has to carry what it needs.
        SymbolRegistry.Clear();
        SymbolRegistry.Reset(exported.Symbols!);
        Assert.Equal(inProject, Render(exported));
    }

    [Fact]
    public void WithoutFlatteningTheSameExportWouldRenderNothing()
    {
        // The counter-test. Without it the one above could pass because the
        // placement drew nothing in both cases.
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        var doc = DocPlacing(sword);

        SymbolRegistry.Reset(project.Symbols);
        var inProject = Render(doc);

        SymbolRegistry.Clear();
        var orphaned = Render(doc);

        Assert.NotEqual(inProject, orphaned);
        Assert.False(AnyInk(orphaned));
    }

    [Fact]
    public void ASymbolsOwnSwatchesTravelWithIt()
    {
        // The subtle one. A symbol's strokes reference project swatches like
        // any others, so flattening the document without walking the symbol's
        // strokes exports a sword painted in whatever literal its strokes
        // happen to carry — here deliberately black rather than green.
        var swatch = new Swatch { Color = "#20c040" };
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        project.Palettes.Add(new Palette { Swatches = [swatch] });
        var sword = Sword(swatch.Id);
        project.Symbols[sword.Id] = sword;
        var doc = DocPlacing(sword);

        PaletteRegistry.Reset(project.Palettes, project.Gradients);
        SymbolRegistry.Reset(project.Symbols);
        var inProject = Render(doc);

        var exported = ProjectIo.Flatten(doc, project);

        PaletteRegistry.Clear();
        SymbolRegistry.Clear();
        PaletteRegistry.Register(exported.Palettes);
        SymbolRegistry.Reset(exported.Symbols!);
        Assert.Equal(inProject, Render(exported));
        Assert.Contains(exported.Palettes.SelectMany(p => p.Swatches), s => s.Id == swatch.Id);
    }

    [Fact]
    public void ADocumentThatPlacesNothingCarriesNoSymbols()
    {
        // Optional means absent, at the export boundary too.
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        var doc = DocumentFactory.CreateDoc(W, H, 12);

        var exported = ProjectIo.Flatten(doc, project);

        Assert.Null(exported.Symbols);
        Assert.False(exported.HasSymbols);
    }

    [Fact]
    public void OnlyTheSymbolsTheDocumentPlacesTravel()
    {
        // An exported walk cycle should not arrive carrying every prop in the
        // production — the same reason the palette flatten inlines only the
        // swatches the document uses.
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        var unused = Sword();
        project.Symbols[sword.Id] = sword;
        project.Symbols[unused.Id] = unused;

        var exported = ProjectIo.Flatten(DocPlacing(sword), project);

        Assert.Equal([sword.Id], exported.Symbols!.Keys);
    }

    [Fact]
    public void AFlattenedDocumentSurvivesASaveAndReload()
    {
        // The whole point of flattening is a file, so the round trip through
        // JSON is part of the claim rather than a separate concern.
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        var doc = DocPlacing(sword);

        SymbolRegistry.Reset(project.Symbols);
        var inProject = Render(doc);

        var exported = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(ProjectIo.Flatten(doc, project)));

        SymbolRegistry.Clear();
        SymbolRegistry.Reset(exported.Symbols!);
        Assert.Equal(inProject, Render(exported));
    }

    [Fact]
    public void ASymbolTheDocumentAlreadyCarriesStillHasItsSwatchesWalked()
    {
        // The document format is plain JSON so an agent can write any part of
        // it, which means a document can arrive already carrying a symbol —
        // one whose strokes reference a project swatch that has not been
        // inlined yet. Flatten has to walk it like any other, or the export
        // gets the literal colour instead of the painted one.
        var swatch = new Swatch { Color = "#20c040" };
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        project.Palettes.Add(new Palette { Swatches = [swatch] });
        var sword = Sword(swatch.Id);
        var doc = DocPlacing(sword);
        doc.Symbols = new Dictionary<string, Symbol> { [sword.Id] = sword };

        var exported = ProjectIo.Flatten(doc, project);

        Assert.Contains(exported.Palettes.SelectMany(p => p.Swatches), s => s.Id == swatch.Id);
    }

    [Fact]
    public void FlatteningAFlattenedDocumentKeepsWhatTravelledTheFirstTime()
    {
        // Exporting an already-exported file must not drop its symbols just
        // because the project it is being flattened against never had them.
        var project = ProjectIo.Create("Knight", "/tmp/unused.lbproj");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        var once = ProjectIo.Flatten(DocPlacing(sword), project);

        var empty = ProjectIo.Create("Other", "/tmp/unused2.lbproj");
        var twice = ProjectIo.Flatten(once, empty);

        Assert.True(twice.HasSymbols);
        Assert.Contains(sword.Id, twice.Symbols!.Keys);
    }
}
