using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Palettes belong to the document, and a swatch has an identity so art can
/// reference it rather than copy it. Interop is GIMP's .gpl, which is the one
/// palette format every other tool reads.
/// </summary>
public class PaletteTests
{
    private static Palette Sample() => new()
    {
        Name = "Knight",
        Columns = 4,
        Swatches =
        [
            new Swatch { Color = "#ff0000", Name = "Cape" },
            new Swatch { Color = "#204080", Name = "Armour shadow" },
            new Swatch { Color = "#ffffff" },
        ],
    };

    [Fact]
    public void SwatchesGetStableIdentities()
    {
        var palette = Sample();
        var ids = palette.Swatches.Select(s => s.Id).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void APaletteRoundTripsThroughTheDocument()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Palettes.Clear();   // just the one under test
        doc.Palettes.Add(Sample());
        var id = doc.Palettes[0].Swatches[1].Id;

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var palette = Assert.Single(reloaded.Palettes);

        Assert.Equal("Knight", palette.Name);
        Assert.Equal(4, palette.Columns);
        Assert.Equal(3, palette.Swatches.Count);
        Assert.Equal("Armour shadow", palette.Swatches[1].Name);
        // The identity has to survive, or every reference to it breaks.
        Assert.Equal(id, palette.Swatches[1].Id);
    }

    [Fact]
    public void ANewDocumentStartsWithBlackAndWhite()
    {
        // The one place "absent unless asked for" does not apply. A swatch is
        // not a feature you opt into — it is the difference between a stroke
        // that carries a colour and one that carries a reference, and only the
        // second can be recoloured later. An empty palette means the first
        // hour of work can never follow a palette edit.
        var palette = Assert.Single(DocumentFactory.CreateDoc(100, 100, 12).Palettes);

        Assert.Equal(["Black", "White"], palette.Swatches.Select(s => s.Name));
        Assert.Equal("#000000", palette.Swatches[0].Color);
        Assert.Equal("#ffffff", palette.Swatches[1].Color);
    }

    [Fact]
    public void GplRoundTripsNamesAndColours()
    {
        var text = GimpPalette.Write(Sample());
        var back = GimpPalette.Read(text);

        Assert.Equal("Knight", back.Name);
        Assert.Equal(4, back.Columns);
        Assert.Equal(3, back.Swatches.Count);
        Assert.Equal("#ff0000", back.Swatches[0].Color);
        Assert.Equal("Cape", back.Swatches[0].Name);
        Assert.Equal("#204080", back.Swatches[1].Color);
        Assert.Null(back.Swatches[2].Name);
    }

    [Fact]
    public void GplWritesTheHeaderOtherToolsLookFor()
    {
        var text = GimpPalette.Write(Sample());
        Assert.StartsWith("GIMP Palette\n", text);
        Assert.Contains("Name: Knight\n", text);
        Assert.Contains("Columns: 4\n", text);
    }

    [Theory]
    // Runs of spaces rather than a tab, which plenty of files in the wild use.
    [InlineData("GIMP Palette\nName: X\n#\n  0   0   0\tBlack\n255 255 255\tWhite\n")]
    // CRLF.
    [InlineData("GIMP Palette\r\nName: X\r\n#\r\n0 0 0\tBlack\r\n255 255 255\tWhite\r\n")]
    // No header at all, and stray comments.
    [InlineData("# exported by something\n0 0 0 Black\n# a note\n255 255 255 White\n")]
    public void GplReadingIsForgivingOfRealFiles(string text)
    {
        var palette = GimpPalette.Read(text);
        Assert.Equal(2, palette.Swatches.Count);
        Assert.Equal("#000000", palette.Swatches[0].Color);
        Assert.Equal("#ffffff", palette.Swatches[1].Color);
        Assert.Equal("Black", palette.Swatches[0].Name);
    }

    [Fact]
    public void AMultiWordSwatchNameSurvives()
    {
        var palette = GimpPalette.Read("0 0 0\tdeep shadow blue\n");
        Assert.Equal("deep shadow blue", palette.Swatches[0].Name);
    }

    [Fact]
    public void GarbageLinesAreSkippedRatherThanThrowing()
    {
        var palette = GimpPalette.Read("GIMP Palette\nName: X\n#\nnot a colour\n1 2\n10 20 30\n");
        var swatch = Assert.Single(palette.Swatches);
        Assert.Equal("#0a141e", swatch.Color);
    }

    [Fact]
    public void WithoutAColumnsHeaderAReasonableGridIsChosen()
    {
        var palette = GimpPalette.Read(string.Concat(Enumerable.Repeat("1 2 3\n", 16)));
        Assert.Equal(16, palette.Swatches.Count);
        Assert.Equal(4, palette.Columns);
    }
}
