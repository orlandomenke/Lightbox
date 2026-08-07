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
