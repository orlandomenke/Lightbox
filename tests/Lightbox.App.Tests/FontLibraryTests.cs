using System.Net;
using System.Net.Http;
using System.Text;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster.Text;

namespace Lightbox.App.Tests;

/// <summary>
/// The two ways a font gets here, and the one rule about which of them may
/// travel inside a document.
/// </summary>
/// <remarks>
/// <b>Nothing in this file touches the network.</b> Every Google response is
/// stubbed, which is the only way this could run in CI and also the only way the
/// interesting cases — the catalogue endpoint changing shape, an offline
/// machine, a family offered only as woff2 — can be tested at all.
/// </remarks>
public class FontLibraryTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(), "lightbox-fonts-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        FontRegistry.Clear();
        if (Directory.Exists(_cache)) Directory.Delete(_cache, recursive: true);
    }

    /// <summary>A stubbed Google, answering by URL.</summary>
    private sealed class Stub(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancel)
        {
            Asked.Add(request.RequestUri!);
            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private const string Catalogue = """
    {
      "axisRegistry": [],
      "familyMetadataList": [
        { "family": "Inter", "category": "Sans Serif", "license": "OFL",
          "fonts": { "400": {}, "700": {}, "400i": {} } },
        { "family": "Roboto", "category": "Sans Serif", "license": "Apache2",
          "fonts": { "400": {} } },
        { "family": "Ubuntu", "category": "Sans Serif", "license": "UFL",
          "fonts": { "300": {} } },
        { "family": "Mystery", "category": "Display", "license": "Something New",
          "fonts": { "400": {} } },
        { "family": "Bare", "category": "Display" }
      ]
    }
    """;

    private static readonly byte[] FontBytes = [0x00, 0x01, 0x00, 0x00, 0x42];

    private const string Css = """
    @font-face {
      font-family: 'Inter';
      font-style: normal;
      font-weight: 400;
      src: url(https://fonts.gstatic.com/s/inter/v13/abcdef.ttf) format('truetype');
    }
    """;

    private GoogleFontSource Google(Func<Uri, HttpResponseMessage> answer, out Stub stub)
    {
        stub = new Stub(answer);
        return new GoogleFontSource(stub, _cache);
    }

    private static HttpResponseMessage Answer(Uri uri) =>
        uri.Host switch
        {
            "fonts.google.com" => Ok(Catalogue),
            "fonts.googleapis.com" => Ok(Css),
            _ => Bytes(FontBytes),
        };

    [Fact]
    public void TheCatalogueBecomesFacesWithTheLicenceThatLetsThemTravel()
    {
        var faces = GoogleFontSource.ParseCatalogue(Catalogue);

        var inter = faces.Where(f => f.Family == "Inter").ToList();
        Assert.Equal(3, inter.Count);
        Assert.Contains(inter, f => f is { Weight: 700, Italic: false });
        Assert.Contains(inter, f => f is { Weight: 400, Italic: true });
        Assert.All(inter, f => Assert.Equal("OFL-1.1", f.Licence));

        Assert.Equal("Apache-2.0", faces.Single(f => f.Family == "Roboto").Licence);
        Assert.Equal("UFL-1.0", faces.Single(f => f.Family == "Ubuntu").Licence);
    }

    [Fact]
    public void ALicenceNobodyTaughtItAboutMeansUsableButNotCarryable()
    {
        var mystery = GoogleFontSource.ParseCatalogue(Catalogue).Single(f => f.Family == "Mystery");

        Assert.Null(mystery.Licence);
        Assert.False(mystery.Embeddable);
    }

    [Fact]
    public void AFamilyListingNoStylesIsStillOffered()
    {
        // The endpoint is not a published contract, so a missing field is a
        // family that loses its styles rather than a browser that loses a family.
        var bare = GoogleFontSource.ParseCatalogue(Catalogue).Single(f => f.Family == "Bare");

        Assert.Equal(400, bare.Weight);
        Assert.False(bare.Italic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"familyMetadataList\": \"surprise\"}")]
    [InlineData(")]}'\n{\"familyMetadataList\": []}")]
    public void ACatalogueItCannotReadIsNoFontsRatherThanACrash(string body) =>
        Assert.Empty(GoogleFontSource.ParseCatalogue(body));

    [Fact]
    public void TheStylesheetGivesUpItsTrueTypeUrl()
    {
        Assert.Equal(
            "https://fonts.gstatic.com/s/inter/v13/abcdef.ttf",
            GoogleFontSource.ParseCss(Css));
    }

    [Fact]
    public void AStylesheetOfferingOnlyWoffIsRefusedRatherThanDownloaded()
    {
        // Skia cannot open woff2, so bytes that arrive under that name would
        // fail later and further away. Better to notice here.
        const string woff = "src: url(https://fonts.gstatic.com/s/inter/v13/a.woff2) format('woff2');";

        Assert.Null(GoogleFontSource.ParseCss(woff));
    }

    [Fact]
    public async Task AFontIsDownloadedOnceAndReadFromTheCacheAfterwards()
    {
        using var google = Google(Answer, out var stub);
        var face = new FontFace("Inter", 400, false, FontOrigin.Google, "OFL-1.1");

        Assert.False(google.IsCached(face));
        Assert.Equal(FontBytes, await google.LoadAsync(face, Ct));
        Assert.True(google.IsCached(face));

        var afterFirst = stub.Asked.Count;
        Assert.Equal(FontBytes, await google.LoadAsync(face, Ct));
        Assert.Equal(afterFirst, stub.Asked.Count);
    }

    [Fact]
    public async Task TheCatalogueIsFetchedOnceAndKeptOnDisk()
    {
        using var first = Google(Answer, out var stub);
        Assert.NotEmpty(await first.FacesAsync(Ct));
        var fetches = stub.Asked.Count;

        using var second = Google(Answer, out var again);
        Assert.NotEmpty(await second.FacesAsync(Ct));

        Assert.Equal(1, fetches);
        Assert.Empty(again.Asked);
    }

    [Fact]
    public async Task BeingOfflineIsSomethingToSayRatherThanSomethingToThrow()
    {
        using var google = Google(
            _ => throw new HttpRequestException("no network"), out _);

        Assert.Empty(await google.FacesAsync(Ct));
        Assert.NotNull(google.Trouble);
        Assert.Contains("Could not reach", google.Trouble);
    }

    [Fact]
    public async Task AnOfflineMachineStillSeesTheFontsItSawLastTime()
    {
        using var online = Google(Answer, out _);
        Assert.NotEmpty(await online.FacesAsync(Ct));

        using var offline = Google(_ => throw new HttpRequestException("no network"), out _);
        // The cache is fresh here, so this does not even reach the network — the
        // point being that opening the browser offline shows a full list.
        Assert.NotEmpty(await offline.FacesAsync(Ct));
    }

    [Fact]
    public void TheStylesheetRequestAsksForTheExactFaceWanted()
    {
        using var google = Google(Answer, out _);

        var request = google.CssRequest(new FontFace("Noto Sans", 700, true, FontOrigin.Google));

        Assert.Contains("family=Noto+Sans", request.ToString());
        Assert.Contains("ital,wght@1,700", request.ToString());
    }

    [Fact]
    public async Task AnOpenLicensedFontIsCarriedInTheDocumentThatUsesIt()
    {
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Inter", 700, false, FontOrigin.Google, "OFL-1.1");

        var choice = await library.ReferenceAsync(face, doc, embed: true, Ct);
        choice.RecordInto(doc);

        Assert.NotNull(choice.Reference.EmbeddedId);
        var carried = doc.Fonts![choice.Reference.EmbeddedId!];
        Assert.Equal("Inter", carried.Family);
        Assert.Equal(700, carried.Weight);
        Assert.Equal("OFL-1.1", carried.Licence);
        Assert.Equal("google", carried.Source);
        Assert.Equal(FontBytes, Convert.FromBase64String(carried.Data));
    }

    [Fact]
    public async Task TheSameFontUsedTwiceIsCarriedOnce()
    {
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Inter", 700, false, FontOrigin.Google, "OFL-1.1");

        var title = await library.ReferenceAsync(face, doc, embed: true, Ct);
        title.RecordInto(doc);
        var caption = await library.ReferenceAsync(face, doc, embed: true, Ct);
        caption.RecordInto(doc);

        Assert.Equal(title.Reference.EmbeddedId, caption.Reference.EmbeddedId);
        Assert.Single(doc.Fonts!);
        Assert.Null(caption.NewId);
    }

    [Fact]
    public async Task AnInstalledFontIsNamedAndNeverCopied()
    {
        // The licence is unknown, so the bytes stay where they are. This is the
        // case the whole policy exists for.
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Helvetica", 400, false, FontOrigin.Installed);

        var choice = await library.ReferenceAsync(face, doc, embed: true, Ct);
        choice.RecordInto(doc);

        Assert.Equal("Helvetica", choice.Reference.Family);
        Assert.Null(choice.Reference.EmbeddedId);
        Assert.Null(doc.Fonts);
    }

    [Fact]
    public async Task TurningEmbeddingOffLeavesAFileThatNamesItsFonts()
    {
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Inter", 400, false, FontOrigin.Google, "OFL-1.1");

        var choice = await library.ReferenceAsync(face, doc, embed: false, Ct);
        choice.RecordInto(doc);

        Assert.Equal("Inter", choice.Reference.Family);
        Assert.Null(choice.Reference.EmbeddedId);
        Assert.Null(doc.Fonts);
    }

    [Fact]
    public async Task UndoingTheTypeThatBroughtAFontTakesTheFontBackOut()
    {
        // Committing text is one undoable edit, so the font it carried has to
        // come back out with it — otherwise a Ctrl+Z leaves a document heavier
        // than it was for a caption that no longer exists.
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Inter", 400, false, FontOrigin.Google, "OFL-1.1");

        var choice = await library.ReferenceAsync(face, doc, embed: true, Ct);
        choice.RecordInto(doc);
        Assert.Single(doc.Fonts!);

        choice.RemoveFrom(doc);
        Assert.Null(doc.Fonts);
    }

    [Fact]
    public async Task UndoingOneCaptionLeavesTheFontAnotherIsStillUsing()
    {
        using var library = new FontLibrary(Google(Answer, out _));
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var face = new FontFace("Inter", 400, false, FontOrigin.Google, "OFL-1.1");

        var first = await library.ReferenceAsync(face, doc, embed: true, Ct);
        first.RecordInto(doc);
        var second = await library.ReferenceAsync(face, doc, embed: true, Ct);
        second.RecordInto(doc);

        // The second caption brought no font of its own, so undoing it removes
        // nothing — the entry belongs to the first.
        second.RemoveFrom(doc);

        Assert.Single(doc.Fonts!);
        Assert.Equal(first.Reference.EmbeddedId, doc.Fonts!.Keys.Single());
    }

    [Fact]
    public void TheInstalledFontsAreThereWithoutWaitingForAnything()
    {
        var installed = FontLibrary.Installed();

        Assert.NotEmpty(installed);
        Assert.All(installed, f =>
        {
            Assert.Equal(FontOrigin.Installed, f.Origin);
            Assert.False(f.Embeddable, $"{f} was offered as carryable with no licence");
        });
        Assert.Equal(
            installed.Count,
            installed.Select(f => (f.Family, f.Weight, f.Italic)).Distinct().Count());
    }

    [Fact]
    public void AFaceNamesItsWeightTheWayAnArtistWouldSayIt()
    {
        Assert.Equal("Regular", new FontFace("X", 400, false, FontOrigin.Installed).StyleName);
        Assert.Equal("Italic", new FontFace("X", 400, true, FontOrigin.Installed).StyleName);
        Assert.Equal("Bold", new FontFace("X", 700, false, FontOrigin.Installed).StyleName);
        Assert.Equal("Light Italic", new FontFace("X", 300, true, FontOrigin.Installed).StyleName);
    }

    [Fact]
    public void ACachedFileNameSurvivesAFamilyWithPunctuationInIt()
    {
        using var google = Google(Answer, out _);

        var path = google.CachedFile(new FontFace("Libre Baskerville!", 400, true, FontOrigin.Google));

        Assert.Equal(_cache, Path.GetDirectoryName(path));
        Assert.EndsWith("-400i.ttf", path);
        Assert.DoesNotContain(' ', Path.GetFileName(path));
    }

    [Fact]
    public void AnEmbeddedFontComesBackAsATypefaceAndAMissingOneAsNothing()
    {
        // The registry never substitutes: a font that is gone means text that
        // cannot be retyped, not text quietly reshaped in something else.
        FontRegistry.Clear();
        FontRegistry.Register(new Dictionary<string, EmbeddedFont>
        {
            ["broken"] = new() { Family = "X", Data = "not base64 !!" },
        });

        Assert.Null(FontRegistry.Embedded("broken"));
        Assert.Null(FontRegistry.Embedded("never-heard-of-it"));
        Assert.Null(FontRegistry.System(new FontRef { Family = "No Such Family Anywhere" }));
    }
}
