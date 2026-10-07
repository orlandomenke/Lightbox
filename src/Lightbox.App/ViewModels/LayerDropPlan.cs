namespace Lightbox.App.ViewModels;

/// <summary>
/// What a drop on a docker row would do, as the row shows it while the pointer
/// is over it.
/// </summary>
/// <remarks>
/// <b>A hint rather than a rectangle.</b> The docking overlay draws itself
/// because it spans the window; a layer row already scrolls, restyles and
/// re-templates on its own, so the cheapest correct indicator is a flag the row
/// binds. It also makes the decision testable without a visual tree.
/// </remarks>
public enum LayerDropHint
{
    /// <summary>Nothing is hovering this row.</summary>
    None,

    /// <summary>It would land above this row — toward the viewer.</summary>
    Above,

    /// <summary>It would land below this row.</summary>
    Below,

    /// <summary>It would go inside this folder, at the top of it.</summary>
    Into,

    /// <summary>
    /// It would land below this row's whole folder, outside it — offered only
    /// where there is no loose row underneath to aim at instead.
    /// </summary>
    BelowFolder,
}

/// <summary>What kind of row the pointer is over, as far as a drop cares.</summary>
public enum LayerDropTarget
{
    /// <summary>A layer that is in no folder.</summary>
    LooseLayer,

    /// <summary>A layer inside a folder, with more of the folder listed under it.</summary>
    GroupedLayer,

    /// <summary>A grouped layer that is the last listed row of its folder.</summary>
    LastGroupedLayer,

    /// <summary>
    /// The last row of a folder that is also the last row in the docker — a
    /// folder at the very bottom of a document with no paper.
    /// </summary>
    /// <remarks>
    /// Its bottom quarter is the only place left that can mean "under the
    /// folder": an open header's lower part means inside, and there is no
    /// loose row below to drop on.
    /// </remarks>
    BottomGroupedLayer,

    /// <summary>A folder header whose members are listed under it.</summary>
    OpenFolder,

    /// <summary>A folder header whose members are hidden.</summary>
    CollapsedFolder,
}

/// <summary>
/// Where a drop lands, decided from the pointer's height in the docker.
/// </summary>
/// <remarks>
/// <para>
/// Split out of the window's drag handlers so the rule can be read and tested
/// on its own. The handlers keep what only they can do — measuring the rows,
/// moving the ghost — and this keeps what an artist would describe as the
/// behaviour.
/// </para>
/// <para>
/// <b>Every pixel of the list answers.</b> The first version asked the control
/// under the pointer which row it belonged to, and the docker is not all rows:
/// two pixels of spacing between every pair, fourteen of indent in front of
/// every folder member. Each of those answered "nowhere", the cursor flipped to
/// refused, and a release there was thrown away — most of why dragging into and
/// within a folder felt like aiming at a moving target. <see cref="Locate"/>
/// gives every height in the list to the nearest row.
/// </para>
/// <para>
/// <b>The line is drawn where the layer will land.</b> The lower quarter of an
/// open folder's header used to mean "below the folder": it drew its line
/// directly under the header — between the header and its own first member —
/// and then put the layer under the whole block, rows away from the line.
/// Directly under an open header <em>is</em> the top of the folder, so that is
/// what it now means.
/// </para>
/// <para>
/// <b>A folder being dragged files into another folder like a layer does</b>
/// (Q204 — folders nest). Dropping one into itself, or into a folder inside
/// it, is refused by the move, and the caller draws no hint for it.
/// </para>
/// </remarks>
public static class LayerDropPlan
{
    /// <summary>The share of a folder header at its top edge that means "above the folder".</summary>
    /// <remarks>
    /// A quarter: wide enough to hit with a pen on a 24-pixel row, narrow
    /// enough that the common gesture — dropping a layer into the folder you
    /// are pointing at — is what aiming at the header gets.
    /// </remarks>
    public const double HeaderEdgeShare = 0.25;

