using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lightbox.App.Services;

namespace Lightbox.App.Controls;

/// <summary>
/// A region of chrome that is drawn at the interface scale (Q200).
/// </summary>
/// <remarks>
/// <para>
/// Its content is laid out at 100% — every size in <c>Density.axaml</c> and
/// every literal in a view means what it says — and the whole region is then
/// scaled as layout, so it also <em>takes</em> the larger room from its parent.
/// A docker at 125% is a quarter wider on screen, not the same width with
/// bigger text crammed into it.
/// </para>
/// <para>
/// <b>At 100% there is no transform at all</b>, not an identity one: optional
/// means absent, and an artist who never touches the setting pays nothing for
/// it existing.
/// </para>
/// <para>
/// Each instance owns its transform and follows the scale only while it is on
/// screen, and weakly even then (B281). A shared transform would be simpler
/// and would tie every region to one object for the life of the process.
/// </para>
/// </remarks>
public class ScaledChrome : LayoutTransformControl, IFollowsUiScale
{
    protected override Type StyleKeyOverride => typeof(LayoutTransformControl);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiScale.Follow(this);
        Apply(UiScale.Current);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiScale.Unfollow(this);
        base.OnDetachedFromVisualTree(e);
    }

    void IFollowsUiScale.OnUiScaleChanged(double old, double now) => Apply(now);

    /// <summary>The factor this region is currently drawn at.</summary>
    public double Scale => LayoutTransform is ScaleTransform s ? s.ScaleX : 1.0;

    private void Apply(double scale)
    {
        if (scale == 1.0)
        {
            if (LayoutTransform is not null) LayoutTransform = null;
            return;
        }
        if (LayoutTransform is ScaleTransform current && current.ScaleX == scale) return;
        LayoutTransform = new ScaleTransform(scale, scale);
    }
}
