namespace Lightbox.Core.Documents;

/// <summary>Where a line sits relative to the text's origin.</summary>
public enum TextAlign
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// A font, as a document refers to one: enough to find it again, never the
/// thing that renders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is read at render time.</b> A text element is baked to glyph
/// contours the moment it is committed (see <see cref="TextElement"/>), and
/// those contours are what the rasterizer draws — so a document opened on a
/// machine that has never heard of this family renders identically, which is
/// invariant 1 applied to type. This block exists so the text can be
/// <em>edited</em> again: re-typing a word means re-shaping it, and re-shaping
/// needs the typeface back.
/// </para>
/// <para>
/// <b>Weight and slant rather than a style name</b>, because a style name is
/// whatever the foundry felt like calling it — "Book", "Roman", "Regular" and
/// "Text" are one weight under four names, and matching on them across a font
/// library fails on exactly the fonts an artist cares about. A CSS weight is a
/// number every source agrees on: the system font manager takes one, Google's
/// catalogue publishes one.
/// </para>
/// </remarks>
public sealed class FontRef
{
    /// <summary>The family name, as the source that offered it spells it.</summary>
    public string Family { get; set; } = "";

    /// <summary>CSS weight: 100 thin … 400 regular … 700 bold … 900 black.</summary>
    public int Weight { get; set; } = 400;

    public bool Italic { get; set; }

    /// <summary>
    /// The key into <see cref="Doc.Fonts"/> when this document carries the font
    /// itself, or null when it only names one to look up.
    /// </summary>
    /// <remarks>
    /// Set only where the licence allows the bytes to travel — see
    /// <see cref="EmbeddedFont"/> for what decides that and why it is not
    /// always. Null is the ordinary case for an installed font and costs
    /// nothing at render time; it costs an artist on another machine the
    /// ability to re-type this text, which is the whole of the difference.
    /// </remarks>
    public string? EmbeddedId { get; set; }

    public FontRef Clone() => (FontRef)MemberwiseClone();

    /// <summary>Whether two references would resolve to the same face.</summary>
    public bool SameFace(FontRef other) =>
        Family == other.Family && Weight == other.Weight && Italic == other.Italic;
}

/// <summary>
/// A font this document carries, so text stays editable away from the machine
/// that typed it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same bargain <see cref="Doc.BrushTips"/> makes, with one extra
/// condition.</b> A tip is the artist's own image and travels with the file
/// without asking anybody; a font is somebody else's work under somebody
/// else's licence, and a document is a file people send each other. So the
/// bytes are carried <em>only</em> where the licence plainly permits
/// redistribution — the OFL, Apache-2.0 and the Ubuntu Font Licence, which is
/// the whole of Google Fonts — and never for a face picked up from the
/// operating system, whose licence this application cannot know and must not
/// guess.
/// </para>
/// <para>
/// <b>The consequence, stated plainly because it is the cost of the choice:</b>
/// text set in an installed commercial font renders perfectly anywhere and can
/// only be re-typed where that font is installed. Text set in a Google font can
/// be re-typed anywhere. Nothing about the picture changes either way.
/// </para>
/// <para>
/// <see cref="Licence"/> and <see cref="Source"/> are recorded rather than
/// implied, so the claim travels with the bytes and a reader can check it. A
/// document that carries a font it should not have carried is a fact somebody
/// needs to be able to see.
/// </para>
/// </remarks>
public sealed class EmbeddedFont
{
    public string Family { get; set; } = "";

    public int Weight { get; set; } = 400;

    public bool Italic { get; set; }

    /// <summary>The licence that permits this to be here — "OFL-1.1", "Apache-2.0", "UFL-1.0".</summary>
    public string Licence { get; set; } = "";

    /// <summary>Where the bytes came from — "google".</summary>
    public string Source { get; set; } = "";

    /// <summary>The font file itself (TTF or OTF), base64, as brush tips are.</summary>
    public string Data { get; set; } = "";

    public EmbeddedFont Clone() => (EmbeddedFont)MemberwiseClone();
}

/// <summary>
/// A piece of set type: what was typed, in what, and where. The strokes it
/// bakes to are the drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a second description of a mark, never a second kind of mark</b> —
/// the same shape <see cref="StrokePath"/> chose for the pen, and for the same
/// reason. Committing a text element shapes it, pulls each glyph's outline out
/// of the typeface, and emits one <see cref="ToolKind.Text"/> stroke per glyph
/// carrying <see cref="Stroke.TextId"/>. Those strokes are ordinary contour
/// fills: they erase, clip, transform, export, inbetween and re-render exactly
/// like every other mark, and nothing downstream of the bake knows that type
/// was involved.
/// </para>
/// <para>
/// <b>Editing is a re-bake, which is the pattern <see cref="Stroke.SimElementId"/>
/// already established:</b> drop every stroke carrying this id from the cel and
/// run again. That is why the element is kept at all — without it the text is
/// still a perfect drawing, but it is no longer a sentence anybody can retype.
/// </para>
/// <para>
/// <b>What it deliberately is not.</b> It is not a raster stamp of a font
/// (Q149), it is not read at render time, and it is not a vector object living
/// outside the stroke record. A document whose <c>texts</c> block were deleted
/// by hand would render pixel for pixel the same and simply stop being
/// editable — which is the honest test of whether the contours or the element
/// are the truth.
/// </para>
/// </remarks>
public sealed class TextElement
{
    public string Id { get; set; } = Ids.NewId("txt");

    /// <summary>What was typed. Newlines break lines; nothing else is markup.</summary>
    public string Text { get; set; } = "";

    public FontRef Font { get; set; } = new();

    /// <summary>Em size, in document pixels.</summary>
    public double Size { get; set; } = 48;

    /// <summary>
    /// Extra advance between glyphs, in thousandths of an em — the unit every
    /// type application uses, so a value copied off a spec sheet means here what
    /// it meant there.
    /// </summary>
    public double Tracking { get; set; }

    /// <summary>
    /// Distance between baselines in document pixels, or null for the font's
    /// own default spacing.
    /// </summary>
    /// <remarks>
    /// Nullable rather than pre-resolved so that changing the size of a block
    /// nobody has re-led keeps looking right: a number here means the artist
    /// chose it, and the absence of one means the typeface still decides.
    /// </remarks>
    public double? LineHeight { get; set; }

    public TextAlign Align { get; set; }

    /// <summary>The alignment origin, in document coordinates.</summary>
    public double X { get; set; }

    /// <summary>The first baseline, in document coordinates.</summary>
    /// <remarks>
    /// The baseline rather than the top of the block, because the baseline is
    /// the line a person is actually setting type against — it is what lines up
    /// with a horizon, a shoulder, or the row of type above it, and it is what
    /// stays put when the size changes.
    /// </remarks>
    public double Y { get; set; }

    public TextElement Clone()
    {
        var copy = (TextElement)MemberwiseClone();
        copy.Font = Font.Clone();
        return copy;
    }
}
