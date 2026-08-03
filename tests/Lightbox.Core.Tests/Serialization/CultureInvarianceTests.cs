using System.Globalization;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A document saved on a machine in one locale must be byte-identical to the
/// same document saved in another, and must load in both.
/// </summary>
/// <remarks>
/// <para>
/// Nothing guarded this before. <c>DocJson</c> is correct today by
/// construction — it goes through <c>System.Text.Json</c>, which formats
/// numbers invariantly by specification, and the places in the codebase that do
/// format text by hand pass <c>CultureInfo.InvariantCulture</c> explicitly
/// (<c>Palette</c>, <c>SymbolRasterizer</c>, <c>KppReader</c> and the rulers).
/// Correct-by-construction and guarded are different things, and this is the
/// class of defect where the difference matters: the failure is silent, it
/// happens on somebody else's machine, and what it damages is saved work.
/// </para>
/// <para>
/// Two locales, each chosen for a specific failure it would expose:
/// </para>
/// <para>
/// <c>de-DE</c> writes a decimal comma. A stroke's <c>pressure</c> of 0.5
/// emitted as <c>0,5</c> is not merely wrong, it is JSON in which one number
/// has become two — <c>[0,5]</c> parses as a two-element array. A document
/// saved that way is unreadable rather than subtly off, and every pressure,
/// hardness and coordinate in the file is affected.
/// </para>
/// <para>
/// <c>tr-TR</c> is the casing trap. Turkish lowercases <c>I</c> to <c>ı</c>,
/// not <c>i</c>, so any culture-sensitive lowering inside a camelCase naming
/// policy or a string enum converter would turn a key like <c>id</c> into
/// <c>ıd</c>. That file would round-trip perfectly on the machine that wrote it
/// and fail to load anywhere else, which is the worst available shape for this
/// bug.
/// </para>
/// <para>
/// The invariant is stated as byte-equality across all three cultures rather
/// than as "no comma appears". Equality catches the separator, the casing, digit
/// grouping and anything else a locale reaches, including whatever a future
/// serializer option introduces; a substring check only catches what it was
/// written to look for.
/// </para>
/// <para>
/// One object is serialized three times, never three freshly built documents —
/// otherwise any id or timestamp minted per document would show up as a
/// difference and the test would be measuring the factory instead of the
/// locale. <c>CultureInfo.CurrentCulture</c> is thread-local and restored in a
/// <c>finally</c>, so a failure cannot leak a locale into whatever xunit runs
/// on this thread next.
/// </para>
/// </remarks>
public class CultureInvarianceTests
{
    /// <summary>
    /// Decimals in several places, a string enum, a camelCase key with an
    /// interior capital, and a label — the surfaces a locale could reach.
    /// </summary>
    private static Doc SampleDoc()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var painted = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        painted.PngBase64 = Convert.ToBase64String([1, 2, 3, 4]);
        painted.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff8800",
            Brush = new BrushSettings { Size = 12.5, Hardness = 0.5, Opacity = 0.875, Spacing = 0.2 },
            Points = [new(10.25, 20.5, 0.5), new(30.75, 40.125, 0.875)],
            Label = "left-arm",
        });
        return doc;
    }

    private static T InCulture<T>(string name, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// The premise every other test in this class rests on: these locales
    /// really are loaded and really do format differently.
    /// </summary>
    /// <remarks>
    /// Without this the whole class can pass for the wrong reason. Under
    /// globalization-invariant mode — a runtime switch, or an ICU-less
    /// container — <c>new CultureInfo("de-DE")</c> still succeeds and still
    /// behaves as the invariant culture, so "invariant output equals de-DE
    /// output" becomes a tautology and every assertion below holds while
    /// guarding nothing. That is the shape `CLAUDE.md` warns about in the
    /// saturation trap: assert the thing is present as well as equal, or the
    /// test also passes when the mechanism is missing.
    /// </remarks>
    [Theory]
    [InlineData("de-DE", "0,5")]
    [InlineData("tr-TR", "0,5")]
    public void TheHostileLocaleIsActuallyLoaded(string culture, string expected)
    {
        // CurrentCulture is read inside the lambda, so it resolves after
        // InCulture has switched it — and named explicitly rather than left
        // implicit, because the whole assertion is about which one applies.
        var formatted = InCulture(culture, () => (0.5).ToString(CultureInfo.CurrentCulture));

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void SavingInAHostileLocale_ProducesTheSameBytes(string culture)
    {
        var doc = SampleDoc();
        var invariant = InCulture(CultureInfo.InvariantCulture.Name, () => DocJson.Serialize(doc));
        var hostile = InCulture(culture, () => DocJson.Serialize(doc));

        // Both sides non-trivial: an invariant-mode container would make these
        // equal by making them the same thing. TheHostileLocaleIsActuallyLoaded
        // is what rules that out.
        Assert.Equal(invariant, hostile);
    }

    /// <summary>
    /// The wire format for AI requests is a separate <c>JsonSerializerOptions</c>
    /// instance, so it gets its own assertion rather than being assumed to
    /// follow. A payload with a decimal comma in it is a malformed request.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void TheCompactWireFormat_IsAlsoLocaleIndependent(string culture)
    {
        var doc = SampleDoc();
        var invariant = InCulture(
            CultureInfo.InvariantCulture.Name,
            () => System.Text.Json.JsonSerializer.Serialize(doc, DocJson.Compact));
        var hostile = InCulture(
            culture,
            () => System.Text.Json.JsonSerializer.Serialize(doc, DocJson.Compact));

        Assert.Equal(invariant, hostile);
    }

    /// <summary>
    /// Writing invariantly is half of it. A reader that parsed
    /// <c>"0.5"</c> under de-DE without an invariant provider would return 5, or
    /// throw — so opening a file must not depend on the locale either.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void OpeningInAHostileLocale_RestoresTheSameValues(string culture)
    {
        var json = DocJson.Serialize(SampleDoc());

        var restored = InCulture(culture, () => DocJson.Deserialize(json));

        var stroke = Assert.Single(
            Assert.IsType<PaintedFrame>(restored.Scene.Layers[0].Cels[0].Frame).Strokes);
        Assert.Equal(12.5, stroke.Brush.Size);
        Assert.Equal(0.5, stroke.Brush.Hardness);
        Assert.Equal(0.875, stroke.Brush.Opacity);
        Assert.Equal(new StrokePoint(10.25, 20.5, 0.5), stroke.Points[0]);
        Assert.Equal(new StrokePoint(30.75, 40.125, 0.875), stroke.Points[1]);
        Assert.Equal(ToolKind.Brush, stroke.Tool);
        Assert.Equal("left-arm", stroke.Label);
    }

    /// <summary>
    /// The round trip that actually matters: saved in one locale, opened in
    /// another. This is the cross-machine case, and it is the one a
    /// same-locale round-trip test cannot see.
    /// </summary>
    [Fact]
    public void ADocumentSavedInOneLocale_OpensInAnother()
    {
        var json = InCulture("de-DE", () => DocJson.Serialize(SampleDoc()));

        var restored = InCulture("tr-TR", () => DocJson.Deserialize(json));

        var stroke = Assert.Single(
            Assert.IsType<PaintedFrame>(restored.Scene.Layers[0].Cels[0].Frame).Strokes);
        Assert.Equal(0.5, stroke.Brush.Hardness);
        Assert.Equal(new StrokePoint(10.25, 20.5, 0.5), stroke.Points[0]);
    }

    /// <summary>
    /// A decimal point is what JSON permits; a decimal comma turns one number
    /// into two and is the specific corruption
    /// <see cref="SavingInAHostileLocale_ProducesTheSameBytes"/> exists to
    /// prevent. Byte-equality already covers it — this names it, so a failure
    /// reads as "the separator moved" rather than "the bytes differ".
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void NoNumberIsWrittenWithADecimalComma(string culture)
    {
        var json = InCulture(culture, () => DocJson.Serialize(SampleDoc()));

        Assert.Contains("0.5", json);
        Assert.DoesNotContain("0,5", json);
    }
}
