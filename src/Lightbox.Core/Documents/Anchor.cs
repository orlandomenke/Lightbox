namespace Lightbox.Core.Documents;

/// <summary>What an anchor is for.</summary>
public enum AnchorKind
{
    /// <summary>
    /// Where the drawing is anchored — feet centre, usually.
    /// </summary>
    /// <remarks>
    /// <see cref="Scene.Pivot"/> is still the document's single unnamed pivot and
    /// is what an engine reads when nothing else is declared. A named anchor of
    /// this kind is for a <em>second</em> one: a character with a ground pivot and
    /// a shadow pivot, say.
    /// </remarks>
    Pivot,

    /// <summary>
    /// A point something else attaches to: a hand holding a weapon, a muzzle a
    /// flash comes out of, a hardpoint.
    /// </summary>
    /// <remarks>
    /// Exported and nothing more. Actually parenting a GameObject to it is the
    /// engine's job, not a drawing application's — what Lightbox owes is the
    /// position, per frame, in the frame's own coordinates.
    /// </remarks>
    Socket,
}

/// <summary>
/// A named point declared once for the whole document, positioned per drawing.
/// </summary>
/// <remarks>
/// <para>
/// The declaration and the positions are deliberately in different places.
/// <see cref="Scene.Anchors"/> holds the names — "left hand", "muzzle" — because
/// a name is a property of the rig and renaming it must not touch a drawing.
/// <see cref="Frame.Anchors"/> holds where that point *is*, because that changes
/// every frame and is what the animator is actually authoring.
/// </para>
/// <para>
/// <b>Positions live on the frame rather than against a frame index</b>, and that
/// is the load-bearing choice. A hold, a re-time, a cel drag and a
/// <see cref="Timeline.TimingPreset"/> all move drawings around the sheet; an
/// index-keyed table would silently point at the wrong drawing after any of them,
/// and the failure would look like an animation bug. On the frame, the anchor
/// travels with the drawing it belongs to, for free.
/// </para>
/// <para>
/// None of this reaches a pixel, so invariant 1 is not in play. It is authored
/// data that leaves through the exporter.
/// </para>
/// </remarks>
public sealed class Anchor
{
    public string Id { get; set; } = Ids.NewId("anc");

    /// <summary>What the artist calls it, and what an engine importer will see.</summary>
    public string Name { get; set; } = "Socket";

    public AnchorKind Kind { get; set; } = AnchorKind.Socket;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Anchor Clone() => (Anchor)MemberwiseClone();
}

/// <summary>Where an anchor sits on one drawing, in document pixels.</summary>
/// <remarks>
/// <para>
/// Document pixels, like <see cref="Pivot"/> and unlike anything normalised: the
/// exporter is the only thing that needs a cell-relative or normalised figure, and
/// it has the trim rect to compute one. Storing a normalised value here would
/// bake the trim into the record and make re-exporting at a different trim wrong.
/// </para>
/// <para>
/// <b>Q144: the direction is per placement, like the position</b> — a hand
/// turns through a swing, so the angle belongs to the drawing and travels with
/// it through a hold, a re-time and a cel drag the same way the position does.
/// Degrees, matching every exported rotation (<c>RotationDeg</c> and kin);
/// zero points along +X and positive turns the way the y-down canvas turns.
/// Null means <em>no direction</em>, and the document serializer drops null
/// keys, so an anchor nobody turned writes exactly what it always wrote.
/// Under a resize the angle is carried, never scaled: rotation is not a
/// length, the accepted limit <see cref="ImageResize"/> already records for
/// guides and bones.
/// </para>
/// </remarks>
public sealed record AnchorPoint(double X, double Y, double? AngleDeg = null);
