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
    // ---- whose brush is it (Q9) -------------------------------------------------

    /// <summary>
    /// Whether the brush follows the tool or the drawing, right now.
    /// </summary>
    /// <remarks>
    /// A chosen preference wins; otherwise the project type decides, and with
    /// no project open there is no type to ask and it is Global — which is
    /// what the application has always done.
    /// </remarks>
    public BrushScope BrushScope =>
        // No project is not a preference that can be overridden — there is
        // nowhere to keep a brush, so the honest answer is the one the
        // application has always given.
        ProjectDocker.Project is not { } project
            ? BrushScope.Global
            : Settings.BrushScopeChoice ?? BrushScopeDefaults.For(project.Manifest.Type);

    /// <summary>The three answers, in the order they are offered.</summary>
    public IReadOnlyList<string> BrushMemoryChoices { get; } =
        ["Follow the project", "Global", "Per project"];

    /// <summary>
    /// The chosen answer, as the Configure page words it. "Follow the project"
    /// stores nothing, so the default keeps tracking the project type rather
    /// than freezing to whatever it happened to mean the day it was read.
    /// </summary>
    public string BrushMemoryChoice
    {
        get => Settings.BrushScopeChoice switch
        {
            BrushScope.Global => "Global",
            BrushScope.PerProject => "Per project",
            _ => "Follow the project",
        };
        set
        {
            var stored = value switch
            {
                "Global" => nameof(BrushScope.Global),
                "Per project" => nameof(BrushScope.PerProject),
                _ => null,
            };
            if (Settings.BrushMemory == stored) return;
            Settings.BrushMemory = stored;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrushScope));
            // Switching to per-project mid-session should hand back what the
            // project already remembers, rather than waiting for a tab change.
            RecallDocumentBrush();
        }
    }

    /// <summary>
    /// Write the working brush onto the document, so reopening it hands the
    /// brush back.
    /// </summary>
    /// <remarks>
    /// Called on stroke commit rather than on save: the case this exists for
    /// is a session that ended without one, and a bookmark that only survives
    /// a deliberate save is no use to somebody who closed the laptop.
    /// </remarks>
    private void RememberDocumentBrush()
    {
        if (BrushScope != BrushScope.PerProject) return;
        if (ProjectDocker.Project is not { } project) return;
        project.Manifest.Brush = _brushes.Brush.Clone();
    }

    /// <summary>Put the document's remembered brush back in the tool bar, if it has one.</summary>
    /// <remarks>
    /// Silent when the document has no brush recorded — an older file, or one
    /// made under Global — because the alternative is resetting the artist's
    /// brush to a default every time they open something, which is worse than
    /// the problem this solves.
    /// </remarks>
    private void RecallDocumentBrush()
    {
        if (BrushScope != BrushScope.PerProject) return;
        if (ProjectDocker.Project?.Manifest.Brush is not { } remembered) return;
        _brushes.Brush = remembered.Clone();
        // The preset combo would otherwise still name whatever was chosen
        // before the switch, describing a brush that is no longer loaded.
        _brushes.Applying(() =>
        {
            SelectedBrushPreset = null;
        });
        NotifyBrushProperties();
    }

    /// <summary>The animation tab a save/AI call should target (a reference tab defers to its owner).</summary>
    public DocumentTab? SaveTargetTab => ActiveTab?.Kind switch
    {
        // A project sheet has no file of its own any more than a symbol does —
        // the project's save writes it — so a view onto one defers to nothing.
        DocumentTabKind.Reference when ActiveTab.SheetSource is not null => null,
        DocumentTabKind.Reference => ActiveTab.Owner ?? ActiveTab,
        // A symbol has no file of its own — it is written by the project's
        // save. Offering Save As on one would produce a document nothing
        // references.
        DocumentTabKind.Symbol => null,
        _ => ActiveTab,
    };

    /// <summary>
    /// The tab a document-scoped operation acts on, or null when nothing is open.
    /// </summary>
    /// <remarks>
    /// Every caller of this used to end in <c>?? Tabs[0]</c>. That fallback was
    /// only ever reached on a symbol tab — where <see cref="SaveTargetTab"/> is
    /// deliberately null — and it was safe because the application could not be
    /// empty. It can now, and <c>Tabs[0]</c> throws there, so the fallback has to
    /// become a question rather than an assumption. Each caller answers it the
    /// same way: do nothing, because the surface that reached it is disabled with
    /// no document open.
    /// </remarks>
    private DocumentTab? TargetTab => SaveTargetTab ?? Tabs.FirstOrDefault();

    /// <summary>Timeline is hidden on reference tabs regardless of the View-menu toggle.</summary>
    public bool ShowTimeline => TimelineVisible && ActiveTab?.Kind != DocumentTabKind.Reference;


    [RelayCommand]
    private void ActivateTab(DocumentTab tab) => ActiveTab = tab;

    private void AttachEditor(DocumentEditor editor)
    {
        _clock.Stop();
        IsPlaying = false;
        StopAudio();
        _strokeBuilder.Cancel();
        _live.ClearEffectState();
        _editor.Changed -= OnDocumentChanged;
        _editor = editor;
        _editor.Changed += OnDocumentChanged;
        // The History docker follows the active document the way everything
        // else here does — through this funnel, not by watching tabs.
        UndoHistory.Attach(editor);
        // And the Edit menu's two entries, which read the same stack. A tab
        // switch brings a different one, and nothing raises Changed on the way
        // in — so without this the menu describes the document you just left.
        RefreshUndoRedo();
        RefreshCropAvailability();
        // B171. A selection describes *this* document's canvas, in that
        // document's coordinates, so it cannot follow the editor being swapped
        // out. Cleared here rather than in each caller because this is the
        // funnel every document change goes through — a new document, a tab
        // switch, an open, a close — and the previous bug was precisely that
        // four callers each cleared some of the per-document state and none of
        // them cleared this. A tab switch puts its own selection back
        // immediately afterwards; every other path wants the empty one.
        ClearSelectionState();
        // ClearFrameRenders subsumes the _cache.Clear() this used to be: it
        // empties the tile cache alongside it, through the one funnel.
        //
        // Keeping the incoming document's frame bitmaps across the swap (B362).
        // _editor is already the document being switched to by this line, so
        // this asks what that document can still use. Measured before the
        // change: every crossing rebuilt 4,147,200 bytes at 1080p, inside the
        // switch, however briefly you had been away — which is the delay the
        // owner reported between clicking a tab and seeing it.
        ClearFrameRenders(keepFramesOf: _editor.Doc.Scene);
        // The bakes hold bitmaps folded from the old document's cache; the
        // keys would miss anyway, but a document switch should not keep two
        // document-sized bitmaps of a scene nobody is looking at.
        _stackBake.Reset();
        _allThumbsDirty = true;
        ClearPlaybackRange();
        OnDocumentChanged();
    }

    /// <summary>
    /// The document the app opens on. It used to come from
    /// <c>CreateDoc()</c> with no paper colour, which produced a document
    /// whose scene declared white paper while no layer supplied it: the canvas
    /// and the layer thumbnail both showed the transparency checkerboard, and
    /// there was nothing called Background to lock. It is now made the same
    /// way File → New makes one, from the scene's own default.
    /// </summary>
    private static Doc StartupDoc() =>
        DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);

    /// <summary>Whether an artist could put a mark on this layer right now.</summary>
    /// <remarks>
    /// <b>The same question <c>CanEdit</c> asks before every mark (B357)</b>,
    /// and asking it differently here is what put the caret on a locked layer:
    /// "paintable" used to mean only <em>not the paper</em>, so a document
    /// whose first real layer was locked — or hidden, or inside a locked
    /// folder — opened with that layer selected and the first stroke went
    /// nowhere. <c>Scene.IsLayerEditable</c> is what accounts for the folder,
    /// which is why this defers to it rather than reading <c>Locked</c>.
    /// </remarks>
    private static bool Paintable(Doc doc, Layer layer) =>
        !layer.IsBackground && doc.Scene.IsLayerVisible(layer) && doc.Scene.IsLayerEditable(layer);

    /// <summary>Index of the first layer an artist can actually draw on.</summary>
    /// <remarks>
    /// Falls back twice rather than once. With nothing paintable at all — every
    /// layer locked, hidden, or paper — the caret goes to the first layer that
    /// is at least not the paper, so it is somewhere an artist recognises and
    /// <c>CanEdit</c> gets to say <em>why</em> the mark did not land. Returning
    /// the paper instead would trade one silent failure for another.
    /// </remarks>
    private static int FirstPaintableLayer(Doc doc)
    {
        var layers = doc.Scene.Layers;
        if (layers.FindIndex(l => Paintable(doc, l)) is var paintable && paintable >= 0) return paintable;
        return layers.FindIndex(l => !l.IsBackground) is var loose && loose >= 0 ? loose : 0;
    }

    /// <summary>Create a document from the File → New dialog in a new tab.</summary>
    /// <summary>
    /// Whether the only thing open is an untouched, unsaved, blank document.
    /// </summary>
    /// <remarks>
    /// The start screen sits over one of these, which is what lets Create on
    /// the document tab reuse it instead of opening a second. Opening the app
    /// and pressing the default button must not leave two tabs, one of which
    /// you never asked for.
    /// </remarks>
    /// <remarks>
    /// <b>B99</b> split this question in two. "Untouched" means *nothing was
    /// drawn*, which is <see cref="DocumentTab.HasWorkToLose"/>; it is no longer
    /// <c>IsDirty</c>, because a never-saved document badges from the moment it
    /// exists and would make every blank document look touched.
    /// </remarks>
    public bool OnlyAnUntouchedBlankDocument =>
        Tabs.Count == 1 && Tabs[0].FilePath is null && !Tabs[0].HasWorkToLose;

    /// <summary>
    /// Whether anything is open at all. The one question every document-scoped
    /// command and every docker asks before doing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backed by <see cref="Tabs"/> rather than by the editor, because the editor
    /// deliberately always has a document — see the constructor. A tab is the
    /// thing an artist opened; the placeholder behind it is an implementation
    /// detail that must never become visible, and asking `Tabs` is what keeps
    /// that true.
    /// </para>
    /// <para>
    /// Raised whenever the collection changes, so the UI can bind to it directly.
    /// </para>
    /// </remarks>
    public bool HasDocument => Tabs.Count > 0;

    public void NewDocument(NewDocumentSettings settings) => NewDocument(settings, reuseBlank: false);

    /// <param name="reuseBlank">
    /// Apply the settings to the blank document already on screen rather than
    /// adding a tab, when that is all there is. Only ever true from the start
    /// screen, where a document tab is already open behind it.
    /// </param>
    public void NewDocument(NewDocumentSettings settings, bool reuseBlank)
    {
        if (reuseBlank && OnlyAnUntouchedBlankDocument)
        {
            ReplaceOnlyTab(settings);
            return;
        }
        var doc = DocumentFactory.CreateDoc(
            settings.Width, settings.Height, settings.Fps,
            settings.TransparentBackground ? null : settings.BackgroundColor);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;

        // Apply feature defaults based on project type if a project is open
        ApplyFeatureDefaults(doc);
        var fresh = new DocumentTab(new DocumentEditor(doc), settings.Name);
        // Land on something paintable. The paper is layer 0 and locked, so
        // selecting it would make the very first stroke bounce.
        fresh.State.LayerIndex = FirstPaintableLayer(doc);
        AddTab(fresh);
        // B99. A document made while a project is open belongs to that project:
        // it gets a row, marked not saved yet, and a project save writes it.
        // Without a Source it was in limbo — no manifest entry, no row, and
        // skipped by SaveProject, which writes only tabs that have one.
        //
        // After AddTab rather than before, and the ordering is load-bearing:
        // adopting announces a project change, which marks the *active* tab's
        // document edited. With `fresh` already active that lands on the right
        // document, and `Source` still being null at that instant costs nothing
        // because adopting has already put the id in the docker's dirty set.
        fresh.Source = ProjectDocker.AdoptNewDocument(settings.Name, doc);
        // And the guides its folder declares, fitted to this paper (Q181).
        // After adoption because that is what decides which folder it is in —
        // there is no scope to resolve until the document has a home.
        ApplyScopedGuides(doc, fresh.Source);
        // The kind of work chosen at creation is a reason to offer that kind's
        // panels — offered, not imposed, which is why it is a choice on the
        // dialog and defaults to leaving the arrangement alone.
        if (settings.Workspace == WorkspaceChoice.ProjectDefaults)
        {
            Workspace.UseDefaultFor(settings.ProjectType);
        }
    }

    /// <summary>Make the one open blank document be the one that was asked for.</summary>
    private void ReplaceOnlyTab(NewDocumentSettings settings)
    {
        var doc = DocumentFactory.CreateDoc(
            settings.Width, settings.Height, settings.Fps,
            settings.TransparentBackground ? null : settings.BackgroundColor);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;

        var tab = Tabs[0];
        tab.Editor = new DocumentEditor(doc) { MaxUndo = tab.Editor.MaxUndo };
        tab.Title = settings.Name;
        tab.State.LayerIndex = FirstPaintableLayer(doc);
        // B67. A different document in the same tab, so the framing the blank
        // one was left at is not this one's. Same reasoning as the tab switch —
        // it is the *document* the view belongs to, not the slot.
        tab.State.View = null;
        // Attached directly, not through ActivateTab: the tab is already the
        // active one, so the property setter sees no change and the view model
        // would keep pointing at the editor that was just replaced.
        AttachEditor(tab.Editor);
        ActiveLayerIndex = FirstPaintableLayer(doc);
        CurrentFrameIndex = 0;
        // Nothing to put down — the record it belonged to is gone.
        TabSwitched?.Invoke(null, tab);
        if (settings.Workspace == WorkspaceChoice.ProjectDefaults)
        {
            Workspace.UseDefaultFor(settings.ProjectType);
        }
    }

    // ---- editing the preset you are on -----------------------------------------

    /// <summary>
    /// Whether the working brush has drifted from the preset it came from.
    /// </summary>
    /// <remarks>
    /// The tool bar's small dot. Without it the state is genuinely ambiguous:
    /// the picker says "Pencil", the brush has been nudged four times, and
    /// nothing on screen distinguishes that from the pencil as shipped — so an
    /// artist either loses the tweaks or saves a duplicate to be safe.
    /// </remarks>
    public bool BrushIsModified =>
        SelectedBrushPreset is { } preset && !BrushComparison.SameMark(preset.Settings, CurrentToolSettings);

    /// <summary>Small enough to sit next to the picker, loud enough to notice.</summary>
    public string BrushModifiedBadge => BrushIsModified ? "●" : "";

    public string BrushModifiedTip => BrushIsModified
        ? $"Changed from “{SelectedBrushPreset?.Name}”. The change stays with this brush while you use " +
          $"others and after a restart. Update it, save it as a new brush, or pick “{SelectedBrushPreset?.Name}” " +
          "again to get the saved one back."
        : "";

    // ---- what a preset has been nudged to (B71) ---------------------------------

    /// <summary>
    /// Record the working settings against the preset they drifted from, or
    /// forget them when they no longer differ.
    /// </summary>
    /// <remarks>
    /// Called on every persist and at the moment of leaving a preset. The first
    /// is what makes a tweak survive a restart, the second is what makes it
    /// survive the switch — and both are needed, because not every edit path
    /// persists (the curve editor and the tip picker write straight to the
    /// settings). Comparing by value rather than remembering a "touched" flag
    /// keeps the same rule the dot uses: a nudge put back is not a tweak.
    /// </remarks>
    private void StashTweak(BrushPreset? preset)
    {
        if (preset is null) return;
        if (BrushComparison.SameMark(preset.Settings, CurrentToolSettings))
        {
            _brushes.Tweaks.Remove(preset.Id);
        }
        else
        {
            _brushes.Tweaks[preset.Id] = CurrentToolSettings.Clone();
        }
    }

    /// <summary>The settings a preset should come on with: its tweak if it has one, else itself.</summary>
    private BrushSettings SettingsToApply(BrushPreset preset) =>
        _brushes.Tweaks.TryGetValue(preset.Id, out var tweak) ? tweak.Clone() : preset.Settings.Clone();

    /// <summary>Can the current preset be updated in place?</summary>
    public bool CanUpdateBrushPreset => SelectedBrushPreset is not null && BrushIsModified;

    /// <summary>
    /// Write the working brush back over the preset it came from.
    /// </summary>
    /// <remarks>
    /// A built-in is updated by <em>shadowing</em> it — a user preset that
    /// reuses its id, which the merge prefers. So the change persists, and
    /// <see cref="RevertBrushPreset"/> can uncover the original by deleting
    /// the shadow. Editing the shipped list in place would have no way back.
    /// </remarks>
    public bool UpdateSelectedPreset()
    {
        if (SelectedBrushPreset is not { } preset) return false;

        var updated = new BrushPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Settings = CurrentToolSettings.Clone(),
            TipPng = preset.TipPng,
            Tags = preset.Tags is null ? null : [.. preset.Tags],
        };

        _brushes.UserPresets.RemoveAll(p => p.Id == preset.Id);
        _brushes.UserPresets.Add(updated);
        ReplaceInChoices(preset, updated);
        PersistBrushState();
        return true;
    }

    /// <summary>True when the selected preset is a built-in that has been overwritten.</summary>
    public bool CanRevertBrushPreset =>
        SelectedBrushPreset is { IsBuiltIn: true } preset && _brushes.UserPresets.Any(p => p.Id == preset.Id);

    /// <summary>Delete the shadow over a built-in, uncovering what shipped.</summary>
    public bool RevertBrushPreset()
    {
        if (SelectedBrushPreset is not { IsBuiltIn: true } preset) return false;
        if (_brushes.UserPresets.RemoveAll(p => p.Id == preset.Id) == 0) return false;

        var original = BuiltInPresets.Create().FirstOrDefault(p => p.Id == preset.Id);
        if (original is null) return false;

        ReplaceInChoices(preset, original);
        _brushes.Applying(() =>
        {
            SelectedBrushPreset = original;
        });
        // Apply it, or the tool bar would show the shipped brush's name over
        // the edited brush's settings and the dot would say "unchanged".
        OnSelectedBrushPresetChanged(original);
        PersistBrushState();
        return true;
    }

    /// <summary>Rename a preset the artist made. Built-ins keep their names.</summary>
    public bool RenamePreset(BrushPreset preset, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || preset.IsBuiltIn) return false;
        preset.Name = trimmed;
        // The list is bound to the objects, so nudge it into re-reading them.
        ReplaceInChoices(preset, preset);
        PersistBrushState();
        return true;
    }

    /// <summary>
    /// Remove a preset. A built-in is reverted rather than removed — it is not
    /// the artist's to delete, and "delete" on one plainly means "give me back
    /// the one that shipped".
    /// </summary>
    public bool DeletePreset(BrushPreset preset)
    {
        if (preset.IsBuiltIn) return RevertBrushPreset();
        if (_brushes.UserPresets.RemoveAll(p => p.Id == preset.Id) == 0) return false;
        _brushes.Tweaks.Remove(preset.Id);

        var at = BrushPresetChoices.IndexOf(preset);
        if (at >= 0) BrushPresetChoices.RemoveAt(at);
        if (SelectedBrushPreset?.Id == preset.Id)
        {
            _brushes.Applying(() =>
            {
                SelectedBrushPreset = null;
            });
        }
        RefreshTagChoices();
        NotifyPresetProperties();
        PersistBrushState();
        return true;
    }

    /// <summary>
    /// Remove several presets at once. Returns how many went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a loop over <see cref="DeletePreset"/>, and the difference is the point of the
    /// method.</b> Each single delete persists the whole store, refreshes the tag list and
    /// raises five property notifications; clearing an imported collection of fifty-six that
    /// way writes the file fifty-six times and rebuilds the tag list fifty-six times, on the
    /// UI thread, which is the same shape of stall as the import that put them there.
    /// </para>
    /// <para>
    /// Built-ins are skipped rather than reverted. On a single delete "give me back the one
    /// that shipped" is the obvious reading of the button; inside a multi-selection it is
    /// not — somebody clearing a folder of imports did not ask for a shipped brush to be
    /// silently restored to factory settings on the way past.
    /// </para>
    /// </remarks>
    public int DeletePresets(IEnumerable<BrushPreset> presets)
    {
        var ids = presets.Where(p => !p.IsBuiltIn).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return 0;

        var removed = _brushes.UserPresets.RemoveAll(p => ids.Contains(p.Id));
        if (removed == 0) return 0;
        foreach (var id in ids) _brushes.Tweaks.Remove(id);

        for (var i = BrushPresetChoices.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(BrushPresetChoices[i].Id)) BrushPresetChoices.RemoveAt(i);
        }

        if (SelectedBrushPreset is { } selected && ids.Contains(selected.Id))
        {
            _brushes.Applying(() =>
            {
                SelectedBrushPreset = null;
            });
        }

        RefreshTagChoices();
        NotifyPresetProperties();
        PersistBrushState();
        return removed;
    }

    // ---- tags -------------------------------------------------------------------

    /// <summary>Every tag any preset carries, in use order. What the picker filters by.</summary>
    public ObservableCollection<string> BrushTagChoices { get; } = [];

    /// <summary>Set the tags on a preset. Built-ins can be tagged too — by shadowing.</summary>
    public bool SetPresetTags(BrushPreset preset, IEnumerable<string> tags)
    {
        var cleaned = CleanTags(tags);

        if (preset.IsBuiltIn && _brushes.UserPresets.All(p => p.Id != preset.Id))
        {
            // Filing a shipped brush is an edit like any other, so it goes
            // through the same shadow rather than mutating the list Create()
            // rebuilds from scratch every launch.
            var shadow = new BrushPreset
            {
                Id = preset.Id,
                Name = preset.Name,
                Tool = preset.Tool,
                Settings = preset.Settings.Clone(),
                TipPng = preset.TipPng,
                Tags = cleaned,
            };
            _brushes.UserPresets.Add(shadow);
            ReplaceInChoices(preset, shadow);
            if (SelectedBrushPreset?.Id == preset.Id)
            {
                _brushes.Applying(() =>
                {
                    SelectedBrushPreset = shadow;
                });
            }
        }
        else
        {
            preset.Tags = cleaned;
        }

        RefreshTagChoices();
        PersistBrushState();
        return true;
    }

    private static List<string>? CleanTags(IEnumerable<string>? tags)
    {
        var cleaned = (tags ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Null rather than empty, so a preset nobody filed writes no key.
        return cleaned.Count == 0 ? null : cleaned;
    }

    private void RefreshTagChoices()
    {
        var seen = BrushPresetChoices
            .SelectMany(p => p.Tags ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BrushTagChoices.Clear();
        foreach (var tag in seen) BrushTagChoices.Add(tag);
    }

    private void ReplaceInChoices(BrushPreset old, BrushPreset replacement)
    {
        var at = BrushPresetChoices.IndexOf(old);
        if (at < 0)
        {
            BrushPresetChoices.Add(replacement);
        }
        else
        {
            // Removing and re-inserting rather than assigning, so a bound list
            // re-reads the row even when the object is the same one renamed.
            BrushPresetChoices.RemoveAt(at);
            BrushPresetChoices.Insert(at, replacement);
        }

        if (SelectedBrushPreset?.Id == replacement.Id)
        {
            _brushes.Applying(() =>
            {
                SelectedBrushPreset = replacement;
            });
        }
        NotifyPresetProperties();
    }

    private void NotifyPresetProperties()
    {
        OnPropertyChanged(nameof(BrushIsModified));
        OnPropertyChanged(nameof(BrushModifiedBadge));
        OnPropertyChanged(nameof(BrushModifiedTip));
        OnPropertyChanged(nameof(CanUpdateBrushPreset));
        OnPropertyChanged(nameof(CanRevertBrushPreset));
    }

    /// <summary>Add imported presets (from .abr/.gbr/.gih/.kpp) and persist them.</summary>
    public int AddImportedPresets(IEnumerable<BrushPreset> presets)
    {
        var added = 0;
        foreach (var preset in presets)
        {
            _brushes.UserPresets.Add(preset);
            BrushPresetChoices.Add(preset);
            added++;
        }
        if (added > 0) PersistBrushState();
        return added;
    }

    /// <summary>
    /// Import brush files (.abr/.gbr/.gih/.kpp) into presets, on this thread.
    /// </summary>
    /// <remarks>
    /// Kept for callers with a handful of files and no window to hold — the MCP surface and
    /// the tests. <b>Anything an artist starts should use
    /// <see cref="ImportBrushFilesAsync"/></b>: the reading is what made the window stop
    /// answering the compositor on a fifty-six brush collection, and this overload does it
    /// right here.
    /// </remarks>
    public (int Added, int Failed) ImportBrushFiles(IEnumerable<(string Name, byte[] Bytes)> files)
    {
        var outcome = BrushImportJob.Read(files.ToList());
        var added = AddImportedPresets(outcome.Presets);
        AiStatus = BrushImportJob.Summarise(outcome);
        return (added, outcome.Unreadable.Count);
    }

    /// <summary>
    /// Import brush files off the UI thread, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reading runs on a worker; only the two steps that touch bound state — adding to
    /// <c>BrushPresetChoices</c> and persisting — happen back here, once, when it is done.
    /// That is the whole fix for the reported "the main window became transparent as if about
    /// to crash": nothing was crashing, the UI thread was simply inside a parser for several
    /// seconds and had stopped painting.
    /// </para>
    /// <para>
    /// <b>Cancellable, because an import of the wrong folder is a real mistake to make.</b>
    /// Giving up keeps the brushes already read rather than throwing them away — they are
    /// what the artist would have got if they had picked fewer files, and discarding them
    /// would make the cancel button cost work rather than save it.
    /// </para>
    /// </remarks>
    public async Task<(int Added, BrushImportOutcome Outcome)> ImportBrushFilesAsync(
        IReadOnlyList<(string Name, byte[] Bytes)> files,
        IProgress<BrushImportProgress>? progress = null,
        CancellationToken cancel = default)
    {
        var outcome = await Task.Run(() => BrushImportJob.Read(files, progress, cancel), cancel)
            .ConfigureAwait(true);

        var added = AddImportedPresets(outcome.Presets);
        AiStatus = BrushImportJob.Summarise(outcome);
        return (added, outcome);
    }

    /// <summary>
    /// Presets grouped by what they cost, stably within each group.
    /// </summary>
    /// <remarks>
    /// <c>OrderBy</c> is a stable sort in LINQ, so a brush never moves
    /// relative to its neighbours of the same cost — the list an artist has
    /// learned the shape of stays learnable.
    /// </remarks>
    private static IEnumerable<BrushPreset> Ordered(IEnumerable<BrushPreset> presets) =>
        presets.OrderBy(p => p.Cost);

    private void PersistBrushState()
    {
        // The brush in hand is the one tweak that is otherwise only stashed on
        // leaving it, and a session that ends without leaving it is exactly the
        // one B71 was filed about.
        StashTweak(SelectedBrushPreset);
        PresetStore.Save(new PresetStore.State
        {
            UserPresets = _brushes.UserPresets,
            LastBrushPresetId = SelectedBrushPreset?.Id,
            LastBrush = _brushes.Brush.Clone(),
            LastEraser = _brushes.Eraser.Clone(),
            // Null rather than empty, so a store with nothing nudged carries no
            // key — the same rule the palette and the project brush follow.
            Tweaks = _brushes.Tweaks.Count == 0 ? null : new Dictionary<string, BrushSettings>(_brushes.Tweaks),
            SmoothingMode = _appStabilisation.Mode.ToString(),
            SmoothingWindow = _appStabilisation.Window,
            SmoothingStrength = _appStabilisation.Strength,
            LazyRadius = _appStabilisation.LazyRadius,
        }, BrushStorePath);
    }

    private void LoadBrushState()
    {
        var state = PresetStore.Load(BrushStorePath);
        foreach (var preset in state.UserPresets) _brushes.UserPresets.Add(preset);
        // Fast brushes first, expressive ones after, each group keeping the
        // order it was declared in. The badge marks them individually; the
        // grouping is what makes the two kinds legible as kinds — an artist
        // scanning for something cheap should not have to read every row.
        foreach (var preset in Ordered(BuiltInPresets.Merge(state.UserPresets)))
        {
            BrushPresetChoices.Add(preset);
        }
        RefreshTagChoices();
        // Only for presets that still exist: a tweak whose brush was deleted
        // by another session, or by a build that dropped a shipped brush, would
        // otherwise sit in the file forever with nothing to apply it to.
        foreach (var (id, tweak) in state.Tweaks ?? [])
        {
            if (BrushPresetChoices.Any(p => p.Id == id)) _brushes.Tweaks[id] = tweak;
        }
        if (state.LastBrush is not null) _brushes.Brush = state.LastBrush.Clone();
        else _brushes.Brush = new BrushSettings { Size = 6, Hardness = 0.8 };
        if (state.LastEraser is not null) _brushes.Eraser = state.LastEraser.Clone();
        if (Enum.TryParse<SmoothingMode>(state.SmoothingMode, out var mode)) _appStabilisation.Mode = mode;
        if (state.SmoothingWindow is { } window) _appStabilisation.Window = Math.Clamp(window | 1, 3, 25);
        if (state.SmoothingStrength is { } strength) _appStabilisation.Strength = Math.Clamp(strength, 0, 0.95);
        if (state.LazyRadius is { } radius) _appStabilisation.LazyRadius = Math.Clamp(radius, 4, 200);
        // Restore the selection WITHOUT re-applying the preset (the working
        // settings above already carry the user's last tweaks on top of it).
        _brushes.Applying(() =>
        {
            SelectedBrushPreset = BrushPresetChoices.FirstOrDefault(p => p.Id == state.LastBrushPresetId);
        });
    }
}
