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

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<Docker, System.Windows.Input.ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> TitleBarExtraProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(TitleBarExtra));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Shown as a ✕ button at the right of the title strip. What "close" means
    /// is the host's choice — a bottom docker collapses down, a side docker
    /// collapses to its side. Null hides the button.
    /// </summary>
    public System.Windows.Input.ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>Extra title-bar controls, placed just before the close button.</summary>
    public object? TitleBarExtra
    {
        get => GetValue(TitleBarExtraProperty);
        set => SetValue(TitleBarExtraProperty, value);
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
