using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The arranged behaviour of the flexible overflow bar — the arithmetic is
/// covered in <see cref="OverflowBarTests"/>; this drives the real panel.
/// </summary>
public class OverflowBarFlexTests
{
    private static (OverflowBar Bar, Border A, Border Flex, Border B, Border C) Host(double width)
    {
        var a = new Border { Width = 100, Height = 20 };
        var flex = new Border { MinWidth = 50, MaxWidth = 150, Height = 20 };
        OverflowBar.SetFlex(flex, true);
        var b = new Border { Width = 100, Height = 20 };
        var c = new Border { Width = 100, Height = 20 };
        var bar = new OverflowBar { Spacing = 10, Width = width };
        bar.Children.Add(a);
        bar.Children.Add(flex);
        bar.Children.Add(b);
        bar.Children.Add(c);
        var window = new Window { Content = bar, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (bar, a, flex, b, c);
    }

    [AvaloniaFact]
    public void WithRoomToSpareTheFlexChildIsAtItsCap()
    {
        var (_, _, flex, _, c) = Host(600);
        Assert.Equal(150, flex.Bounds.Width, 1.0);
        Assert.True(c.Bounds.X + c.Bounds.Width <= 600.5, "the last child overflowed a bar with room to spare");
    }

    [AvaloniaFact]
    public void SqueezingTheBarShrinksTheFlexChildFirst()
    {
        // Natural: 100+150+100+100 + 3x10 = 480. At 440 the flexible child
        // gives the missing 40 and everything stays on the bar.
        var (bar, _, flex, _, c) = Host(440);
        Assert.Equal(110, flex.Bounds.Width, 1.0);
        Assert.True(c.Bounds.X + c.Bounds.Width <= 440.5,
            $"the last child ends at {c.Bounds.X + c.Bounds.Width} in a 440 bar");
        var more = bar.Children.OfType<Button>().Single();
        Assert.False(more.IsVisible, "nothing overflowed, so there is no menu to open");
    }

    [AvaloniaFact]
    public void PastTheFloorTheMenuOpensAndNothingSticksOut()
    {
        // Even with the flexible child at its 50 floor the row wants 380;
        // at 300 something must leave, and whatever stays must fit — with
        // the ▾ visible inside the bar, not past its edge.
        var (bar, _, flex, _, _) = Host(300);
        var more = bar.Children.OfType<Button>().Single();
        Assert.True(more.IsVisible, "the bar is over-full but shows no ▾");
        Assert.True(more.Bounds.X + more.Bounds.Width <= 300.5,
            $"the ▾ ends at {more.Bounds.X + more.Bounds.Width} in a 300 bar");
        foreach (var child in bar.Children.OfType<Border>())
        {
            var visibleOnBar = child.Bounds.X < 300;
            if (visibleOnBar)
            {
                Assert.True(child.Bounds.X + child.Bounds.Width <= 300.5,
                    $"a child at x={child.Bounds.X} w={child.Bounds.Width} sticks out of a 300 bar");
                Assert.True(child.Bounds.X + child.Bounds.Width <= more.Bounds.X + 0.5,
                    "a child runs underneath the ▾");
            }
        }
    }
    [AvaloniaFact]
    public void ZeroWidthChildrenCostNoRoom()
    {
        // B308's third cause. A tool-options group is a UserControl whose
        // contents are gated on the tool, so with another tool in hand it stays
        // visible and measures to nothing — and each one was still costing a
        // spacing gap and a place in the row. Seven of them spent 98px of a
        // 273px bar on nothing, which is what pushed the text tool's options
        // into the ▾ at every ordinary window size.
        var bar = new OverflowBar { Spacing = 14 };
        for (var i = 0; i < 7; i++)
        {
            bar.Children.Add(new Border { Width = 0, Height = 20 });
        }
        var wanted = new Border { Width = 200, Height = 20 };
        bar.Children.Add(wanted);

        var window = new Window { Content = bar, Width = 300, Height = 60 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(300, 60));
        window.Arrange(new Rect(0, 0, 300, 60));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The seven empties must buy nothing, so the one real child starts at 0
        // and stays on the bar rather than being parked past its right edge.
        Assert.Equal(0, wanted.Bounds.X);
        Assert.True(
            wanted.Bounds.X < bar.Bounds.Width,
            $"a 200px child was pushed off a {bar.Bounds.Width:F0}px bar by seven empty siblings");

        window.Close();
    }

}
