using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

public enum LayerKind
{
    Painted,
    Vector,
}

/// <summary>
/// How a layer composites over the layers below it (Photoshop-style).
/// Render-time only: layer pixels are stored un-blended, so the stroke
/// record stays the single source of truth.
/// </summary>
public enum LayerBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

/// <summary>
/// A cel on the timeline. <c>Frame == null</c> means "hold the previous
/// exposed frame" — the classic exposure-sheet model.
/// </summary>
public sealed class Cel
{
    public Frame? Frame { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Cel Clone()
    {
        var copy = (Cel)MemberwiseClone();
        copy.Frame = Frame?.Clone();
        return copy;
    }
}

/// <summary>
/// A layer folder: layers referencing it render as a visual group in the
/// docker, and its visibility gates every member. Members stay ordinary
/// layers in Scene.Layers (contiguous, kept so by the group operations) —
/// compositing order is unchanged.
/// </summary>
public sealed class LayerGroup
{
    public string Id { get; set; } = Ids.NewId("group");

    public string Name { get; set; } = "Folder";

    public bool Visible { get; set; } = true;

    /// <summary>Locking a folder locks every layer inside it.</summary>
    public bool Locked { get; set; }

    /// <summary>Header accent color in the docker (hex, e.g. "#4a6ea9").</summary>
    public string Color { get; set; } = "#4a6ea9";

    /// <summary>Docker-only view preference (not undoable).</summary>
    public bool Collapsed { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public LayerGroup Clone() => (LayerGroup)MemberwiseClone();
}

/// <summary>
/// A set of layers that are one drawing — lines, colour, details, effects —
/// declared once so they behave as one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A link is not a folder.</b> A folder is where layers <em>sit</em>; a link
/// is what they <em>are</em> to each other, and the two are wanted in different
/// combinations often enough that collapsing them would cost more than it saves
/// — a link spanning two folders is an ordinary thing to want, and a folder
/// whose members are unrelated is the ordinary case.
/// </para>
/// <para>
/// <b>What travels is opt-in, per property</b> (Q90). The answer taken was a
/// general link rather than a bone-specific one, and the price of a general
/// link is that every property has to answer what inheriting it means — so a
/// link made to rig a character must not quietly start sharing alpha lock.
/// Each travelling property is a nullable flag, absent until switched on.
/// </para>
/// <para>
/// <b>It holds across every frame</b>, because it is a property of the layer
/// structure rather than of a drawing. That is the half that matters: binding
/// a two-layer character over two hundred frames was four hundred manual
/// operations, and a link is one.
/// </para>
/// </remarks>
public sealed class LayerLink
{
    public string Id { get; set; } = Ids.NewId("link");

    public string Name { get; set; } = "Linked";

    /// <summary>Accent colour in the docker (hex), so a link reads at a glance.</summary>
    public string Color { get; set; } = "#c98a3a";

    /// <summary>
    /// Whether rig binding travels this link: every member follows the bone
    /// any one of them names. Null — and absent — until switched on.
    /// </summary>
    public bool? Bones { get; set; }

    /// <summary>Whether rig binding travels this link. Derived; never serialized.</summary>
    [JsonIgnore] public bool CarriesBones => Bones == true;

    /// <summary>Whether alpha lock travels this link.</summary>
    public bool? Alpha { get; set; }

    /// <summary>Whether alpha lock travels this link. Derived; never serialized.</summary>
    [JsonIgnore] public bool CarriesAlpha => Alpha == true;

    /// <summary>Whether visibility travels this link — hide the lines, hide the colour.</summary>
    public bool? Visibility { get; set; }

    /// <summary>Whether visibility travels this link. Derived; never serialized.</summary>
    [JsonIgnore] public bool CarriesVisibility => Visibility == true;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public LayerLink Clone() => (LayerLink)MemberwiseClone();
}

public sealed class Layer
{
    public string Id { get; set; } = Ids.NewId("layer");

    public string Name { get; set; } = "Layer";

    public LayerKind Kind { get; set; } = LayerKind.Painted;

    public bool Visible { get; set; } = true;

    /// <summary>
    /// Blocks everything that changes pixels or geometry — paint, fill,
    /// transform, delete, blank, cel edits, and external writes. Visibility,
    /// opacity, blend mode and reordering stay available, so a locked layer
    /// is still useful as reference. A locked layer still renders and still
    /// exports: locking is about editing, not about hiding.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Restrict painting to pixels that already have content, so colour can
    /// be changed without altering the silhouette. Recorded per stroke at
    /// paint time (see <see cref="Stroke.AlphaLocked"/>) rather than consulted
    /// at render time, or toggling it would repaint existing art.
    /// </summary>
    public bool AlphaLocked { get; set; }

    /// <summary>
    /// This layer is the document's paper. It holds the background colour as
    /// real, editable content rather than as a scene property, so unlocking
    /// it and painting on it works like any other layer — and erasing it
    /// genuinely reveals transparency instead of a colour the renderer keeps
    /// putting back.
    /// </summary>
    public bool IsBackground { get; set; }

