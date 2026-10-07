using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lightbox.Gallery.Stories;

/// <summary>One named state of a component: a label and a way to build it fresh.</summary>
/// <remarks>
/// A builder rather than a control, because a look is applied by reloading the
/// styles and the app names its colours with StaticResource: a control built
/// before the reload keeps the old look's colours.
/// </remarks>
public sealed record StoryState(string Name, Func<Control> Build);

/// <summary>
/// A component in its named states. The catalogue in <see cref="Catalog"/> is a
/// registry on purpose: the window lists it, the snapshot run renders it, and a
/// story that is not in it exists nowhere.
/// </summary>
public sealed record Story(string Name, string Note, IReadOnlyList<StoryState> States, int Columns = 4);

/// <summary>Small helpers every story uses, so stories read as the component and its states.</summary>
public static class Kit
{
    /// <summary>
    /// Show a control as hovered, pressed, focused and so on without a pointer.
    /// Pseudo-classes are what the styles select on, so setting one is exactly
    /// what the styles see when the real pointer arrives.
    /// </summary>
    public static T Force<T>(T control, params string[] pseudoClasses) where T : Control
    {
        foreach (var p in pseudoClasses) ((IPseudoClasses)control.Classes).Add(p);
        return control;
    }

    public static T With<T>(T control, params string[] classes) where T : Control
    {
        foreach (var c in classes) control.Classes.Add(c);
        return control;
    }

    /// <summary>An icon as the app draws one: a Path.icon on a named geometry.</summary>
    public static Path Icon(string key, double size = 12) => new()
    {
        Classes = { "icon" },
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Data = (Geometry)Application.Current!.FindResource(key)!,
    };

    /// <summary>Run an action on a value and hand the value on — for one-line story builders.</summary>
    public static void Let<T>(this T value, Action<T> action) => action(value);

    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(children);
        return row;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var col = new StackPanel { Spacing = spacing };
        col.Children.AddRange(children);
        return col;
    }
}
