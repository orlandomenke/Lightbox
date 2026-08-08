using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A glyph toggle's state is the icon, and the icon actually changes.
/// </summary>
/// <remarks>
/// <para>
/// The eye, the lock, the onion skin: the old treatment filled a checked
/// toggle with the accent, so the panel said "on" in colour and nothing in
/// shape. The owner's call — one button colour with a hover, the glyph swaps
/// (open eye / closed eye), the off state dims, and the only purple left is a
/// subtle press flash.
/// </para>
/// <para>
/// Asserted at the Path, because the recipe is styles all the way down and a
/// missing resource fails as "the icon just never changes" rather than as an
/// error anyone sees.
/// </para>
/// </remarks>
public class GlyphToggleTests
{
    private static (ToggleButton Toggle, Path Glyph) Show(params string[] classes)
    {
        var toggle = new ToggleButton { Content = new Path() };
        foreach (var c in classes) toggle.Classes.Add(c);
        var w = new Window { Width = 100, Height = 100, Content = toggle };
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (toggle, toggle.GetVisualDescendants().OfType<Path>().First());
    }

    [AvaloniaFact]
    public void TheEyeOpensAndClosesRatherThanChangingColour()
    {
        var (toggle, glyph) = Show("icon", "glyph", "eye");

        toggle.IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var closed = glyph.Data;
        var dimmed = glyph.Opacity;

        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var open = glyph.Data;

        Assert.NotNull(closed);
        Assert.NotNull(open);
        Assert.NotSame(closed, open);          // the DRAWING is the state
        Assert.True(dimmed < glyph.Opacity,    // and off is dimmer than on
            $"off ({dimmed}) is not dimmer than on ({glyph.Opacity})");
    }

    [AvaloniaFact]
    public void EveryStatefulGlyphResolvesBothDrawings()
    {
        // The recipe is style-driven, so a renamed resource fails silently: the
        // icon simply never swaps. One loop, both states, every role.
        foreach (var role in new[] { "eye", "lock", "onion" })
        {
            var (toggle, glyph) = Show("icon", "glyph", role);
            toggle.IsChecked = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var off = glyph.Data;
            toggle.IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(off is not null && glyph.Data is not null && !ReferenceEquals(off, glyph.Data),
                $"the {role} glyph does not swap between its states");
        }
    }

    [AvaloniaFact]
    public void AGlyphToggleCarriesNoAccentFill()
    {
        // The purple box is what this treatment replaces. The checked state
        // must not paint the accent behind the glyph — the press flash is the
        // only purple left, and it is transient.
        var (toggle, _) = Show("icon", "glyph", "eye");
        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var presenter = toggle.GetVisualDescendants().OfType<ContentPresenter>().First();
        var ground = presenter.Background as ISolidColorBrush;
        Assert.True(ground is null || ground.Color.A == 0 || ground.Opacity == 0,
            $"a checked glyph toggle paints {ground?.Color}, and the state belongs in the glyph");
    }
}
