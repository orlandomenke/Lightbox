namespace Lightbox.App.ViewModels;

/// <summary>
/// What a drop on a docker row would do, as the row shows it while the pointer
/// is over it.
/// </summary>
/// <remarks>
/// <b>A hint rather than a rectangle.</b> The docking overlay draws itself
/// because it spans the window; a layer row already scrolls, restyles and
/// re-templates on its own, so the cheapest correct indicator is a flag the row
/// binds. It also makes the decision testable without a visual tree — which
/// matters here, because synthetic drag input through Xvfb is exactly the
/// unreliable road <c>MANUAL_TESTING.md</c> warns about, and a dropped event
/// looks the same as a wrong answer.
/// </remarks>
public enum LayerDropHint
{
    /// <summary>Nothing is hovering this row.</summary>
    None,

    /// <summary>It would land above this row — toward the viewer.</summary>
    Above,

    /// <summary>It would land below this row.</summary>
    Below,

    /// <summary>It would go inside this folder.</summary>
    Into,
}

/// <summary>
/// Where a drop lands, decided from the pointer's position down a row.
/// </summary>
/// <remarks>
/// <para>
/// Split out of the window's drag handlers so the rule can be read and tested
/// on its own. The handlers keep what only they can do — finding the row under
/// the pointer, moving the ghost — and this keeps what an artist would describe
/// as the behaviour.
/// </para>
/// <para>
/// <b>A folder header is three zones and a layer row is two</b>, because a
/// folder is the one target that can be joined as well as passed. Splitting the
/// header in half would make "file this layer away" and "put it above the
/// folder" the same gesture at a one-pixel boundary; giving the middle half to
/// <see cref="LayerDropHint.Into"/> leaves a quarter at each end for the
/// artist who means beside rather than in.
/// </para>
/// <para>
/// <b>A folder being dragged has no <c>Into</c> anywhere</b>, and that is not a
/// styling choice: <c>Layer.GroupId</c> is a single id, so folders do not nest
/// and there is nothing for a folder to be filed into. The header then splits
/// in half like any other row.
/// </para>
/// </remarks>
public static class LayerDropPlan
{
    /// <summary>The share of a folder header at each end that means "beside".</summary>
    /// <remarks>
    /// A quarter each, so the middle half files into the folder. Wide enough to
    /// hit with a pen on a 24-pixel row, narrow enough that the common gesture —
    /// dropping a layer into the folder you are pointing at — is the one you get
    /// by aiming at the middle of it.
    /// </remarks>
    public const double HeaderEdgeShare = 0.25;

    /// <summary>
    /// What dropping here would do.
    /// </summary>
    /// <param name="fraction">
    /// How far down the row the pointer is, 0 at the top edge and 1 at the
    /// bottom. Values outside that are clamped rather than refused: a pointer a
    /// pixel past the edge of the row it is closest to is still pointing at it.
    /// </param>
    /// <param name="targetIsFolder">The row under the pointer is a folder header.</param>
    /// <param name="draggingFolder">What is being carried is a folder, not a layer.</param>
    public static LayerDropHint Resolve(double fraction, bool targetIsFolder, bool draggingFolder)
    {
        var y = Math.Clamp(fraction, 0, 1);
        if (targetIsFolder && !draggingFolder)
        {
            if (y < HeaderEdgeShare) return LayerDropHint.Above;
            if (y > 1 - HeaderEdgeShare) return LayerDropHint.Below;
            return LayerDropHint.Into;
        }
        return y < 0.5 ? LayerDropHint.Above : LayerDropHint.Below;
    }
}
