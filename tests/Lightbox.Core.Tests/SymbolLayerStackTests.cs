using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using System.Text.Json;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q171, step one: a symbol owns a layer stack, and a file only says so when
/// there is one.
/// </summary>
/// <remarks>
/// The record change, and nothing else — no compositing, no UI. What has to
/// hold here is that every symbol already on disc means exactly what it meant,
/// and that the new axis costs a project that never uses it nothing at all.
/// </remarks>
public class SymbolLayerStackTests
{
    private static Stroke Line(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#101010",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 10, y + 10, 1)],
    };

    private static string Json(Symbol symbol) => JsonSerializer.Serialize(symbol, DocJson.Options);

    private static Symbol Read(string json) =>
        JsonSerializer.Deserialize<Symbol>(json, DocJson.Options)!;

    // ---- the axis --------------------------------------------------------------

    [Fact]
    public void TheFrameCountIsTheLongestLayerNotTheSumOfThem()
    {
        // The bug this replaces went the other way: it flattened the stack into
        // the frame list, so two layers of three drawings read as a six-frame
        // animation. A colour layer holding one drawing under a three-frame line
        // test is a three-frame symbol.
        var symbol = new Symbol
        {
            Layers =
            [
                new Layer { Name = "Colour", Cels = [new Cel { Frame = new Frame() }] },
                new Layer
                {
                    Name = "Lines",
                    Cels =
                    [
                        new Cel { Frame = new Frame() },
                        new Cel { Frame = new Frame() },
                        new Cel { Frame = new Frame() },
                    ],
                },
            ],
        };

        Assert.Equal(3, symbol.FrameCount);
    }

    [Fact]
    public void FramesAtIsTheStackBottomFirstAndNotTheWholeSymbol()
    {
        var bottom = new Frame { Strokes = [Line(0, 0)] };
        var top = new Frame { Strokes = [Line(50, 50)] };
        var symbol = new Symbol
        {
            Layers =
            [
                new Layer { Name = "Colour", Cels = [new Cel { Frame = bottom }] },
                new Layer { Name = "Lines", Cels = [new Cel { Frame = top }] },
            ],
        };

        Assert.Equal([bottom, top], symbol.FramesAt(0));
    }

    [Fact]
    public void AHiddenLayerShowsNothingAndStillCounts()
    {
        // Hidden is a render decision, not a timing one: turning the colour off
        // must not make the animation a different length.
        var symbol = new Symbol
        {
            Layers =
            [
                new Layer { Name = "Colour", Visible = false, Cels = [new Cel { Frame = new Frame() }, new Cel { Frame = new Frame() }] },
                new Layer { Name = "Lines", Cels = [new Cel { Frame = new Frame() }] },
            ],
        };

        Assert.Single(symbol.FramesAt(0));
        Assert.Equal(2, symbol.FrameCount);
    }

    // ---- the file --------------------------------------------------------------

    [Fact]
    public void ASingleLayerSymbolWritesNoLayersKey()
    {
        // The rule every optional block in this model follows: absent unless
        // used. A project that never puts a second layer in a symbol must not
        // grow a key, and must not have its files rewritten on the next save.
        var symbol = new Symbol { Name = "Sword", Layers = Symbol.Flat("Sword", [new Frame { Strokes = [Line(0, 0)] }]) };

        var json = Json(symbol);

        Assert.DoesNotContain("\"layers\"", json);
        Assert.Contains("\"frames\"", json);
    }

    [Fact]
    public void AStackWritesLayersAndComesBackWhole()
    {
        var symbol = new Symbol
        {
            Name = "Head",
            Layers =
            [
                new Layer { Name = "Colour", Opacity = 0.5, Cels = [new Cel { Frame = new Frame { Strokes = [Line(0, 0)] } }] },
                new Layer { Name = "Lines", Cels = [new Cel { Frame = new Frame { Strokes = [Line(20, 20)] } }] },
            ],
        };

        var read = Read(Json(symbol));

        Assert.Contains("\"layers\"", Json(symbol));
        Assert.Equal(2, read.Layers.Count);
        Assert.Equal("Colour", read.Layers[0].Name);
        Assert.Equal(0.5, read.Layers[0].Opacity);
        Assert.Equal("Lines", read.Layers[1].Name);
    }

