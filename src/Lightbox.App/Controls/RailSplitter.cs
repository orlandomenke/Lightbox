using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Lightbox.App.Controls;

/// <summary>
/// A column splitter that takes the width it needs from the grid's flexible
/// column rather than from whatever happens to sit next to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug this exists for.</b> A stock <see cref="GridSplitter"/> moves
/// width between the two definitions it sits between, and the tool rail's
/// right-hand neighbour is the left dock strip — which is <c>0px</c> whenever
/// no docker is parked there, which is the default. So there was nothing to
/// take from: the rail could be dragged <em>narrower</em> all day, because the
/// collapsed column will accept width and hand it straight on to the canvas,
/// and could not be dragged <em>wider</em> at all. Dock something on the left
/// and the same drag worked perfectly, which is what made it look arbitrary.
/// </para>
/// <para>
/// None of <c>GridResizeBehavior</c>'s four values fix it, because every one of
/// them names a definition <em>adjacent</em> to the splitter and the adjacent
/// one is the problem. So this resizes the column before it against the first
/// starred column it can find — the canvas — which is where the width was
/// always going to come from and go back to.
/// </para>
/// </remarks>
public class RailSplitter : GridSplitter
{
    private ColumnDefinition? _target;
    private ColumnDefinition? _flex;
    private double _startWidth;
    private double _startFlex;

    protected override void OnDragStarted(VectorEventArgs e)
    {
        _target = null;
        _flex = null;
        if (Parent is not Grid grid) return;

        var mine = GetValue(Grid.ColumnProperty);
        if (mine <= 0 || mine >= grid.ColumnDefinitions.Count) return;

        _target = grid.ColumnDefinitions[mine - 1];
        _flex = grid.ColumnDefinitions.FirstOrDefault(c => c.Width.IsStar);
        _startWidth = _target.ActualWidth;
        _startFlex = _flex?.ActualWidth ?? 0;
    }

    protected override void OnDragDelta(VectorEventArgs e)
    {
        if (_target is null) return;

        var wanted = _startWidth + e.Vector.X;

        // The rail's own limits first, then the canvas's: widening the rail
        // past the point where the canvas hits its MinWidth would otherwise
        // silently squeeze the drawing, which is the one thing on screen that
        // must not lose room to a toolbar.
        wanted = Math.Clamp(wanted, Math.Max(1, _target.MinWidth), _target.MaxWidth);
        if (_flex is { } flex)
        {
            var headroom = _startFlex - Math.Max(flex.MinWidth, 1);
            wanted = Math.Min(wanted, _startWidth + headroom);
        }

        // Pixels, not Auto: a drag is the artist saying how wide this is, and
        // it stays that width until they say otherwise. ReflowToolRail reads
        // exactly this to tell a chosen width from one it is free to pick.
        _target.Width = new GridLength(wanted, GridUnitType.Pixel);
    }

    protected override void OnDragCompleted(VectorEventArgs e)
    {
        _target = null;
        _flex = null;
    }
}
