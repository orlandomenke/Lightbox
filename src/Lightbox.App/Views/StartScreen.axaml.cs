using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>What the start screen was answered with.</summary>
/// <remarks>
/// Every field null means "nothing" — Escape, or the close button. That is a
/// complete answer, not a cancelled one: the blank document the screen opened
/// over is what the artist gets.
/// </remarks>
public sealed record StartChoice
{
    public NewDocumentSettings? Document { get; init; }

    public NewProjectSettings? Project { get; init; }

    /// <summary>A path from the recents list, or one chosen with a picker.</summary>
    public string? Open { get; init; }

    public RecentKind OpenKind { get; init; }

    /// <summary>True when the artist asked to be shown a picker rather than a path.</summary>
    public bool Browse { get; init; }

    /// <summary>Whether to stop offering this screen.</summary>
    public bool DontShowAgain { get; init; }

    public static StartChoice Nothing => new();
}

/// <summary>
/// The screen the application opens on: what to make, or what to go back to.
/// </summary>
/// <remarks>
/// <para>
/// Shown <i>over</i> an already-open untitled document rather than instead of
/// one. That is what makes Escape a complete answer — it closes the screen and
/// leaves a blank page, so the fast path stays a keystroke rather than a
/// preference somebody has to go and find.
/// </para>
/// <para>
/// The fields are the real ones: <see cref="NewDocumentPanel"/> and
/// <see cref="NewProjectPanel"/> are the same controls File → New uses, so
/// there is no second set of presets to keep in step.
/// </para>
/// </remarks>
public partial class StartScreen : Window
{
    private bool _closing;

    public StartScreen() : this(new RecentItems())
    {
    }

    public StartScreen(RecentItems recent)
    {
        InitializeComponent();
        var existing = recent.Existing();
        RecentList.ItemsSource = existing;
        NoRecents.IsVisible = existing.Count == 0;
        RecentList.IsVisible = existing.Count > 0;
        // Closing by the title bar is Escape by another route, and must not
        // leave the caller waiting for an answer that never comes.
        Closing += (_, _) =>
        {
            if (!_closing) Answer = StartChoice.Nothing with { DontShowAgain = DontShowBox.IsChecked == true };
        };
    }

    /// <summary>
    /// What was chosen. Set before the window closes so the caller can read it
    /// after <c>ShowDialog</c> regardless of which route closed it.
    /// </summary>
    public StartChoice Answer { get; private set; } = StartChoice.Nothing;

    private bool DontShow => DontShowBox.IsChecked == true;

    private void Finish(StartChoice choice)
    {
        _closing = true;
        Answer = choice with { DontShowAgain = DontShow };
        Close();
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        // A selected recent beats the form: clicking a file and then Create is
        // the same sentence as double-clicking it, and reading it as "make a
        // new document" would throw away what the artist actually pointed at.
        if (RecentList.SelectedItem is RecentItem selected)
        {
            Finish(new StartChoice { Open = selected.Path, OpenKind = selected.Kind });
            return;
        }
        Finish(Tabs.SelectedIndex == 1
            ? new StartChoice { Project = ProjectFields.Collect() }
            : new StartChoice { Document = DocumentFields.Collect() });
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => Finish(StartChoice.Nothing);

    private void OnOpenFile(object? sender, RoutedEventArgs e) =>
        Finish(new StartChoice { Browse = true, OpenKind = RecentKind.Document });

    private void OnOpenProject(object? sender, RoutedEventArgs e) =>
        Finish(new StartChoice { Browse = true, OpenKind = RecentKind.Project });

    private void OnRecentActivated(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentItem item) return;
        Finish(new StartChoice { Open = item.Path, OpenKind = item.Kind });
    }
}
