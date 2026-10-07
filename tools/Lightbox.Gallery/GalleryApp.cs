using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Lightbox.Gallery;

/// <summary>
/// The gallery's application: the app's own resources and styles, under a look.
/// </summary>
/// <remarks>
/// Not a subclass of Lightbox's <c>App</c>, which is sealed and starts the
/// document window; it loads the same two files instead, which is why those
/// files exist (Styles/AppResources.axaml, Styles/AppStyles.axaml).
/// </remarks>
public sealed class GalleryApp : Application
{
    /// <summary>The look the gallery opens on: the recommendation in Q203.</summary>
    public static Look Opening { get; set; } = new(LookTheme.DarkLit, LookCorners.Tight, Effects: true);

    public Look Look { get; private set; } = Opening;

    public override void Initialize() => Looks.Apply(this, Look);

    public void Use(Look look)
    {
        Look = look;
        Looks.Apply(this, look);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new GalleryWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
