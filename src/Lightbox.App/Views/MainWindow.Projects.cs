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

/// <summary>The project docker, conversion, the start screen, recents and templates.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c>, which was 5,544 lines. See
/// <c>docs/DESIGN-mainviewmodel-decomposition.md</c> — the view is 79% single-section
/// state over one <c>_vm</c> field, so it needed splitting rather than decomposing.
/// </remarks>
public partial class MainWindow
{
    // ---- projects --------------------------------------------------------------

    /// <summary>Ctrl+S: save without a picker when the tab already knows where it lives.</summary>
    private async void OnSaveInPlaceClicked(object? sender, RoutedEventArgs e) =>
        await SaveOrSaveAsAsync();

    /// <summary>
    /// Save where the document already lives, or ask where to put it.
    /// </summary>
    /// <remarks>
    /// The rule the Save button has always followed, extracted so the
    /// unsaved-changes dialog gives the same answer (B75). Two places deciding
    /// separately what Save means is how they come to disagree — and the one
    /// that would have been written second is the one an artist meets while
    /// losing work.
    /// </remarks>
    private async Task SaveOrSaveAsAsync(string? suggestedName = null)
    {
        if (_vm.CanSaveInPlace)
        {
            _vm.Save();
            return;
        }
        // Nowhere to put it yet, so Save falls through to Save as… rather than
        // silently doing nothing.
        await SaveDocumentAsAsync(suggestedName);
    }

    /// <summary>
    /// Make sure the document is on disk before something outside the app is told about it.
    /// </summary>
    /// <param name="action">
    /// What was attempted, phrased to follow "before" — "exporting", "marking this Ready".
    /// </param>
    /// <param name="refuseLabel">What refusing does, named after the thing it undoes.</param>
    /// <returns>
    /// True when the document is saved and the caller may go ahead; false when the artist
    /// declined, in which case the caller must undo whatever prompted this.
    /// </returns>
    /// <remarks>
    /// Shared by the export and the status change on purpose. Both are claims about a file
    /// made to somebody else — an engine, a designer waiting on the asset — and two
    /// implementations of "is it saved?" would eventually disagree about it.
    /// </remarks>
    private async Task<bool> EnsureSavedAsync(
        string action, string refuseLabel, (string? FilePath, bool HasUnsavedEdits)? about = null)
    {
        // The row being marked when there is one, otherwise the document in front. Asking
        // about the active tab while marking a different row is the near-miss this avoids.
        var facts = about ?? (_vm.SaveTargetTab?.FilePath, _vm.SaveTargetTab?.IsDirty ?? false);
        var gate = Services.SaveRequirement.For(facts.FilePath, facts.HasUnsavedEdits);

        if (gate == Services.SaveGate.SaveInPlaceFirst)
        {
            // It already has a home, so no dialog: asking permission to write where the
            // artist already said it goes is a click in the way.
            _vm.Save();
            return true;
        }
        if (!Services.SaveRequirement.NeedsTheArtist(gate)) return true;

        var choice = await new SaveFirstDialog(
            Services.SaveRequirement.Explain(gate, action), refuseLabel)
            .ShowDialog<SaveFirstChoice?>(this);
        if (choice != SaveFirstChoice.SaveAs) return false;

        await SaveDocumentAsAsync();
        // Saved, or the picker was cancelled too — either way the answer is whether there
        // is a file now, not whether a button was pressed.
        return _vm.SaveTargetTab?.FilePath is { Length: > 0 };
    }

    /// <summary>
    /// Put the current Save / Save as gestures on their menu items.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="Services.ShortcutMap"/> rather than written in the XAML, which
    /// is the whole point of registering them: an artist who rebinds Save sees the new key
    /// on the menu instead of a label that lies. Refreshed after the Configure window
    /// closes for the same reason.
    /// </remarks>
    private void ShowSaveGestures()
    {
        SaveMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.save")?.Current;
        SaveAsMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.saveAs")?.Current;
    }

    private async void OnNewProjectClicked(object? sender, RoutedEventArgs e)
    {
        var suggested = _vm.ActiveTab?.Title is { Length: > 0 } t && !t.StartsWith("Untitled") ? t : "Project";
        if (await new NewProjectDialog(suggested).ShowDialog<NewProjectSettings?>(this) is not { } settings) return;
        await CreateProjectAsync(settings);
    }