    /// <summary>The share of a folder header at each edge that means "beside the folder".</summary>
    /// <remarks>
    /// A third each way since Q210: above, into, below. The quarter that came
    /// before left the slot between two folders a sliver to hit — and on an open
    /// folder there was no "below" on the header at all, so putting a folder
    /// after another meant finding the top edge of whatever row came next. The
    /// owner reported folders could not be moved between folders.
    /// </remarks>
    public const double FolderHeaderEdgeShare = 1.0 / 3;

    /// <summary>
    /// Which row a height in the list belongs to, and how far down that row it is.
    /// </summary>
    /// <param name="spans">Each row's top and bottom, in list order, in one coordinate space.</param>
    /// <param name="y">The pointer's height in the same space.</param>
    /// <returns>
    /// The row index (−1 when there are no rows) and the fraction down it, 0 at
    /// its top and 1 at its bottom. A gap between two rows goes to whichever
    /// edge is nearer; above the first row or below the last clamps to it.
    /// </returns>
    public static (int Index, double Fraction) Locate(IReadOnlyList<(double Top, double Bottom)> spans, double y)
    {
        if (spans.Count == 0) return (-1, 0.5);
        for (var i = 0; i < spans.Count; i++)
        {
            var (top, bottom) = spans[i];
            // The boundary with the next row is the middle of the gap between them.
            var limit = i + 1 < spans.Count ? (bottom + spans[i + 1].Top) / 2 : double.PositiveInfinity;
            if (y >= limit) continue;
            var height = bottom - top;
            return (i, height <= 0 ? 0.5 : Math.Clamp((y - top) / height, 0, 1));
        }
        return (spans.Count - 1, 1);
    }

    /// <summary>What dropping here would do.</summary>
    /// <param name="fraction">How far down the row the pointer is; clamped to 0..1.</param>
    /// <param name="target">What the row under the pointer is.</param>
    /// <remarks>
    /// <para>
    /// One table for a carried layer and a carried folder (Q204): folders nest,
    /// so a folder dropped on a header goes into it like a layer does. A drop
    /// that is impossible — a folder into itself, anything under the paper —
    /// is not this table's business: the caller tries the move and draws no
    /// hint for one that would be refused or change nothing.
    /// </para>
    /// <para>
    /// A header's top third is above the folder, outside it; its middle third is
    /// into it; its bottom third is below the whole folder, outside it — open or
    /// closed (Q210; it was a quarter, and an open folder had no "below"). The last row of a
    /// folder at the bottom of the docker gives its bottom quarter to "below the
    /// folder", which is otherwise the one place nothing can be aimed at.
    /// </para>
    /// </remarks>
    public static LayerDropHint Resolve(double fraction, LayerDropTarget target)
    {
        var y = Math.Clamp(fraction, 0, 1);
        var upper = y < 0.5;
        return target switch
        {
            LayerDropTarget.BottomGroupedLayer =>
                y > 1 - HeaderEdgeShare ? LayerDropHint.BelowFolder
                : upper ? LayerDropHint.Above
                : LayerDropHint.Below,
            // Below an OPEN folder is below the whole folder, and the hint is
            // drawn under its last row rather than under the header — the line
            // and the landing must agree (ShowLayerDropHint).
            LayerDropTarget.OpenFolder =>
                y < FolderHeaderEdgeShare ? LayerDropHint.Above
                : y > 1 - FolderHeaderEdgeShare ? LayerDropHint.BelowFolder
                : LayerDropHint.Into,
            LayerDropTarget.CollapsedFolder =>
                y < FolderHeaderEdgeShare ? LayerDropHint.Above
                : y > 1 - FolderHeaderEdgeShare ? LayerDropHint.Below
                : LayerDropHint.Into,
            _ => upper ? LayerDropHint.Above : LayerDropHint.Below,
        };
    }
}
