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
    // ---- the character library's picker (Q138) ----------------------------------

    /// <summary>
    /// The fast path: a flyout of everything the library folders offer, one
    /// click from the project browser. The window is the browsing home; both
    /// read the same <see cref="LibraryViewModel"/>, so neither can drift.
    /// </summary>
    private void OnProjectImportFromLibrary(object? sender, RoutedEventArgs e)
    {
        var vm = _vm.Characters;
        vm.ConfirmReplaceEdited ??= names => LibraryWindow.ConfirmReplaceEditedAsync(this, names);
        vm.Rescan();

        var flyout = new MenuFlyout();
        // A dozen at most: the picker is for the shelf you know, and past that
        // the window's list is the legible surface.
        const int shown = 12;
        foreach (var row in vm.Entries.Take(shown))
        {
            var item = new MenuItem { Header = $"{row.Name}  ·  {row.LibraryName}, {row.Summary}" };
            item.Click += async (_, _) => await vm.Import(row);
            flyout.Items.Add(item);
        }
        if (vm.Entries.Count > shown)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = $"… and {vm.Entries.Count - shown} more in the library window",
                IsEnabled = false,
            });
        }
        if (vm.Entries.Count == 0)
        {
            // Two different kinds of nothing, said apart: not set up, and empty.
            flyout.Items.Add(new MenuItem
            {
                Header = vm.HasRoots
                    ? "The library folders offer no characters"
                    : "No library folders set up yet",
                IsEnabled = false,
            });
        }
        flyout.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Browse library…" };
        browse.Click += (_, _) => OpenLibraryWindow();
        flyout.Items.Add(browse);
        flyout.ShowAt((Control)sender!);
    }

    /// <summary>
    /// The library's browsing home — reached from the picker's last item and
    /// from the registered <c>project.libraryWindow</c> command, one method so
    /// the two ways in cannot come to differ.
    /// </summary>
    private void OpenLibraryWindow() => new LibraryWindow(_vm.Characters).Show(this);

    // ---- re-filing a document by dragging it -----------------------------------

    private static readonly DataFormat<string> ProjectRowFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-project-row");

    private ProjectRow? _draggedRow;

    /// <summary>
    /// Start a drag from a document row. Character rows are drop targets only:
    /// dragging a character would have to mean reordering, and reordering
    /// characters is not a thing the project model has an opinion about yet.
    /// </summary>
    private async void OnProjectRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ProjectRow pressed) return;

        // B108. One rule, before any other question: a press on a row selects
        // that row — either button, every kind of row. It used to select on
        // right-click for anything and on left-click only for a document, so a
        // left press on a folder left the selection wherever it was. Both
        // toolbar surfaces read that selection as "where I am", which is how
        // 🗁 came to reveal the project folder and ＋ New to file at the root
        // while the artist was plainly looking at a folder.
        //
        // The decision lives in the docker (SelectFromPointer) rather than here,
        // because synthetic pointer input is unreliable in this environment and
        // a rule that only exists inside a pointer handler is one no test can
        // reach.
        _vm.ProjectDocker.SelectFromPointer(pressed);

        // Right-click stops here: every item in the row's menu acts on the
        // selection, which has just been set to the row under the pointer.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        // Documents and sheets drag; folders stay drop targets only.
        if (pressed is not ({ Animation: not null } or { Sheet: not null }) || pressed is not { } row) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _draggedRow = row;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ProjectRowFormat, row.Key ?? ""));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("project-row-drag", ex);
        }
        finally
        {
            _draggedRow = null;
        }
    }

    // B114. Five handlers became one: Animation, Character, Scene, Shot and
    // Document were four names for two things, and the New menu says so now.
    private async void OnProjectNewDocument(object? sender, RoutedEventArgs e) =>
        await CreateProjectItemAsync(ProjectViewModel.NewDocumentItem);

    private async void OnProjectNewFolder(object? sender, RoutedEventArgs e) =>
        await CreateProjectItemAsync(ProjectViewModel.NewFolderItem);

    /// <summary>
    /// Ask what it is called, then create it.
    /// </summary>
    /// <remarks>
    /// <b>B65.</b> Every one of these wrote to disk immediately under
    /// <c>Character 3</c> or <c>Scene 2</c>, so a project filled with numbered
    /// items and the only correction was a file manager — B64 says the docker
    /// cannot rename. The prompt comes first and cancelling creates nothing,
    /// which is the ordering B66 and B78 also landed on: a name is a question,
    /// not something to fix afterwards.
    /// </remarks>
    private Task CreateProjectItemAsync(ProjectViewModel.NewItemKind kind)
    {
        // B65. The sequence moved into the docker so the cancel path is
        // testable; what stays here is the dialog, which is all a window should
        // own. Attached once rather than per call — it is the same dialog every
        // time and re-assigning it on each click would be a subscription leak
        // waiting to be written.
        _vm.ProjectDocker.AskName ??= (k, suggested) =>
            PromptForText($"New {k.Label.ToLowerInvariant()}", "Name", suggested);
        return _vm.ProjectDocker.CreateAsync(kind);
    }

    private void OnProjectOpen(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedRowCommand.Execute(null);

    private void OnProjectOpenExternally(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedExternallyCommand.Execute(null);

    private void OnProjectReveal(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RevealSelectedCommand.Execute(null);

    private void OnProjectDuplicate(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);

    /// <summary>
    /// The docker row's road to the same history window the File menu opens —
    /// a version is worth looking at without opening the document first.
    /// Folders no-op: a folder is not a file and has no history of its own.
    /// </summary>
    private async void OnProjectRowHistory(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Project is null || _vm.ProjectDocker.Selected is not { } row) return;
        var history = row switch
        {
            { Animation: { } d } => _vm.HistoryFor(d.Id, d.Path, d.Name),
            { Sheet: { } s } => _vm.HistoryFor(s.Id, s.Path, s.Name),
            _ => null,
        };
        if (history is null) return;
        await new VersionHistoryWindow(history).ShowDialog(this);
    }

    /// <summary>
    /// Export the selected folder: count it, confirm it, then write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30's last mile.</b> <c>ExportPlan</c> could describe an artifact
    /// holding forty documents and <c>ProjectViewModel</c> could count and
    /// describe one, and no view called either — the whole of export scoping was
    /// reachable from tests and from nowhere an artist could press.
    /// </para>
    /// <para>
    /// <b>The count comes before the folder picker, and before anything is
    /// written.</b> "2 files from 47 documents, 3 held back by status" tells you
    /// whether you picked the right folder in a way <em>are you sure?</em>
    /// cannot — and the plan is computed without reading a drawing, so asking is
    /// cheap even when the answer is no.
    /// </para>
    /// <para>
    /// <b>One artifact failing does not stop the rest.</b> A plan of nine where
    /// the fourth cannot be written should produce eight files and a sentence
    /// naming the fourth, not four files and an exception.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Ask the model what the selected character is. The command holds every
    /// precondition and says which one is missing, so this is only the gesture.
    /// </summary>
    private void OnProjectReadSubject(object? sender, RoutedEventArgs e) =>
        _vm.AiReadSubjectCommand.Execute(null);

    /// <summary>Q38's free-entry half: type any character.</summary>
    /// <remarks>
    /// The prompt is supplied here and the decision lives on the view model, the
    /// same split B65 uses for the name box — a cancel path inside a window
    /// handler is a path no test can reach.
    /// </remarks>
    private async void OnProjectChooseGlyph(object? sender, RoutedEventArgs e)
    {
        _vm.ProjectDocker.AskGlyph ??= current =>
            PromptForText("Folder glyph", "Glyph", current);
        await _vm.ProjectDocker.ChooseIconAsync();
    }

    private async void OnProjectExportFolder(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.Project is null) return;

        var summary = docker.DescribeExportPlan();
        if (!await ConfirmAsync("Export", summary, "Export")) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export to folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } destination) return;

        var missing = new List<string>();
        var planned = docker.ResolveExport(destination, missing);
        if (planned.Count == 0)
        {
            _vm.AiStatus = missing.Count > 0
                ? $"Nothing exported — {string.Join(", ", missing)} could not be read."
                : "Nothing to export: every document was held back.";
            return;
        }

        var written = 0;
        var failed = new List<string>();
        foreach (var item in planned)
        {
            try
            {
                var run = await Task.Run(
                    () => Services.ExportRunner.Run(item.Documents, item.Preset, item.Path, item.Names));
                // No files means the runner refused — a target that cannot hold
                // several documents says so rather than throwing.
                if (run.Files.Count == 0)
                {
                    failed.Add($"{item.Name}: {run.Summary}");
                    continue;
                }
                written++;
                // Recorded only on success, so a failed artifact stays stale and
                // shows up in StaleExports rather than looking freshly built.
                docker.RecordExport(item.Artifact, item.Path);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Name}: {ex.Message}");
            }
        }

        var said = $"{written} file(s) written to {Path.GetFileName(destination)}";
        if (missing.Count > 0) said += $" — could not read {string.Join(", ", missing)}";
        if (failed.Count > 0) said += $" — {string.Join("; ", failed)}";
        _vm.AiStatus = said;
    }

    /// <summary>
    /// The selected drawing, exported somewhere it cannot break the build.
    /// </summary>
    /// <remarks>
    /// A test is a different destination rather than a smaller export: it writes
    /// to <c>test-exports/</c> beside the project, forces per-document grouping
    /// and drops the status filter — grouping is about the deliverable and a test
    /// is not one, and the filter exists to keep work in progress out of a
    /// shipped sheet, which is precisely what a test wants in. No confirmation,
    /// because nothing it can overwrite is a deliverable.
    /// </remarks>
    private async void OnProjectTestExport(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.PlanTestExport() is not var (reference, preset, path))
        {
            // Says so rather than doing nothing: a menu item that is sometimes
            // inert and never explains itself reads as the app being broken.
            _vm.AiStatus = "Select a drawing to test-export.";
            return;
        }
        if (docker.Project is not { } project) return;
        if (ProjectIo.LoadDocument(project, reference) is not { } doc)
        {
            _vm.AiStatus = $"“{reference.Name}” could not be read.";
            return;
        }

        try
        {
            var run = await Task.Run(() => Services.ExportRunner.Run(doc, preset, path));
            _vm.AiStatus = $"Test export: {run.Summary}";
        }
        catch (Exception ex)
        {
            _vm.AiStatus = $"Test export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Hand the row to the context menu's items, because nothing else will.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>MenuFlyout</c>'s items have no DataContext.</b> Not the window's,
    /// not the target's — measured, not assumed: the host DockPanel's is the
    /// <see cref="ProjectRow"/> and the MenuItem's is null. So every binding in
    /// that menu resolves against nothing, which is fine for a
    /// <c>Click</c> handler and fatal for an <c>ItemsSource</c>.
    /// </para>
    /// <para>
    /// That is not a hypothetical. <em>Share a palette here</em> and <em>Export
    /// this as</em> shipped with <c>$parent[Window]</c> bindings three lines
    /// below a comment warning that they do not resolve here, and both submenus
    /// have been <b>empty since the day they landed</b> — the view models were
    /// correct, the tests exercised the view models, and no artist could reach
    /// either one. Found by asserting the binding resolved rather than that the
    /// command worked.
    /// </para>
    /// <para>
    /// Set on the top-level items only. Children generated from an
    /// <c>ItemsSource</c> take the list entry as their own DataContext, which is
    /// what their <c>$parent[MenuItem]</c> bindings hop over to get back here.
    /// </para>
    /// </remarks>
    private void OnProjectRowMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout) return;
        var row = (flyout.Target as Control)?.DataContext;
        foreach (var item in flyout.Items.OfType<MenuItem>()) item.DataContext = row;
    }

    private void OnProjectRemove(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

    /// <summary>
    /// Delete for real, asking first when there is something inside.
    /// </summary>
    /// <remarks>
    /// <b>B87.</b> The docker decides <em>whether</em> to ask and what the
    /// question says; this only puts it on screen. A view model that opened its
    /// own dialogs would be one no test could drive, which is the same split
    /// B65 uses for the name prompt.
    /// </remarks>
    private async void OnProjectDeletePermanently(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.DeleteNeedsConfirmation
            && !await ConfirmAsync("Delete permanently", docker.DeleteWarning, "Delete"))
        {
            return;
        }
        docker.DeleteSelectedPermanentlyCommand.Execute(null);
    }

    /// <summary>A yes/no the artist has to mean, with the destructive verb spelled out.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var yes = false;
        var confirm = new Button { Content = confirmLabel, IsDefault = false };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
        // Cancel is the default and Delete is not, which is the opposite of the
        // save dialog B75 landed: there, Enter should reach the outcome that
        // cannot destroy anything, and here that is Cancel.
        confirm.Click += (_, _) => { yes = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return yes;
    }

    private void OnProjectRowRename(object? sender, RoutedEventArgs e)
    {
        // The root row commits now too — it renames the project, folder
        // included (owner's call, 2026-08-13, superseding the B62 refusal).
        if (_vm.ProjectDocker.Selected is not { } row || row.IsSheet) return;
        row.IsRenaming = true;
    }

    /// <summary>
    /// Double-click on the name starts the rename — the layer panel's idiom,
    /// applied to the row the pointer is on.
    /// </summary>
    /// <remarks>
    /// On the name text only, and handled so the click stops there: the row's
    /// own double-click opens the document, and one gesture must not mean both
    /// depending on nothing the artist can see.
    /// </remarks>
    private void OnProjectNameDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ViewModels.ProjectRow row }) return;
        if (row.IsSheet) return;   // sheets rename in their own panel
        // The root row renames the project — folder on disk included (owner's
        // call, 2026-08-13, superseding the B62-era refusal).
        _vm.ProjectDocker.SelectFromPointer(row);
        row.IsRenaming = true;
        e.Handled = true;
    }

    /// <summary>
    /// Set the selected document's production status — and, with auto-export on, hand it
    /// to the engine.
    /// </summary>
    /// <remarks>
    /// The status comes off the menu item's <c>Tag</c> rather than from six near-identical
    /// handlers. A tag that does not parse does nothing rather than guessing a status, so
    /// a typo in the XAML is inert instead of quietly marking things Design.
    /// </remarks>
    private async void OnProjectStatusSet(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is not { } row) return;
        if ((sender as Control)?.Tag as string is not { } tag) return;
        if (!Enum.TryParse<Lightbox.Core.Projects.AssetStatus>(tag, out var status)) return;

        // A status is a message to a designer: "this drawing is finished, go and use it".
        // It is worth nothing if the drawing was never written down, and Ready in
        // particular fires an auto-export of a file that is not there. So the status does
        // not change at all until there is one — no half state to explain afterwards.
        if (!await EnsureSavedAsync(
                $"marking this {Lightbox.Core.Projects.AssetStatuses.Label(status)}",
                "Revert status change",
                _vm.SaveFactsFor(row)))
        {
            _vm.AiStatus = "Status unchanged — the drawing has not been saved.";
            return;
        }

        _vm.SetProjectStatus(row, status);
    }

    private void OnProjectStatusClear(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is { } row) _vm.SetProjectStatus(row, null);
    }

    private void OnProjectNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ProjectRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                // B64. The box stays open when the rename was refused, so the
                // artist can fix the name rather than retype it from scratch —
                // and the docker's status line says which of the several
                // reasons it was.
                if (_vm.ProjectDocker.Rename(row, box.Text ?? ""))
                {
                    row.IsRenaming = false;
                    RememberRenamedProject(row);
                }
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = row.Name; // revert, so the LostFocus commit is a no-op
                row.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }

    private void OnProjectNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ProjectRow row) return;
        // Losing focus commits what it can and always closes the box: leaving
        // an edit open on a row nobody is looking at is how a rename gets
        // applied to whatever is clicked next.
        if (row.IsRenaming)
        {
            if (_vm.ProjectDocker.Rename(row, box.Text ?? "")) RememberRenamedProject(row);
            else box.Text = row.Name;
        }
        row.IsRenaming = false;
    }

    /// <summary>
    /// A renamed project's recents entry points at a folder that no longer
    /// exists; re-remembering the new root keeps File ▸ Recent honest.
    /// </summary>
    private void RememberRenamedProject(ViewModels.ProjectRow row)
    {
        if (row.IsRoot && _vm.ProjectDocker.Project?.Root is { Length: > 0 } root)
        {
            _vm.Remember(root, RecentKind.Project);
        }
    }

    /// <summary>
    /// Copy the selected row's path. The view model records it too, so the
    /// behaviour is testable without a clipboard, but the clipboard is the
    /// point and only the window has one.
    /// </summary>
    private async void OnProjectCopyPath(object? sender, RoutedEventArgs e)
    {
        _vm.ProjectDocker.CopySelectedPathCommand.Execute(null);
        if (Clipboard is { } clipboard && _vm.ProjectDocker.CopiedPath is { Length: > 0 } path)
        {
            try
            {
                using var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.Create(DataFormat.Text, path));
                await clipboard.SetDataAsync(transfer);
            }
            catch (Exception ex)
            {
                Rendering.CanvasControl.LogDiag("project-copy-path", ex);
            }
        }
    }

    private void OnProjectRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DropTargetFor(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnProjectRowDrop(object? sender, DragEventArgs e)
    {
        if (_draggedRow is not { } row) return;
        if (DropTargetFor(e) is not { } target) return;
        e.Handled = true;
        _vm.ProjectDocker.Move(row, target.Folder);
    }

    /// <summary>
    /// Where a drop would land: the folder under the pointer, or the project
    /// itself when the pointer is over a loose document or past the end of the
    /// list. Null when the drop would change nothing.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Two axes collapsed into one. This used to read
    /// <c>over?.Character</c> and compare characters and folders separately,
    /// with a comment (B94) about a third axis slipping past — there is one axis
    /// now, so there is nothing to keep in step.
    /// <para>
    /// Dropping into the empty space below the tree means the project, and so
    /// does dropping onto the project row: neither has a folder.
    /// </para>
    /// </remarks>
    private (ProjectFolder? Folder, bool Valid)? DropTargetFor(DragEventArgs e)
    {
        if (_draggedRow is null) return null;
        var over = (e.Source as Control)?.DataContext as ProjectRow;
        // A document row means the folder it is in; a folder row means itself.
        return (over?.Folder, true);
    }

    private async void OnExportDocumentClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export a standalone document",
            SuggestedFileName = $"{_vm.ActiveTab?.Title ?? "untitled"}.lightbox.json",
            FileTypeChoices = [LightboxFileType],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        // Flattened: every shared palette and gradient it uses travels with it,
        // so the file re-renders with the project gone.
        await writer.WriteAsync(_vm.ExportStandaloneDocument());
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export PNG sequence to folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir is null) return;
        var clip = _vm.ResolvedAudioPathForExport() is not null ? _vm.AudioClipNow : null;
        var written = await Task.Run(() =>
        {
            var files = Services.SequenceExporter.ExportPngSequence(_vm.Doc, dir);
            // The scratch track rides along as plain PCM, the one encoding
            // every comp package reads (Q56).
            if (clip is not null)
            {
                Services.VideoExporter.WriteWavPcm16(clip, Path.Combine(dir, "audio.wav"));
            }
            return files;
        });
        _vm.AiStatus = clip is null
            ? $"Exported {written.Count} PNG frame(s)."
            : $"Exported {written.Count} PNG frame(s) and audio.wav.";
    }

    /// <summary>
    /// <c>File ▸ Export video…</c> — the settings window owns the whole
    /// render (B146). It used to be a save picker whose every answer, the
    /// missing encoder included, went to the AI bar; that row is hidden
    /// whenever assistance is off, so the export looked like it did nothing.
    /// </summary>
    private async void OnExportVideoClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new VideoExportWindow(
            _vm.Doc,
            () => _vm.ResolvedAudioPathForExport(),
            _vm.ActiveTab?.Title ?? "animation");
        await dialog.ShowDialog(this);
        // Echoed into the status strip as well, for the artist who has closed
        // the window and wants to know what happened.
        if (dialog.Reported is { Length: > 0 } said) _vm.AiStatus = said;
    }

    /// <summary>
    /// <c>File ▸ Export for a game engine…</c> — the entry point Pillar 5 did not have.
    /// </summary>
    /// <remarks>
    /// Two steps on purpose: the settings, then the path. Asking for a filename first
    /// and the format afterwards is how somebody ends up with a <c>.png</c> holding a
    /// PNG sequence's folder name.
    /// </remarks>
    private async void OnExportSheetClicked(object? sender, RoutedEventArgs e)
    {
        // Asked before the settings dialog rather than after: finding out that the drawing
        // was never saved is worse after picking a preset and a filename than before.
        if (!await EnsureSavedAsync("exporting", "Don't export")) return;

        var dialog = new ExportWindow();
        await dialog.ShowDialog(this);
        if (dialog.Chosen is not { } preset) return;

        string path;
        if (preset.Target == Lightbox.Core.Projects.ExportTarget.PngSequence)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export PNG sequence to folder",
                AllowMultiple = false,
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;
            path = dir;
        }
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export sprite sheet",
                SuggestedFileName = (_vm.ActiveTab?.Title ?? "sheet") + ".png",
                DefaultExtension = "png",
            });
            if (file?.TryGetLocalPath() is not { } sheet) return;
            path = sheet;
        }

        try
        {
            var run = await Task.Run(() => Services.ExportRunner.Run(_vm.Doc, preset, path));
            _vm.AiStatus = _vm.DescribeExport(run);
        }
        catch (Exception ex)
        {
            // A failed export must say so in the status line rather than taking the
            // window down. An unwritable path is the ordinary case, not a bug.
            _vm.AiStatus = $"Export failed: {ex.Message}";
        }
    }
}
