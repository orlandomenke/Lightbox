using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The numeric fields' editing manners: an emptied field is not an error, a
/// click into one selects the value for typing, and a 0–1 brush fraction
/// reads and writes as 0–100 while the bound value never leaves 0–1.
/// </summary>
public class FieldBehaviourTests(ITestOutputHelper output)
{
    private static NumericUpDown Field(bool percent = false)
    {
        var field = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1,
            Increment = 0.05m,
            FormatString = "0.00",
            Value = 0.42m,
        };
        FieldMath.SetEnabled(field, true);
        if (percent) FieldMath.SetPercent(field, true);
        return field;
    }

    // ---- emptying a field is not a request to unset it ------------------------------------

    /// <summary>
    /// Clearing the text used to convert to null, which a two-way binding to
    /// a double cannot take: the field went into a validation-error state
    /// ("System.InvalidOperationException…") that blocked typing until a drag
    /// moved the value. An empty commit now means "no change".
    /// </summary>
    [AvaloniaFact]
    public void AnEmptiedFieldKeepsItsValueInsteadOfTurningInvalid()
    {
        var field = Field();

        var committed = field.TextConverter!.Convert("", typeof(decimal?), null, CultureInfo.InvariantCulture);

        output.WriteLine($"empty text commits as {committed}");
        Assert.Equal(0.42m, committed);
    }

    [AvaloniaFact]
    public void UnreadableTextAlsoKeepsTheValue()
    {
        var field = Field();

        Assert.Equal(0.42m, field.TextConverter!.Convert("brush", typeof(decimal?), null, CultureInfo.InvariantCulture));
        Assert.Equal(0.42m, field.TextConverter!.Convert("1/0", typeof(decimal?), null, CultureInfo.InvariantCulture));
    }

    // ---- percent fields: the text is 0–100, the value never leaves 0–1 ----------------------

    [AvaloniaFact]
    public void APercentFieldReadsTypedWholeNumbersAsFractions()
    {
        var field = Field(percent: true);
        var converter = field.TextConverter!;

        Assert.Equal(0.5m, converter.Convert("50", typeof(decimal?), null, CultureInfo.InvariantCulture));
        // The field arithmetic still works, on the visible scale.
        Assert.Equal(0.6m, converter.Convert("50+10", typeof(decimal?), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void APercentFieldShowsWholePointsWhateverItsFormatStringSays()
    {
        var field = Field(percent: true);
        var converter = field.TextConverter!;

        // FormatString="0.00" was written for the 0–1 scale; the percent text
        // must not inherit it and render "50.00".
        Assert.Equal("50", converter.ConvertBack(0.5m, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("12.5", converter.ConvertBack(0.125m, typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every 0–1 field in the main window presents as percent. A new fraction
    /// field added without the attribute would be the one field in the app
    /// still speaking 0–1, which is exactly the inconsistency the owner
    /// reported.
    /// </summary>
    [Fact]
    public void EveryFractionFieldInTheMainWindowIsAPercentField()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        var xaml = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));

        var bare = System.Text.RegularExpressions.Regex
            .Matches(xaml, @"<NumericUpDown\b[^>]*?/?>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(tag => tag.Contains("Maximum=\"1\"") && !tag.Contains("FieldMath.Percent=\"True\""))
            .ToList();

        output.WriteLine(bare.Count == 0
            ? "every 0–1 field presents as percent"
            : $"still 0–1: {string.Join("\n", bare.Select(t => t[..Math.Min(t.Length, 90)]))}");
        Assert.Empty(bare);
    }

    // ---- a click selects the value ------------------------------------------------------------

    private static (Window Window, NumericUpDown Field) Shown()
    {
        var field = new NumericUpDown
        {
            Minimum = 0, Maximum = 100, Increment = 1, Value = 42, Width = 90,
        };
        ValueDrag.SetEnabled(field, true);
        var window = new Window { Content = field, Width = 300, Height = 100 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, field);
    }

    private static Point Centre(Visual item, Window window) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void ClickingAnUnfocusedFieldSelectsTheWholeValue()
    {
        var (window, field) = Shown();
        var centre = Centre(field, window);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = field.FindDescendantOfType<TextBox>()!;
        output.WriteLine($"selected: '{text.SelectedText}' of '{text.Text}'");
        Assert.Equal(text.Text, text.SelectedText);
    }

    /// <summary>A second click is somebody placing a caret to edit digits — it must not re-select.</summary>
    [AvaloniaFact]
    public void ClickingAgainInsideAFocusedFieldPlacesTheCaretInstead()
    {
        var (window, field) = Shown();
        var centre = Centre(field, window);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // Past the double-click window, or the second press reads as a
        // double-click and word-selects — a different gesture than the
        // second-click-places-caret this test is about.
        Thread.Sleep(600);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = field.FindDescendantOfType<TextBox>()!;
        Assert.NotEqual(text.Text, text.SelectedText);
    }

    /// <summary>A scrub is still a scrub: dragging must not end with the text highlighted.</summary>
    [AvaloniaFact]
    public void ScrubbingDoesNotSelectTheText()
    {
        var (window, field) = Shown();
        var centre = Centre(field, window);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseMove(new Point(centre.X + 30, centre.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(centre.X + 30, centre.Y), MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = field.FindDescendantOfType<TextBox>()!;
        output.WriteLine($"value after scrub: {field.Value}, selected: '{text.SelectedText}'");
        Assert.NotEqual(42, field.Value);
        Assert.True(string.IsNullOrEmpty(text.SelectedText));
    }
}