    [Fact]
    public void AnOldFlatSymbolLoadsAsOneLayer()
    {
        // The shape every symbol on disc has. It has to come back as the same
        // object a one-layer symbol is, or the two would render differently.
        const string old = """
            {"id":"sym_1","name":"Sword","kind":"prop","fps":12,"version":3,
             "frames":[{"strokes":[]},{"strokes":[]}]}
            """;

        var read = Read(old);

        Assert.Single(read.Layers);
        Assert.Equal(2, read.Layers[0].Cels.Count);
        Assert.Equal(2, read.FrameCount);
        Assert.Equal(3, read.Version);
        Assert.Equal(SymbolKind.Prop, read.Kind);
    }

    [Fact]
    public void AnOldFlatSymbolRoundTripsBackToTheOldShape()
    {
        // The half that makes the read worth having: loading and saving an
        // existing project must not rewrite its symbol files.
        const string old = """
            {"id":"sym_1","name":"Sword","kind":"prop","fps":12,"version":1,
             "frames":[{"strokes":[]}]}
            """;

        var json = Json(Read(old));

        Assert.DoesNotContain("\"layers\"", json);
    }

    // ---- the clips a capture brings with it (Q173) -------------------------------

    [Fact]
    public void ASymbolThatClipsNothingWritesNoClipRegionsKey()
    {
        // Absent unless used. A symbol made from whole lines references no
        // region, so a project full of them grows no key.
        var symbol = new Symbol { Name = "Sword", Layers = Symbol.Flat("Sword", [new Frame()]) };

        Assert.DoesNotContain("\"clipRegions\"", Json(symbol));
    }

    /// <summary>A symbol carries the clips its strokes name, through a round trip.</summary>
    /// <remarks>
    /// Q173: clip regions otherwise live on the document and reach the renderer
    /// from whichever one is open. A symbol is placed into documents it was not
    /// made in, so the region has to travel with it or the sword resolves its
    /// clip against a stranger's shapes.
    /// </remarks>
    [Fact]
    public void ASymbolCarriesTheClipRegionsItsStrokesName()
    {
        // Clip regions are content-hashed and keyed by that hash, so the id is
        // the dictionary key rather than a field on the region.
        const string clipId = "clip_deadbeef";
        var region = new ClipRegion
        {
            Contours = [[new StrokePoint(0, 0, 1), new StrokePoint(10, 0, 1), new StrokePoint(10, 10, 1)]],
        };
        var stroke = Line(0, 0);
        stroke.ClipId = clipId;
        var symbol = new Symbol
        {
            Name = "Sword",
            Layers = Symbol.Flat("Sword", [new Frame { Strokes = [stroke] }]),
            ClipRegions = new Dictionary<string, ClipRegion> { [clipId] = region },
        };

        var json = Json(symbol);
        var read = Read(json);

        Assert.Contains("\"clipRegions\"", json);
        Assert.True(read.HasClipRegions);
        Assert.True(read.ClipRegions!.ContainsKey(clipId));
        Assert.Equal(clipId, read.AllFrames.First().Strokes[0].ClipId);
    }

    [Fact]
    public void ClipRegionsAreIndependentOfTheLayersOrFramesChoice()
    {
        // The two keys answer different questions: one is how the drawings are
        // arranged, the other is what they are clipped by. A one-layer symbol
        // that clips something still writes the old shape for its drawings.
        const string clipId = "clip_cafe";
        var region = new ClipRegion { Contours = [[new StrokePoint(0, 0, 1)]] };
        var stroke = Line(0, 0);
        stroke.ClipId = clipId;
        var symbol = new Symbol
        {
            Name = "Sword",
            Layers = Symbol.Flat("Sword", [new Frame { Strokes = [stroke] }]),
            ClipRegions = new Dictionary<string, ClipRegion> { [clipId] = region },
        };

        var json = Json(symbol);

        Assert.Contains("\"frames\"", json);
        Assert.DoesNotContain("\"layers\"", json);
        Assert.Contains("\"clipRegions\"", json);
    }

    [Fact]
    public void ALayerCarryingIntentIsWrittenAsAStackEvenAloneable()
    {
        // One layer is not enough on its own to justify the flat shape: a layer
        // that has been hidden or faded says something the old shape has nowhere
        // to put, and writing it flat would drop it on the next save.
        var symbol = new Symbol { Name = "Sword", Layers = Symbol.Flat("Sword", [new Frame()]) };
        symbol.Layers[0].Opacity = 0.25;

        var json = Json(symbol);

        Assert.Contains("\"layers\"", json);
        Assert.Equal(0.25, Read(json).Layers[0].Opacity);
    }
}
