using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Every boxed control is the same shape, and a field is a well.
/// </summary>
/// <remarks>
/// <para>
/// Reported as "every boxed element on the page has rounded corners except
/// those text boxes". It was exact: a ComboBox and a NumericUpDown sit in the
/// same row of the Layers docker, and the combo was round while the field was
/// square. Nothing was wrong with either control on its own, which is why it
/// survived every review that looked at one control at a time — the same shape
/// of miss as the button sizes <c>DESIGN.md</c> calls "the most common way this
/// app has looked unfinished".
/// </para>
/// <para>
/// The ground is asserted here too, and it is a **reversal**: fields were given
/// a white tint so they sat above their panel, read off the first reference.
/// The owner's call is the opposite — a field takes the darkest surface and
/// reads as a well cut into the panel. That is what makes a field on a docker,
/// a field on a dialog and a field in a flyout the same colour, with only the
/// surface behind them changing.
/// </para>
/// </remarks>
public class FieldShapeTests
{
    private static Window Host(out TextBox box, out NumericUpDown num, out ComboBox combo)
    {
        box = new TextBox { Text = "x" };
        num = new NumericUpDown { Value = 1 };
        combo = new ComboBox();
        var w = new Window { Width = 320, Height = 220 };
        w.Content = new StackPanel { Children = { box, num, combo } };
        w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return w;
    }

    /// <summary>The border that actually paints the control's box.</summary>
    private static Border PaintedBox(TemplatedControl c) =>
        c.GetVisualDescendants().OfType<Border>()
            .First(b => b.Bounds.Width > 0 && b.BorderThickness != default);

    [AvaloniaFact]
    public void EveryFieldIsTheSameShapeAsEveryOtherOne()
    {
        Host(out var box, out var num, out var combo);

        var shapes = new (string Name, CornerRadius Corner)[]
        {
            ("TextBox", PaintedBox(box).CornerRadius),
            ("NumericUpDown", PaintedBox(num).CornerRadius),
            ("ComboBox", PaintedBox(combo).CornerRadius),
        };

        var distinct = shapes.Select(s => s.Corner).Distinct().ToList();
        Assert.True(distinct.Count == 1,
            "fields disagree on their corner radius: "
            + string.Join(", ", shapes.Select(s => $"{s.Name}={s.Corner}")));

        // And it is actually rounded, not uniformly square — "they all agree"
        // is satisfied by every one of them being wrong the same way.
        Assert.True(distinct[0].TopLeft > 0, $"fields are square: {distinct[0]}");
    }

    [AvaloniaFact]
    public void TheInnerTextBoxFitsInsideTheFieldThatHostsIt()
    {
        // The first version of the corner fix passed while the screen stayed
        // square, because every property read correct: the radius was set, and
        // the corners were CLIPPED, not painted square. The generic
        // `TextBox MinHeight=24` rule sat later in Density.axaml than the
        // template-scoped one and silently won — a 24px inner box inside a
        // 22px host loses its top border and all four corners to the clip.
        // Properties cannot see that; only geometry can, so this measures it.
        Host(out _, out var num, out _);

        var inner = num.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(inner.Bounds.Height <= num.Bounds.Height + 0.5,
            $"the inner text box is {inner.Bounds.Height} tall inside a "
            + $"{num.Bounds.Height} host — its corners and top border are being "
            + "clipped off, which reads as a square field");
    }

    [AvaloniaFact]
    public void TheDigitsSitCentredInTheField()
    {
        // B140. Fluent's theme padding is "10,6,6,5" — eleven vertical pixels,
        // which in a 22px field leaves less than one line box of room. The
        // digits sat low and lost their bottoms: "100" read as unreadable
        // stumps. The padding flows template-bound from the control's own
        // Padding into the inner TextBox, so the control's Padding is the one
        // property that reaches it — styling the inner parts directly loses to
        // the template binding (Template outranks Style in Avalonia).
        Host(out _, out var num, out _);

        var presenter = num.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
        double top = 0;
        for (Visual? n = presenter; n is not null && !ReferenceEquals(n, num); n = n.GetVisualParent())
        {
            top += n.Bounds.Y;
        }

        var centred = (num.Bounds.Height - presenter.Bounds.Height) / 2;
        Assert.True(Math.Abs(top - centred) <= 1.0,
            $"the text sits at y={top:F1} in a {num.Bounds.Height} field; centred would be {centred:F1}");
        // And it fits. A line pushed down far enough is also clipped, and a
        // centred-but-overflowing line would pass the assertion above.
        Assert.True(top >= -0.5 && top + presenter.Bounds.Height <= num.Bounds.Height + 0.5,
            $"the {presenter.Bounds.Height}px line overflows the {num.Bounds.Height}px field from y={top:F1}");
    }

    [AvaloniaFact]
    public void AFieldIsAWellRatherThanARaisedSurface()
    {
        // The reversal, stated as the rule rather than as a colour: a field
        // takes the *darkest* surface, so it is a hole in whatever it lands on
        // and the same colour everywhere it lands.
        Application.Current!.TryFindResource("BackgroundPrimaryBrush", out var primary);
        var well = ((SolidColorBrush)primary!).Color;

        foreach (var key in new[] { "TextControlBackground", "ComboBoxBackground" })
        {
            Assert.True(Application.Current!.TryFindResource(
                key, Avalonia.Styling.ThemeVariant.Dark, out var found), $"{key} does not resolve");
            Assert.Equal(well, Assert.IsType<SolidColorBrush>(found).Color);
        }

        // Hover still lifts. The direction argument holds at the point of
        // contact even though it lost the resting state: pointing at something
        // should make it lighter, never darker, and Fluent's original did the
        // opposite — #66000000 resting against #99000000 hovered.
        Application.Current!.TryFindResource(
            "TextControlBackgroundPointerOver", Avalonia.Styling.ThemeVariant.Dark, out var hover);
        var tint = Assert.IsType<SolidColorBrush>(hover);
        Assert.True(tint.Color.R > well.R && tint.Opacity is > 0 and < 1,
            $"hover {tint.Color} at {tint.Opacity} does not lift the well {well}");
    }
}
