using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The tool rail's width, as opposed to what it lays out inside it.
/// </summary>
/// <remarks>
/// <para>
/// Two defects, reported together and with one cause between them: <i>"it
/// starts at a width showing way too much space around it. Then I am able to
/// shrink it. But unable to grow it."</i>
/// </para>
/// <list type="bullet">
/// <item>The column was a fixed 88px chosen for a two-column layout, and Q56
/// then made the count depend on the window height — so a tall window laid out
/// one 34px column inside it and showed dead space down both sides.</item>
/// <item>The splitter's right-hand neighbour is the left dock strip, which is
/// 0px with nothing docked there. A stock <c>GridSplitter</c> moves width
/// between its two neighbours, so there was nothing to take: shrinking worked,
/// growing did not.</item>
/// </list>
/// </remarks>
[Collection("BrushState")]
public sealed class ToolRailWidthTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainWindow Shown(double w, double h)
    {
        var window = new MainWindow { Width = w, Height = h };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Grid WorkArea(MainWindow w) => w.FindControl<Grid>("WorkArea")!;

    private static void Drag(MainWindow window, double dx)
    {
        var splitter = WorkArea(window).Children
            .OfType<Lightbox.App.Controls.RailSplitter>().First();
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragStartedEvent, Vector = default });
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(dx, 0) });
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent, Vector = new Vector(dx, 0) });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The rail is no wider than the tools it is showing. Measured as slack
    /// rather than as an exact width, because the exact width is chrome — a
    /// border, two margins — and pinning it would make this test fail for a
    /// change to the padding rather than for the defect it guards.
    /// </summary>
    [AvaloniaFact]
    public void TheRailIsNoWiderThanTheColumnsItChose()
    {
        var window = Shown(1400, 1400);
        var rail = window.FindControl<WrapPanel>("ToolButtons")!;
        var column = WorkArea(window).ColumnDefinitions[0];

        var slack = column.ActualWidth - rail.Width;
        output.WriteLine($"tall: column {column.ActualWidth:F1}, one column of tools {rail.Width:F1}, slack {slack:F1}");

        Assert.Equal(34.0, rail.Width, 1);          // Q56: one column when tall
        Assert.True(slack < 20, $"the rail wastes {slack:F1}px around a {rail.Width}px column");
    }

    /// <summary>The other half of Q56: two columns, and the rail widens to hold them.</summary>
    [AvaloniaFact]
    public void TheRailWidensWhenTheWindowIsShortEnoughToNeedTwoColumns()
    {
        var window = Shown(1400, 1400);
        var rail = window.FindControl<WrapPanel>("ToolButtons")!;
        var column = WorkArea(window).ColumnDefinitions[0];
        var tall = column.ActualWidth;

        window.Height = 380;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"tall {tall:F1} -> short {column.ActualWidth:F1}, tools {rail.Width:F1}");
        Assert.Equal(68.0, rail.Width, 1);
        Assert.True(column.ActualWidth > tall, "two columns of tools must not be squeezed into a one-column rail");
        Assert.True(column.ActualWidth - rail.Width < 20, "and must not leave a gap either");
    }

    /// <summary>
    /// <b>The reported bug.</b> Nothing is docked on the left, so the column
    /// beside the splitter is 0px — and a stock splitter takes its width from
    /// exactly there. Shrinking worked because a collapsed column will accept
    /// width; growing had nothing to draw on.
    /// </summary>
    [AvaloniaFact]
    public void TheRailGrowsAsWellAsShrinks_WithNothingDockedBesideIt()
    {
        var window = Shown(1400, 900);
        var columns = WorkArea(window).ColumnDefinitions;
        Assert.True(columns[2].ActualWidth < 1, "this test is about the collapsed left strip");

        // Grow first, because growing is the direction that was broken. There
        // is barely any room to shrink into now that the rail sizes itself —
        // it starts a few pixels above its own MinWidth — which is why this
        // goes out and back rather than in and out.
        var start = columns[0].ActualWidth;
        Drag(window, +60);
        var grown = columns[0].ActualWidth;
        Drag(window, -30);
        var back = columns[0].ActualWidth;
        output.WriteLine($"start {start:F1} -> grow {grown:F1} -> shrink {back:F1}");

        Assert.True(grown > start + 50, $"growing moved only {grown - start:F1}px");
        Assert.True(back < grown - 25, $"shrinking moved only {grown - back:F1}px");
    }

    /// <summary>
    /// A drag is a decision, so the rail stops sizing itself — otherwise the
    /// next window resize would silently take the artist's width back.
    /// </summary>
    [AvaloniaFact]
    public void ADraggedWidthSurvivesTheWindowChangingShape()
    {
        var window = Shown(1400, 1400);
        var columns = WorkArea(window).ColumnDefinitions;

        Drag(window, +70);
        var chosen = columns[0].ActualWidth;
        Assert.True(chosen > 90, $"the drag should have widened the rail, got {chosen:F1}");

        window.Height = 700;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"chosen {chosen:F1}, after resize {columns[0].ActualWidth:F1}");
        Assert.Equal(chosen, columns[0].ActualWidth, 1);
    }

    /// <summary>
    /// The canvas is the one thing that must not lose room to a toolbar, so the
    /// rail stops widening when the centre column reaches its floor — rather
    /// than the drag simply being ignored, which is what it looked like before.
    /// </summary>
    [AvaloniaFact]
    public void TheRailWillNotEatTheCanvas()
    {
        var window = Shown(700, 900);
        var columns = WorkArea(window).ColumnDefinitions;

        Drag(window, +5000);

        output.WriteLine($"rail {columns[0].ActualWidth:F1}, centre {columns[4].ActualWidth:F1} (min {columns[4].MinWidth})");
        Assert.True(columns[0].ActualWidth <= columns[0].MaxWidth + 0.5);
        Assert.True(columns[4].ActualWidth >= columns[4].MinWidth - 0.5);
    }
}