    /// <summary>
    /// Ask where the folder goes, then make the project.
    /// </summary>
    /// <remarks>
    /// After the settings rather than before them, because the picker is the
    /// part someone might back out of and there is no sense collecting a name
    /// and a type first only to throw them away. Shared with the start screen,
    /// which collects the same settings without a dialog.
    /// </remarks>
    private async Task CreateProjectAsync(NewProjectSettings settings)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where the project folder goes",
            AllowMultiple = false,
        });
        if (folder.Count == 0 || folder[0].TryGetLocalPath() is not { } parent) return;

        var root = Path.Combine(parent, settings.Name + Lightbox.Core.Projects.ProjectIo.Extension);
        _vm.NewProject(root, settings.Name, settings.Type, settings.Workspace);
    }

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a .lbproj folder",
            AllowMultiple = false,
        });
        if (folder.Count == 0 || folder[0].TryGetLocalPath() is not { } root) return;
        _vm.OpenProject(root);
    }

    /// <summary>Double-click a project row to open that animation as a tab.</summary>
    private void OnProjectRowActivated(object? sender, Avalonia.Input.TappedEventArgs e) =>
        _vm.ProjectDocker.OpenSelected();

    // ---- converting a project ---------------------------------------------------

    private static readonly (string Label, ProjectType? Type)[] ProjectTypeChoices =
    [
        ("Unset", null),
        ("Illustration", ProjectType.Illustration),
        ("Animation", ProjectType.Animation),
        ("Game art", ProjectType.GameArt),
        ("Storyboard", ProjectType.Storyboard),
        ("Comic", ProjectType.Comic),
        ("Asset library", ProjectType.AssetLibrary),
    ];

    /// <summary>
    /// Fill the convert submenu, in code for the usual reason: a menu declared
    /// in the template lives in a popup where its bindings resolve to nothing.
    /// Rebuilt each time so the tick follows the current type.
    /// </summary>
    private void RefreshConvertMenu()
    {
        ConvertProjectMenu.Items.Clear();
        var current = _vm.ProjectDocker.Project?.Manifest.Type;
        foreach (var (label, type) in ProjectTypeChoices)
        {
            var item = new MenuItem
            {
                Header = label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = type == current,
            };
            var target = type;
            item.Click += async (_, _) => await ConvertAsync(target);
            ConvertProjectMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Convert, then report — and offer the new type's panels as a separate
    /// question.
    /// </summary>
    /// <remarks>
    /// Separate because rearranging somebody's screen as a side effect of a
    /// menu item is how a tool loses trust. The conversion has already
    /// happened and cannot fail; declining only means keeping the panels.
    /// </remarks>
    private async Task ConvertAsync(ProjectType? to)
    {
        if (_vm.ProjectDocker.Project?.Manifest.Type == to) return;
        if (_vm.ConvertProject(to) is not { } report) return;
        if (to is not null && await ConfirmWorkspaceAsync(report))
        {
            _vm.TakeProjectTypeWorkspace();
        }
    }

    private async Task<bool> ConfirmWorkspaceAsync(ProjectIo.ConversionReport report)
    {
        var take = false;
        var dialog = new Window
        {
            Title = "Project converted",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var notes = new StackPanel { Spacing = 6 };
        foreach (var note in report.Notes)
        {
            notes.Children.Add(new TextBlock
            {
                Text = "• " + note,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12,
            });
        }
        var useDefaults = new Button { Content = "Use this type's panels", MinWidth = 150 };
        var keep = new Button { Content = "Keep my panels", MinWidth = 120, IsCancel = true };
        useDefaults.Click += (_, _) => { take = true; dialog.Close(); };
        keep.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Now a {report.To} project. Nothing was rewritten.",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                notes,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { useDefaults, keep },
                },
            },
        };
        await dialog.ShowDialog(this);
        return take;
    }

    // ---- the start screen ------------------------------------------------------

    /// <summary>
    /// Ask what to open, once, on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Over the untitled document the window already opened with, not instead
    /// of it. Escape is therefore a complete answer rather than a cancelled
    /// one: the screen closes and the blank page is already there. That is
    /// what keeps "open it and draw" a keystroke instead of a setting somebody
    /// has to go and find.
    /// </para>
    /// <para>
    /// Public and returning a task so a test can drive it without waiting for
    /// a real Loaded.
    /// </para>
    /// </remarks>
    /// <summary>Say that the previous run ended unexpectedly, and where to look.</summary>
    /// <remarks>
    /// The status strip rather than a dialog: by now the artist has started
    /// working again, and a modal about something that happened before they
    /// opened the app interrupts the wrong moment. The crash-time dialog is
    /// where the interrupting is meant to happen; this is the record for when
    /// that could not be shown.
    /// </remarks>
    public void NotePreviousCrash(string logPath) =>
        _vm.AiStatus = $"Lightbox closed unexpectedly last time — details in {logPath}";

    public async Task OfferStartScreenAsync()
    {
        if (!_vm.Settings.ShowStartScreen) return;
        await AskWhatToOpenAsync();
    }

    /// <summary>Show the start screen and act on the answer, gate or no gate.</summary>
    private async Task AskWhatToOpenAsync()
    {
        var screen = new StartScreen(_vm.Settings.Recent);
        await screen.ShowDialog(this);
        await ApplyStartChoiceAsync(screen.Answer);
    }

    /// <summary>
    /// The last tab closed, so ask what to open next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing the last document used to conjure a replacement, which is the
    /// same complaint as escaping the start screen: a canvas nobody chose,
    /// arrived at by not answering. Asking is the honest version — and it is
    /// asked <em>after</em> the tab is gone, so the answer can be "nothing" and
    /// leave the application empty.
    /// </para>
    /// <para>
    /// Posted rather than shown inline. <c>CloseTab</c> is still unwinding when
    /// this fires — the tab strip and the canvas have not caught up — and a
    /// modal opened from inside that paints over a window still describing a
    /// document that no longer exists.
    /// </para>
    /// <para>
    /// It ignores <c>ShowStartScreen</c>, which gates the startup offer only.
    /// That preference means "do not interrupt me on the way in"; this is a
    /// question about something the artist just did.
    /// </para>
    /// </remarks>
    private void OnLastDocumentClosed() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            async () =>
            {
                // Something may have been opened in the gap — a recent, a
                // project row — and asking then would be asking about a
                // question that has already been answered.
                if (_vm.HasDocument) return;
                await AskWhatToOpenAsync();
            },
            Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>Act on what the start screen was answered with.</summary>
    /// <remarks>
    /// The screen used to carry a "Don't show this again" checkbox, from the
    /// days when it sat over an already-open blank document and skipping it
    /// cost nothing. The application now starts empty, so the screen *is* the
    /// question — opting out of it here would opt into staring at an empty
    /// workspace on every launch. The preference survives as
    /// <c>ShowStartScreen</c> under Edit, where turning it off is a deliberate
    /// choice rather than a checkbox ticked on the way past.
    /// </remarks>
    public async Task ApplyStartChoiceAsync(StartChoice choice)
    {
        if (choice.Document is { } document)
        {
            _vm.NewDocument(document, choice.ReuseBlank);
            return;
        }
        if (choice.Project is { } project)
        {
            await CreateProjectAsync(project);
            return;
        }
        if (choice.Open is { } path)
        {
            _vm.OpenRecent(new Services.RecentItem
            {
                Path = path,
                Name = Services.RecentItems.DisplayNameOf(path),
                Kind = choice.OpenKind,
            });
            return;
        }
        if (!choice.Browse) return;
        if (choice.OpenKind == Services.RecentKind.Project) OnOpenProjectClicked(this, new RoutedEventArgs());
        else OnOpenClicked(this, new RoutedEventArgs());
    }

    // ---- recents ---------------------------------------------------------------

    /// <summary>
    /// Fill the Open recent submenu. Built each time it opens, in code.
    /// </summary>
    /// <remarks>
    /// In code because a menu declared in the template lives in a popup, where
    /// the bindings that would reach the view model resolve to nothing — items
    /// that look right and do nothing. Each time because the list changes
    /// whenever anything is opened or saved.
    /// </remarks>
    private void RefreshRecentMenu()
    {
        RecentMenu.Items.Clear();
        var entries = _vm.RecentEntries;
        if (entries.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "Nothing yet", IsEnabled = false });
            return;
        }
        foreach (var entry in entries)
        {
            var item = new MenuItem
            {
                Header = $"{entry.Glyph}  {entry.Name}",
                // The folder, because two characters can both have a "walk".
                [ToolTip.TipProperty] = entry.Path,
            };
            var target = entry;
            item.Click += (_, _) => _vm.OpenRecent(target);
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear the list" };
        clear.Click += (_, _) => _vm.ForgetRecentsCommand.Execute(null);
        RecentMenu.Items.Add(clear);
    }

    // ---- templates (Q12) --------------------------------------------------------

    /// <summary>
    /// Build <c>New from template…</c> when it opens.
    /// </summary>
    /// <remarks>
    /// Built on open rather than bound, for the same reason as the recents list:
    /// finding the project's templates reads documents, and doing that on every
    /// property change would read the project every time anything at all
    /// happened.
    /// </remarks>
    private void RefreshTemplatesMenu()
    {
        TemplatesMenu.Items.Clear();
        var templates = _vm.TemplateChoices;
        if (templates.Count == 0)
        {
            TemplatesMenu.Items.Add(new MenuItem
            {
                Header = "No templates yet",
                IsEnabled = false,
                [ToolTip.TipProperty] =
                    "Mark any document as a template with File ▸ Use as template. "
                    + "Real templates come from work you have already done.",
            });
            return;
        }
        foreach (var reference in templates)
        {
            var item = new MenuItem { Header = reference.Name, [ToolTip.TipProperty] = reference.Path };
            var target = reference;
            item.Click += (_, _) => _vm.NewFromTemplate(target);
            TemplatesMenu.Items.Add(item);
        }
    }

    private async void OnUpdateFromTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.PreviewTemplatePull() is not { } preview)
        {
            _vm.AiStatus = "This document did not come from a template, or its template is gone.";
            return;
        }

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(preview);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } options) _vm.UpdateFromTemplate(options);
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open animation",
            AllowMultiple = false,
            FileTypeFilter = [LightboxFileType],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        _vm.OpenDocumentTab(DocJson.Deserialize(json), files[0].TryGetLocalPath());
    }

    /// <summary>Show a dialog asking how to place a multi-frame symbol.</summary>
    public async Task<PlacementChoice?> ShowPlacementChoiceDialogAsync(Symbol symbol)
    {
        var dialog = new PlacementChoiceDialog();
        return await dialog.ShowDialog<PlacementChoice?>(this);
    }
}
