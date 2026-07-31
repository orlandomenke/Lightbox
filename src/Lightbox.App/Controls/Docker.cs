using Avalonia;
using Avalonia.Controls;

namespace Lightbox.App.Controls;

/// <summary>
/// Reusable panel block in the spirit of Krita's dockers: a title strip,
/// optional top and bottom option bars, and the docker's content in between.
/// Composition is fixed by the control template so every docker in the app
/// gets the same look; each instance only supplies its bars and content.
/// </summary>
public class Docker : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Docker, string?>(nameof(Title));

    public static readonly StyledProperty<object?> TopBarProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(TopBar));

    public static readonly StyledProperty<object?> BottomBarProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(BottomBar));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Option bar shown directly under the title (null = none).</summary>
    public object? TopBar
    {
        get => GetValue(TopBarProperty);
        set => SetValue(TopBarProperty, value);
    }

    /// <summary>Option bar pinned to the docker's bottom edge (null = none).</summary>
    public object? BottomBar
    {
        get => GetValue(BottomBarProperty);
        set => SetValue(BottomBarProperty, value);
    }
}
