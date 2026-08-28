using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>Settings collected by the File → New dialog.</summary>
/// <param name="ProjectType">
/// What kind of work this is for, or null for <b>None</b> — a single file with
/// no project structure at all. Null is the honest default: the app is
/// document-first, and a picture that never becomes a project must not be made
/// to carry one. The type is used to offer that type's workspace, and is
/// recorded on the project when one is created.
/// </param>
/// <param name="Workspace">
/// What to do with the panel arrangement. Defaults to keeping whatever is on
/// screen, because the common case is making another document while working.
/// </param>
public sealed record NewDocumentSettings(
    string Name,
    int Width,
    int Height,
    int Fps,
    int Ppi,
    string BackgroundColor,
    bool TransparentBackground,
    Lightbox.Core.Projects.ProjectType? ProjectType = null,
    WorkspaceChoice Workspace = WorkspaceChoice.Keep);

/// <summary>What a New should do to the panels.</summary>
public enum WorkspaceChoice
{
    /// <summary>Leave the arrangement alone.</summary>
    Keep,

    /// <summary>Switch to the built-in workspace for the chosen project type.</summary>
    ProjectDefaults,
}

public enum DocumentTabKind
{
    Animation,
    Reference,

    /// <summary>A project symbol, opened to be drawn on.</summary>
    Symbol,
}

/// <summary>
/// One open document: its editor (which owns the undo history, so switching
/// tabs never loses it), where it was saved, and whether it has unsaved
/// changes (shown as a • in the tab strip).
///
/// A Reference tab edits a character-sheet view of its <see cref="Owner"/>
/// animation document: its editor wraps a scene that shares the view's layer
/// list, so edits land in the owning document (and dirty the owner, never
/// this tab).
/// </summary>
public sealed partial class DocumentTab : ObservableObject
{
    internal DocumentTab(DocumentEditor editor, string title)
    {
        Editor = editor;
        _title = title;
    }

    public DocumentTabKind Kind { get; init; } = DocumentTabKind.Animation;

    /// <summary>The animation tab whose document owns this reference view.</summary>
    /// <remarks>
    /// Null on a Reference tab whose sheet belongs to the <em>project</em>
    /// (<see cref="SheetSource"/>) — those follow the <see cref="Symbol"/>
    /// arrangement instead, because the sheet is not part of any document.
    /// </remarks>
    public DocumentTab? Owner { get; init; }

    /// <summary>The character-sheet view this tab edits (Reference tabs only).</summary>
    public ReferenceView? View { get; init; }

    /// <summary>
    /// The project sheet this tab's view belongs to, or null when the sheet
    /// lives inside a document (a standalone file's sheet, which keeps
    /// <see cref="Owner"/> instead).
    /// </summary>
    /// <remarks>
    /// The same job <see cref="Source"/> does for a document: it says which
    /// project slot the edits belong to, so the funnel can mark the sheet
    /// dirty and the project's save can write it. Exactly one of this and
    /// <see cref="Owner"/> is set on a Reference tab.
    /// </remarks>
    public Lightbox.Core.Projects.SheetRef? SheetSource { get; init; }

    /// <summary>
    /// The project symbol this tab edits (Symbol tabs only).
    /// </summary>
    /// <remarks>
    /// The same arrangement as <see cref="View"/>: the tab's editor wraps a
    /// scene whose cels hold the symbol's own frame objects, so a stroke lands
    /// in the symbol rather than in a copy of it. There is no
    /// <see cref="Owner"/> — a symbol belongs to the project, not to whichever
    /// animation happened to be open when it was made.
    /// </remarks>
    public Symbol? Symbol { get; init; }