    /// <summary>
    /// Overrides what the exporter would decide about this layer on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three states, and all three are needed. <c>true</c> leaves the layer out of
    /// every export whatever the mode says — the reference photo, the notes-to-self
    /// layer, the colour-check swatch. <c>false</c> keeps it in even when detection
    /// would drop it, which is the escape hatch for the case where the background
    /// <em>is</em> the asset: a backdrop, a sky, a tiling floor. <c>null</c>, the
    /// default, leaves it to <see cref="Export.BackgroundHandling"/>.
    /// </para>
    /// <para>
    /// Nullable rather than a <c>bool</c> so a document that never touches it writes
    /// no key — the camera's rule. A plain <c>bool</c> would put
    /// <c>"omitFromExport": false</c> on every layer of every document ever saved.
    /// </para>
    /// </remarks>
    public bool? OmitFromExport { get; set; }

    /// <summary>Whether the artist pinned this layer out of exports.</summary>
    [JsonIgnore] public bool IsOmittedFromExport => OmitFromExport == true;

    /// <summary>Whether the artist pinned this layer *into* exports.</summary>
    [JsonIgnore] public bool IsKeptInExport => OmitFromExport == false;

    /// <summary>
    /// Distance behind the picture plane, in document units — the multiplane
    /// depth of `docs/DESIGN-3d-space.md` stage 1. Null, and absent, on a
    /// layer that lives on the plane, which is every layer ever saved before
    /// this existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Depth without a camera does nothing.</b> Parallax is the
    /// depth-dependent response to camera moves (<see cref="LayerDepth"/>), so
    /// an asset document — which never has a camera — is untouched by
    /// construction, and no feature conflict is needed anywhere.
    /// </para>
    /// <para>
    /// Nullable rather than a <c>double</c> defaulting to zero, for
    /// <see cref="OmitFromExport"/>'s stated reason: a plain double would put
    /// <c>"depth": 0</c> on every layer of every document ever saved. Zero and
    /// null mean the same plane; authoring writes null for zero so the
    /// distinction can never leak into a file.
    /// </para>
    /// <para>
    /// Positive is away from the viewer (a far hill moves less); a small
    /// negative depth floats a layer in front of the plane and moves it more.
    /// <see cref="LayerDepth.Factor"/> clamps the useful range.
    /// </para>
    /// </remarks>
    public double? Depth { get; set; }

    /// <summary>Whether this layer sits off the picture plane. Derived; never serialized.</summary>
    [JsonIgnore] public bool HasDepth => Depth is not null && Depth.Value != 0;

    /// <summary>Whether this layer participates in onion-skin ghosting.</summary>
    public bool OnionEnabled { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;

    /// <summary>The layer folder this layer belongs to (null = ungrouped).</summary>
    public string? GroupId { get; set; }

    /// <summary>The link this layer is a member of (null = unlinked).</summary>
    public string? LinkId { get; set; }

    /// <summary>
    /// The bone every stroke on this layer follows unless it carries weights
    /// of its own — null, and absent, on a layer that is not rigged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named on the layer rather than inferred from adjacency</b> (Q90).
    /// Layer-above/below addressing was declined because reordering layers
    /// would silently retarget it, and a link that retargets when you drag
    /// something is the invisible-failure shape this area keeps producing.
    /// </para>
    /// <para>
    /// <b>The empty string means the whole skeleton</b>, auto-bound by
    /// distance, rather than one named bone — which is what a body wants where
    /// a cut-out limb wants a single bone. It is a value rather than a second
    /// nullable flag because "unrigged", "rigged to one bone" and "rigged to
    /// the skeleton" are three states of one question.
    /// </para>
    /// <para>
    /// Per-stroke <see cref="Stroke.Weights"/> still win wherever they exist:
    /// this is the layer's answer for lines nobody has painted weights on, and
    /// painting weights on one is how an artist overrides it.
    /// </para>
    /// </remarks>
    public string? BoneId { get; set; }

    /// <summary>Whether this layer follows the rig at all. Derived; never serialized.</summary>
    [JsonIgnore] public bool IsRigged => BoneId is not null;

    /// <summary>Whether this layer follows the whole skeleton rather than one bone.</summary>
    [JsonIgnore] public bool FollowsWholeSkeleton => BoneId is { Length: 0 };

    /// <summary>One entry per timeline frame; a null Frame is a hold.</summary>
    /// <summary>
    /// The effects element that owns this layer, or null for every layer an
    /// artist made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q123: an element bakes into a layer of its own, so it can be hidden,
    /// blended and re-baked as a clean replace without touching anything else.
    /// Held here rather than matched by name, because renaming a layer is an
    /// ordinary thing to do and should not orphan the element — which would show
    /// up as the next bake quietly making a second layer.
    /// </para>
    /// <para>
    /// Provenance, never behaviour: the layer renders, exports and composites
    /// exactly as any other, and clearing this leaves an ordinary layer full of
    /// ordinary strokes. Absent from the file on every layer of every document
    /// that has never made an effect.
    /// </para>
    /// </remarks>
    public string? SimId { get; set; }

    public List<Cel> Cels { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Layer Clone()
    {
        var copy = (Layer)MemberwiseClone();
        copy.Cels = Cels.Select(c => c.Clone()).ToList();
        return copy;
    }
}
