using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- document tabs --------------------------------------------------------

    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    [ObservableProperty]
    private DocumentTab? _activeTab;


    partial void OnActiveTabChanged(DocumentTab? value)
    {
        // Before the null return: a last tab closing must clear the docker's
        // "editing this" mark, not leave it on a row nobody is editing.
        ProjectDocker.MarkEditing(value?.Source?.Id ?? value?.Owner?.Source?.Id);
        if (value is null) return;
        foreach (var tab in Tabs) tab.IsActive = tab == value;
        OnPropertyChanged(nameof(ShowTimeline));
        OnPropertyChanged(nameof(ReferenceSheetsView));
        // The template state is per document, so switching tabs changes all
        // three. Without this the File menu kept the previous tab's answer:
        // "Use as template" ticked on a document that is not one, and Update
        // from template greyed out on a copy that could be updated.
        OnPropertyChanged(nameof(IsActiveDocumentTemplate));
        OnPropertyChanged(nameof(TemplateLabel));
        OnPropertyChanged(nameof(CanUpdateFromTemplate));
        // Whether the File menu can offer a version follows the tab, the same
        // way the template entries just above do.
        OnPropertyChanged(nameof(CanSaveVersion));
        if (value.Editor == _editor) return;

        _switchingTabs = true;
        var leaving = Tabs.FirstOrDefault(t => t.Editor == _editor);
        if (leaving is not null)
        {
            leaving.State.FrameIndex = CurrentFrameIndex;
            leaving.State.LayerIndex = ActiveLayerIndex;
            leaving.State.ReferenceIndex = ActiveReferenceIndex;
            // B171. Handed over rather than copied: AttachEditor is about to
            // drop the view model's reference, so the tab becomes the only
            // owner and there is nothing left to alias.
            leaving.State.Selection = HasSelection ? _selectionContours : null;
        }
        AttachEditor(value.Editor);
        // B56, and note that the line below it already had the guard: a document with no layers
        // is loadable, `Clamp(0, 0, -1)` throws, and the frame clamp beside this one was written
        // defensively while the layer clamp was not.
        ActiveLayerIndex = Math.Clamp(value.State.LayerIndex, 0, Math.Max(0, Scene.Layers.Count - 1));
        // Negative-proofed rather than capped at FrameCount: past the end is a
        // place the playhead is allowed to stand (PlayheadPastTheEnd), and the
        // old cap silently snapped a parked tab — or a Q111 restore — onto its
        // last drawing. B56's throw-guard survives in the Max.
        CurrentFrameIndex = Math.Max(0, value.State.FrameIndex);
        // B67. Not clamped, because the index is already bounds-checked where it
        // is read and an out-of-range value means "this document has fewer
        // strips than that one did" rather than an error to repair.
        ActiveReferenceIndex = value.State.ReferenceIndex;
        // B171. After AttachEditor cleared it, so this is a restore rather than
        // a survival: a tab with no remembered selection arrives with none.
        if (value.State.Selection is { Count: > 0 } remembered)
        {
            _selectionContours = remembered;
            NotifySelection();
        }
        RecallDocumentBrush();
        _switchingTabs = false;
        // After the switch, so a handler asking the view model anything sees the
        // arriving document rather than a half-swapped one. The canvas framing
        // rides on this: it is view-only state (invariant 5) and belongs to the
        // window, which is the only thing that owns a CanvasControl.
        TabSwitched?.Invoke(leaving, value);
    }

    /// <summary>
    /// A different document became active: <c>(leaving, arriving)</c>.
    /// </summary>
    /// <remarks>
    /// <b>B67.</b> Exists because the canvas framing is per document and the
    /// view model must not know what a canvas is. Both tabs are handed over
    /// because a subscriber has to put something down before it picks the next
    /// one up, and by the time <c>PropertyChanged</c> fires for
    /// <see cref="ActiveTab"/> the tab being left has already been forgotten.
    /// </remarks>
    public event Action<DocumentTab?, DocumentTab>? TabSwitched;

    // ---- project commands ---------------------------------------------------

    /// <summary>
    /// Start a project at <paramref name="root"/>, adopting the document that
    /// is already open as its first animation.
    ///
    /// Adopting rather than starting empty is the point: the artist has been
    /// drawing, and the container should form around that work instead of
    /// asking them to recreate it somewhere else.
    /// </summary>
    public void NewProject(
        string root, string name,
        ProjectType? type = null,
        WorkspaceChoice workspace = WorkspaceChoice.Keep)
    {
        var project = ProjectIo.Create(name, root);
        project.Manifest.Type = type;

        if (SaveTargetTab is { } tab)
        {
            // B83/B84. A project-level document, not an animation of an invented
            // character. Creating one named after the project put the artist's
            // first drawing at `characters/<project>/animations/…` and left a
            // folder called "project" inside "characters" — which is what B84
            // reports, and the two unrequested folders B83 counts.
            var reference = ProjectIo.AddDocument(project, tab.Title, tab.Doc);
            tab.Source = reference;
            // The document's palettes and gradients become the project's:
            // shared is the whole reason the container exists.
            project.Palettes.AddRange(tab.Doc.Palettes);
            foreach (var (id, gradient) in tab.Doc.Gradients) project.Gradients[id] = gradient;
        }

        ProjectDocker.Adopt(project);
        SaveProject(everything: true);
        Remember(root, RecentKind.Project);
        if (workspace == WorkspaceChoice.ProjectDefaults) Workspace.UseDefaultFor(type);
        AiStatus = $"Created project “{name}”.";
    }

    public void OpenProject(string root)
    {
        try
        {
            var project = ProjectIo.Load(root);
            ProjectDocker.Adopt(project);
            // Open the first animation so the project is not an empty shell —
            // and so the registries have something to resolve against.
            if (project.Manifest.Documents.FirstOrDefault() is { } first
                && ProjectIo.LoadDocument(project, first) is { } doc)
            {
                OpenProjectDocument(first, doc);
            }
            OnProjectChanged();
            Remember(root, RecentKind.Project);
            AiStatus = $"Opened project “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            AiStatus = $"Could not open that project: {ex.Message}";
        }
    }

    /// <summary>
    /// Write the project, and only the animations that changed.
    /// </summary>
    /// <param name="everything">
    /// Write every loaded document regardless. True for the first save of a
    /// new project, where nothing has been "changed" since it was created but
    /// none of it is on disk yet.
    /// </param>
    public void SaveProject(bool everything = false)
    {
        if (ProjectDocker.Project is not { } project) return;
        try
        {
            // Same guard as Save(): no document file is written while an
            // in-place autosave write might still be heading for it.
            _autosave.FinishPendingWrite();
            ProjectIo.Save(project, everything ? null : ProjectDocker.Dirty);
            ProjectDocker.MarkAllSaved();
            foreach (var tab in Tabs)
            {
                // B99. A tab with no Source is not in the project, so a project
                // save does not write it and must not claim to have. It keeps
                // its badge, which is now the truth rather than a stale flag.
                if (tab.Source is not null) tab.MarkSaved();
            }
            AiStatus = $"Saved “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save the project: {ex.Message}";
        }
    }

    // ---- templates (Q12) --------------------------------------------------------

    /// <summary>
    /// Whether the active document is marked as a template.
    /// </summary>
    /// <remarks>
    /// A template is an ordinary document with a flag, so this is genuinely all
    /// that "make one" does: it does not move, it does not change, it gains a
    /// flag and starts appearing in one more list. Setting it marks the document
    /// edited, because it is a change to the document and has to be saved like
    /// one.
    /// </remarks>
    public bool IsActiveDocumentTemplate
    {
        get => SaveTargetTab?.Doc.IsTemplateDocument ?? false;
        set
        {
            if (SaveTargetTab is not { } tab || tab.Doc.IsTemplateDocument == value) return;
            // B98. Through the editor, not around it. Dirtiness is now derived
            // from the edit record, so a mutation that bypasses it changes the
            // document without the badge noticing — the opposite failure to the
            // one B98 fixes, and the more dangerous of the two.
            tab.Editor.Perform(doc => Core.Projects.Templates.SetTemplate(doc, value));
            MarkDocumentEdited();
            OnPropertyChanged(nameof(IsActiveDocumentTemplate));
            OnPropertyChanged(nameof(TemplateLabel));
            OnPropertyChanged(nameof(CanUpdateFromTemplate));
            AiStatus = value
                ? "Marked as a template. New from template… will offer it."
                : "No longer a template. The document is otherwise unchanged.";
        }
    }

    public string TemplateLabel =>
        IsActiveDocumentTemplate ? "This document is a template" : "Use as template";

    /// <summary>
    /// The project's templates, for the New from template… list.
    /// </summary>
    /// <remarks>
    /// Empty without a project, and that is the whole reason the feature is
    /// project-scoped: a standalone template is a file you Open and then Save as,
    /// which has always worked. What a project adds is being able to <em>list</em>
    /// them.
    /// </remarks>
    public IReadOnlyList<DocumentRef> TemplateChoices =>
        ProjectDocker.Project is { } project ? Core.Projects.Templates.InProject(project) : [];

    /// <summary>Start a new document from a template — a copy, with no live link.</summary>
    public void NewFromTemplate(DocumentRef reference)
    {
        if (ProjectDocker.Project is not { } project) return;
        if (ProjectIo.LoadDocument(project, reference) is not { } template) return;

        var copy = Core.Projects.Templates.NewFromTemplate(template, reference.Id);
        var name = $"{reference.Name} copy";
        var added = ProjectIo.AddDocument(project, name, copy, ProjectDocker.TargetFolder);

        ProjectDocker.Adopt(project);
        ProjectDocker.MarkDirty(added);
        OpenProjectDocument(added, copy);
        AiStatus = $"New from \"{reference.Name}\". It is a copy — editing the template later leaves it alone.";
    }

    /// <summary>
    /// Whether this document can be asked to pull from the template it came from.
    /// </summary>
    /// <remarks>
    /// Needs a project, a recorded template id, and that template still to exist.
    /// A document whose template has been deleted simply cannot be asked, which
    /// is the whole point of the link pointing document → template: nothing
    /// breaks, the option just is not there.
    /// </remarks>
    public bool CanUpdateFromTemplate => TemplateOfActiveDocument() is not null;

    private Doc? TemplateOfActiveDocument()
    {
        if (ProjectDocker.Project is not { } project) return null;
        if (SaveTargetTab?.Doc.TemplateId is not { Length: > 0 } id) return null;
        // B114. Was a concat of three lists; the project has one.
        var reference = project.Manifest.Documents.FirstOrDefault(r => r.Id == id);
        if (reference is null) return null;
        var template = ProjectIo.LoadDocument(project, reference);
        return template is { IsTemplateDocument: true } ? template : null;
    }

    /// <summary>What a pull would change, or null when there is nothing to pull from.</summary>
    public Core.Projects.Templates.PullPreview? PreviewTemplatePull() =>
        TemplateOfActiveDocument() is { } template && SaveTargetTab is { } tab
            ? Core.Projects.Templates.Preview(tab.Doc, template)
            : null;

    /// <summary>
    /// Pull the ticked changes from the template, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The direction is the safety property: the artist reaches out to the
    /// template, one document at a time, when they say so. Nothing ever travels
    /// the other way, so a finished shot cannot change under anybody.
    /// </remarks>
    public int UpdateFromTemplate(Core.Projects.Templates.PullOptions options)
    {
        if (TemplateOfActiveDocument() is not { } template) return 0;
        var changed = 0;
        _editor.Perform(doc => changed = Core.Projects.Templates.Apply(doc, template, options));
        if (changed == 0)
        {
            // Nothing moved, so the undo step would be an empty one the artist
            // has to press through. Drop it.
            _editor.Undo();
            AiStatus = "Nothing to pull — the document already matches its template.";
            return 0;
        }

        OnDocumentChanged();
        MarkDocumentEdited();
        AiStatus = $"Pulled {changed} change{(changed == 1 ? "" : "s")} from the template. One undo puts it back.";
        return changed;
    }

    /// <summary>
    /// Can the current tab be saved without asking where? True for a project
    /// animation and for a loose document that already has a path.
    /// </summary>
    public bool CanSaveInPlace =>
        ProjectDocker.HasProject && SaveTargetTab?.Source is not null
        || SaveTargetTab?.FilePath is { Length: > 0 }
        // A view onto a project sheet (B249). It has no file of its own — the
        // project's save writes it, exactly as a symbol's — so Save must mean
        // "save the project" rather than falling through to a picker that
        // offers to write a document nothing would reference.
        || IsProjectSheetView;

    /// <summary>The active tab is a view onto a sheet the project owns.</summary>
    /// <remarks>
    /// Asked of <see cref="ActiveTab"/> rather than <c>SaveTargetTab</c>, which
    /// is deliberately null here: a sheet view defers to no document, and that
    /// is exactly why Save had nowhere to go.
    /// </remarks>
    public bool IsProjectSheetView =>
        ProjectDocker.HasProject
        && ActiveTab is { Kind: DocumentTabKind.Reference, SheetSource: not null };

    /// <summary>
    /// Save without a picker. Missing entirely until now — every save opened a
    /// dialog even when the tab already knew where it came from.
    /// </summary>
    public void Save()
    {
        if (ProjectDocker.HasProject && SaveTargetTab?.Source is not null)
        {
            SaveProject();
            return;
        }
        // B249: the sheet travels in the project file, so this is the same
        // save — and the tab has to be told, or it keeps its dot and the
        // close prompt still claims there is unsaved work.
        if (IsProjectSheetView)
        {
            SaveProject();
            ActiveTab?.MarkSaved();
            return;
        }
        if (SaveTargetTab is not { FilePath: { Length: > 0 } path } tab) return;
        try
        {
            // With in-place autosave on, a background write may be heading for
            // this exact path; writing over it mid-flight collides on the temp
            // file, and a stale snapshot landing late would undo this save.
            _autosave.FinishPendingWrite();
            StampPlayhead(tab);
            DocJson.Save(tab.Doc, path);
            tab.MarkSaved();
            AiStatus = $"Saved {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>
    /// A standalone copy of the active document, with every project resource
    /// it references inlined — what "Export document…" writes.
    /// </summary>
    public string ExportStandaloneDocument()
    {
        var doc = SaveTargetTab?.Doc ?? Doc;
        if (ProjectDocker.Project is { } project) doc = ProjectIo.Flatten(doc, project);
        return DocJson.Serialize(doc);
    }

    /// <summary>Open a loaded document in a new tab.</summary>
    public void OpenDocumentTab(Doc doc, string? filePath)
    {
        var title = filePath is null ? NextUntitledName() : TitleFromPath(filePath);
        var tab = new DocumentTab(new DocumentEditor(doc), title) { FilePath = filePath };
        // B136. Land on something paintable — the same line File ▸ New has
        // carried since B56's era, missing here. A saved document's layer 0 is
        // its locked paper, so every document opened from disk started with
        // the one layer that refuses strokes: the cursor showed, the status
        // strip said "locked", and nothing appeared. Reported as "unable to
        // draw on the last build".
        tab.State.LayerIndex = FirstPaintableLayer(doc);
        // Q111: reopen where the artist was parked. Negative-proofed only —
        // past the end is a place the playhead is allowed to stand
        // (PlayheadPastTheEnd), so capping at FrameCount would snap a
        // legitimately parked scene back onto its last drawing.
        tab.State.FrameIndex = Math.Max(0, doc.PlayheadFrame ?? 0);
        // B99. Opened from disk means it *is* what is on disk — without this it
        // would inherit the never-saved default and badge a file nobody touched.
        if (filePath is not null) tab.MarkSaved();
        AddTab(tab);
        if (filePath is not null) Remember(filePath, RecentKind.Document);
    }

    // ---- what you had open last -----------------------------------------------

    /// <summary>
    /// Record that something was opened or saved.
    /// </summary>
    /// <remarks>
    /// Saved as well as opened: a document written for the first time is one
    /// you have every reason to come back to, and leaving it out means the
    /// entry only appears the second time you use it.
    /// </remarks>
    public void Remember(string path, RecentKind kind)
    {
        Settings.Recent.Add(path, "", kind, DateTimeOffset.Now);
        Settings.Save();
        OnPropertyChanged(nameof(RecentEntries));
        OnPropertyChanged(nameof(HasRecents));
    }

    /// <summary>The recents that are still on disk, newest first.</summary>
    public IReadOnlyList<RecentItem> RecentEntries => Settings.Recent.Existing();

    public bool HasRecents => RecentEntries.Count > 0;

    [RelayCommand]
    public void ForgetRecents()
    {
        Settings.Recent.Clear();
        Settings.Save();
        OnPropertyChanged(nameof(RecentEntries));
        OnPropertyChanged(nameof(HasRecents));
    }

    /// <summary>
    /// Open something from the recents list, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// One entry point so the menu, the start screen and a double-click all
    /// take the same route — including the part where a file that has since
    /// been moved says so instead of doing nothing.
    /// </remarks>
    public void OpenRecent(RecentItem? item)
    {
        if (item is null) return;
        if (item.Kind == RecentKind.Project)
        {
            if (!Directory.Exists(item.Path))
            {
                AiStatus = $"“{item.Name}” is no longer at {item.Path}.";
                return;
            }
            OpenProject(item.Path);
            return;
        }
        if (!File.Exists(item.Path))
        {
            AiStatus = $"“{item.Name}” is no longer at {item.Path}.";
            return;
        }
        try
        {
            OpenDocumentTab(DocJson.Load(item.Path), item.Path);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            AiStatus = $"Could not open {item.Name}: {ex.Message}";
        }
    }

    /// <summary>Close a tab. The view confirms unsaved changes before calling this.</summary>
    public void CloseTab(DocumentTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        Tabs.Remove(tab);
        // An animation tab takes its reference-view tabs with it.
        foreach (var orphan in Tabs.Where(t => t.Owner == tab).ToList()) Tabs.Remove(orphan);
        // B99. Closing a document that was never written takes its row with it.
        // Here rather than in the close handler because this is the one funnel
        // every close goes through, and the handler has already resolved the
        // save-or-discard question by the time it calls this: if the artist chose
        // Save the file now exists, so the row stays.
        //
        // A reference view belongs to the document it was opened from, so only a
        // tab that owns its own document can take a row out of the project.
        if (tab.Owner is null) ProjectDocker.ForgetIfNeverWritten(tab.Source);

        // Closing the last tab used to conjure a replacement, which meant there
        // was no way to arrive at an empty application and the canvas of that
        // invented document became whatever you drew on next. Now the workspace
        // simply empties, and the window asks what to open — the same question
        // the start screen asks, at the only other moment it is the right one.
        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            OnPropertyChanged(nameof(HasDocument));
            LastDocumentClosed?.Invoke();
            return;
        }

        OnPropertyChanged(nameof(HasDocument));
        if (ActiveTab == tab || ActiveTab is null || !Tabs.Contains(ActiveTab))
        {
            ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    /// <summary>
    /// The last document was closed and nothing is open.
    /// </summary>
    /// <remarks>
    /// An event rather than the view model opening a dialog itself, for the
    /// reason every other dialog here is the window's job: a view model that
    /// shows windows cannot be tested headlessly, and this one is reached by
    /// `CloseTab`, which the suite drives constantly.
    /// </remarks>
    public event Action? LastDocumentClosed;

    /// <summary>The active document was written to disk: adopt the name, clear the dirty dot.</summary>
    public void NotifySaved(string filePath)
    {
        if (SaveTargetTab is not { } tab) return;
        tab.FilePath = filePath;
        tab.Title = TitleFromPath(filePath);
        tab.MarkSaved();
        Remember(filePath, RecentKind.Document);
        // B99's other half. A document adopted at creation has to be released
        // when the artist gives it a home outside the project — otherwise its row
        // stays, pointing at a file that was never written there, and the next
        // project save writes a second copy inside the project. Saved into the
        // project instead, the record follows the file.
        if (tab.Source is { } source && !ProjectDocker.AdoptSavedPath(source, filePath))
        {
            tab.Source = null;
        }
        // The other direction: a loose document saved inside the project
        // joins it, and every project surface — the docker row, the manager
        // window, the tab's badge, the assets — resolves from the manifest
        // entry the adoption makes.
        else if (tab is { Source: null, Kind: DocumentTabKind.Animation }
                 && ProjectDocker.AdoptExistingFile(tab.Doc, filePath) is { } adopted)
        {
            tab.Source = adopted;
        }
        // Adoption or release changes what the docker should highlight and what
        // the tab strip should badge, without the active tab having changed.
        if (tab == ActiveTab)
        {
            ProjectDocker.MarkEditing(tab.Source?.Id);
        }
    }

    private void AddTab(DocumentTab tab)
    {
        var wasEmpty = Tabs.Count == 0;
        // B257. Undo and redo swap the editor's document object, and the
        // project holds its own reference to the one it loaded — the reference
        // a project save writes to disk. Subscribed here rather than where the
        // project opens a document, because the tab is the thing that owns an
        // editor, and a tab that becomes a project document later (a new
        // project adopting the open drawing) is then already wired.
        tab.Editor.DocReplaced += _ => RepointProjectCache(tab);
        Tabs.Add(tab);
        ActiveTab = tab;
        // Coming back from empty is the transition the whole UI hangs off, and a
        // property that only ever falls is worse than no property at all.
        if (wasEmpty) OnPropertyChanged(nameof(HasDocument));
    }

    /// <summary>
    /// Point the project's loaded-document cache at the object this tab's
    /// editor now holds.
    /// </summary>
    /// <remarks>
    /// <b>B257, and the reason it was invisible.</b> `project.Loaded[id]` is
    /// what <c>ProjectIo.Save</c> writes, and it was set once when the document
    /// was opened. An ordinary edit mutates the document in place, so the two
    /// references agreed through any amount of drawing — and a single undo
    /// swapped the editor onto the snapshot instance and left the project
    /// holding the other one. Everything after that undo landed on a document
    /// nothing would ever write, and the save reported success. Reopening
    /// showed the file as it stood at the undo: work present up to that point,
    /// everything after it gone.
    /// <para>
    /// Reads <see cref="DocumentTab.Source"/> at call time rather than
    /// capturing it, so a tab that is adopted into a project after it was
    /// opened is covered by the same subscription.
    /// </para>
    /// </remarks>
    private void RepointProjectCache(DocumentTab tab)
    {
        if (ProjectDocker.Project is not { } project) return;
        if ((tab.Owner ?? tab).Source is not { } source) return;
        project.Loaded[source.Id] = tab.Doc;
    }

    private int _untitledCounter = 1;

    private string NextUntitledName() => $"Untitled-{++_untitledCounter}";

    private static string TitleFromPath(string path)
    {
        var name = Path.GetFileName(path);
        const string suffix = ".lightbox.json";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// Something in a document changed, whatever it was — the edit funnel's
    /// outward face. What the reference-view windows re-render on: they follow
    /// a sheet that can be edited from its tab, the docker, or an undo, and
    /// this is the one place all three pass through (the same argument B31
    /// makes for the cache invalidated below).
    /// </summary>
    public event Action? DocumentEdited;

    private void MarkDocumentEdited()
    {
        _autosave.MarkDirty();
        // B31: the encoded reference views are only valid while the drawing is. This is the
        // funnel that sees a stroke commit — OnDocumentChanged returns early for those — so a
        // cache invalidated anywhere else would hand a model art that had since changed.
        InvalidateReferenceViewCache();
        DocumentEdited?.Invoke();
        // Same funnel, same reason, pointed at the canvas instead of the AI:
        // a view taped onto the canvas is re-flattened the moment its sheet
        // is edited (Q69 chose live over snapshot). No linked strip, no cost.
        RefreshLinkedReferenceStrips();
        // The guard sits here as well as inside MarkActiveTabEdited because it
        // has always covered the rebake too: mid-switch there is no playhead
        // worth baking against, and the arriving tab re-runs this funnel.
        if (_switchingTabs || ActiveTab is null) return;
        MarkActiveTabEdited();
        RebakeLiveSamples();
    }

    /// <summary>
    /// The bookkeeping half of <see cref="MarkDocumentEdited"/>: which tab and
    /// which project source now carry unsaved work. Split out so a change that
    /// dirties the file without touching a pixel (a reference dial, B191) can
    /// say so without paying for the pixel-derived machinery above.
    /// </summary>
    private void MarkActiveTabEdited()
    {
        if (_switchingTabs || ActiveTab is not { } tab) return;
        // Here rather than in OnDocumentChanged: stroke commits take that
        // method's scoped-edit early return, and a stroke is exactly the edit
        // an incremental save must not miss.
        if ((tab.Owner ?? tab).Source is { } source) ProjectDocker.MarkDirty(source);
        // A switch, so that adding a third kind of tab cannot quietly re-bind
        // an else onto the wrong branch — which is exactly what adding the
        // second one did, and it dirtied every reference tab.
        switch (tab.Kind)
        {
            case DocumentTabKind.Reference:
                // Undo/redo replaces the wrapper doc's layer list; keep the
                // owning document's view pointed at whatever the editor holds.
                if (tab.View is { } view) view.Layers = Doc.Scene.Layers;
                // A project sheet's edits belong to the project, the way a
                // symbol's do — there is no owning document to dirty, and the
                // project's save is what writes them.
                if (tab.SheetSource is { } filed && ProjectDocker.Project is { } project)
                {
                    project.DirtySheets.Add(filed.Id);
                }
                // The edit belongs to the owning document. B95: refresh this
                // tab too, so the sheet an artist is looking at shows the badge
                // rather than making them go and find the parent.
                if (tab.Owner is { } owner) owner.RefreshDirty();
                tab.RefreshDirty();
                break;

            case DocumentTabKind.Symbol:
                // A symbol belongs to the project, so there is no owning
                // document to dirty — the project's own save writes it. What
                // has to happen here is the version bump, which is what makes
                // every placement of it redraw.
                SyncEditedSymbol();
                break;

            default:
                // B98. Not "this is now dirty" — "look again at whether it is".
                // The edit that got us here already moved the editor's revision
                // if it changed anything, and if it did not, nothing should
                // change here either.
                tab.RefreshDirty();
                break;
        }
    }

    /// <summary>
    /// Re-freeze the all-layers-live strokes at the playhead, because something
    /// underneath them may have just moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes Live live. The alternative was handing a backdrop to
    /// every render path at render time; this hands it to the stroke once per
    /// edit instead, so there is one place that can be wrong rather than four,
    /// and canvas and export cannot disagree.
    /// </para>
    /// <para>
    /// Not an undo step, on purpose. The bake is derived from the layers below
    /// and the stroke that owns it, not something anybody authored, and an undo
    /// history with a "the background moved" entry between every real edit
    /// would be unusable. Undo re-enters here anyway — it goes through the same
    /// funnel — so the sample follows the document back.
    /// </para>
    /// <para>
    /// Only the playhead's frames, and that is exact rather than a shortcut: an
    /// edit happens at the playhead, so the strokes it can invalidate are the
    /// ones exposed there. The one case it does not cover is a held cel shown
    /// across a range whose backdrop differs along it — a frame carries one
    /// sample and can only answer for one index.
    /// </para>
    /// </remarks>
    private void RebakeLiveSamples()
    {
        var scene = Scene;
        // Nothing is rendered until a live stroke is actually found, so the
        // cost on an ordinary document is the exposure lookups plus a scan of
        // the strokes on the playhead's frames — no compose, no materialize.
        //
        // There was a document-wide "does anything sample?" guard in front of
        // this. It was removed on measurement grounds rather than taste: it
        // walked every cel of every layer and every stroke of every frame,
        // which on a long scene is more work than the loop it was protecting,
        // and it ran on every edit.
        var below = new List<(Layer Layer, Frame? Frame)>();
        foreach (var layer in scene.Layers)
        {
            var exposed = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (exposed is { } painted && LiveStrokes(painted) is { Count: > 0 } live)
            {
                Rebake(live, below, scene.Width, scene.Height);
                InvalidateFrameRender(painted.Id);
                _dirtyThumbIds.Add(painted.Id);
            }
            // An adjustment layer exposes no drawing and still belongs in the
            // stack a sample froze against — it changes what the stroke saw.
            if (scene.IsLayerVisible(layer) && (exposed is not null || layer.IsAdjustment))
            {
                below.Add((layer, exposed));
            }
        }
    }

    private static List<Stroke> LiveStrokes(Frame frame) =>
        [.. frame.Strokes.Where(s => s.Brush.SampleSource == SampleSource.AllLayersLive)];

    /// <summary>Re-freeze one frame's live strokes against the stack beneath it.</summary>
    private void Rebake(List<Stroke> live, List<(Layer Layer, Frame? Frame)> below, int width, int height)
    {
        if (below.Count == 0)
        {
            // Nothing underneath: there is no backdrop to follow, so the stroke
            // reverts to reading its own layer rather than keeping a stale one.
            foreach (var stroke in live) stroke.Baked = null;
            return;
        }

        var passes = new List<RenderPass>(below.Count);
        foreach (var b in below)
        {
            // The stroke re-reads what it visibly sat on, so the stack it
            // froze against is shaped and filtered like the composite
            // (IndexOf is fine: a rebake happens per edit, not per pointer
            // event).
            var layerIndex = Scene.Layers.IndexOf(b.Layer);
            if (b.Layer.IsAdjustment)
            {
                if (EffectPasses.AdjustmentPass(Scene, layerIndex, CurrentFrameIndex, _cache) is { } adj)
                {
                    passes.Add(adj);
                }
                continue;
            }
            if (b.Frame is not { } exposed) continue;
            var shapes = LayerShapes.For(Scene, layerIndex, CurrentFrameIndex);
            if (shapes is { Count: 0 }) continue;
            passes.Add(new RenderPass(
                _cache.Get(exposed, width, height, celIndex: CurrentFrameIndex),
                null, b.Layer.Opacity, SceneRenderer.ToSkia(b.Layer.BlendMode),
                Shapes: LayerShapes.Resolve(shapes, _cache, width, height, CurrentFrameIndex),
                Effect: EffectPasses.SelfFilter(b.Layer, CurrentFrameIndex),
                Style: EffectPasses.SelfStyle(b.Layer, CurrentFrameIndex)));
        }
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var image = SceneRenderer.Compose(width, height, passes, SKColors.Transparent);
        using var beneath = SKBitmap.FromImage(image);
        foreach (var stroke in live) stroke.Baked = BrushEngine.BakeSample(stroke, beneath, info);
    }

    // ---- projects -----------------------------------------------------------

    /// <summary>
    /// The project docker's state. Holds no project until one is created or
    /// opened — the app is document-first and shows no project UI until then.
    /// </summary>
    public ProjectViewModel ProjectDocker { get; }

    /// <summary>
    /// The character library — see <see cref="LibraryViewModel"/>. Lazy so a
    /// session that never opens the library builds nothing for it; both the
    /// picker and the library window read this one instance (Q138).
    /// </summary>
    public LibraryViewModel Characters => _library ??= new LibraryViewModel(
        Settings, () => ProjectDocker.Project, AfterLibraryImport);

    private LibraryViewModel? _library;

    /// <summary>
    /// What every import path owes, however it was reached — the two UI
    /// surfaces and the MCP op all land here, so none can forget half of it.
    /// The import mutated the manifest and loaded documents in memory; the
    /// docker must show it and the disk must hold it — an import that
    /// vanishes with the session is slice 1's round-trip lesson.
    /// </summary>
    internal void AfterLibraryImport(ImportResult result)
    {
        ProjectDocker.Refresh();
        SaveProject(everything: true);
        var summary = string.Join(", ", new[]
        {
            result.Added.Count > 0 ? $"{result.Added.Count} added" : null,
            result.Replaced.Count > 0 ? $"{result.Replaced.Count} updated" : null,
            result.KeptEdited.Count > 0 ? $"{result.KeptEdited.Count} kept (edited here)" : null,
        }.Where(part => part is not null));
        AiStatus = $"Imported “{result.Folder.Name}”{(summary.Length > 0 ? $": {summary}" : "")}.";
    }

    /// <summary>
    /// Which panels are open, where, and how big — the whole workspace.
    /// </summary>
    /// <remarks>
    /// Owned here rather than by the window so a layout survives the window
    /// being rebuilt, and so the tests can drive it without one.
    /// </remarks>
    public WorkspaceViewModel Workspace { get; } = new();

    /// <summary>Preferences that are not about pixels — see <see cref="AppSettings"/>.</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Composite layers on the GPU rather than the CPU (B125, experimental).
    /// </summary>
    /// <remarks>
    /// Goes through here rather than the settings object directly, so the render
    /// thread's mirror and the persisted value cannot drift apart — and so the
    /// canvas repaints immediately instead of on the next thing that happens to
    /// dirty it.
    /// </remarks>
    /// <summary>
    /// Record the pen's tilt and the hand's speed <em>even when the brush
    /// ignores them</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An override, not the switch.</b> Ordinarily the brush decides — a
    /// brush with tilt curves records tilt, one without does not
    /// (<see cref="PenAxisUse"/>) — which costs nothing and needs no
    /// configuration. This is for the artist who wants the numbers kept anyway,
    /// so a stroke can be given a tilt curve months later and actually respond
    /// to it. The trade is size: recording all three roughly doubles the
    /// record, measured.
    /// </para>
    /// <para>
    /// <b>Saved the moment it changes</b>, like every other preference here, so
    /// the answer survives the session that set it. Turning it off never
    /// touches art already made — the axes are in the points of strokes that
    /// recorded them and stay there (invariant 4); this decides what the next
    /// stroke captures and nothing more.
    /// </para>
    /// </remarks>
    public bool AlwaysRecordPenAxes
    {
        get => Settings.AlwaysRecordPenAxes;
        set
        {
            if (Settings.AlwaysRecordPenAxes == value) return;
            Settings.AlwaysRecordPenAxes = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the next stroke would capture tilt: the brush asks, or the artist did.</summary>
    /// <remarks>
    /// <b>Nothing binds to these — they are read at <c>BeginStroke</c>.</b>
    /// That is deliberate: a bound property would need a change notification
    /// from every place the active brush or one of its curves moves, and a
    /// missed one records a stroke under the previous brush's answer. Asking at
    /// the moment the stroke starts cannot go stale.
    /// </remarks>
    internal bool WouldRecordTilt =>
        AlwaysRecordPenAxes || PenAxisUse.NeedsTilt(CurrentToolSettings);

    /// <summary>Whether the next stroke would capture speed: the brush asks, or the artist did.</summary>
    internal bool WouldRecordSpeed =>
        AlwaysRecordPenAxes || PenAxisUse.NeedsSpeed(CurrentToolSettings);

    public bool GpuCompositing
    {
        get => Settings.GpuCompositing;
        set
        {
            if (Settings.GpuCompositing == value) return;
            Settings.GpuCompositing = value;
            Rendering.GpuComposite.SettingEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            // The composite path changed under the canvas, so what is on screen
            // was produced by the other one. Republish rather than wait.
            _publish.InvalidateWholeCanvas();
            PublishSnapshot();
        }
    }


    /// <summary>Minutes between autosaves; 0 turns it off. Persists immediately.</summary>
    public double AutosaveMinutes
    {
        get => Settings.AutosaveMinutes;
        set
        {
            if (Math.Abs(Settings.AutosaveMinutes - value) < 1e-9) return;
            Settings.AutosaveMinutes = value;
            _autosave.Reschedule(Settings.AutosaveInterval);
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutosaveLabel));
        }
    }

    /// <summary>Also write over the document's own file, once it has one.</summary>
    /// <summary>
    /// Whether the start screen is offered when the application opens.
    /// </summary>
    /// <remarks>
    /// The screen has a "don't show this again" of its own, which is where it
    /// gets turned off. This is the way back — a setting you can only switch
    /// off from a screen you no longer see is a setting you cannot switch on.
    /// </remarks>
    public bool ShowStartScreen
    {
        get => Settings.ShowStartScreen;
        set
        {
            if (Settings.ShowStartScreen == value) return;
            Settings.ShowStartScreen = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether a console window opens at startup carrying the diagnostic traces.
    /// </summary>
    /// <remarks>
    /// Takes effect on the next start rather than immediately, and the menu
    /// says so. Opening one mid-session is possible but would produce a window
    /// that had missed everything up to that point — which is the opposite of
    /// what somebody turning this on wants.
    /// </remarks>
    public bool ShowDiagnosticsConsole
    {
        get => Settings.ShowDiagnosticsConsole;
        set
        {
            if (Settings.ShowDiagnosticsConsole == value) return;
            Settings.ShowDiagnosticsConsole = value;
            Settings.Save();
            OnPropertyChanged();
            AiStatus = value
                ? "The diagnostics console will open the next time Lightbox starts."
                : "The diagnostics console will not open next time.";
        }
    }

    /// <summary>Where the crash reports and the survivable-failure log live.</summary>
    public string DiagnosticsFolder => Services.DiagnosticLog.Directory;

    /// <summary>The exact build, for a bug report to name.</summary>
    public string BuildLabel => $"Lightbox {Services.DiagnosticLog.Build}";

    public bool AutosaveInPlace
    {
        get => Settings.AutosaveInPlace;
        set
        {
            if (Settings.AutosaveInPlace == value) return;
            Settings.AutosaveInPlace = value;
            _autosave.InPlace = value;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutosaveLabel));
        }
    }

    public string AutosaveLabel =>
        Settings.AutosaveInterval is null
            ? "Autosave off"
            : $"Autosave every {Settings.AutosaveMinutes:0.##} min{(Settings.AutosaveInPlace ? ", in place" : "")}";

    /// <summary>Whether any project UI should exist at all.</summary>
    public bool HasProject => ProjectDocker.HasProject;

    /// <summary>
    /// Change what the open project is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a migration: the type is a statement about intent that tooling and
    /// export read, so converting is exactly a change of that statement. No
    /// document is read, rewritten or recreated, and nothing already authored
    /// is dropped — a camera keyframed under Animation is still there under
    /// Game art, ignored rather than erased.
    /// </para>
    /// <para>
    /// The workspace is left alone. Which panels somebody wants is a
    /// preference and converting a project is a decision about the project;
    /// rearranging the screen as a side effect of a menu item is how a tool
    /// loses trust. <see cref="TakeProjectTypeWorkspace"/> is the separate,
    /// asked-for move.
    /// </para>
    /// </remarks>
    public ProjectIo.ConversionReport? ConvertProject(ProjectType? to)
    {
        if (ProjectDocker.Project is not { } project) return null;
        var report = ProjectIo.Convert(project, to);
        SaveProject();
        OnProjectChanged();
        OnPropertyChanged(nameof(ProjectTypeLabel));
        AiStatus = string.Join("  ", report.Notes);
        return report;
    }

    /// <summary>Switch to the current project type's default panels, when asked.</summary>
    public void TakeProjectTypeWorkspace()
    {
        if (ProjectDocker.Project?.Manifest.Type is { } type) Workspace.UseDefaultFor(type);
    }

    /// <summary>What the project is for, for a menu header.</summary>
    public string ProjectTypeLabel => ProjectDocker.Project?.Manifest.Type is { } type
        ? $"Project type — {type}"
        : "Project type — unset";

    private void OnProjectChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        // Read before RegisterResources re-derives it: a change that removed
        // the variant's last attachment nulls the resolver in there, and the
        // repaint below must still happen or the armor outlives its record.
        var dressedBefore = Rendering.AttachmentOverlay.Resolver is not null;
        RegisterResources();
        // An attachment edited in the project window is a pixel change on the
        // canvas behind it (Q143) — found by an adversarial pass asserting
        // the repaint this line is: the editor's status line promised "the
        // canvas shows it" and nothing asked the canvas to. A no-op for every
        // project that wears nothing, before and after.
        AttachmentsMayHaveMoved(evenIfBare: dressedBefore);
        MarkDocumentEdited();
    }

    /// <summary>A blank document for a new animation, matching the active scene's shape.</summary>
    private Doc NewAnimationDoc()
    {
        var scene = Scene;
        return DocumentFactory.CreateDoc(
            scene.Width, scene.Height, scene.Fps,
            scene.TransparentBackground ? null : scene.BackgroundColor);
    }

    /// <summary>
    /// A blank document sized like the current scene — what the docker's
    /// ＋ New makes, exposed so the project window's creator makes the same
    /// one rather than a second definition of "blank".
    /// </summary>
    public Doc NewProjectDocument() => NewAnimationDoc();

    /// <summary>Open a project animation as a tab, or focus the tab it is already in.</summary>
    /// <remarks>
    /// Public because the project window opens documents too, and it has to go
    /// through this one rather than making its own tab — the focus-what-is-open
    /// branch is the whole reason opening twice does not produce two tabs.
    /// </remarks>
    public void OpenProjectDocument(DocumentRef reference, Doc doc)
    {
        if (Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id) is { } already)
        {
            ActiveTab = already;
            return;
        }
        var opened = new DocumentTab(new DocumentEditor(doc), reference.Name) { Source = reference };
        opened.State.LayerIndex = FirstPaintableLayer(doc);
        AddTab(opened);
    }

    /// <summary>
    /// Point the engine's registries at the project's shared resources AND the
    /// active document's.
    ///
    /// This is the whole of Pillar 1's sharing, and it needs no engine change:
    /// the brush engine already resolves swatches, gradients, tips and clips by
    /// id at render time. Widening the scope is all it takes for two animations
    /// under one character to paint from one palette.
    /// </summary>
    /// <summary>
    /// Re-scope the registries after the project's shared resources changed
    /// outside a document edit — importing a palette, or a test adding one.
    /// </summary>
    public void RefreshProjectResources() => RegisterResources();

    /// <summary>Whether a frame's render is still cached — B102's test probe.</summary>
    internal bool IsFrameCached(string frameId) => _cache.Holds(frameId);

    /// <summary>Paint from a palette swatch, as picking one in the panel does.</summary>
    internal void PickSwatchForTest(string swatchId) => PaintWithSwatch(swatchId);

    private void RegisterResources()
    {
        // Imported textures come in with everything else the document carries,
        // so a file opened on a machine that has never seen one still paints
        // the paper it was drawn on.
        if (Doc.Textures is { Count: > 0 } textures) TextureRegistry.Register(textures);

        var palettes = Doc.Palettes.AsEnumerable();
        var gradients = new Dictionary<string, Gradient>(Doc.Gradients);
        if (ProjectDocker.Project is { } project)
        {
            // Document first, project second, so a document's own copy of a
            // swatch id loses to the project's — the shared one is the live one.
            //
            // Q30 step 2: only the project palettes this document can actually
            // see. Until now every palette went in for every document, which
            // reads as working until a project has two characters and the
            // goblin's reds turn up in the knight's picker. A project that
            // declares no scopes still gets everything — that is the
            // new-projects-only migration, at the one place a reader can tell
            // the two shapes apart.
            var visible = PaletteScopes.VisibleTo(
                project.Manifest, (SaveTargetTab ?? ActiveTab)?.Source);
            palettes = palettes.Concat(
                visible is null
                    ? project.Palettes
                    : project.Palettes.Where(p => visible.Contains(p.Id)));
            // Q30 step 4: the same scoping palettes got, for the same reason —
            // a gradient made for the knight's shield has no business in the
            // goblin's picker. Null still means the project scopes none.
            var visibleGradients = GradientScopes.VisibleTo(
                project.Manifest, (SaveTargetTab ?? ActiveTab)?.Source);
            foreach (var (id, gradient) in project.Gradients)
            {
                if (visibleGradients is null || visibleGradients.Contains(id)) gradients[id] = gradient;
            }
        }
        // Active variants swap their copies in for the base palettes here, at
        // the one funnel every palette passes through on its way to rendering
        // — see MainViewModel.Variants.cs for why it must be a stand-in.
        var resolved = ApplyVariantStandIns(palettes.ToList());
        PaletteRegistry.Reset(resolved, gradients);
        // And what those variants wear rides the same funnel (Q143), so the
        // canvas and an export of this document agree about the armor the way
        // they already agree about the colours.
        ConfigureAttachmentOverlay();
        // Symbols are project-scoped while a project is open, which is the
        // point of them: the sword lives above the animations that hold it. A
        // document carries its own only when it arrived flattened from
        // somewhere else, and then the project's copy of an id wins — the same
        // precedence the palettes use, for the same reason.
        var symbols = new Dictionary<string, Lightbox.Core.Documents.Symbol>();
        foreach (var (id, symbol) in Doc.Symbols ?? []) symbols[id] = symbol;
        if (ProjectDocker.Project is { } withSymbols)
        {
            foreach (var (id, symbol) in withSymbols.Symbols) symbols[id] = symbol;
        }
        SymbolRegistry.Reset(symbols);
        // Every colour picker in the app — the panel's and every flyout's —
        // offers the same swatches, because they are all looking at the same
        // document.
        _paletteSwatches = resolved.SelectMany(p => p.Swatches).ToList();
        ColorPickerViewModel.PaletteSource = () => _paletteSwatches;
        // The way back. Every picker in the app can put its colour in the
        // palette, and they all mean the same palette — the one the docker has
        // selected — because there is one document.
        ColorPickerViewModel.PaletteSink = request =>
        {
            var outcome = PaletteDocker.AddColor(request);
            // The docker's own status line is easy to miss when the wheel is
            // open over the canvas, so a refusal says so in the status bar too.
            if (outcome.Message is { Length: > 0 } message) AiStatus = message;
            return outcome;
        };
        ColorPickerViewModel.PaletteTargetSource = () => PaletteDocker.PaletteTargets;
        ColorPicker.RefreshPalette();
        // The source is static, so both halves of the pair see the new list —
        // but each has to be told to look again, or the background picker keeps
        // showing the previous document's swatches.
        BackgroundPicker.RefreshPalette();

        if (Scene.References is { Count: > 0 } strips)
        {
            Lightbox.Raster.ReferenceStripRegistry.Register(
                strips.Select(s => (s.Id, s.Png)));
            // Video references carry no PNG — their pixels come back off the
            // footage itself (Q56).
            foreach (var strip in strips.Where(s => s.VideoPath is not null || s.VideoData is not null))
            {
                RegisterVideoReference(strip);
            }
        }
    }

    private IReadOnlyList<Swatch> _paletteSwatches = [];

    // ---- document I/O -------------------------------------------------------

    // ---- external producers (IPC/MCP) ---------------------------------------

    /// <summary>The layer external tools target when they don't name one.</summary>
    public Layer ActiveLayerForIpc => ActiveLayer;

    /// <summary>Composite one timeline frame to PNG (no onion skin, no live stroke).</summary>
    public string RenderFramePng(int frameIndex)
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        for (var layerIndex = 0; layerIndex < scene.Layers.Count; layerIndex++)
        {
            var layer = scene.Layers[layerIndex];
            if (!scene.IsLayerVisible(layer)) continue;
            if (layer.IsAdjustment)
            {
                if (EffectPasses.AdjustmentPass(scene, layerIndex, frameIndex, _cache) is { } adj)
                {
                    passes.Add(adj);
                }
                continue;
            }
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            var shapes = LayerShapes.For(scene, layerIndex, frameIndex);
            if (shapes is { Count: 0 }) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height, celIndex: frameIndex), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode),
                Shapes: LayerShapes.Resolve(shapes, _cache, scene.Width, scene.Height, frameIndex),
                Effect: EffectPasses.SelfFilter(layer, frameIndex),
                Style: EffectPasses.SelfStyle(layer, frameIndex)));
        }
        if (EffectPasses.SceneStackPass(scene, frameIndex) is { } grade) passes.Add(grade);
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SceneRenderer.BackgroundOf(scene));
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>
    /// Insert externally produced inbetween frames (already validated, sorted
    /// by t) after key <paramref name="aIndex"/>. One undo step. Returns the
    /// number of frames inserted.
    /// </summary>
    public int InsertExternalInbetweens(string layerId, int aIndex, List<List<Stroke>> strokeFrames)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        if (!CanEdit(layer, "insert inbetweens on it")) return 0;
        var frames = strokeFrames.Select(s =>
        {
            var frame = NewFrameFor(layer, s, FrameRole.Inbetween);
            // Q31: provenance on every frame AI touched — an agent working the
            // document over MCP is exactly that, whatever model drives it.
            frame.Ai = new AiProvenance("MCP agent");
            return frame;
        }).ToList();
        _editor.InsertInbetweens(layerId, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        return frames.Count;
    }

    /// <summary>
    /// Append externally produced strokes to the key exposed at
    /// <paramref name="frameIndex"/>. One undo step. Returns strokes added
    /// (0 when the layer has no key there).
    /// </summary>
    public int AppendExternalStrokes(string layerId, int frameIndex, List<Stroke> strokes)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        if (!CanEdit(layer, "draw on it")) return 0;
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, frameIndex);
        if (keyIndex < 0) return 0;
        var frame = layer.Cels[keyIndex].Frame!;
        _editor.Perform(_ =>
        {
            StrokesOf(frame).AddRange(strokes);
            // Q31: the frame was AI-touched, even when the artist drew the
            // rest of it. Absent stays absent on frames no agent reaches.
            frame.Ai ??= new AiProvenance("MCP agent");
        });
        InvalidateFrameRender(frame.Id);
        _dirtyThumbIds.Add(frame.Id);
        PublishSnapshot();
        RefreshThumbnails();
        return strokes.Count;
    }

    /// <summary>Replace the ACTIVE tab's document (fresh editor, clean state).</summary>
    /// <remarks>
    /// With nothing open there is nothing to replace, and refusing would hand
    /// back a document the caller asked to see and cannot. So the empty case
    /// opens it instead — same intent, one tab either way.
    /// </remarks>
    public void ReplaceDocument(Doc doc)
    {
        if (Tabs.Count == 0)
        {
            var opened = new DocumentTab(new DocumentEditor(doc), doc.Scene.Name);
            opened.State.LayerIndex = FirstPaintableLayer(doc);
            opened.State.FrameIndex = Math.Max(0, doc.PlayheadFrame ?? 0);
            AddTab(opened);
            opened.MarkSaved();
            return;
        }

        _switchingTabs = true;
        var tab = ActiveTab ?? Tabs[0];
        tab.Editor = new DocumentEditor(doc);
        AttachEditor(tab.Editor);
        // B136's other door: index 0 is the locked paper on any document that
        // has one, and a replace is how tests and the MCP surface open files.
        ActiveLayerIndex = FirstPaintableLayer(doc);
        // Q111: reopen where the artist was parked, not at the start.
        // Negative-proofed only — past the end is legal (PlayheadPastTheEnd).
        CurrentFrameIndex = Math.Max(0, doc.PlayheadFrame ?? 0);
        // A fresh editor sits at revision 0 and this document came from disk,
        // so that is its saved point.
        tab.MarkSaved();
        _switchingTabs = false;
    }

    /// <summary>
    /// The playhead crosses into the record only here, at the moment the
    /// record is written (Q110): the frame the artist is parked on rides into
    /// the file so the document reopens showing what it showed when it was put
    /// down. Null at frame 0 — optional means absent.
    /// </summary>
    private void StampPlayhead(DocumentTab tab)
    {
        var frame = tab == ActiveTab ? CurrentFrameIndex : tab.State.FrameIndex;
        tab.Doc.PlayheadFrame = frame > 0 ? frame : null;
    }

    /// <summary>Serialize the save target (a reference tab serializes its owning document).</summary>
    public string SerializeDocument()
    {
        if (SaveTargetTab is { } tab)
        {
            StampPlayhead(tab);
            return DocJson.Serialize(tab.Doc);
        }
        Doc.PlayheadFrame = CurrentFrameIndex > 0 ? CurrentFrameIndex : null;
        return DocJson.Serialize(Doc);
    }
}
