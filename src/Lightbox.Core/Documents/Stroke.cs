namespace Lightbox.Core.Documents;

/// <summary>
/// One brush stroke: the atomic unit of drawing, and the unit of exchange
/// with the AI. A stroke is pure data — geometry plus paint parameters —
/// so it can be serialized to JSON, interpolated, and re-rendered
/// deterministically.
/// </summary>
public sealed class Stroke
{
    public string Id { get; set; } = Ids.NewId("s");

    public ToolKind Tool { get; set; } = ToolKind.Brush;

    /// <summary>CSS-style hex color, e.g. "#1a1a1a".</summary>
    public string Color { get; set; } = "#1a1a1a";

    public BrushSettings Brush { get; set; } = new();

    public List<StrokePoint> Points { get; set; } = [];

    /// <summary>
    /// The authored nodes and handles this stroke was shaped with, or null for a
    /// stroke that was simply drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Absent unless used, and that is a property rather than a preference.</b>
    /// A hand-drawn stroke writes no <c>path</c> key; clearing a path removes the
    /// key again. Every stroke of every document would otherwise carry an empty
    /// object, which is the medium block's mistake repeated
    /// (<c>CLAUDE.md</c> → *"Optional has two halves"*), so
    /// <c>AHandDrawnStrokeSerializesNoPathKey</c> ships beside it.
    /// </para>
    /// <para>
    /// <b><see cref="Points"/> stays the truth.</b> The renderer, the index, the
    /// contour tracer, the transform and the AI wire format never look here. A
    /// path is re-flattened into points whenever it is edited, and any operation
    /// that maps points must map this too or drop it —
    /// see <see cref="StrokePath"/> for the whole of that argument.
    /// </para>
    /// </remarks>
    public StrokePath? Path { get; set; }

    /// <summary>Inner contours of a <see cref="ToolKind.Fill"/> stroke (even-odd holes); null otherwise.</summary>
    public List<List<StrokePoint>>? Holes { get; set; }

    /// <summary>
    /// Key into <see cref="Doc.ClipRegions"/>: the selection that was active
    /// when this stroke was painted, applied on every re-render so the
    /// document stays self-contained.
    /// </summary>
    public string? ClipId { get; set; }

    /// <summary>
    /// The gradient this stroke paints, for <see cref="ToolKind.Gradient"/>.
    /// Resolved against <see cref="Doc.Gradients"/>, so the mark is
    /// reproducible from the record alone.
    /// </summary>
    public string? GradientId { get; set; }

    /// <summary>
    /// The palette swatch this stroke's colour comes from, or null for a
    /// literal colour.
    ///
    /// When set, the swatch wins at render time — that is the whole point:
    /// edit the swatch and every stroke referencing it changes, on every
    /// frame and every layer, vector and raster alike. <see cref="Color"/> is
    /// still kept up to date as the last resolved value, so a document whose
    /// palette has been stripped, or whose swatch was deleted, still renders
    /// the colour the artist last saw rather than falling back to black.
    /// </summary>
    public string? SwatchId { get; set; }

    /// <summary>
    /// Which palette that swatch came from, or null when nothing recorded it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30 step 2, and the one hazard scoping introduces.</b> Palettes are
    /// the deliberate exception to *resources are a library, not something
    /// rendering reads* — live recolour is the feature, so a swatch id really is
    /// resolved at render time. That was safe while every document saw every
    /// palette. Once palettes are scoped, moving a document between folders
    /// changes which swatches resolve, and a swatch id that means one colour
    /// under the knight can mean another under the goblin.
    /// </para>
    /// <para>
    /// Independently authored palettes will not collide, because swatch ids are
    /// generated. <b>Duplicated palettes will</b> — and duplicating a palette to
    /// tweak it is the obvious thing to do, which is what turns this from
    /// theoretical into the next bug report.
    /// </para>
    /// <para>
    /// Nullable and absent from the file unless a stroke was painted from a
    /// palette, so nothing that paints a literal colour carries the key.
    /// </para>
    /// </remarks>
    public string? PaletteId { get; set; }

    /// <summary>
    /// Painted on an alpha-locked layer: this stroke may only touch pixels
    /// that already had content beneath it. Recorded here rather than read
    /// from the layer at render time, so flipping the layer's alpha lock
    /// never repaints work already done (invariant 4). The mask itself is not
    /// stored — the rasterizer stamps strokes in order, so the content before
    /// this stroke is exactly what it has already drawn.
    /// </summary>
    public bool AlphaLocked { get; set; }

    /// <summary>
    /// The effects element that baked this stroke, or null for everything an
    /// artist drew.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Provenance and a handle, never behaviour.</b> It changes no pixel:
    /// deleting it leaves an ordinary stroke that renders identically, exactly
    /// as <c>Frame.AiProvenance</c> does. What it buys is re-baking — drop every
    /// stroke carrying this id in the element's range and run again — and the
    /// ability to tell an artist that the line they just tried to reshape came
    /// from a simulation that will overwrite it.
    /// </para>
    /// <para>
    /// Absent from the file on every stroke that was drawn by hand, which is
    /// every stroke in a document that has never made an effect.
    /// </para>
    /// </remarks>
    public string? SimId { get; set; }

