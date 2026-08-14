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
    // ---- character sheets -----------------------------------------------------

    /// <summary>
    /// The sheets the artist can consult right now — fresh list so the docker
    /// re-reads.
    /// </summary>
    /// <remarks>
    /// <b>Q25 re-answered:</b> two sources, switched by context. A document in
    /// a project sees the project's sheets — the ones filed on its folder's
    /// ancestry plus the project-wide ones, via <see cref="ProjectSheets.VisibleTo"/> —
    /// and a standalone document keeps the sheets inside it. A project sheet
    /// tab itself (no <c>Source</c>) lists every sheet in the project, the way
    /// a loose file alongside a project does.
    /// </remarks>
    public IReadOnlyList<ReferenceSheet> ReferenceSheetsView
    {
        get
        {
            var tab = SaveTargetTab ?? ActiveTab;
            if (ProjectDocker.Project is { } project
                && (tab is null or { Source: not null } or { SheetSource: not null }))
            {
                return [.. ProjectSheets.VisibleTo(project.Manifest, tab?.Source)
                    .Select(r => ProjectSheets.Load(project, r))
                    .OfType<ReferenceSheet>()];
            }
            return (tab?.Doc ?? Doc).ReferenceSheets.ToList();
        }
    }

    /// <remarks>
    /// <para>
    /// These two write straight to <c>Doc.ReferenceSheets</c> rather than going through
    /// <c>DocumentEditor</c>, which is why they announce the edit themselves. That was already
    /// true of undo — adding a sheet is not undoable — and B31 gave it a second consequence: the
    /// encoded reference views are cached, and a document edit that does not reach
    /// <see cref="MarkDocumentEdited"/> is a document edit the cache never hears about.
    /// </para>
    /// <para>
    /// Harmless today, since both only add an <em>empty</em> container under a fresh id and
    /// there is nothing stale to serve. It is the day somebody adds "duplicate view", cloning an
    /// existing view's layers, that this path would quietly serve the original's picture — so
    /// the call goes in now, while it costs one line, rather than in the commit that would need
    /// to know to add it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A character sheet was created on a document that has no file behind it.
    /// </summary>
    /// <remarks>
    /// <b>B66.</b> A sheet lives in <c>Doc.ReferenceSheets</c>, so it is saved
    /// when its document is saved and nowhere otherwise — and on an untitled
    /// document that is nowhere at all. Reported as "character sheets are not
    /// saved to disk", which is true and is a property of the document rather
    /// than of the sheet. Q25 answered (a): sheets stay part of a document, and
    /// the fix is to make sure the document has somewhere to live rather than to
    /// give the sheet a file of its own.
    /// </remarks>
    public event Action? ReferenceSheetNeedsAFile;

    /// <summary>
    /// Whether a sheet created now would have nothing on disk behind it.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither is enough alone. <c>FilePath</c> is null
    /// for a document that has never been saved; <c>Source</c> is null for one
    /// that is not in a project. A document in a project is saved by the
    /// project, so it needs no prompt even before its first write — which is the
    /// "in a project, a character sheet is directly added" half of the report.
    /// </remarks>
    public bool AReferenceSheetWouldBeUnsaved =>
        TargetTab is { FilePath: null, Source: null };

    /// <param name="name">
    /// What to call it. Null keeps the old numbered default, which is what the
    /// existing callers pass and what B65's rule says a *new* surface should
    /// stop doing — the prompt supplies a real name before anything is written.
    /// </param>
    /// <returns>
    /// The sheet, or null when nothing is open — a sheet belongs to a document
    /// and there is no document to put it in.
    /// </returns>
    public ReferenceSheet? AddReferenceSheet(string? name = null)
    {
        if (TargetTab is not { } target) return null;

        // Q25 re-answered: in a project the sheet is the project's, filed on
        // the top folder above this document, written by the project's save.
        // Not through the document's editor — the document does not change.
        if (target.Source is { } source && ProjectDocker.Project is { } project)
        {
            var filed = ProjectSheets.Add(
                project, name ?? "", ProjectSheets.DefaultScope(project.Manifest, source));
            ProjectDocker.Refresh();
            OnPropertyChanged(nameof(ReferenceSheetsView));
            return filed;
        }

        var needsAFile = AReferenceSheetWouldBeUnsaved;

        var sheet = new ReferenceSheet
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"Character {target.Doc.ReferenceSheets.Count + 1}"
                : name.Trim(),
        };
        // B98. Through the editor rather than around it, so adding a sheet is
        // one undoable step and moves the revision — which is what raises the
        // badge now. Mutating the list directly left the badge to be asserted
        // by hand, and an add that could not be undone.
        target.Editor.Perform(doc => doc.ReferenceSheets.Add(sheet));
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));

        // Announced after the sheet exists, not before: if the artist cancels the
        // save the work is still there to save later, which is the opposite of
        // creating nothing and telling them why.
        if (needsAFile) ReferenceSheetNeedsAFile?.Invoke();
        return sheet;
    }

    /// <inheritdoc cref="AddReferenceSheet"/>
    public void AddReferenceView(ReferenceSheet sheet)
    {
        // A project sheet is edited directly, the way a symbol is — there is
        // no owning document whose editor could record the step.
        if (ProjectSheetRefOf(sheet) is { } filed && ProjectDocker.Project is { } project)
        {
            var (width, height) = DefaultViewSize();
            var added = ReferenceView.Create($"view {sheet.Views.Count + 1}", width, height);
            sheet.Views.Add(added);
            project.DirtySheets.Add(filed.Id);
            OnPropertyChanged(nameof(ReferenceSheetsView));
            OpenReferenceView(added);
            return;
        }

        if (TargetTab is not { } target) return;
        var view = ReferenceView.Create($"view {sheet.Views.Count + 1}", Scene.Width, Scene.Height);
        target.Editor.Perform(_ => sheet.Views.Add(view));
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));
        OpenReferenceView(view);
    }

    /// <summary>The project registry entry behind a sheet, or null for a document's own.</summary>
    private SheetRef? ProjectSheetRefOf(ReferenceSheet sheet) =>
        ProjectDocker.Project is { } project
            ? ProjectSheets.FindRef(project.Manifest, sheet.Id)
            : null;

    /// <summary>Open a project sheet from its docker row — its first view, made if absent.</summary>
    /// <remarks>
    /// A sheet with no views yet gets one rather than a refusal: the double-click
    /// means "let me draw on this", and an empty sheet has nothing else it could
    /// mean. The view is project state, so it is queued for the next save the
    /// same way a stroke would be.
    ///
    /// Public for the project window's Open too, as OpenProjectDocument is.
    /// </remarks>
    public void OpenProjectSheet(SheetRef filed)
    {
        if (ProjectDocker.Project is not { } project) return;
        if (ProjectSheets.Load(project, filed) is not { } sheet)
        {
            AiStatus = $"“{filed.Name}” is missing from disk.";
            return;
        }
        if (sheet.Views.Count == 0)
        {
            var (width, height) = DefaultViewSize();
            sheet.Views.Add(ReferenceView.Create("view 1", width, height));
            project.DirtySheets.Add(filed.Id);
            OnPropertyChanged(nameof(ReferenceSheetsView));
        }
        OpenReferenceView(sheet.Views[0]);
    }

    /// <summary>The active scene's size, or the stock canvas when nothing is open.</summary>
    /// <remarks>
    /// A project sheet can be made and opened with no document open at all —
    /// the docker is on screen whenever a project is — so the size cannot be
    /// read from a tab that may not exist.
    /// </remarks>
    private (int Width, int Height) DefaultViewSize() =>
        Tabs.Count == 0 ? (960, 540) : (Scene.Width, Scene.Height);

    /// <summary>A sheet or view really was renamed in the docker.</summary>
    /// <remarks>
    /// <b>B95.</b> This used to be called from a <c>LostFocus</c> handler and to
    /// mark the document dirty unconditionally, so clicking into a name box and
    /// out again — typing nothing — raised the badge. Every other rename handler
    /// in the window guards; this one did not. The caller now compares the text
    /// and only calls when it actually changed, and the mark goes through the
    /// editor so the rename is undoable like any other edit.
    /// </remarks>
    public void MarkReferenceRenamed(object? renamed = null)
    {
        // A project sheet's rename dirties the sheet, not a document. The
        // renamed thing is the box's DataContext — a sheet, or a view inside
        // one — and the registry entry's name follows on the save that writes it.
        if (ProjectDocker.Project is { } project && SheetOf(renamed) is { } sheet
            && ProjectSheetRefOf(sheet) is { } filed)
        {
            project.DirtySheets.Add(filed.Id);
            OnPropertyChanged(nameof(ReferenceSheetsView));
            ProjectDocker.Refresh();
            return;
        }
        if (SaveTargetTab is { } tab) tab.Editor.Perform(_ => { });
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));
    }

    /// <summary>The sheet a rename touched: the sheet itself, or the one holding a view.</summary>
    private ReferenceSheet? SheetOf(object? renamed) => renamed switch
    {
        ReferenceSheet sheet => sheet,
        ReferenceView view => ReferenceSheetsView.FirstOrDefault(s => s.Views.Contains(view)),
        _ => null,
    };

    /// <summary>Redraw the sheet list without claiming anything changed.</summary>
    public void RefreshReferenceList() => OnPropertyChanged(nameof(ReferenceSheetsView));

    /// <summary>
    /// Tape a flattened copy of the view onto the canvas, or take it down —
    /// one strip per view, toggled. Returns the strip, or null when it was
    /// removed or there is nowhere to put one.
    /// </summary>
    /// <remarks>
    /// A <see cref="ReferenceStrip"/> rather than a layer, on purpose: the
    /// strip already renders over the paper and under every drawing, carries
    /// the opacity, scale and offset the request asked for, and holds the
    /// standing promise that a reference never reaches an exported pixel. A
    /// "temporary layer" would re-answer all four questions inside the layer
    /// stack, where every answer is harder (Q69).
    /// </remarks>
    public ReferenceStrip? ToggleViewOnCanvas(ReferenceView view)
    {
        if (TargetTab is not { } target) return null;
        var scene = target.Doc.Scene;

        if (scene.References?.FirstOrDefault(s => s.SheetViewId == view.Id) is { } taped)
        {
            target.Editor.Perform(doc => doc.Scene.References?.Remove(taped));
            Lightbox.Raster.ReferenceStripRegistry.Forget(taped.Id);
            AfterReferenceChange();
            return null;
        }

        var strip = new ReferenceStrip
        {
            Name = view.Name,
            Png = RenderReferenceViewPng(view),
            SheetWidth = view.Width,
            SheetHeight = view.Height,
            Cells = [new ReferenceCell { X = 0, Y = 0, Width = view.Width, Height = view.Height }],
            SheetViewId = view.Id,
            Pinned = true,
        };
        strip.Scale = FitScale(strip, scene);
        strip.CentreOn(scene.Width, scene.Height);

        var index = 0;
        target.Editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
        });
        Lightbox.Raster.ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return strip;
    }

    /// <summary>Whether this view is currently taped onto the canvas.</summary>
    public bool IsViewOnCanvas(ReferenceView view) =>
        (SaveTargetTab?.Doc ?? Doc).Scene.References
            ?.Any(s => s.SheetViewId == view.Id) == true;

    /// <summary>
    /// Guards <see cref="RefreshLinkedReferenceStrips"/> against the edits it
    /// makes itself announcing back into it.
    /// </summary>
    private bool _refreshingLinkedStrips;

    /// <summary>
    /// Re-flatten every taped-up view whose picture no longer matches its
    /// strip — the "live" half of Q69's decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from the edit funnel, so it runs on every document edit and has
    /// to be cheap when there is nothing to do — the common case is no linked
    /// strip and costs one null check. With a linked strip, the re-flatten
    /// re-reads per-layer bitmaps that are already cached and pays one PNG
    /// encode at the view's authored size, per edit, per linked strip. That
    /// is the price of "live" and it was chosen knowingly over a snapshot
    /// with a refresh button; the string compare below keeps edits that did
    /// not change the picture from re-registering identical bytes.
    /// </para>
    /// <para>
    /// Deliberately not an <c>Editor.Perform</c>: the strip's pixels are
    /// derived from the view, so undoing the drawing already restores them —
    /// this refresh re-runs on the undo's own funnel pass. A second undo step
    /// for the derived copy would make the artist undo everything twice.
    /// </para>
    /// </remarks>
    private void RefreshLinkedReferenceStrips()
    {
        if (_refreshingLinkedStrips) return;
        var doc = SaveTargetTab?.Doc ?? Doc;
        if (doc.Scene.References is not { Count: > 0 } strips) return;

        _refreshingLinkedStrips = true;
        try
        {
            var views = doc.ReferenceSheets.SelectMany(s => s.Views).ToDictionary(v => v.Id);
            var changed = false;
            foreach (var strip in strips)
            {
                if (strip.SheetViewId is not { } viewId) continue;
                // A deleted view degrades like a missing video file: the last
                // pixels stand and the strip stops following anything.
                if (!views.TryGetValue(viewId, out var view)) continue;

                var png = RenderReferenceViewPng(view);
                if (png == strip.Png) continue;
                strip.Png = png;
                strip.SheetWidth = view.Width;
                strip.SheetHeight = view.Height;
                if (strip.Cells.Count == 1)
                {
                    strip.Cells[0].Width = view.Width;
                    strip.Cells[0].Height = view.Height;
                }
                Lightbox.Raster.ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
                changed = true;
            }
            if (changed)
            {
                NotifyReference();
                PublishSnapshot();
            }
        }
        finally
        {
            _refreshingLinkedStrips = false;
        }
    }

    /// <summary>Open (or focus) the tab editing a character-sheet view.</summary>
    public void OpenReferenceView(ReferenceView view)
    {
        if (Tabs.FirstOrDefault(t => t.View == view) is { } open)
        {
            ActiveTab = open;
            return;
        }

        // A project sheet's view follows the symbol arrangement: no owner tab,
        // because the sheet belongs to the project rather than to whichever
        // document happened to be active when it was opened.
        if (ProjectDocker.Project is { } project)
        {
            var filed = (project.Manifest.Sheets ?? [])
                .Select(r => (Ref: r, Sheet: project.LoadedSheets.GetValueOrDefault(r.Id)))
                .FirstOrDefault(x => x.Sheet?.Views.Contains(view) ?? false);
            if (filed.Sheet is { } projectSheet)
            {
                AddTab(new DocumentTab(
                    new DocumentEditor(WrapperFor(view)), $"{projectSheet.Name} / {view.Name}")
                {
                    Kind = DocumentTabKind.Reference,
                    View = view,
                    SheetSource = filed.Ref,
                });
                return;
            }
        }

        if (TargetTab is not { } owner) return;
        var sheet = owner.Doc.ReferenceSheets.FirstOrDefault(s => s.Views.Contains(view));
        var referenceTab = new DocumentTab(
            new DocumentEditor(WrapperFor(view)), $"{sheet?.Name ?? "Sheet"} / {view.Name}")
        {
            Kind = DocumentTabKind.Reference,
            Owner = owner,
            View = view,
        };
        // B98. The owner has to know about the view, because a stroke drawn here
        // moves *this* editor's revision while landing in the owner's document —
        // without the registration the owner never notices it was changed.
        owner?.Views.Add(referenceTab);
        AddTab(referenceTab);
    }

    /// <summary>
    /// A single-frame document around a view. The wrapper scene SHARES the
    /// view's layer list, so edits land in the sheet directly.
    /// </summary>
    private static Doc WrapperFor(ReferenceView view) => new()
    {
        Scene = new Scene
        {
            Name = view.Name,
            Width = view.Width,
            Height = view.Height,
            FrameCount = 1,
            Layers = view.Layers,
        },
    };

    /// <summary>Flatten one character-sheet view to PNG (for AI reference and MCP).</summary>
    /// <summary>Throw away encoded reference views — something in the document moved.</summary>
    private void InvalidateReferenceViewCache() => _referenceImages.Invalidate();

    /// <summary>
    /// Run the edit funnel, for a test that changes the model directly.
    /// </summary>
    /// <remarks>
    /// Some edits an artist makes to a sheet — hiding one of its layers, for instance — are
    /// property sets on the record rather than commands on this view model, and they reach the
    /// funnel through the UI that made them. A test poking the record has no UI, so it needs a
    /// way to say "and that was an edit" without a second copy of what an edit means.
    /// </remarks>
    internal void MarkDocumentEditedForTests() => MarkDocumentEdited();

    /// <summary>One view as base64 PNG, at the size it was drawn.</summary>
    /// <remarks>
    /// <b>Uncapped, and that is the contract.</b> This overload is what the MCP surface answers
    /// <c>render_reference_view</c> with, and an agent asking for a picture of a view should get
    /// the view — the 768 px cap belongs to an AI *request*, where it exists because providers
    /// bill by area. Capping here instead shrank the MCP reply as a side effect of B31 and was
    /// caught by <c>RenderReferenceView_ProducesDecodablePng</c>, which had asserted the
    /// authored width since the feature landed. The cap is applied at
    /// <see cref="EncodedReferenceView"/>, one call site, where the reason for it is true.
    /// </remarks>
    public string RenderReferenceViewPng(ReferenceView view) => RenderReferenceViewPng(view, 0);

    /// <summary>One view as base64 PNG, no wider or taller than <paramref name="longEdge"/>.</summary>
    /// <remarks>
    /// <paramref name="longEdge"/> of 0 or less means the authored size. Explicit rather than
    /// defaulted, so a new caller has to say which of the two it wants.
    /// </remarks>
    public string RenderReferenceViewPng(ReferenceView view, int longEdge) =>
        _referenceImages.Render(view, longEdge);


    /// <summary>Up to two rendered character-sheet views to ride along with AI requests.</summary>
    /// <remarks>
    /// Encoded once per view and reused until the document changes — see
    /// <see cref="_referenceViewPngs"/>. The first call after an edit pays; the ones after it
    /// do not, which is the whole of B31.
    /// </remarks>
    private IReadOnlyList<string>? CollectReferenceImages()
    {
        // ReferenceSheetsView rather than the document's own list, so a project
        // document rides with the sheets filed above it — which is the whole
        // point of filing them there — and a standalone one keeps its own.
        var views = ReferenceSheetsView
            .SelectMany(s => s.Views)
            .Where(v => v.Layers.Any(l => l.Visible))
            .Take(2)
            .Select(_referenceImages.Encoded)
            .ToList();
        return views.Count > 0 ? views : null;
    }

    /// <summary>The color docker's state, kept in sync with <see cref="ColorHex"/>.</summary>
    public ColorPickerViewModel ColorPicker { get; }

    /// <summary>The background half of the pair, with the same wheel and palette.</summary>
    public ColorPickerViewModel BackgroundPicker { get; }

    partial void OnColorHexChanged(string value)
    {
        OnPropertyChanged(nameof(ForegroundColorHex));
        ColorPicker.SetHex(value);
        if (_settingColorFromSwatch) return;

        // In swatch-edit mode the picker drives the selected swatch, which is
        // what makes dragging the wheel recolour the drawing live.
        if (PaletteDocker.ApplyPickerColor(value)) return;

        // Otherwise the artist has chosen a colour by some other means — the
        // wheel, the hex box, the eyedropper — and the link to the swatch has
        // to go. Keeping it would mean painting in one colour while recording
        // a reference to another, and the stroke would jump the moment anyone
        // touched the palette.
        ActiveSwatchId = null;
        ActivePaletteId = null;
    }

    /// <summary>
    /// Keep the background picker showing the background colour, however it
    /// changed — X swaps it, D resets it, and the wheel must not still be
    /// pointing at the colour it used to be.
    /// </summary>
    partial void OnBackgroundColorHexChanged(string value) => BackgroundPicker.SetHex(value);
}