    /// <summary>
    /// The layer in this tab's wrapper scene that <em>is</em> the symbol
    /// (Symbol tabs only).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A symbol holds one layer's drawing</b> — <c>Symbol.Frames</c> is the
    /// time axis and there is no layer axis (Q171). So the sync has to know
    /// which layer to read, and it cannot be "index 0": a paste inserts at the
    /// active index, so a second layer arriving there would silently make the
    /// pasted work the symbol and the artist's drawing an extra frame of it.
    /// </para>
    /// <para>
    /// By id rather than by reference, for the reason
    /// <c>_selectedPlacementId</c> is: undo replaces the wrapper's layer
    /// wholesale, and a reference kept across one points at an object the
    /// document no longer contains. <c>Layer.Clone</c> is a
    /// <c>MemberwiseClone</c>, so the id survives the round trip that the
    /// object does not.
    /// </para>
    /// <para>
    /// <b>This goes away when a symbol owns a layer stack</b>, which is Q171's
    /// answer and is not built. Until then it is what keeps layers from
    /// becoming frames.
    /// </para>
    /// </remarks>
    public string? SymbolLayerId { get; init; }

    internal DocumentEditor Editor { get; set; }

    public Doc Doc => Editor.Doc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    /// <summary>
    /// The editor revision this tab was last written to disk at, or −1 for a
    /// document that has never been written at all.
    /// </summary>
    /// <remarks>
    /// <b>B99.</b> Minus one rather than zero, and the distinction is the bug:
    /// a fresh document sits at revision 0, so a saved-at-zero default would
    /// make "never saved" indistinguishable from "saved and untouched". A new
    /// document has nothing on disk and must say so.
    /// </remarks>
    private long _savedRevision = -1;

    /// <summary>
    /// Reference tabs editing views of this document. Their edits land in this
    /// document, so this tab is dirty while any of them is.
    /// </summary>
    internal List<DocumentTab> Views { get; } = [];

    /// <summary>Whether this tab's own editor has moved since it was saved.</summary>
    private bool HasOwnChanges => _savedRevision != Editor.Revision;

    /// <summary>
    /// Whether this document differs from what is on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B98.</b> Derived, not assigned. It used to be a settable flag that
    /// each edit path raised for itself, which failed in both directions: paths
    /// that changed nothing still raised it (B79, B94, B95, B96), and no path
    /// could lower it when an edit was undone (B97). Deriving it from
    /// <see cref="DocumentEditor.Revision"/> means the question is answered by
    /// the edit record rather than by whoever remembered to ask.
    /// </para>
    /// <para>
    /// This is also what makes <b>Q24</b> structural. Brush settings are saved
    /// separately and must never feed this badge; now they cannot, because
    /// choosing a brush pushes no undo step. Nothing has to remember the rule.
    /// </para>
    /// </remarks>
    public bool IsDirty => Kind switch
    {
        // A reference tab is a view onto its owner's document, so it is dirty
        // exactly when the owner is. B95: the reporter found the badge on the
        // parent and not on the sheet, and an artist looking at the sheet
        // should not have to check another tab to learn there is unsaved work.
        DocumentTabKind.Reference => Owner?.IsDirty ?? false,
        // A symbol belongs to the project rather than to a file of its own, so
        // there is nothing for it to be out of step with; the project's save
        // writes it. Surfacing unsaved symbol work is a gap, but it belongs to
        // the project's dirty state rather than to this tab.
        DocumentTabKind.Symbol => false,
        // Own edits, or an edit made through any character-sheet view of this
        // document. A reference tab drives its own editor over a wrapper scene,
        // so the strokes land here while the revision moves there — asking each
        // view separately rather than summing avoids two views' movements
        // cancelling out.
        _ => HasOwnChanges || Views.Any(v => v.HasOwnChanges),
    };

    /// <summary>Whether this document has never been written to disk.</summary>
    public bool IsUnsaved => Kind switch
    {
        DocumentTabKind.Reference => Owner?.IsUnsaved ?? false,
        DocumentTabKind.Symbol => false,
        _ => _savedRevision < 0,
    };

