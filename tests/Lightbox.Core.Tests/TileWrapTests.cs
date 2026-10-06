using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The seamless-tile record (Q192): what a stroke carries, what it writes,
/// and how it follows the paper.
/// </summary>
public class TileWrapTests
{
    private static Stroke Mark(TileWrap? wrap = null) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#112233",
        Brush = new BrushSettings { Size = 10 },
        Points = [new StrokePoint(190, 50, 1), new StrokePoint(210, 50, 1)],
        Wrap = wrap,
    };

    [Fact]
    public void TheTileIsThePageAndItsNeighboursAreEight()
    {
        var scene = new Scene { Width = 200, Height = 140, OriginX = 16, OriginY = -4 };

        var tile = TileWrap.OfScene(scene);

        Assert.Equal(16, tile.Left);
        Assert.Equal(-4, tile.Top);
        Assert.Equal(200, tile.Width);
        Assert.Equal(140, tile.Height);
        Assert.False(tile.IsIdentity);
        var offsets = tile.Offsets();
        Assert.Equal(8, offsets.Length);
        Assert.Contains((-200.0, 0.0), offsets);
        Assert.Contains((200.0, 140.0), offsets);
        Assert.DoesNotContain((0.0, 0.0), offsets);
    }

    [Fact]
    public void ATileThatCannotWrapIsTheIdentityAndOffersNoCopies()
    {
        Assert.True(new TileWrap { Width = 0, Height = 10 }.IsIdentity);
        Assert.True(new TileWrap { Width = 10, Height = double.PositiveInfinity }.IsIdentity);
        Assert.Empty(new TileWrap { Width = 0, Height = 0 }.Offsets());
    }

    [Fact]
    public void AStrokeDrawnWithoutTilingSerializesNoWrapKey()
    {
        // Optional means absent: a document that never tiled writes no key.
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [Mark()] } }] });

        Assert.DoesNotContain("\"wrap\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void AStrokePaintedAsATileKeepsItAcrossASaveAndReload()
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame { Strokes = [Mark(new TileWrap { Left = 0, Top = 0, Width = 200, Height = 140 })] } }],
        });

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"wrap\"", json);
        // Derived members stay derived — none of them becomes a second key.
        Assert.DoesNotContain("isIdentity", json);
        Assert.DoesNotContain("offsets", json);

        var back = DocJson.Deserialize(json).Scene.Layers[0].Cels[0].Frame!.Strokes[0].Wrap;
        Assert.NotNull(back);
        Assert.Equal(200, back!.Width);
        Assert.Equal(140, back.Height);
    }

    [Fact]
    public void AClonedStrokeOwnsItsTile()
    {
        // The B379 lesson applied before it can bite: an undo snapshot is a
        // clone, and a shared tile would be rescaled along with the live one.
        var source = Mark(new TileWrap { Width = 200, Height = 140 });

        var clone = source.Clone();

        Assert.NotNull(clone.Wrap);
        Assert.NotSame(source.Wrap, clone.Wrap);
        Assert.Equal(200, clone.Wrap!.Width);
    }

    [Fact]
    public void AnImageResizeMovesTheTileWithThePaper()
    {
        var doc = new Doc();
        doc.Scene.Width = 200;
        doc.Scene.Height = 140;
        var stroke = Mark(new TileWrap { Left = 0, Top = 0, Width = 200, Height = 140 });
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [stroke] } }] });

        Assert.True(ImageResize.Apply(doc, 400, 420, new NoResampler()));

        Assert.Equal(400, stroke.Wrap!.Width);
        Assert.Equal(420, stroke.Wrap.Height);
        Assert.Equal(0, stroke.Wrap.Left);
    }

    private sealed class NoResampler : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => pngBase64;
    }
}
