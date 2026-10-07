using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Lightbox.Gallery;

/// <summary>The palette a look draws with. <see cref="Current"/> is the app as it ships.</summary>
public enum LookTheme { Current, DarkLit, StudioGrey }

/// <summary>The corner set. <see cref="Current"/> leaves the app's own radii alone.</summary>
public enum LookCorners { Current, Square, Tight, Soft }

/// <summary>
/// One candidate look: a palette, a corner set and whether the light effects
/// (shadows, glows) are on. The three are independent on purpose (Q203): the
/// owner asked to try corners and themes separately rather than as bundles.
/// </summary>
public sealed record Look(LookTheme Theme, LookCorners Corners, bool Effects)
{
    public static readonly Look AsShipped = new(LookTheme.Current, LookCorners.Current, false);

    /// <summary>A file-name-safe name, used for snapshot paths.</summary>
    public string Slug => $"{Theme}-{Corners}-{(Effects ? "fx" : "flat")}".ToLowerInvariant();

    public override string ToString() => Slug;
}

/// <summary>
/// Puts a look onto the application: the app's own resources and styles, then
/// the look's overrides on top.
/// </summary>
/// <remarks>
/// <para>
/// <b>By reloading, not by swapping brushes.</b> The app's styles name its
/// colours with <c>StaticResource</c> (176 places), which resolves once, at load.
/// A live swap would need every one of them turned into a
/// <c>DynamicResource</c> first; a reload gets the same picture by building the
/// styles again against new resources, and leaves the app untouched until a
/// look is chosen.
/// </para>
/// <para>
/// Every include is a <em>new</em> instance each time. An include loads its
/// file once and keeps the result, so reusing one would keep the old look's
/// resolved colours.
/// </para>
/// </remarks>
public static class Looks
{
    private static readonly Uri Base = new("avares://Lightbox.Gallery/");

    /// <summary>
    /// The gallery's own <c>Looks</c> folder in the source tree, when it can be
    /// found. Set, the look files are read from disk instead of from the built
    /// assembly, so an edit shows the moment it is saved. The snapshot run
    /// leaves it null and renders what was built.
    /// </summary>
    public static string? SourceDir { get; set; }

    /// <summary>What failed to load in the last <see cref="Apply"/>, for the window to show.</summary>
    public static IReadOnlyList<string> Errors => _errors;

    private static readonly List<string> _errors = [];

