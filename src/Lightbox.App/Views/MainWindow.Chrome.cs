using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using static Lightbox.App.Views.PlacementChoiceDialog;

namespace Lightbox.App.Views;

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c> under Q76, which was 5,706 lines across 37
/// sections with 79% of its fields touched by exactly one of them. Every field this
/// file uses is either declared here or in the shared block at the top of
/// <c>MainWindow.axaml.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainWindow
{
    // ---- the chrome is ours -------------------------------------------------

    /// <summary>
    /// A press on the title bar's bare chrome moves the window. Presses that
    /// land on the menu, a caption button or anything else interactive stay
    /// with their control — the same walk the docker headers do.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        for (var node = e.Source as Visual; node is not null && !ReferenceEquals(node, TitleBar); node = node.GetVisualParent())
        {
            if (node is Button or Menu or MenuItem) return;
        }
        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        for (var node = e.Source as Visual; node is not null && !ReferenceEquals(node, TitleBar); node = node.GetVisualParent())
        {
            if (node is Button or Menu or MenuItem) return;
        }
        ToggleMaximised();
    }

    private void OnMinimiseClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximiseClicked(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;


    private async void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        var settings = await new NewDocumentDialog().ShowDialog<NewDocumentSettings?>(this);
        if (settings is null) return;
        _vm.NewDocument(settings);
    }

    /// <summary>What the artist chose when told the document has unsaved changes.</summary>
    private enum UnsavedChoice
    {
        /// <summary>Closed the dialog, or pressed Escape. The document stays open.</summary>
        Cancel,

        /// <summary>Close it and lose the edits.</summary>
        Discard,

        /// <summary>Write it first, then close.</summary>
        Save,
    }

    private async void OnCloseTabClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DocumentTab tab) return;
        // HasWorkToLose, not IsDirty — B99 split them exactly for this moment.
        // A brand-new document badges dirty because it differs from disk (it has
        // no disk), but there is nothing in it to lose, and "close the tab you
        // just made" should not argue. Gating on IsDirty was masked while
        // closing the last tab conjured a replacement; with the application able
        // to empty, it put the unsaved-changes dialog on an untouched blank.
        if (!tab.HasWorkToLose)
        {
            _vm.CloseTab(tab);
            return;
        }

        switch (await ConfirmDiscardAsync(tab.Title))
        {
            case UnsavedChoice.Cancel:
                return;

            case UnsavedChoice.Save:
                // Save acts on the active document, so the tab being closed has
                // to be the active one — otherwise pressing Save on tab B's
                // dialog would write tab A and close B unsaved, which is the
                // failure this whole entry is about wearing a different hat.
                _vm.ActiveTab = tab;
                await SaveOrSaveAsAsync(tab.Title);

                // Still dirty means the file picker was cancelled. Closing now
                // would discard the work the artist just asked to keep, so the
                // close is abandoned instead — Cancel on the picker cancels the
                // close, which is the only reading that does not lose anything.
                if (tab.IsDirty) return;
                break;

            case UnsavedChoice.Discard:
                break;
        }
        _vm.CloseTab(tab);
    }

    /// <remarks>
    /// <b>B75.</b> This offered Discard and Cancel only, so the artist who wanted
    /// to keep the work had to cancel, save by hand and close again — and the one
    /// who did not read carefully lost it. Save is the default button because it
    /// is the outcome that cannot destroy anything; Discard is the one that
    /// needs deliberate aim.
    /// </remarks>
    private Task<UnsavedChoice> ConfirmDiscardAsync(string title) => ConfirmDiscardAsync([title]);

    /// <inheritdoc cref="ConfirmDiscardAsync(string)"/>
    /// <remarks>
    /// <b>B80.</b> Takes a list because closing the window can have several
    /// documents in flight, and a chain of modal boxes — one per tab, each
    /// answerable differently — is a worse answer than one that says how much is
    /// at stake. The single-tab call is the same dialog with one name in it, so
    /// the two paths cannot drift into disagreeing about what Save means.
    /// </remarks>
    private async Task<UnsavedChoice> ConfirmDiscardAsync(IReadOnlyList<string> titles)
    {
        var result = UnsavedChoice.Cancel;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        // "Save all" when there is more than one, because "Save" beside a list of
        // four names does not say whether it means all of them or the first.
        var save = new Button
        {
            Content = titles.Count > 1 ? "Save all" : "Save",
            MinWidth = 80,
            IsDefault = true,
        };
        var discard = new Button { Content = "Discard changes", MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        save.Click += (_, _) => { result = UnsavedChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { result = UnsavedChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    // Named rather than counted. "3 documents have unsaved
                    // changes" is a number to weigh and the names are the thing
                    // an artist actually recognises — and the list is what makes
                    // Discard a decision rather than a gamble.
                    Text = titles.Count == 1
                        ? $"“{titles[0]}” has unsaved changes."
                        : $"{titles.Count} documents have unsaved changes:\n"
                          + string.Join("\n", titles.Select(t => $"    • {t}")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    // Discard sits furthest from Save on purpose: the destructive
                    // button should not be the one a fast hand lands on next to
                    // the safe one.
                    Children = { discard, cancel, save },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>
    /// Set once the artist has answered the unsaved-changes dialog and the close
    /// may go ahead, so the re-close does not ask again.
    /// </summary>
    private bool _closeConfirmed;

    /// <summary>
    /// Closing the application asks about unsaved work, the way closing a tab does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B80.</b> There was no handler at all — closing a *tab* asked and
    /// closing the *window* did not, so quitting with edits in flight lost them
    /// silently. Worse than B75, which at least told the artist something.
    /// </para>
    /// <para>
    /// <b>Cancel, then re-close.</b> <c>Closing</c> cannot be awaited, so the
    /// only way to ask a question during it is to refuse the close, ask, and
    /// close again — which is why <see cref="_closeConfirmed"/> exists rather
    /// than being a smell. Without it the second <c>Close()</c> re-enters here
    /// and asks the same question for ever.
    /// </para>
    /// <para>
    /// <b>A cancelled picker abandons the whole close</b>, exactly as it does for
    /// a single tab: the artist asked to keep the work, and no reading of
    /// "cancel the save" ends with the application exiting and the work gone.
    /// </para>
    /// </remarks>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;

        // B99. HasWorkToLose, not IsDirty: a never-saved document badges from
        // the moment it exists, and File ▸ New followed by a close must not
        // argue about a drawing nobody made. What this must still catch — and
        // what B80 shipped unable to catch — is a new document that *has* been
        // drawn in, which has no file at all and is the easiest work to lose.
        var dirty = _vm.Tabs.Where(t => t.HasWorkToLose).ToList();
        if (dirty.Count == 0) return;

        e.Cancel = true;
        switch (await ConfirmDiscardAsync(dirty.Select(t => t.Title).ToList()))
        {
            case UnsavedChoice.Cancel:
                return;

            case UnsavedChoice.Save:
                foreach (var tab in dirty)
                {
                    // A project save writes every dirty document at once, so by
                    // the time the loop reaches the second tab it is usually
                    // already clean. Re-saving would be harmless and asking for
                    // a filename again would not.
                    if (!tab.IsDirty) continue;
                    // Save acts on the active document, so the tab being saved
                    // has to be the active one — the same trap B75 records, and
                    // it is worse here because the loop would write one document
                    // several times and never touch the others.
                    _vm.ActiveTab = tab;
                    await SaveOrSaveAsAsync(tab.Title);
                    if (tab.IsDirty) return;
                }
                break;

            case UnsavedChoice.Discard:
                break;
        }

        _closeConfirmed = true;
        Close();
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e) => await SaveDocumentAsAsync();

    /// <param name="suggestedName">
    /// What to put in the picker's name box, when the caller already asked the
    /// artist for a name.
    /// </param>
    /// <remarks>
    /// <b>B78.</b> B66 added a name prompt before creating a character sheet and
    /// then offered this dialog for a document with no file — so the artist typed
    /// a name and was immediately asked for one again, with the first answer
    /// thrown away. The two prompts ask different questions, *what is this
    /// called* and *where does it go*, and the second should arrive already
    /// answering the first. Two correct prompts in sequence are one bad prompt.
    /// </remarks>
    /// <summary>
    /// A tab title as something a filesystem will accept.
    /// </summary>
    /// <remarks>
    /// <b>B249.</b> A reference view is titled <c>Hero / front</c> — which reads
    /// correctly on a tab and is a <em>path</em> to every operating system we
    /// ship to. Handed to the save picker it produced a name that could not be
    /// written, so the artist pressed Save, answered the dialog, and got the
    /// same dialog again on the next Ctrl+S, for ever.
    /// <para>
    /// The separator becomes a hyphen rather than being dropped, so
    /// <c>Hero / front</c> saves as <c>Hero-front</c> and still says which view
    /// it is. Everything else illegal goes the same way, and a run of hyphens
    /// collapses — a title of punctuation must not become a filename of dashes.
    /// </para>
    /// </remarks>
    internal static string FileStem(string title)
    {
        var chars = title.Trim().Select(c => Illegal(c) ? '-' : c).ToArray();
        var stem = new string(chars).Trim(' ', '-');
        while (stem.Contains("--")) stem = stem.Replace("--", "-");
        while (stem.Contains(" -") || stem.Contains("- "))
        {
            stem = stem.Replace(" -", "-").Replace("- ", "-");
        }
        return stem.Length == 0 ? "untitled" : stem;
    }

    /// <summary>
    /// Whether a character has no place in a file name on <em>any</em> platform
    /// Lightbox ships to.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>Path.GetInvalidFileNameChars()</c> alone</b>, which answers for
    /// the machine it is running on: on Linux that is a list of two, so a title
    /// carrying <c>\</c> or <c>:</c> sails through and produces a file the same
    /// project cannot open on Windows. The fixed set is Windows', which is the
    /// strictest of the three, unioned with whatever the host adds.
    /// </remarks>
    private static bool Illegal(char c) =>
        c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
        || char.IsControl(c)
        || System.IO.Path.GetInvalidFileNameChars().Contains(c);

    private async Task SaveDocumentAsAsync(string? suggestedName = null)
    {
        var stem = FileStem(
            string.IsNullOrWhiteSpace(suggestedName)
                ? _vm.SaveTargetTab?.Title ?? _vm.ActiveTab?.Title ?? "untitled"
                : suggestedName);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save animation",
            SuggestedFileName = $"{stem}.lightbox.json",
            FileTypeChoices = [LightboxFileType],
        });
        if (file is null) return;
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(_vm.SerializeDocument());
        }
        _vm.NotifySaved(file.TryGetLocalPath() ?? file.Name);
    }
}
