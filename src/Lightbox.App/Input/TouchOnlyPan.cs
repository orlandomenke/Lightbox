using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;

namespace Lightbox.App.Input;

/// <summary>
/// The pan gesture every scroller carries, for a finger only.
/// </summary>
/// <remarks>
/// <para>
/// A scroller's content can be panned by dragging it, and the framework offers
/// that to a pen as well as to a finger. For a finger it is the only way to
/// scroll. For a pen in this application it is the wrong reading of nearly
/// every drag: reordering a layer, sweeping a block of cels, carrying a swatch
/// to the canvas are all pen drags that begin inside a scrolling list.
/// </para>
/// <para>
/// <b>What is measured, and what is not.</b> Measured: the same hand-raised
/// drag pans a plain list by the same amount for a pen as for a finger, and
/// with this in place the pen pans it not at all while the finger still does
/// (<c>PenDoesNotPanTests</c>). Not reproduced: the gesture taking the pointer
/// away from a drag already under way. It does take the pointer once it has
/// travelled its start distance, and that would end the Layers docker's drag,
/// but a headless run could not make it happen — once the gesture holds the
/// pointer the platform feeds it directly, which raised events cannot do. The
/// report this answers (B421) was of a pen on a tablet, and only a tablet can
/// say whether that half of it is gone.
/// </para>
/// <para>
/// A pen has the wheel, the scrollbar and the drag-to-the-edge scroll; it does
/// not need this one.
/// </para>
/// <para>
/// A subclass rather than a filter in front, because the gesture is fed from
/// the routed events with handled ones included: nothing upstream can keep a
/// press from it by marking it handled.
/// </para>
/// </remarks>
public sealed class TouchOnlyScrollGestureRecognizer : ScrollGestureRecognizer
{
    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        // Never tracked, so never moved and never captured: the three later
        // callbacks all begin by asking whether this pointer is the tracked one.
        if (e.Pointer.Type == PointerType.Pen) return;
        base.PointerPressed(e);
    }
}

/// <summary>
/// Puts <see cref="TouchOnlyScrollGestureRecognizer"/> on every scroller, as
/// each one appears.
/// </summary>
/// <remarks>
/// <b>As each appears, not once at startup.</b> Dockers are built late, rebuilt
/// on a workspace switch and floated into windows of their own, and a scroller
/// the walk had missed would pan under a pen with nothing to say why that one
/// list behaves differently. The framework's gesture arrives with the
/// scroller's template, so it is swapped when the presenter is loaded.
/// </remarks>
public static class TouchOnlyPan
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Control.LoadedEvent.AddClassHandler<ScrollContentPresenter>((presenter, _) => Apply(presenter));
    }

    internal static void Apply(ScrollContentPresenter presenter)
    {
        ScrollGestureRecognizer? stock = null;
        foreach (var recognizer in presenter.GestureRecognizers)
        {
            if (recognizer is TouchOnlyScrollGestureRecognizer) return;
            if (recognizer is ScrollGestureRecognizer scroll) stock = scroll;
        }
        if (stock is null) return;

        var touchOnly = new TouchOnlyScrollGestureRecognizer
        {
            ScrollStartDistance = stock.ScrollStartDistance,
        };
        // What the template binds on the stock one, bound again: which ways
        // this presenter can scroll, and whether a flick keeps going.
        touchOnly.Bind(
            ScrollGestureRecognizer.CanHorizontallyScrollProperty,
            presenter.GetObservable(ScrollContentPresenter.CanHorizontallyScrollProperty));
        touchOnly.Bind(
            ScrollGestureRecognizer.CanVerticallyScrollProperty,
            presenter.GetObservable(ScrollContentPresenter.CanVerticallyScrollProperty));
        touchOnly.Bind(
            ScrollGestureRecognizer.IsScrollInertiaEnabledProperty,
            presenter.GetObservable(ScrollViewer.IsScrollInertiaEnabledProperty));

        presenter.GestureRecognizers.Remove(stock);
        presenter.GestureRecognizers.Add(touchOnly);
    }
}