    /// <summary>
    /// Whether closing this would lose work the artist did.
    /// </summary>
    /// <remarks>
    /// <b>B99,</b> and deliberately not the same question as
    /// <see cref="IsDirty"/>. The badge means *this differs from disk*, which a
    /// brand-new empty document does — it has no file at all. Prompting means
    /// *there is something to lose*, which an untouched new document has not,
    /// and File ▸ New followed by a close should not argue about it. Same split
    /// as B76's pending versus missing: worth knowing is not worth acting on.
    /// </remarks>
    public bool HasWorkToLose =>
        (Editor.Revision > 0 || Views.Any(v => v.Editor.Revision > 0)) && IsDirty;

    /// <summary>This document was written to disk as it currently stands.</summary>
    /// <remarks>
    /// Its views are marked too: their edits are part of this document, so a
    /// save that wrote them has written them.
    /// </remarks>
    public void MarkSaved()
    {
        foreach (var view in Views) view.SetSavedRevision(view.Editor.Revision);
        SetSavedRevision(Editor.Revision);
    }

    /// <summary>
    /// Re-read the dirty state after something may have changed it — an edit,
    /// an undo, or a save.
    /// </summary>
    /// <remarks>
    /// A derived property still has to say when it changed. Every mutation
    /// funnels through <c>MarkDocumentEdited</c> or a save, so this is called
    /// from there rather than by subscribing to the editor — a subscription
    /// would fire during a stroke, at pointer rate, for a badge that can only
    /// change once per stroke.
    /// </remarks>
    public void RefreshDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsUnsaved));
        OnPropertyChanged(nameof(HasWorkToLose));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    private void SetSavedRevision(long revision)
    {
        _savedRevision = revision;
        RefreshDirty();
        // A view's badge mirrors its owner's, so the owner has to re-announce.
        foreach (var view in Views) view.RefreshDirty();
    }

    /// <summary>
    /// Adopt another tab's saved point — for a reference or symbol tab, whose
    /// edits belong to a document it does not own.
    /// </summary>
    internal void AdoptSavedRevisionFrom(DocumentTab other)
    {
        _savedRevision = other._savedRevision;
        RefreshDirty();
    }

    [ObservableProperty]
    private bool _isActive;

    public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

    public string? FilePath { get; set; }

    /// <summary>
    /// The project slot this tab came from, or null for a standalone document.
    /// Alongside <see cref="FilePath"/> rather than instead of it: a project
    /// animation is saved by the project, a loose file by its own path, and
    /// Save has to know which it is looking at.
    /// </summary>
    /// <remarks>
    /// A full property because the tab strip's project badge derives from it,
    /// and a tab is adopted into a project mid-session (Save As into one) —
    /// an init would leave the badge answering the question at open time.
    /// </remarks>
    public Lightbox.Core.Projects.DocumentRef? Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged(nameof(IsProjectWork));
        }
    }

    private Lightbox.Core.Projects.DocumentRef? _source;

    /// <summary>
    /// Whether this tab's work belongs to the open project — the tab strip's
    /// P badge, so a loose file and a project animation are tellable apart
    /// before saving teaches the difference.
    /// </summary>
    /// <remarks>
    /// Any of the project slots counts: a document's <see cref="Source"/>, a
    /// project sheet's <see cref="SheetSource"/>, a <see cref="Symbol"/> (a
    /// symbol only exists in a project), or a reference view whose owning
    /// document is project work.
    /// </remarks>
    public bool IsProjectWork =>
        Source is not null || SheetSource is not null || Symbol is not null
        || Owner?.Source is not null;

    /// <summary>
    /// Where the artist was in this document — playhead, layer, reference and
    /// canvas framing — put down on the way out of the tab and picked up on the
    /// way back in.
    /// </summary>
    /// <remarks>
    /// <b>B67.</b> Was two loose ints (<c>SavedFrameIndex</c>,
    /// <c>SavedLayerIndex</c>) and grew a third and a fourth. One object so
    /// there is a single answer to "what belongs to a document rather than to
    /// the process", and so the next thing to join the list is a field rather
    /// than another pair of save/restore lines to forget.
    /// </remarks>
    internal DocumentScopedState State { get; } = new();
}