    /// <summary>Walk up from the executable to the repository's Looks folder.</summary>
    public static string? FindSourceDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var looks = System.IO.Path.Combine(dir.FullName, "tools", "Lightbox.Gallery", "Looks");
            if (Directory.Exists(looks)) return looks;
        }
        return null;
    }

    public static void Apply(Application app, Look look)
    {
        _errors.Clear();
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceInclude(Base) { Source = new Uri("avares://Lightbox.App/Styles/AppResources.axaml") });
        if (look.Theme != LookTheme.Current)
        {
            if (Resource($"avares://Lightbox.Gallery/Looks/{look.Theme}.axaml") is { } theme)
            {
                resources.MergedDictionaries.Add(theme);
            }
        }
        if (Resource("avares://Lightbox.Gallery/Looks/Tokens.axaml") is { } tokens)
        {
            resources.MergedDictionaries.Add(tokens);
        }
        AddShape(resources, look);
        app.Resources = resources;
        // Studio grey is a light world: Fluent's stock controls draw dark text
        // only under its Light variant.
        app.RequestedThemeVariant = look.Theme == LookTheme.StudioGrey ? ThemeVariant.Light : ThemeVariant.Dark;

        app.Styles.Clear();
        var appStyles = Style("avares://Lightbox.App/Styles/AppStyles.axaml");
        app.Styles.Add(appStyles);
        if (look.Theme != LookTheme.Current) RepointFluent(appStyles, look.Theme);
        AddStyle(app, "Gallery.Styles.axaml");
        AddStyle(app, "Controls.axaml");
        AddStyle(app, "Borders.axaml");
        // The edge light follows the theme (owner, 2026-10-07): chrome in the
        // dark, prism in the light. Picked here, once, so every container names
        // one key and never asks which theme it is in.
        if (app.TryFindResource(look.Theme == LookTheme.StudioGrey ? "LookPrismBrush" : "LookChromeBrush", out var edge)
            && edge is ConicGradientBrush conic)
        {
            resources["LookEdgeBrush"] = conic;
            // The same light, faint, for buttons and toggles (owner,
            // 2026-10-07): the docker frames lost theirs; the controls gained a
            // subtle one. One brush, so a control never asks which theme it is in.
            var subtle = new ConicGradientBrush { Center = conic.Center, Angle = conic.Angle, Opacity = 0.42 };
            foreach (var stop in conic.GradientStops) subtle.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
            resources["LookEdgeSubtleBrush"] = subtle;
        }
        if (look.Theme != LookTheme.Current) AddStyle(app, "Lit.Styles.axaml");
        if (look.Corners != LookCorners.Current) AddStyle(app, "Corners.Styles.axaml");
    }

    /// <summary>
    /// Corner radii and shadows, as resources the look's styles name.
    /// </summary>
    /// <remarks>
    /// In code rather than XAML because a <see cref="BoxShadows"/> is not
    /// something a resource dictionary can spell, and the corner sets are three
    /// numbers each. "Current" corners are the app's own 3 · 6, so a lit theme
    /// can be judged with today's shapes.
    /// </remarks>
    private static void AddShape(ResourceDictionary resources, Look look)
    {
        var (control, container, pill) = look.Corners switch
        {
            LookCorners.Square => (0.0, 0.0, 0.0),
            LookCorners.Tight => (4.0, 6.0, 999.0),
            LookCorners.Soft => (8.0, 14.0, 999.0),
            _ => (3.0, 6.0, 3.0),
        };
        // Five roles, and every rounded thing in a look names one of them
        // (Corners.Styles.axaml is the list of who gets which):
        //   container          docker, canvas bar, menus, popups, tooltips
        //   control            button, field, combo, selector well, slider track, rows, tabs
        //   control inner      inside a control's 2 px padding: selected segment, checkbox
        //   container inner    inside a container's 2 px padding: canvas-bar icon, menu item
        //   pill               switches, badges, thumbs — round, or square in Square
        // An inner radius is its parent's minus the padding, so nested shapes
        // are concentric instead of two unrelated roundings.
        resources["LookControlRadius"] = new CornerRadius(control);
        resources["LookContainerRadius"] = new CornerRadius(container);
        resources["LookPillRadius"] = new CornerRadius(pill);
        resources["LookControlInnerRadius"] = new CornerRadius(Math.Max(0, control - 2));
        resources["LookContainerInnerRadius"] = new CornerRadius(Math.Max(0, container - 2));
        resources["LookTabRadius"] = new CornerRadius(control, control, 0, 0);
        if (look.Theme == LookTheme.Current)
        {
            // The prototypes name the active fill; the shipped palette has none.
            resources["LookBarActiveBrush"] = new SolidColorBrush(Colors.White, 0.13);
        }
        if (look.Corners != LookCorners.Current)
        {
            // Fluent's stock templates read these two, so controls nobody styled
            // by hand — checkboxes, combo drop-downs, list items — follow too.
            resources["ControlCornerRadius"] = new CornerRadius(control);
            resources["OverlayCornerRadius"] = new CornerRadius(container);
        }

        // The rim is an inset shadow one pixel tall, not a top border: Avalonia
        // draws no box shadow on a border whose thickness is not uniform, so a
        // 0,1,0,0 rim silently switched every drop shadow off. Found by
        // measuring the Light story. Rim and drop now travel together.
        var grey = look.Theme == LookTheme.StudioGrey;
        var rim = grey ? "#8CFFFFFF" : "#14FFFFFF";
        var rimStrong = grey ? "#D9FFFFFF" : "#26FFFFFF";
        var shade = grey ? "#4D282826" : "#A6000000";
        var near = grey ? "#38282826" : "#80000000";
        resources["LookIslandShadow"] = BoxShadows.Parse(
            $"inset 0 1 0 0 {rim}" + (look.Effects ? $", 0 10 28 -12 {shade}, 0 2 6 -2 {near}" : ""));
        resources["LookRaisedShadow"] = BoxShadows.Parse(
            $"inset 0 1 0 0 {rimStrong}" + (look.Effects ? $", 0 2 5 -2 {near}" : ""));
        resources["LookInsetShadow"] = BoxShadows.Parse(
            grey ? "inset 0 1 2 0 #26000000" : "inset 0 1 2 0 #59000000");
    }

    /// <summary>
    /// The lit looks have no colour for "on" — the chrome is grey (Q203) — so
    /// Fluent's accent, which every stock toggle, check and slider derives from,
    /// becomes the raised neutral too.
    /// </summary>
    private static void RepointFluent(StyleInclude appStyles, LookTheme theme)
    {
        if (appStyles.Loaded is not Styles loaded) return;
        foreach (var fluent in loaded.OfType<FluentTheme>())
        {
            fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
            {
                Accent = Color.Parse("#4A4A50"),
                RegionColor = Color.Parse("#222225"),
            };
            fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
            {
                Accent = Color.Parse("#2A2A29"),
                RegionColor = Color.Parse("#B8B8B6"),
            };
        }
    }

    /// <summary>Every combination, in the order the snapshots are written.</summary>
    public static IEnumerable<Look> All()
    {
        yield return Look.AsShipped;
        foreach (var theme in new[] { LookTheme.DarkLit, LookTheme.StudioGrey })
        foreach (var corners in new[] { LookCorners.Square, LookCorners.Tight, LookCorners.Soft })
        foreach (var fx in new[] { true, false })
        {
            yield return new Look(theme, corners, fx);
        }
    }

    /// <summary>
    /// A look's resources: from disk while live, from the assembly otherwise.
    /// A file that will not parse is reported and left out, so a typo mid-edit
    /// shows as a message rather than closing the gallery.
    /// </summary>
    private static IResourceProvider? Resource(string uri)
    {
        var name = uri[(uri.LastIndexOf('/') + 1)..];
        if (SourceDir is null) return new ResourceInclude(Base) { Source = new Uri(uri) };
        try
        {
            return AvaloniaRuntimeXamlLoader.Parse<ResourceDictionary>(
                File.ReadAllText(System.IO.Path.Combine(SourceDir, name)), typeof(Looks).Assembly);
        }
        catch (Exception e)
        {
            _errors.Add($"{name}: {e.Message}");
            return null;
        }
    }

    private static void AddStyle(Application app, string name)
    {
        if (SourceDir is null)
        {
            app.Styles.Add(Style($"avares://Lightbox.Gallery/Looks/{name}"));
            return;
        }
        try
        {
            app.Styles.Add(AvaloniaRuntimeXamlLoader.Parse<Styles>(
                File.ReadAllText(System.IO.Path.Combine(SourceDir, name)), typeof(Looks).Assembly));
        }
        catch (Exception e)
        {
            _errors.Add($"{name}: {e.Message}");
        }
    }

    private static StyleInclude Style(string uri) => new(Base) { Source = new Uri(uri) };
}