    /// <summary>
    /// Optional semantic name ("head-outline", "left-arm"). When two
    /// keyframes label a stroke identically, the inbetweener matches them
    /// directly, and an LLM can use labels to track anatomy across frames.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// How this stroke follows the rig: which bones move it, and by how much
    /// per control point. Null — and absent from the file — for every stroke
    /// that was never bound, which is every stroke in an unrigged document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the stroke rather than anywhere global (invariant 4: settings that
    /// reach pixels are per stroke). The order of the list is the fixed
    /// evaluation order the skinning solve uses, so a reload blends in the
    /// same sequence and lands on the same doubles.
    /// </para>
    /// <para>
    /// Weights are influence, not the pose: binding a stroke changes nothing
    /// on screen until a pose exists, and posing never writes here. See
    /// <see cref="Skinning"/> and <c>docs/DESIGN-bones.md</c>.
    /// </para>
    /// </remarks>
    public List<BoneBinding>? Weights { get; set; }

    /// <summary>
    /// The bind-pose path this stroke's dab dynamics seed from, when it
    /// differs from <see cref="Points"/>. Null on every stroke that was never
    /// posed — which is every stroke an artist draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the field that keeps a rigged character from boiling.</b>
    /// Every dab dynamic is seeded from the IEEE-754 bits of the dab's
    /// position, so a stroke whose coordinates move per pose would re-roll
    /// scatter, jitter and grain every frame. A posed stroke therefore stamps
    /// at <see cref="Points"/> and seeds from here: same rest path, same
    /// pixels' worth of character, whatever the pose
    /// (<c>docs/DESIGN-bones.md</c>, "The one trap").
    /// </para>
    /// <para>
    /// Written only by <see cref="Skinning.PoseStroke"/>, with one point per
    /// entry in <see cref="Points"/> — a mismatched count means a hand-edited
    /// document, and the renderer falls back to ordinary seeding rather than
    /// guessing a correspondence.
    /// </para>
    /// </remarks>
    public List<StrokePoint>? RestPoints { get; set; }

    /// <summary>
    /// The pixels a baked all-layers smudge sampled, and where they sat.
    /// </summary>
    /// <remarks>
    /// Null on every other stroke, and absent from the file — this is the one
    /// thing in the record that is pixels rather than instructions, so it is
    /// carried only by the strokes that asked to freeze what was underneath
    /// them. Cropped to the stroke's own reach, not the canvas, because the
    /// alternative is a full-size PNG per smudge.
    /// </remarks>
    public BakedSample? Baked { get; set; }

    /// <summary>
    /// A copy with everything intact, and by default a fresh id.
    /// </summary>
    /// <param name="newId">
    /// False when the copy has to <em>be</em> this stroke rather than resemble it —
    /// an undo snapshot restores the stroke that was there, so it keeps the id the
    /// rest of the record refers to. True everywhere an artist gets a second,
    /// separate mark: duplicating a cel, generating an inbetween.
    /// </param>
    /// <remarks>
    /// <b>This used to list every property by hand, and the list was the bug.</b>
    /// It silently omitted <see cref="SwatchId"/> and <see cref="GradientId"/>, so a
    /// copied drawing kept its colour but lost its link to the palette — and
    /// recolouring the swatch afterwards changed the original and not the copy. The
    /// remark here then said the list must be exhaustive, which is true and is not
    /// something a reader can enforce.
    /// <para>
    /// <see cref="MemberwiseClone"/> removes the possibility instead of warning
    /// about it: every value field is copied because the runtime copies them, so a
    /// property added to a stroke cannot go quiet. What is left below is only the
    /// members that are references and would otherwise be shared — a much shorter
    /// list, and one whose omissions <c>DocCloneTests</c> detects by walking the
    /// graph.
    /// </para>
    /// <para>
    /// <see cref="Baked"/> stays shared deliberately, as it always has: see
    /// <see cref="BakedSample"/>, which is replaced or dropped rather than edited,
    /// and can be a megabyte of base64.
    /// </para>
    /// </remarks>
    public Stroke Clone(bool newId = true)
    {
        var copy = (Stroke)MemberwiseClone();
        if (newId) copy.Id = Ids.NewId("s");
        copy.Brush = Brush.Clone();
        copy.Points = [.. Points];
        copy.Holes = Holes?.Select(h => new List<StrokePoint>(h)).ToList();
        // Deep, like the points beside it: a shared path would let reshaping one
        // copy of a duplicated cel silently reshape the other, and the two would
        // then disagree with their own points.
        copy.Path = Path?.Clone();
        copy.Weights = Weights?.Select(w => w.Clone()).ToList();
        copy.RestPoints = RestPoints is null ? null : [.. RestPoints];
        return copy;
    }
}

/// <summary>
/// A frozen backdrop: the composite a stroke read, and the document-space
/// corner it starts at.
/// </summary>
/// <remarks>
/// Shared by reference when a stroke is cloned. It is immutable in practice —
/// nothing edits a baked sample, it is replaced or dropped — and copying a
/// megabyte of base64 per cel duplication would be a poor trade for a
/// guarantee nothing needs.
/// </remarks>
public sealed class BakedSample
{
    /// <summary>Bare base64 of a PNG, no data-URL prefix.</summary>
    public string PngBase64 { get; set; } = "";

    public int X { get; set; }

    public int Y { get; set; }
}
