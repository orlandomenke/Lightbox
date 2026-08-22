using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// Something a variant wears: a symbol riding an anchor through every
/// animation of the subject — Winter Armour's pauldron on the knight's
/// shoulder socket, drawn once (Q143).
/// </summary>
/// <remarks>
/// <para>
/// <b>The anchor is named by <em>name</em>, not id, and that is load-bearing.</b>
/// Anchor ids are per document — the knight's Walk and Run each declare their
/// own "shoulder" — so an id here could only ever reach one animation, which
/// is the opposite of the point. Names are already the cross-document
/// contract: the export sidecar keys anchors by name because "leftHand" is
/// what an engine script looks for, and this binds by the same word for the
/// same reason.
/// </para>
/// <para>
/// <b>The anchor is also the per-frame override channel.</b> Q143 asked for
/// "always overwriteable", and most of the layering falls out of data that
/// already exists rather than a new store: nudge the anchor on one drawing
/// and the attachment moves there; aim it and the attachment turns; clear it
/// from a drawing and the attachment is absent there; and a wholesale
/// different drawing is <c>ProjectIo.OverrideDocument</c>, where the
/// attachment machinery simply finds no anchor to ride unless the override
/// declares one.
/// </para>
/// <para>
/// Nothing here reaches the document record. An attachment resolves to
/// <em>ephemeral</em> <see cref="SymbolPlacement"/>s at render time — never
/// stored on a frame — so invariant 1 holds: a document opened without its
/// project renders exactly what its own record says.
/// </para>
/// </remarks>
public sealed class VariantAttachment
{
    public string Id { get; set; } = Ids.NewId("att");

    /// <summary>The anchor's <em>name</em>, matched per document — see the class remarks.</summary>
    public string Anchor { get; set; } = "";

    /// <summary>The project symbol this shows.</summary>
    public string SymbolId { get; set; } = "";

    /// <summary>
    /// Offset from the anchor, in document pixels — in the anchor's own frame
    /// when <see cref="FollowAngle"/> is on, so a hilt stays in the fist as
    /// the fist turns.
    /// </summary>
    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    public double Scale { get; set; } = 1;

    /// <summary>Extra rotation in degrees, on top of the anchor's aim when following.</summary>
    public double AngleDeg { get; set; }

    /// <summary>
    /// Turn with the anchor's authored direction (Q144). On by default —
    /// riding the aim is why the aim exists — and off for the things that
    /// stay upright however the limb turns, a lantern on a swinging arm.
    /// </summary>
    public bool FollowAngle { get; set; } = true;
}

/// <summary>
/// Resolving a variant's attachments into the placements a frame shows.
/// </summary>
public static class VariantAttachments
{
    /// <summary>
    /// The ephemeral placements a variant's attachments produce at one
    /// timeline index of one document, in attachment order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pure function of the record — anchors resolved through holds like the
    /// exporter resolves them, angles authored, nothing rolled — so a re-render
    /// is the same render (invariant 2) and the AI's view of a frame matches
    /// the artist's.
    /// </para>
    /// <para>
    /// An anchor name the document does not declare, or declares but does not
    /// place on this drawing, contributes nothing: absence is the off state,
    /// exactly as it is for a hitbox, and it is what makes "no sword in the
    /// frames where the hand is off-screen" one <em>Clear here</em> per
    /// drawing rather than a feature.
    /// </para>
    /// <para>
    /// The placements are new objects every call and never stored — handing a
    /// caller cached instances would invite exactly the mutation this type
    /// exists to avoid. Cycling symbols advance with the timeline for free:
    /// <see cref="SymbolPlacement.FrameIndexAt"/> already counts from the cel
    /// index, and a zero offset starts the cycle at the sequence's start.
    /// </para>
    /// </remarks>
    public static List<SymbolPlacement> ResolveAt(
        Scene scene, int index, IReadOnlyList<VariantAttachment> attachments)
    {
        var placements = new List<SymbolPlacement>();
        if (attachments.Count == 0 || scene.Anchors is not { Count: > 0 } declared) return placements;

        var resolved = Anchors.ResolvedAt(scene, index);
        foreach (var attachment in attachments)
        {
            if (attachment.SymbolId.Length == 0) continue;
            var anchor = declared.FirstOrDefault(a =>
                string.Equals(a.Name, attachment.Anchor, StringComparison.Ordinal));
            if (anchor is null || !resolved.TryGetValue(anchor.Id, out var point)) continue;

            var baseAngle = attachment.FollowAngle ? point.AngleDeg ?? 0 : 0;
            var rad = baseAngle * (Math.PI / 180.0);
            var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
            placements.Add(new SymbolPlacement
            {
                SymbolId = attachment.SymbolId,
                X = point.X + attachment.OffsetX * cos - attachment.OffsetY * sin,
                Y = point.Y + attachment.OffsetX * sin + attachment.OffsetY * cos,
                ScaleX = attachment.Scale,
                ScaleY = attachment.Scale,
                Angle = baseAngle + attachment.AngleDeg,
            });
        }
        return placements;
    }
}
