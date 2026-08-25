using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// What a document holds when somebody has typed in it — and, more to the
/// point, what it holds when nobody has.
/// </summary>
/// <remarks>
/// Text adds two blocks to <see cref="Doc"/> and one key to every stroke, which
/// is three chances to break *optional means absent*. A document that has never
/// been typed in must serialize exactly as it did before the text tool existed.
/// </remarks>
public class TextRecordTests
{
    private static Doc Drawn()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Points = [new(10, 20, 0.5), new(30, 40, 0.8)],
        });
        return doc;
    }

    private static TextElement Element() => new()
    {
        Id = "txt1",
        Text = "TITLE",
        Size = 72,
        X = 100,
        Y = 140,
        Align = TextAlign.Centre,
        Tracking = 40,
        Font = new FontRef { Family = "Inter", Weight = 700 },
    };

    [Fact]
    public void ADocumentNobodyHasTypedInWritesNoTextKeys()
    {
        var json = DocJson.Serialize(Drawn());

        Assert.DoesNotContain("\"texts\"", json);
        Assert.DoesNotContain("\"fonts\"", json);
        Assert.DoesNotContain("\"textId\"", json);
    }

    [Fact]
    public void ATextElementSurvivesASaveAndReload()
    {
        var doc = Drawn();
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = Element() };

        var text = DocJson.Deserialize(DocJson.Serialize(doc)).Texts!["txt1"];

        Assert.Equal("TITLE", text.Text);
        Assert.Equal(72, text.Size);
        Assert.Equal(100, text.X);
        Assert.Equal(140, text.Y);
        Assert.Equal(TextAlign.Centre, text.Align);
        Assert.Equal(40, text.Tracking);
        Assert.Equal("Inter", text.Font.Family);
        Assert.Equal(700, text.Font.Weight);
        Assert.False(text.Font.Italic);
    }

    [Fact]
    public void TypeNobodyHasReLedWritesNoLineHeight()
    {
        // Null means "the typeface decides", which is not the same as any number
        // this application could pick — see TextElement.LineHeight.
        var doc = Drawn();
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = Element() };

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"lineHeight\"", json);
        Assert.Null(DocJson.Deserialize(json).Texts!["txt1"].LineHeight);
    }

    [Fact]
    public void AGlyphStrokeCarriesItsElementBackAcrossASave()
    {
        var doc = Drawn();
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = Element() };
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Text,
            TextId = "txt1",
            Points = [new(0, 0, 1), new(10, 0, 1), new(10, 10, 1)],
            Holes = [[new(2, 2, 1), new(8, 2, 1), new(8, 8, 1)]],
        });

        var restored = DocJson.Deserialize(DocJson.Serialize(doc));
        var glyph = restored.Scene.Layers[0].Cels[0].Frame!.Strokes[^1];

        Assert.Equal(ToolKind.Text, glyph.Tool);
        Assert.Equal("txt1", glyph.TextId);
        Assert.Single(glyph.Holes!);
    }

    [Fact]
    public void AFontOnlyNamedIsNotAFontCarried()
    {
        // The ordinary case for an installed face: the document says which font
        // it was, and carries nothing. See EmbeddedFont for why that is the
        // default rather than a limitation.
        var doc = Drawn();
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = Element() };

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"family\": \"Inter\"", json);
        Assert.DoesNotContain("\"embeddedId\"", json);
        Assert.DoesNotContain("\"fonts\"", json);
    }

    [Fact]
    public void ACarriedFontRoundTripsWithTheLicenceThatLetItTravel()
    {
        var doc = Drawn();
        var element = Element();
        element.Font.EmbeddedId = "f1";
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = element };
        doc.Fonts = new Dictionary<string, EmbeddedFont>
        {
            ["f1"] = new()
            {
                Family = "Inter",
                Weight = 700,
                Licence = "OFL-1.1",
                Source = "google",
                Data = Convert.ToBase64String([0, 1, 2, 3]),
            },
        };

        var restored = DocJson.Deserialize(DocJson.Serialize(doc));
        var font = restored.Fonts!["f1"];

        Assert.Equal("f1", restored.Texts!["txt1"].Font.EmbeddedId);
        Assert.Equal("OFL-1.1", font.Licence);
        Assert.Equal("google", font.Source);
        Assert.Equal([0, 1, 2, 3], Convert.FromBase64String(font.Data));
    }

    [Fact]
    public void CloningADocumentCopiesItsTypeRatherThanSharingIt()
    {
        var doc = Drawn();
        doc.Texts = new Dictionary<string, TextElement> { ["txt1"] = Element() };

        var copy = doc.Clone();
        copy.Texts!["txt1"].Text = "CHANGED";
        copy.Texts["txt1"].Font.Family = "Something Else";

        Assert.Equal("TITLE", doc.Texts["txt1"].Text);
        Assert.Equal("Inter", doc.Texts["txt1"].Font.Family);
    }

    [Fact]
    public void ContourKindsAnswerInOnePlace()
    {
        // The registry that stopped six copies of "is Fill or ClearRegion" from
        // each having to learn about type separately.
        Assert.True(ToolKind.Fill.FillsAContour());
        Assert.True(ToolKind.ClearRegion.FillsAContour());
        Assert.True(ToolKind.Text.FillsAContour());
        Assert.False(ToolKind.Brush.FillsAContour());
        Assert.False(ToolKind.Eraser.FillsAContour());
        Assert.False(ToolKind.Gradient.FillsAContour());
    }
}
