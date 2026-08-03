using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>What a collision shape is for, in the engine's terms.</summary>
/// <remarks>
/// Three roles rather than one flag, because an engine treats them differently
/// and the artist is authoring the distinction, not a detail of it: a hurtbox is
/// where the character <em>can be hit</em>, a hitbox is where they <em>hit</em>,
/// and a physics shape is what the world collides with. Exported as the role name
/// so an importer can filter by layer without guessing from a naming convention.
/// </remarks>
public enum ShapeRole
{
    /// <summary>
    /// Where the character can be hit. Usually present on every frame, and usually
    /// the one an artist draws first.
    /// </summary>
    Hurtbox,

    /// <summary>
    /// Where the character hits. Present only on the frames where the attack
    /// connects, which is exactly what per-drawing placement gives for free —
    /// an unplaced shape is an absent shape, so there is no "active" flag to keep
    /// in step with the drawing.
    /// </summary>
    Hitbox,

    /// <summary>
    /// What the world collides with: the body the ground stops and a wall blocks.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Hurtbox"/> because they genuinely differ — a
    /// character's physics body is usually a stable capsule that does not follow
    /// the animation, while their hurtbox follows the drawing closely.
    /// </remarks>
    Physics,
}

/// <summary>
/// A named collision shape declared once for the whole document, sized and placed
/// per drawing.
/// </summary>
/// <remarks>
/// <para>
/// The same split as <see cref="Anchor"/>, for the same reason: the name and the
/// role are properties of the rig and live on <see cref="Scene.Shapes"/>, so
/// renaming "sword" cannot touch a drawing; the rectangle is what the animator
/// authors frame by frame and lives on <see cref="Frame.Shapes"/>, so it travels
/// with its drawing through a hold, a re-time, a cel drag or a timing preset.
/// </para>
/// <para>
/// <b>Rectangles only, deliberately.</b> A rect is what every 2D engine takes
/// directly — Unity's <c>BoxCollider2D</c>, Godot's <c>RectangleShape2D</c>,
/// GameMaker's bbox — so it exports without a conversion step and without a
/// decomposition that could change the shape's meaning. A polygon needs a real
/// editor (add, move and delete vertices, over the canvas, per frame) and a
/// per-engine conversion, and half a polygon editor is worse than a whole rect
/// one. When polygons arrive they arrive as a second kind beside this, and every
/// rect authored today keeps working.
/// </para>
/// <para>
/// None of this reaches a pixel, so invariant 1 is not in play. It is authored
/// data that leaves through the exporter.
/// </para>
/// </remarks>
public sealed class CollisionShape
{
    public string Id { get; set; } = Ids.NewId("shp");

    /// <summary>What the artist calls it, and what an engine importer will see.</summary>
    public string Name { get; set; } = "Body";

    public ShapeRole Role { get; set; } = ShapeRole.Hurtbox;
}

/// <summary>
/// A collision shape's rectangle on one drawing, in document pixels.
/// </summary>
/// <remarks>
/// Top-left origin and document pixels, like <see cref="AnchorPoint"/> and unlike
/// anything normalised or pivot-relative: the exporter is the only thing that
/// needs a cell-relative, pivot-relative or world-unit figure, and it has the trim
/// rect and the pivot to compute one. Storing a converted value here would bake
/// the trim into the record and make a re-export at a different trim wrong.
/// </remarks>
public sealed record ShapeBox(double X, double Y, double W, double H)
{
    /// <summary>
    /// The rectangle's centre, which is what a collider offset is measured from.
    /// </summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> because a read-only property serializes like any other
    /// one, and a convenience getter that writes two derived keys onto every shape
    /// of every frame is the exact mistake <c>BlendOrNormal</c> made.
    /// </remarks>
    [JsonIgnore] public double CentreX => X + W / 2;

    /// <inheritdoc cref="CentreX"/>
    [JsonIgnore] public double CentreY => Y + H / 2;
}
