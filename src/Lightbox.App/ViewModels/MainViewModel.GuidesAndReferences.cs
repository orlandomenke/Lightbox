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
    // ---- imported references ------------------------------------------------------

    // ---- guides -----------------------------------------------------------------

    /// <summary>The guides on this document, or an empty list.</summary>
    public IReadOnlyList<Guide> Guides => Scene.Guides ?? [];

    public bool HasGuides => Scene.HasGuides;

    /// <summary>
    /// Whether guides constrain what you draw right now.
    /// </summary>
    /// <remarks>
    /// A working state rather than a document property, the same side of the
    /// line as onion skin: the guides are authored and saved, but whether you
    /// are currently drawing against them is how you are working this minute.
    /// It survives the session through settings, not through the file.
    /// </remarks>
    [ObservableProperty]
    private bool _snapToGuides = true;


    partial void OnSnapToleranceChanged(double value)
    {
        if (Math.Abs(Settings.SnapTolerance - value) < 1e-9) return;
        Settings.SnapTolerance = value;
        Settings.Save();
    }

    /// <summary>
    /// The pitch a new grid is made with, in document pixels.
    /// </summary>
    /// <remarks>
    /// A preference, not document data: once a grid exists, its spacing lives
    /// on the guide, so changing this never moves a lattice somebody has
    /// already drawn against.
    /// </remarks>
    public double GridSpacing
    {
        get => Settings.GridSpacing;
        set
        {
            var clamped = Math.Clamp(value, 1, 4096);
            if (Math.Abs(Settings.GridSpacing - clamped) < 1e-9) return;
            Settings.GridSpacing = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>How many rays a new vanishing point is drawn with.</summary>
    /// <inheritdoc cref="GridSpacing" path="/remarks"/>
    public int VanishingPointRays
    {
        get => Settings.VanishingPointRays;
        set
        {
            var clamped = Math.Clamp(value, Guide.MinRays, Guide.MaxRays);
            if (Settings.VanishingPointRays == clamped) return;
            Settings.VanishingPointRays = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>How many heads tall a new character height scale is.</summary>
    /// <inheritdoc cref="GridSpacing" path="/remarks"/>
    public int HeightScaleHeads
    {
        get => Settings.HeightScaleHeads;
        set
        {
            var clamped = Math.Clamp(value, 1, 32);
            if (Settings.HeightScaleHeads == clamped) return;
            Settings.HeightScaleHeads = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>How much of the canvas height a new character height scale stands in.</summary>
    /// <inheritdoc cref="AppSettings.HeightScaleFill" path="/remarks"/>
    public double HeightScaleFill
    {
        get => Settings.HeightScaleFill;
        set
        {
            var clamped = Math.Clamp(value, 0.05, 1.0);
            if (Math.Abs(Settings.HeightScaleFill - clamped) < 1e-9) return;
            Settings.HeightScaleFill = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The grid guides on this document, if any.</summary>
    public IReadOnlyList<Guide> GridGuides =>
        Guides.Where(g => g.Kind == GuideKind.Grid).ToList();

    /// <summary>The character height scales on this document, if any.</summary>
    public IReadOnlyList<Guide> HeightScaleGuides =>
        Guides.Where(g => g.Kind == GuideKind.HeightScale).ToList();

    /// <summary>
    /// Change a placed grid's pitch, as one undoable step.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GridSpacing"/> on purpose. That is what the
    /// next grid will be; this reaches into one that exists, and only an
    /// explicit edit should ever do that.
    /// </remarks>
    public void SetGridSpacing(Guide guide, double spacing)
    {
        var clamped = Math.Clamp(spacing, 1, 4096);
        var before = guide.Spacing;
        if (Math.Abs(before - clamped) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Spacing = clamped, _ => guide.Spacing = before);
        NotifyGuidesView();
    }

    /// <summary>Change a placed grid's angle, as one undoable step.</summary>
    public void SetGridAngle(Guide guide, double angle)
    {
        var before = guide.Angle;
        if (Math.Abs(before - angle) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Angle = angle, _ => guide.Angle = before);
        NotifyGuidesView();
    }

    /// <summary>Turn a placed guide's drawing or snapping on or off, undoably.</summary>
    public void SetGuideFlags(Guide guide, bool visible, bool snaps)
    {
        var before = (guide.Visible, guide.Snaps);
        if (before == (visible, snaps)) return;
        _editor.PerformDelta(
            _ => { guide.Visible = visible; guide.Snaps = snaps; },
            _ => { guide.Visible = before.Visible; guide.Snaps = before.Snaps; });
        NotifyGuidesView();
    }




    /// <summary>Pin a placed guide in place, or let it go, undoably.</summary>
    public void SetGuideLocked(Guide guide, bool locked)
    {
        if (guide.Locked == locked) return;
        _editor.PerformDelta(_ => guide.Locked = locked, _ => guide.Locked = !locked);
        NotifyGuidesView();
    }

    // ---- the selected guide, and the numbers behind it ---------------------------
    //
    // A guide has always been draggable and never adjustable: the numbers that
    // decide what it *is* — a grid's pitch, a character's head count, how many
    // rays come out of a vanishing point — lived in a configuration window two
    // menus away, and half of them lived nowhere at all. The way in is the Move
    // tool, because picking a guide up and changing it are the same intention
    // and it is the tool already reaching for them; the options land in the
    // tool-options bar and docker, which the Move tool left empty.

    /// <summary>
    /// The guide the options are pointed at, by id, or null when the selection
    /// is empty or holds more than one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By id rather than by object or by position. Undo replaces the whole
    /// document, so the object under the selection is gone after one; the
    /// position shifts when a guide before it is removed. The id survives both,
    /// which is the reason <c>MainWindow.GuideById</c> already works this way.
    /// </para>
    /// <para>
    /// <b>B215: derived from <see cref="Selection"/> rather than stored beside
    /// it.</b> This used to be a second selection: the Move tool wrote here and
    /// the Arrow wrote into the manager, neither read the other, and the
    /// consequences were all real — the options bar stayed blank for a guide
    /// picked with the Arrow, and the manager's own group-move was unreachable
    /// with the tool that populated it. One selection cannot disagree with
    /// itself, which is the whole of the fix.
    /// </para>
    /// <para>
    /// Null on a multiple selection, and that is the honest answer rather than
    /// a shortcut: the options bar edits <em>a</em> guide's numbers — a grid's
    /// pitch, a head count — and there is no single guide for them to mean.
    /// The group is still perfectly movable; it is the numbers that need one.
    /// </para>
    /// </remarks>
    public string? SelectedGuideId =>
        Selection.SelectedGuideIds is { Count: 1 } ids ? ids.First() : null;

    /// <summary>The selection moved: repoint the options and repaint the rig.</summary>
    private void OnGuideSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedGuideId));
        NotifySelectedGuide();
        // The canvas draws the selected guides brightest, so a selection that
        // changed without a guide changing still has to reach it.
        GuidesChanged?.Invoke();
    }

    /// <summary>The selected guide, or null — including when its id no longer names one.</summary>
    public Guide? SelectedGuide =>
        SelectedGuideId is { Length: > 0 } id ? Guides.FirstOrDefault(g => g.Id == id) : null;

    public bool HasSelectedGuide => SelectedGuide is not null;

    /// <summary>
    /// Select a guide by id, or clear the selection with null.
    /// </summary>
    /// <param name="shift">Add to the selection rather than replacing it.</param>
    /// <param name="alt">Take this one out of the selection.</param>
    /// <remarks>
    /// The modifiers travel because both tools that reach guides now go through
    /// one press path (B215), and the Arrow's whole vocabulary is Shift to add
    /// and Alt to subtract. Clearing goes through the guide category alone
    /// rather than <c>ClearAllSelections</c>: a press that missed every guide
    /// has said nothing about the symbol or the anchor that may also be picked.
    /// </remarks>
    public void SelectGuide(string? id, bool shift = false, bool alt = false)
    {
        if (id is null)
        {
            Selection.ClearGuideSelection();
            return;
        }
        Selection.SelectGuideWithModifiers(id, shift, alt);
    }

    public bool SelectedGuideIsGrid => SelectedGuide?.Kind == GuideKind.Grid;

    public bool SelectedGuideIsHeightScale => SelectedGuide?.Kind == GuideKind.HeightScale;

    public bool SelectedGuideIsVanishingPoint => SelectedGuide?.Kind == GuideKind.VanishingPoint;

    /// <summary>
    /// Whether the selected guide has an angle worth showing.
    /// </summary>
    /// <remarks>
    /// A vanishing point has no angle — its directions depend on where you are
    /// standing — and a height scale stands up, by definition. Showing a dial
    /// that does nothing is worse than showing none.
    /// </remarks>
    public bool SelectedGuideHasAngle =>
        SelectedGuide?.Kind is GuideKind.Line or GuideKind.Grid or GuideKind.Isometric;

    /// <summary>What the selected guide is, for the panel's heading.</summary>
    public string SelectedGuideLabel => SelectedGuide switch
    {
        null => "No guide selected",
        { Name: { Length: > 0 } name } => name,
        { Kind: GuideKind.Line } g => Math.Abs(((g.Angle % 180) + 180) % 180 - 90) < 1
            ? "Vertical guide" : "Guide line",
        { Kind: GuideKind.Grid } => "Grid",
        { Kind: GuideKind.Isometric } => "Isometric axes",
        { Kind: GuideKind.VanishingPoint } => "Vanishing point",
        { Kind: GuideKind.HeightScale } => "Character height scale",
        _ => "Guide",
    };

    /// <summary>
    /// Move the selected guide to an exact place, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The typed half of the drag: dragging is how a guide is placed by eye and
    /// this is how it is placed by number — a horizon at exactly y=540, two
    /// vanishing points exactly as far outside the frame as each other.
    /// </remarks>
    public void SetGuidePosition(Guide guide, double x, double y)
    {
        if (guide.Locked) return;
        var before = (guide.X, guide.Y);
        if (Math.Abs(before.X - x) < 1e-9 && Math.Abs(before.Y - y) < 1e-9) return;
        _editor.PerformDelta(
            _ => { guide.X = x; guide.Y = y; },
            _ => { guide.X = before.X; guide.Y = before.Y; });
        NotifyGuidesView();
    }

    /// <summary>Change how many rays a vanishing point is drawn with, undoably.</summary>
    public void SetVanishingPointRays(Guide guide, int rays)
    {
        var clamped = Math.Clamp(rays, Guide.MinRays, Guide.MaxRays);
        var before = guide.Divisions;
        if (before == clamped) return;
        _editor.PerformDelta(_ => guide.Divisions = clamped, _ => guide.Divisions = before);
        NotifyGuidesView();
    }

    public double GuideX
    {
        get => SelectedGuide?.X ?? 0;
        set { if (SelectedGuide is { } g) SetGuidePosition(g, value, g.Y); }
    }

    public double GuideY
    {
        get => SelectedGuide?.Y ?? 0;
        set { if (SelectedGuide is { } g) SetGuidePosition(g, g.X, value); }
    }

    public double GuideAngle
    {
        get => SelectedGuide?.Angle ?? 0;
        set { if (SelectedGuide is { } g) SetGridAngle(g, value); }
    }

    /// <summary>A grid's cell, or one head of a height scale, in document pixels.</summary>
    public double GuideSpacing
    {
        get => SelectedGuide?.Spacing ?? 0;
        set
        {
            if (SelectedGuide is not { } g) return;
            if (g.Kind == GuideKind.HeightScale) SetHeightScale(g, value, g.Divisions ?? 1);
            else SetGridSpacing(g, value);
        }
    }

    public int GuideHeads
    {
        get => SelectedGuide?.Divisions ?? HeightScaleHeads;
        set { if (SelectedGuide is { } g) SetHeightScale(g, g.Spacing, value); }
    }

    public int GuideRays
    {
        get => SelectedGuide?.RayCount ?? VanishingPointRays;
        set { if (SelectedGuide is { } g) SetVanishingPointRays(g, value); }
    }

    public bool GuideVisible
    {
        get => SelectedGuide?.Visible ?? true;
        set { if (SelectedGuide is { } g) SetGuideFlags(g, value, g.Snaps); }
    }

    public bool GuideSnaps
    {
        get => SelectedGuide?.Snaps ?? true;
        set { if (SelectedGuide is { } g) SetGuideFlags(g, g.Visible, value); }
    }

    public bool GuideLocked
    {
        get => SelectedGuide?.Locked ?? false;
        set { if (SelectedGuide is { } g) SetGuideLocked(g, value); }
    }

    /// <summary>
    /// Whether this kind of guide has a default worth writing back.
    /// </summary>
    /// <remarks>
    /// Three kinds carry numbers a new one is made from — a grid's pitch, a
    /// vanishing point's fan, a height scale's proportions. A guide line and
    /// the isometric axes are made from where you put them, so there is nothing
    /// for the button to save and it is absent rather than inert.
    /// </remarks>
    public bool CanSaveGuideDefaults =>
        SelectedGuide?.Kind is GuideKind.Grid or GuideKind.VanishingPoint or GuideKind.HeightScale;

    /// <summary>What "Set as default" would do, said plainly on the button's tip.</summary>
    public string GuideDefaultsHint => SelectedGuide?.Kind switch
    {
        GuideKind.Grid => "Make this cell size the one new grids are created with",
        GuideKind.VanishingPoint => "Make this many rays the fan new vanishing points are drawn with",
        GuideKind.HeightScale => "Make this head count and this share of the canvas height what new scales stand at",
        _ => "This kind of guide has no default to save",
    };

    /// <summary>
    /// Write the selected guide's numbers back as the defaults new guides of
    /// its kind are made with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The persistent half of the owner's "temporarily or persistent": every
    /// edit in the options is temporary in the sense that matters — it changes
    /// this guide on this document and nothing else — and this one button is
    /// the deliberate act that also changes what comes next. A preference that
    /// rewrote itself every time somebody nudged one guide would make the
    /// default meaningless.
    /// </para>
    /// <para>
    /// A height scale saves a <i>proportion</i> rather than a head height in
    /// pixels, so the default still lands as a figure on a canvas of a
    /// different size. See <see cref="AppSettings.HeightScaleFill"/>.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSaveGuideDefaults))]
    private void SaveGuideDefaults()
    {
        if (SelectedGuide is not { } guide) return;
        switch (guide.Kind)
        {
            case GuideKind.Grid:
                GridSpacing = guide.Spacing;
                AiStatus = $"New grids will be made at {GridSpacing:0.##} px.";
                break;
            case GuideKind.VanishingPoint:
                VanishingPointRays = guide.RayCount;
                AiStatus = $"New vanishing points will be drawn with {VanishingPointRays} rays.";
                break;
            case GuideKind.HeightScale:
                var heads = Math.Max(1, guide.Divisions ?? 1);
                HeightScaleHeads = heads;
                if (Scene.Height > 0) HeightScaleFill = guide.Spacing * heads / Scene.Height;
                AiStatus =
                    $"New height scales will be {HeightScaleHeads} heads tall, "
                    + $"standing in {HeightScaleFill:P0} of the canvas.";
                break;
            default:
                return;
        }
    }

    /// <summary>Remove the guide the options are pointed at.</summary>
    [RelayCommand]
    private void RemoveSelectedGuide()
    {
        if (SelectedGuide is not { } guide) return;
        RemoveGuide(guide);
        Selection.ClearGuideSelection();
    }

    private void NotifySelectedGuide()
    {
        OnPropertyChanged(nameof(SelectedGuide));
        OnPropertyChanged(nameof(HasSelectedGuide));
        OnPropertyChanged(nameof(SelectedGuideIsGrid));
        OnPropertyChanged(nameof(SelectedGuideIsHeightScale));
        OnPropertyChanged(nameof(SelectedGuideIsVanishingPoint));
        OnPropertyChanged(nameof(SelectedGuideHasAngle));
        OnPropertyChanged(nameof(SelectedGuideLabel));
        OnPropertyChanged(nameof(GuideX));
        OnPropertyChanged(nameof(GuideY));
        OnPropertyChanged(nameof(GuideAngle));
        OnPropertyChanged(nameof(GuideSpacing));
        OnPropertyChanged(nameof(GuideHeads));
        OnPropertyChanged(nameof(GuideRays));
        OnPropertyChanged(nameof(GuideVisible));
        OnPropertyChanged(nameof(GuideSnaps));
        OnPropertyChanged(nameof(GuideLocked));
        OnPropertyChanged(nameof(CanSaveGuideDefaults));
        OnPropertyChanged(nameof(GuideDefaultsHint));
        SaveGuideDefaultsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Put a raw point where the guides say it belongs.</summary>
    /// <remarks>
    /// The <em>stroke</em> version: stateful, because a stroke commits to one
    /// guide and is then held on it. Everything that places a single point
    /// wants <see cref="SnappedPoint"/> instead.
    /// </remarks>
    private (double X, double Y) Guided(double x, double y) =>
        !SnapToGuides || Scene.Guides is not { Count: > 0 } guides
            ? (x, y)
            : _guideSnap.Apply(guides, x, y, SnapTolerance);

    /// <summary>
    /// Pull one placed point onto the guides, or leave it where it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B216.</b> The four lines this replaces were copied at five call sites
    /// and absent at nine more, which is the whole of that bug: snapping was
    /// not a system tools opted into, it was a fragment each tool remembered or
    /// forgot. A named method is what makes "does this tool snap?" a question
    /// with one answer per tool rather than a grep.
    /// </para>
    /// <para>
    /// Stateless, unlike <see cref="Guided"/>. A point being placed has no
    /// direction to commit to — there is no travel yet to measure one from —
    /// so the nearest guide wins and nothing is remembered between calls. That
    /// is also what makes it safe on a hover, which is where half the new
    /// callers are: a preview must show where the click will land, and a
    /// stateful snap would let the preview change what the click then does.
    /// </para>
    /// </remarks>
    private (double X, double Y) SnappedPoint(double x, double y) =>
        !SnapToGuides || Scene.Guides is not { Count: > 0 } guides
            ? (x, y)
            : Snapper.Point(guides, x, y, SnapTolerance);

    /// <summary>
    /// The point snapper the canvas hands its own gestures through.
    /// </summary>
    /// <remarks>
    /// The canvas builds the marquee shapes itself — it owns the rubber band —
    /// so it cannot ask <see cref="SnappedPoint"/> the way the view model's own
    /// tools do. It gets the same function through a delegate instead, which is
    /// the pattern every other decision this control delegates already uses
    /// (<c>SetLinePicker</c>, <c>SetPathEditEntry</c>). Handing over the
    /// function rather than the guides keeps `Snapper` and `SnapToGuides` on
    /// this side of the line, where the record is.
    /// </remarks>
    public (double X, double Y) SnapPointForCanvas(double x, double y) => SnappedPoint(x, y);

    /// <summary>Add a guide. The first one brings the machinery into being.</summary>
    public Guide AddGuide(
        GuideKind kind, double x, double y, double angle = 0, double spacing = 32,
        string? name = null, int? divisions = null)
    {
        var guide = new Guide
        {
            Kind = kind, X = x, Y = y, Angle = angle, Spacing = spacing,
            Name = name, Divisions = divisions,
        };
        _editor.Perform(doc => (doc.Scene.Guides ??= []).Add(guide));
        NotifyGuidesView();
        return guide;
    }

    /// <summary>
    /// Change a height scale's proportions — one head's height and how many
    /// of them — as one undoable step.
    /// </summary>
    public void SetHeightScale(Guide guide, double unit, int divisions)
    {
        var clampedUnit = Math.Clamp(unit, 1, 4096);
        var clampedDivisions = Math.Clamp(divisions, 1, 32);
        var before = (guide.Spacing, guide.Divisions);
        if (Math.Abs(before.Spacing - clampedUnit) < 1e-9
            && before.Divisions == clampedDivisions)
        {
            return;
        }
        _editor.PerformDelta(
            _ => { guide.Spacing = clampedUnit; guide.Divisions = clampedDivisions; },
            _ => { guide.Spacing = before.Spacing; guide.Divisions = before.Divisions; });
        NotifyGuidesView();
    }

    private double? _heightScaleUnitBefore;

    /// <summary>
    /// Pull a height scale's top while the pointer is still down: the total
    /// height changes and the divisions follow, because a head count is a
    /// proportion and resizing the character does not change it.
    /// </summary>
    /// <remarks>
    /// Live like <see cref="DragGuide"/> — nothing is recorded until
    /// <see cref="EndHeightScaleResize"/> closes the gesture into one step.
    /// </remarks>
    public void DragHeightScaleTop(Guide guide, double dy)
    {
        if (guide.Locked || guide.Kind != GuideKind.HeightScale) return;
        _heightScaleUnitBefore ??= guide.Spacing;
        var divisions = Math.Max(1, guide.Divisions ?? 1);
        // The top moving down by dy shrinks the whole scale by dy, spread
        // evenly over the divisions. Floored so it cannot be dragged inside
        // out — a scale of no height has no top to pull back.
        guide.Spacing = Math.Max(1, guide.Spacing - dy / divisions);
        // The view only, for the reason above (B353) — and this method's own
        // remark already promised it: "nothing is recorded until
        // EndHeightScaleResize closes the gesture into one step."
        NotifyGuidesView();
    }

    /// <summary>Close a height-scale resize: the whole of it becomes one undo step.</summary>
    public void EndHeightScaleResize(Guide guide)
    {
        if (_heightScaleUnitBefore is not { } before) return;
        _heightScaleUnitBefore = null;
        var after = guide.Spacing;
        if (Math.Abs(after - before) < 1e-9) return;
        // Back to where the drag started, then forward again as one recorded
        // step — so undo returns the top to where it was picked up.
        guide.Spacing = before;
        _editor.PerformDelta(_ => guide.Spacing = after, _ => guide.Spacing = before);
        NotifyGuidesView();
    }

    public void RemoveGuide(Guide guide)
    {
        var id = guide.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.Guides?.RemoveAll(g => g.Id == id);
            // Absent, not empty: a document whose last guide goes writes no
            // guide key again.
            if (doc.Scene.Guides is { Count: 0 }) doc.Scene.Guides = null;
        });
        NotifyGuidesView();
    }

    /// <summary>Move a guide's anchor, in document pixels.</summary>
    public void MoveGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        _editor.PerformDelta(
            _ => { guide.X += dx; guide.Y += dy; },
            _ => { guide.X -= dx; guide.Y -= dy; });
        NotifyGuidesView();
    }

    private (double X, double Y) _guideDragTotal;

    /// <summary>
    /// Move a guide while the pointer is still down.
    /// </summary>
    /// <remarks>
    /// Nothing is recorded until the drag ends. A pointer move arrives every
    /// few milliseconds, so recording each one would bury the last real edit
    /// under fifty identical nudges and make undoing a drag a job rather than
    /// a keystroke.
    /// </remarks>
    public void DragGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        guide.X += dx;
        guide.Y += dy;
        _guideDragTotal = (_guideDragTotal.X + dx, _guideDragTotal.Y + dy);
        // The view only (B353). This runs once per pointer event, and the
        // recording half of NotifyGuides is not free: it publishes a frame
        // through the whole compose pipeline and calls MarkDocumentEdited,
        // which re-flattens every taped reference strip and invalidates the
        // reference-view cache. None of that is wanted mid-gesture — the
        // guide is chrome, the canvas redraws it from GuidesChanged, and
        // EndGuideDrag records the whole move as one step.
        NotifyGuidesView();
    }

    /// <summary>Close a guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuideDrag(Guide guide)
    {
        var (dx, dy) = _guideDragTotal;
        _guideDragTotal = default;
        if (dx == 0 && dy == 0) return;
        // Back to where the drag started, then forward again through the
        // recorded path — so undo returns it to the place it was picked up
        // from rather than to the last pointer event.
        guide.X -= dx;
        guide.Y -= dy;
        MoveGuide(guide, dx, dy);
    }

    [RelayCommand]
    private void ClearGuides()
    {
        if (!HasGuides) return;
        _editor.Perform(doc => doc.Scene.Guides = null);
        NotifyGuidesView();
    }

    // NotifyGuides used to live here, and did three things: the view, a
    // PublishSnapshot, and a MarkDocumentEdited (B354).
    //
    // Every one of its seventeen call sites turned out to sit inside a method
    // that had just performed an editor operation — enumerated rather than
    // assumed — and an editor operation already runs OnDocumentChanged, which
    // does all three of those itself. So the recording half was a second
    // helping: two autosave marks, two reference-view cache invalidations, two
    // reference-strip re-flattens and two publishes for one guide edit.
    //
    // The remaining callers say NotifyGuidesView, which is what they meant.
    // If a guide is ever mutated *without* going through the editor, it will
    // need its own mark rather than this one back: a notifier that dirties the
    // document is indistinguishable from one that does not until you count.

    /// <summary>
    /// Tell everything that shows guides to re-read them, without recording an
    /// edit.
    /// </summary>
    /// <remarks>
    /// The view half of <see cref="NotifyGuides"/>, split out because two
    /// different things move the guides under the UI. An <em>edit</em> goes
    /// through <see cref="NotifyGuides"/> and also publishes and dirties the
    /// document. A <em>document swap</em> — a tab switch, a file opened, an
    /// undo — changes which guides are on screen without being an edit, and it
    /// runs through <c>OnDocumentChanged</c>, which calls this. That call was
    /// missing, and the cost was the owner's report "the guides are gone when I
    /// reopen": the file carried them, the model held them, and the canvas was
    /// never told — nothing drew them until the next guide edit.
    /// </remarks>
    private void NotifyGuidesView()
    {
        OnPropertyChanged(nameof(Guides));
        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(GridGuides));
        OnPropertyChanged(nameof(HeightScaleGuides));
        // The options bar reads the selected guide's numbers, and a drag, an
        // undo or a pull from a set all move them without going near a setter.
        NotifySelectedGuide();
        GuidesChanged?.Invoke();
    }

    /// <summary>The guides changed; the canvas redraws its chrome from this.</summary>
    public event Action? GuidesChanged;

    // ---- guide sets: the authoring half Q30 step 4 shipped without ---------
    //
    // The resolver (GuideScopes), the manifest record (GuideSet) and the
    // sharing menu in the project window all existed, and nothing could create
    // a set — the owner's report that filed the roadmap item was "guides we
    // are not even able to create or assign". These are the missing verbs:
    // save the document's guides as a named set, offer the visible sets back,
    // and pull one into a document.

    /// <summary>
    /// The guide sets this document may pull from: what its scope declares, or
    /// every set in the project while nothing is scoped — Q30's
    /// new-projects-only migration, same as palettes and gradients.
    /// </summary>
    public IReadOnlyList<GuideSet> OfferedGuideSets => GuideSetsVisibleTo(ActiveTab?.Source);

    /// <summary>The same, for a named document — the testable seam.</summary>
    internal IReadOnlyList<GuideSet> GuideSetsVisibleTo(DocumentRef? document)
    {
        if (ProjectDocker.Project is not { } project) return [];
        if (project.Manifest.GuideSets is not { Count: > 0 } sets) return [];
        var visible = GuideScopes.VisibleTo(project.Manifest, document);
        return visible is null ? sets : [.. sets.Where(s => visible.Contains(s.Id))];
    }

    /// <summary>A set needs guides to hold and a project to live in.</summary>
    public bool CanSaveGuideSet => HasGuides && ProjectDocker.Project is not null;

    /// <summary>
    /// Every set in the project, for the editor — scoping filters the offers a
    /// document sees, never the library itself.
    /// </summary>
    public IReadOnlyList<GuideSet> ProjectGuideSets =>
        ProjectDocker.Project?.Manifest.GuideSets ?? (IReadOnlyList<GuideSet>)[];

    /// <summary>The menus re-read the offers; call after anything that changes them.</summary>
    public void NotifyGuideSetOffers()
    {
        OnPropertyChanged(nameof(OfferedGuideSets));
        OnPropertyChanged(nameof(PullGuideSetMenu));
        OnPropertyChanged(nameof(HasGuideSetOffers));
        OnPropertyChanged(nameof(CanSaveGuideSet));
    }

    /// <summary>The offers as menu entries — the project window's idiom.</summary>
    public IReadOnlyList<ScopeMenuEntry> PullGuideSetMenu =>
        [.. OfferedGuideSets.Select(s => new ScopeMenuEntry(s.Name, PullGuideSetCommand, s))];

    /// <summary>So the menu can hide an entry that would open onto nothing.</summary>
    public bool HasGuideSetOffers => OfferedGuideSets.Count > 0;

    /// <summary>
    /// Save this document's guides as a named set in the project — into a new
    /// set, or over <paramref name="overwriteId"/>'s.
    /// </summary>
    /// <remarks>
    /// Copies, both ways (see <see cref="GuideSet"/>'s remarks): the set is a
    /// library, so moving a guide in the document afterwards must not silently
    /// edit the library, and vice versa.
    /// </remarks>
    public GuideSet? SaveGuidesAsSet(string name, string? overwriteId = null)
    {
        if (ProjectDocker.Project is not { } project) return null;
        if (Scene.Guides is not { Count: > 0 } guides) return null;

        var sets = project.Manifest.GuideSets ??= [];
        var set = overwriteId is null ? null : sets.FirstOrDefault(s => s.Id == overwriteId);
        if (set is null)
        {
            set = new GuideSet();
            sets.Add(set);
        }
        if (name.Trim() is { Length: > 0 } trimmed) set.Name = trimmed;
        set.Guides = [.. guides.Select(g => g.Clone())];
        // The paper they were drawn on, so a pull onto other paper can put
        // them back where they belong (Q181). Recorded on every save,
        // including a save over an older set — the guides are this document's
        // now, so the canvas has to be this document's too.
        set.Canvas = AuthoredCanvas.Of(Scene);
        SaveProject();
        NotifyGuideSetOffers();
        AiStatus = $"Guides saved as “{set.Name}”. Share it onto a folder from the project window.";
        return set;
    }

    public void RenameGuideSet(GuideSet set, string name)
    {
        if (ProjectDocker.Project is null || name.Trim() is not { Length: > 0 } trimmed) return;
        set.Name = trimmed;
        SaveProject();
        NotifyGuideSetOffers();
    }

    /// <summary>
    /// Remove a set from the project, and every declaration that shared it —
    /// a declaration pointing at nothing would scope the kind and offer air.
    /// </summary>
    public void DeleteGuideSet(GuideSet set)
    {
        if (ProjectDocker.Project is not { } project) return;
        project.Manifest.GuideSets?.RemoveAll(s => s.Id == set.Id);
        // Absent, not empty: a project whose last set goes writes no key again.
        if (project.Manifest.GuideSets is { Count: 0 }) project.Manifest.GuideSets = null;
        ResourceScopes.Retract(project.Manifest, GuideScopes.Kind, set.Id);
        SaveProject();
        NotifyGuideSetOffers();
        AiStatus = $"Guide set “{set.Name}” deleted.";
    }

    /// <summary>
    /// Copy a set's guides into this document, fitted to its paper, as one
    /// undoable step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fresh ids on the copies: pulling the same set twice is two independent
    /// batches, and removing one guide by id must not take its twin with it.
    /// </para>
    /// <para>
    /// <b>Fitted, not copied verbatim</b> (Q181). A set authored on 4K landing
    /// on 1080p at its authored pixel coordinates is four times too tall with
    /// its anchor off the paper, which made a shared height chart useless
    /// across the resolutions one project actually holds.
    /// <see cref="GuideSetFit"/> carries it; a set saved before sets recorded
    /// their paper has no canvas and lands exactly as it always did.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void PullGuideSet(GuideSet? set)
    {
        if (set is null || set.Guides.Count == 0) return;
        var copies = GuideSetFit.Onto(set, AuthoredCanvas.Of(Scene));
        foreach (var copy in copies) copy.Id = Ids.NewId("gd");
        var ids = copies.Select(c => c.Id).ToHashSet();
        _editor.PerformDelta(
            apply: doc => (doc.Scene.Guides ??= []).AddRange(copies),
            revert: doc =>
            {
                doc.Scene.Guides?.RemoveAll(g => ids.Contains(g.Id));
                if (doc.Scene.Guides is { Count: 0 }) doc.Scene.Guides = null;
            });
        NotifyGuidesView();
        AiStatus = $"Added {copies.Count} guide{(copies.Count == 1 ? "" : "s")} from “{set.Name}”.";
    }

    /// <summary>The references on this document, or an empty list.</summary>
    public IReadOnlyList<ReferenceStrip> References =>
        Scene.References is { } strips ? strips : [];

    public bool HasReferences => Scene.HasReferences;

    /// <summary>
    /// The reference being edited. Index rather than the object, so the
    /// selection survives an undo — which replaces the whole document.
    /// </summary>
    [ObservableProperty]
    private int _activeReferenceIndex;

    public ReferenceStrip? ActiveReference =>
        Scene.References is { } strips && ActiveReferenceIndex >= 0 && ActiveReferenceIndex < strips.Count
            ? strips[ActiveReferenceIndex]
            : null;

    partial void OnActiveReferenceIndexChanged(int value) => NotifyReference();

    /// <summary>The cell of the active reference showing at the playhead, or null.</summary>
    public ReferenceCell? ActiveReferenceCell => ActiveReference?.CellAt(CurrentFrameIndex);

    public bool HasReferenceCell => ActiveReferenceCell is not null;

    /// <summary>
    /// Import a sheet, slice it, and lay it against the timeline from the
    /// playhead.
    /// </summary>
    /// <param name="addFrames">
    /// Extend the timeline to fit the reference. On by default because it is
    /// what importing a run cycle means: you are here to draw those frames,
    /// and being handed a twelve-frame reference on a one-frame document with
    /// eleven of it invisible is not a state anybody asked for.
    /// </param>
    /// <summary>
    /// Import an image file as a reference. Everything becomes PNG on the way
    /// in: the document carries the image itself rather than a path — a
    /// reference that broke when the file moved would break silently, and you
    /// would not notice until you were drawing against nothing. False when
    /// the file cannot be read as an image.
    /// </summary>
    public bool ImportReferenceImageFile(string path)
    {
        string png;
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded is null) return false;
            png = Lightbox.Raster.PngCodec.Encode(decoded);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return ImportReference(System.IO.Path.GetFileNameWithoutExtension(path), png) is not null;
    }

    /// <summary>
    /// Import in-memory image bytes as a reference — a picture dragged from a
    /// browser, where there is no file to point at. Same normalization as the
    /// file path: everything becomes PNG inside the document. False when the
    /// bytes do not decode as an image.
    /// </summary>
    public bool ImportReferenceImageBytes(string name, byte[] bytes)
    {
        // Through a codec rather than Decode(byte[]): the byte[] overload
        // throws on bytes no codec recognises — a page dragged instead of its
        // picture — where the path overload's null means "not an image".
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return false;
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return false;
        return ImportReference(name, Lightbox.Raster.PngCodec.Encode(decoded)) is not null;
    }

    public ReferenceStrip? ImportReference(
        string name, string pngBase64, SliceOptions options = default, bool addFrames = true)
    {
        SKBitmap sheet;
        try
        {
            sheet = Lightbox.Raster.PngCodec.Decode(pngBase64);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return null;
        }

        var strip = new ReferenceStrip
        {
            Name = name,
            Png = pngBase64,
            SheetWidth = sheet.Width,
            SheetHeight = sheet.Height,
            Cells = SliceSheet(sheet, options),
        };
        sheet.Dispose();
        if (strip.Cells.Count == 0) return null;

        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (addFrames && strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return strip;
    }

    /// <summary>
    /// Footage to draw against (Q56): the clip's frames extracted at the
    /// scene's fps and laid against the timeline like any reference. The
    /// document keeps the path — relative when the file lives near it — and
    /// the pixels are rebuilt from the footage on load. Returns null on
    /// success or a sentence saying why not.
    /// </summary>
    public async Task<string?> ImportVideoReference(
        string path, Services.ClipStorage storage = Services.ClipStorage.ReferenceByPath)
    {
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg)
        {
            return "FFmpeg was not found — reinstall Lightbox, or install FFmpeg and put it on PATH.";
        }
        // The extraction is an FFmpeg run — seconds, off the UI thread. The
        // document edit below stays on it, like every other edit.
        var fps = Math.Max(1, Scene.Fps);
        var (extracted, error) = await Task.Run(() =>
        {
            var r = Services.VideoReferenceImporter.Extract(ffmpeg, path, fps, out var e);
            return (r, e);
        });
        if (extracted is not { } result) return error ?? "The clip could not be read.";

        var stored = path;
        if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir)
        {
            var relative = System.IO.Path.GetRelativePath(docDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal)
                && !System.IO.Path.IsPathRooted(relative))
            {
                stored = relative;
            }
        }

        var strip = new ReferenceStrip
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            SheetWidth = result.Sheet.Width,
            SheetHeight = result.Sheet.Height,
            Cells = result.Cells,
        };
        switch (storage)
        {
            case Services.ClipStorage.ReferenceByPath:
                strip.VideoPath = stored;
                break;
            case Services.ClipStorage.ReferenceEmbedded:
                // The contact sheet itself, stored the way image references
                // store — self-contained, no path, no FFmpeg on reopen.
                strip.Png = Lightbox.Raster.PngCodec.Encode(result.Sheet);
                break;
            case Services.ClipStorage.Production:
                strip.VideoData = Convert.ToBase64String(File.ReadAllBytes(path));
                strip.RendersInExport = true;
                // Material, not a ghost: production footage shows and
                // exports at full strength unless the artist dials it back.
                strip.Opacity = 1.0;
                break;
        }
        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return null;
    }

    /// <summary>
    /// Rebuild a loaded video reference's pixels from its footage. Quiet when
    /// FFmpeg or the file is gone — drawing against nothing is the reference
    /// system's standing answer to a source it cannot read.
    /// </summary>
    private void RegisterVideoReference(ReferenceStrip strip)
    {
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not null) return;
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg) return;

        string resolved;
        if (strip.VideoData is { } data)
        {
            // Production footage (Q57): the clip travels in the document, so
            // the extraction reads a temp copy of its own bytes.
            resolved = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"lightbox-footage-{strip.Id}.bin");
            try
            {
                if (!File.Exists(resolved))
                {
                    File.WriteAllBytes(resolved, Convert.FromBase64String(data));
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                return;
            }
        }
        else
        {
            resolved = strip.VideoPath!;
            if (!System.IO.Path.IsPathRooted(resolved))
            {
                if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is not { Length: > 0 } docDir) return;
                resolved = System.IO.Path.Combine(docDir, resolved);
            }
        }
        if (!File.Exists(resolved)) return;

        var extracted = Services.VideoReferenceImporter.Extract(ffmpeg, resolved, Math.Max(1, Scene.Fps), out _);
        if (extracted is { } result)
        {
            Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        }
    }

    private static List<ReferenceCell> SliceSheet(SKBitmap sheet, SliceOptions options)
    {
        // Grid mode never reads a pixel, so a sheet the artist has described
        // does not pay for the scan.
        if (options.Columns > 0 && options.Rows > 0)
        {
            return StripSlicer.Grid(sheet.Width, sheet.Height, options.Columns, options.Rows);
        }

        using var rgba = sheet.ColorType == SKColorType.Rgba8888
            ? null
            : sheet.Copy(SKColorType.Rgba8888);
        var source = rgba ?? sheet;
        using var pixmap = source.PeekPixels();
        var occupied = pixmap is null
            ? new bool[source.Width * source.Height]
            : StripSlicer.Occupancy(pixmap.GetPixelSpan(), source.Width, source.Height, options);
        // Detect finds the drawings and discards the furniture — a title
        // banner, a watermark, a signature. Slice projects occupancy onto the
        // axes, which is exact for a clean atlas and hopeless for a page: the
        // banner is content in every column, so the projection never returns
        // to zero and the whole sheet reads as one cell. Fall back to Slice
        // only when nothing looked like a drawing.
        var found = StripSlicer.Detect(occupied, source.Width, source.Height, options);
        return found.Count > 0
            ? found
            : StripSlicer.Slice(occupied, source.Width, source.Height, options);
    }

    /// <summary>
    /// Shrink an oversized sheet to fit the canvas. A reference bigger than
    /// the document is the common case — a 2000px sprite sheet against a 960px
    /// scene — and landing at 1:1 puts the character off screen with no
    /// obvious way back.
    /// </summary>
    private static double FitScale(ReferenceStrip strip, Scene scene)
    {
        var cell = strip.Cells[0];
        if (cell.Width <= 0 || cell.Height <= 0) return 1;
        var fit = Math.Min(scene.Width / (double)cell.Width, scene.Height / (double)cell.Height);
        return fit >= 1 ? 1 : Math.Round(fit, 3);
    }

    /// <summary>
    /// Columns and rows for the grid override. Zero on both means "work it
    /// out from the pixels", which is what <c>Detect</c> restores.
    /// </summary>
    [ObservableProperty]
    private int _referenceColumns;

    [ObservableProperty]
    private int _referenceRows;

    /// <summary>Dragging on the canvas lines the reference up instead of drawing.</summary>
    [ObservableProperty]
    private bool _referenceAlignMode;

    [RelayCommand]
    private void ApplyReferenceGrid() =>
        ResliceReference(new SliceOptions(Math.Max(0, ReferenceColumns), Math.Max(0, ReferenceRows)));

    [RelayCommand]
    private void DetectReferenceFrames()
    {
        ReferenceColumns = 0;
        ReferenceRows = 0;
        ResliceReference(default);
    }

    /// <summary>Cut the active sheet up again — a different grid, or auto-detect.</summary>
    public void ResliceReference(SliceOptions options)
    {
        if (ActiveReference is not { } strip) return;
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not { } sheet) return;

        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells = SliceSheet(sheet, options);
            live.LayOutFrom(Math.Max(0, first));
        });
        AfterReferenceChange();
    }

    [RelayCommand]
    private void RemoveReference()
    {
        if (ActiveReference is not { } strip) return;
        var id = strip.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.References?.RemoveAt(ActiveReferenceIndex);
            // Absent, not empty: a document whose last reference is removed
            // goes back to writing no key at all.
            if (doc.Scene.References is { Count: 0 }) doc.Scene.References = null;
        });
        Lightbox.Raster.ReferenceStripRegistry.Forget(id);
        ActiveReferenceIndex = Math.Max(0, ActiveReferenceIndex - 1);
        AfterReferenceChange();
    }

    /// <summary>
    /// The reference showing under a document point, or -1 — topmost wins,
    /// which is the last one in the list, the same back-to-front reading the
    /// compositor gives <see cref="Scene.References"/>.
    /// </summary>
    public int ReferenceStripAt(double x, double y)
    {
        if (Scene.References is not { } strips) return -1;
        for (var i = strips.Count - 1; i >= 0; i--)
        {
            var strip = strips[i];
            if (!strip.Visible || strip.Opacity <= 0) continue;
            if (strip.CellAt(CurrentFrameIndex) is not { } cell) continue;
            var (cx, cy, w, h) = CellRect(strip, cell);
            if (x >= cx && x <= cx + w && y >= cy && y <= cy + h) return i;
        }
        return -1;
    }

    /// <summary>
    /// Move one reference through the stack — list order is z-order, so this
    /// is "bring it forward" and "send it backward" in one method. The whole
    /// stack still sits over the paper and under every drawing layer; only
    /// how the references overlap each other changes.
    /// </summary>
    public void MoveReferenceInStack(int from, int to)
    {
        if (Scene.References is not { } strips) return;
        to = Math.Clamp(to, 0, strips.Count - 1);
        if (from < 0 || from >= strips.Count || from == to) return;
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References!;
            var strip = live[from];
            live.RemoveAt(from);
            live.Insert(to, strip);
        });
        // Follow the strip, so the panel and the gizmos keep talking about
        // the one the artist just moved.
        ActiveReferenceIndex = to;
        AfterReferenceChange();
    }

    // ---- editing the grid by hand ---------------------------------------------

    /// <summary>
    /// The grid gizmos are showing and everything else is off.
    /// </summary>
    /// <remarks>
    /// A mode rather than a tool. While it is on, every box on the sheet is
    /// editable at once and the canvas is not a place to draw — the same
    /// bargain <see cref="ReferenceAlignMode"/> makes, for the same reason: a
    /// half-drawn mark made while adjusting a grid is one you then have to
    /// find and undo. Escape leaves it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuppressesPainting))]
    private bool _referenceGridEditMode;

    partial void OnReferenceGridEditModeChanged(bool value)
    {
        if (!value) SelectedReferenceCell = -1;
        PublishSnapshot();
    }

    /// <summary>Whether some mode has taken the canvas away from the tools.</summary>
    public bool SuppressesPainting => ReferenceGridEditMode;

    /// <summary>Which box the gizmos have selected, or -1.</summary>
    [ObservableProperty]
    private int _selectedReferenceCell = -1;

    /// <summary>
    /// Where a cell lands on the canvas, in document pixels.
    /// </summary>
    /// <remarks>
    /// The same arithmetic the compositor does in <see cref="ScenePassBuilder.ReferenceSpecs"/>,
    /// exposed so the gizmos can be drawn and hit-tested against exactly what
    /// is on screen. Two copies of this would drift and the boxes would stop
    /// sitting on the drawings they describe.
    /// </remarks>
    public (double X, double Y, double W, double H) CellRect(ReferenceStrip strip, ReferenceCell cell)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return (
            strip.OffsetX + cell.Dx + cell.X * scale,
            strip.OffsetY + cell.Dy + cell.Y * scale,
            cell.Width * scale,
            cell.Height * scale);
    }

    /// <summary>A document point in the active sheet's own pixels.</summary>
    public (double X, double Y) DocToSheet(ReferenceStrip strip, ReferenceCell cell, double x, double y)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return ((x - strip.OffsetX - cell.Dx) / scale, (y - strip.OffsetY - cell.Dy) / scale);
    }

    /// <summary>The box under a document point, or -1.</summary>
    public int ReferenceCellAt(double x, double y)
    {
        if (ActiveReference is not { } strip) return -1;
        // Backwards, so the box drawn last — the one on top — wins.
        for (var i = strip.Cells.Count - 1; i >= 0; i--)
        {
            var (cx, cy, w, h) = CellRect(strip, strip.Cells[i]);
            if (x >= cx && x <= cx + w && y >= cy && y <= cy + h) return i;
        }
        return -1;
    }

    /// <summary>Move one box, in document pixels.</summary>
    public void MoveReferenceCell(int index, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>
    /// Resize one box by dragging a corner, in document pixels.
    /// </summary>
    /// <remarks>
    /// The window onto the sheet changes, not the nudge: growing a box shows
    /// more of the drawing rather than scaling it. A box that scaled its
    /// contents would be a second zoom control with no way to tell it from the
    /// first.
    /// </remarks>
    public void ResizeReferenceCell(int index, bool left, bool top, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var scale = Math.Max(0.01, strip.Scale);
        var cell = strip.Cells[index];
        var before = cell.Clone();

        var sx = (int)Math.Round(dx / scale);
        var sy = (int)Math.Round(dy / scale);
        var x = left ? cell.X + sx : cell.X;
        var y = top ? cell.Y + sy : cell.Y;
        var w = left ? cell.Width - sx : cell.Width + sx;
        var h = top ? cell.Height - sy : cell.Height + sy;
        // A box with no area is a box you cannot get hold of again.
        if (w < 4 || h < 4) return;

        _editor.PerformDelta(
            _ => { cell.X = x; cell.Y = y; cell.Width = w; cell.Height = h; },
            _ =>
            {
                cell.X = before.X;
                cell.Y = before.Y;
                cell.Width = before.Width;
                cell.Height = before.Height;
            });
        AfterReferenceChange();
    }

    /// <summary>
    /// Put a box's pivot at a document point.
    /// </summary>
    /// <remarks>
    /// Recorded in sheet pixels, so it stays on the same part of the drawing
    /// when the sheet is nudged or rescaled afterwards.
    /// </remarks>
    public void SetReferencePivot(int index, double docX, double docY)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        var (x, y) = DocToSheet(strip, cell, docX, docY);
        var (beforeX, beforeY) = (cell.PivotX, cell.PivotY);
        _editor.PerformDelta(
            _ => { cell.PivotX = x; cell.PivotY = y; },
            _ => { cell.PivotX = beforeX; cell.PivotY = beforeY; });
        AfterReferenceChange();
    }

    /// <summary>Remove one box. The sheet is untouched; only the window goes.</summary>
    public void DeleteReferenceCell(int index)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.RemoveAt(index);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = -1;
        AfterReferenceChange();
    }

    /// <summary>
    /// Draw a box by hand, from a rectangle in document pixels.
    /// </summary>
    /// <remarks>
    /// The escape hatch from detection. A sheet whose figures overlap, or
    /// whose rows have no gutter, cannot be found from the pixels — no amount
    /// of looking finds a boundary that is not there — so the answer is to let
    /// the artist draw it rather than to guess.
    /// </remarks>
    public void AddReferenceCell(double x, double y, double w, double h)
    {
        if (ActiveReference is not { } strip) return;
        if (w < 4 || h < 4) return;
        var scale = Math.Max(0.01, strip.Scale);
        var sheetX = (int)Math.Round((x - strip.OffsetX) / scale);
        var sheetY = (int)Math.Round((y - strip.OffsetY) / scale);
        var cell = new ReferenceCell
        {
            X = sheetX,
            Y = sheetY,
            Width = (int)Math.Round(w / scale),
            Height = (int)Math.Round(h / scale),
        };
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.Add(cell);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = strip.Cells.Count - 1;
        AfterReferenceChange();
    }

    /// <summary>Whether the timeline is short of frames for the boxes found.</summary>
    public bool ReferenceNeedsKeyframes =>
        ActiveReference is { Cells.Count: > 0 } strip && strip.Cells.Count > Scene.FrameCount;

    /// <summary>
    /// One keyframe per box, lined up on the pivots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things at once, because they are one intention: the timeline grows
    /// to hold the reference, and the cells are registered so the pivot sits
    /// still. Being handed an eight-frame reference on a one-frame document is
    /// not a state anybody asked for, and neither is a run cycle that has to
    /// be nudged into place eight times.
    /// </para>
    /// <para>
    /// Alignment is separable — <see cref="ReferenceStrip.AlignByPivot"/> is
    /// its own call — because someone matching a walk wants the travel left in
    /// and someone matching a drawing does not.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void GenerateReferenceKeyframes()
    {
        if (ActiveReference is not { Cells.Count: > 0 } strip) return;
        var wanted = strip.Cells.Count;
        var first = Math.Max(0, strip.Slots.FindIndex(s => s >= 0));

        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            while (scene.FrameCount < first + wanted) DocumentEditor.AppendFrame(scene);
            var live = scene.References![ActiveReferenceIndex];
            live.LayOutFrom(first);
            live.AlignByPivot();
        });
        AfterReferenceChange();
        AiStatus = $"{wanted} frames from “{strip.Name}”, aligned on their pivots.";
    }

    /// <summary>Move the cell showing at the playhead, in document pixels.</summary>
    public void NudgeReferenceCell(double dx, double dy)
    {
        if (ActiveReferenceCell is not { } cell) return;
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Move the whole sheet, every frame together.</summary>
    public void NudgeReference(double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        _editor.PerformDelta(
            _ => { strip.OffsetX += dx; strip.OffsetY += dy; },
            _ => { strip.OffsetX -= dx; strip.OffsetY -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Undo every per-frame nudge on the active sheet.</summary>
    [RelayCommand]
    private void ClearReferenceAlignment()
    {
        if (ActiveReference is not { } strip) return;
        var before = strip.Cells.ConvertAll(c => (c.Dx, c.Dy));
        _editor.PerformDelta(
            _ => { foreach (var c in strip.Cells) (c.Dx, c.Dy) = (0, 0); },
            _ =>
            {
                for (var i = 0; i < strip.Cells.Count && i < before.Count; i++)
                {
                    (strip.Cells[i].Dx, strip.Cells[i].Dy) = before[i];
                }
            });
        AfterReferenceChange();
    }

    public double ReferenceScale
    {
        get => ActiveReference?.Scale ?? 1;
        set => SetReference(Math.Clamp(value, 0.05, 8), (s, v) => s.Scale = v, ActiveReference?.Scale ?? 1);
    }

    public double ReferenceOpacity
    {
        get => ActiveReference?.Opacity ?? 0.5;
        set => SetReference(Math.Clamp(value, 0, 1), (s, v) => s.Opacity = v, ActiveReference?.Opacity ?? 0.5);
    }

    public bool ReferenceVisible
    {
        get => ActiveReference?.Visible ?? false;
        set => SetReference(value, (s, v) => s.Visible = v, ActiveReference?.Visible ?? false);
    }

    public bool ReferenceFollowsTimeline
    {
        get => ActiveReference?.FollowsTimeline ?? true;
        set => SetReference(value, (s, v) => s.FollowsTimeline = v, ActiveReference?.FollowsTimeline ?? true);
    }

    public double ReferenceCellDx
    {
        get => ActiveReferenceCell?.Dx ?? 0;
        set => NudgeReferenceCell(value - (ActiveReferenceCell?.Dx ?? 0), 0);
    }

    public double ReferenceCellDy
    {
        get => ActiveReferenceCell?.Dy ?? 0;
        set => NudgeReferenceCell(0, value - (ActiveReferenceCell?.Dy ?? 0));
    }

    /// <summary>Which frame of the sheet the playhead is on, for the panel's label.</summary>
    public string ReferenceCellLabel
    {
        get
        {
            if (ActiveReference is not { } strip) return "";
            var slot = CurrentFrameIndex < strip.Slots.Count ? strip.Slots[CurrentFrameIndex] : -1;
            return slot < 0 ? "no reference on this frame" : $"reference frame {slot + 1} of {strip.Cells.Count}";
        }
    }

    private void SetReference<T>(T value, Action<ReferenceStrip, T> apply, T current,
        [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (ActiveReference is not { } strip || EqualityComparer<T>.Default.Equals(value, current)) return;
        // A view setting, not an edit to the artwork: no undo entry, the same
        // treatment layer visibility gets.
        apply(strip, value);
        OnPropertyChanged(property);
        AfterReferenceViewTweak();
    }

    /// <summary>
    /// A reference dial moved — opacity, scale, visibility: repaint and mark
    /// the file dirty, and touch nothing that is derived from the artwork.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B191.</b> These setters used to run <see cref="AfterReferenceChange"/>,
    /// whose <see cref="MarkDocumentEdited"/> half re-bakes every
    /// all-layers-live stroke at the playhead — a full-canvas compose and a
    /// PNG encode per stroke — and whose direct <see cref="PublishSnapshot"/>
    /// replays the whole frame when such a stroke is on it, because a live
    /// frame is never cached. A slider drag fires per pointer tick, so a
    /// painted-on document turned each tick into hundreds of milliseconds of
    /// UI-thread work and the drag into minutes of queued replays: the
    /// reported freeze, and the allocation storm behind the crash after it.
    /// </para>
    /// <para>
    /// A strip is not a layer: no live bake, no linked-strip flatten and no
    /// reference-view PNG can see its opacity, scale or visibility, so
    /// skipping that machinery drops no derived state. What a dial does still
    /// owe is the repaint — through <see cref="RequestSnapshot"/>, so a drag
    /// coalesces to the canvas's pace instead of publishing per tick — and
    /// the unsaved-changes bookkeeping, which is the funnel's cheap half.
    /// </para>
    /// </remarks>
    private void AfterReferenceViewTweak()
    {
        NotifyReference();
        RequestSnapshot();
        _autosave.MarkDirty();
        MarkActiveTabEdited();
        ReferenceChanged?.Invoke();
    }

    /// <summary>
    /// A reference or one of its cells changed. The window redraws the grid
    /// gizmos from this — they are a snapshot, so nothing else would tell it.
    /// </summary>
    public event Action? ReferenceChanged;

    private void AfterReferenceChange()
    {
        NotifyReference();
        OnPropertyChanged(nameof(TimelineVideoClips));
        PublishSnapshot();
        MarkDocumentEdited();
        ReferenceChanged?.Invoke();
    }

    // ---- picking a reference up on the canvas (Q108) --------------------------------

    /// <summary>
    /// A reference is selected on the canvas: its box is drawn, and the Arrow
    /// will move or scale it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which</b> reference is <see cref="ActiveReferenceIndex"/> — the same
    /// one the docker calls active, deliberately. Two notions of "the reference
    /// I am working on" would disagree the first time somebody clicked one on
    /// the canvas and looked at the panel. This flag is only whether the canvas
    /// is showing its handles.
    /// </para>
    /// <para>
    /// <b>Not a mode.</b> Nothing is switched on to make it happen and nothing
    /// is switched off by it: the Arrow selects a reference the way it selects a
    /// line, and the next thing the artist clicks decides what happens next.
    /// That is the whole difference from <see cref="ReferenceAlignMode"/>, which
    /// stays for the job it is actually good at — nudging one frame's cell into
    /// register (Q108).
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCanvasReference))]
    [NotifyPropertyChangedFor(nameof(CanvasReferenceLocked))]
    private bool _referenceSelectedOnCanvas;

    partial void OnReferenceSelectedOnCanvasChanged(bool value) => ReferenceChanged?.Invoke();

    /// <summary>The reference the Arrow has hold of, or null.</summary>
    public ReferenceStrip? SelectedCanvasReference =>
        ReferenceSelectedOnCanvas ? ActiveReference : null;

    /// <summary>
    /// Select the reference under a document point, or drop the selection when
    /// there is none there.
    /// </summary>
    /// <returns>Whether a reference was hit.</returns>
    /// <remarks>
    /// Locked references are still <em>selected</em> — that is what makes the
    /// lock legible rather than mysterious, since the box appears with no grips
    /// on it and the artist can see why the drag did nothing.
    /// </remarks>
    public bool SelectReferenceOnCanvasAt(double x, double y)
    {
        var hit = ReferenceStripAt(x, y);
        if (hit < 0)
        {
            ClearCanvasReferenceSelection();
            return false;
        }
        ActiveReferenceIndex = hit;
        ReferenceSelectedOnCanvas = true;
        return true;
    }

    public void ClearCanvasReferenceSelection()
    {
        if (!ReferenceSelectedOnCanvas) return;
        ReferenceSelectedOnCanvas = false;
    }

    /// <summary>
    /// The selected reference's rectangle at the playhead, in document space —
    /// what the canvas draws its box and grips from.
    /// </summary>
    public (double X, double Y, double W, double H)? CanvasReferenceBounds
    {
        get
        {
            if (SelectedCanvasReference is not { } strip) return null;
            if (strip.CellAt(CurrentFrameIndex) is not { } cell) return null;
            return CellRect(strip, cell);
        }
    }

    /// <summary>
    /// Whether the selected reference refuses to be dragged — its own lock, or
    /// the workspace's lock-them-all.
    /// </summary>
    /// <remarks>
    /// Two switches, one answer, and the canvas asks only this: a caller that
    /// checked one of them would work until somebody used the other.
    /// </remarks>
    public bool CanvasReferenceLocked =>
        SelectedCanvasReference is { IsLocked: true } || Workspace.ReferencesLocked;

    /// <summary>Pin a reference where it is, or let it go, undoably.</summary>
    /// <remarks>The guide's lock, in the same shape — see <see cref="SetGuideLocked"/>.</remarks>
    public void SetReferenceLocked(ReferenceStrip strip, bool locked)
    {
        var before = strip.Locked;
        // Null rather than false when unlocking: the key goes away again, so a
        // document that locked something and changed its mind is byte-identical
        // to one that never did.
        bool? after = locked ? true : null;
        if (before == after) return;
        _editor.PerformDelta(_ => strip.Locked = after, _ => strip.Locked = before);
        AfterReferenceViewTweak();
        OnPropertyChanged(nameof(CanvasReferenceLocked));
        OnPropertyChanged(nameof(SelectedReferenceLocked));
    }

    /// <summary>The selected reference's own lock, for a toggle to bind to.</summary>
    public bool SelectedReferenceLocked
    {
        get => SelectedCanvasReference?.IsLocked ?? ActiveReference?.IsLocked ?? false;
        set
        {
            if ((SelectedCanvasReference ?? ActiveReference) is { } strip) SetReferenceLocked(strip, value);
        }
    }

    // ---- moving and scaling it, as one undo step each (B192) -------------------------

    /// <summary>What the sheet was before the gesture started — the undo half.</summary>
    private (ReferenceStrip Strip, double OffsetX, double OffsetY, double Scale)? _referenceGesture;

    /// <summary>
    /// Take hold of the selected reference. Nothing is written yet.
    /// </summary>
    /// <returns>Whether there is something to drag — false when locked or nothing is selected.</returns>
    /// <remarks>
    /// <b>B192 is why the gesture has a beginning at all.</b> The align-mode drag
    /// went through <c>PerformDelta</c> per pointer event, so a single drag left
    /// dozens of undo steps to press back through and re-ran the whole
    /// document-changed storm on each one. A gesture is one act, so it is one
    /// step: the strip is mutated directly while the pointer is down, repainting
    /// through B191's cheap path, and the step is registered on release.
    /// </remarks>
    public bool BeginReferenceGesture()
    {
        if (SelectedCanvasReference is not { } strip || CanvasReferenceLocked) return false;
        _referenceGesture = (strip, strip.OffsetX, strip.OffsetY, strip.Scale);
        return true;
    }

    /// <summary>Move the held reference by a document-space delta.</summary>
    public void DragReferenceBy(double dx, double dy)
    {
        if (_referenceGesture is not { } held) return;
        held.Strip.OffsetX += dx;
        held.Strip.OffsetY += dy;
        AfterReferenceViewTweak();
    }

    /// <summary>The smallest a reference can be dragged down to.</summary>
    /// <remarks>
    /// A sheet scaled to nothing is unrecoverable by pointer — there would be no
    /// box left to grab a corner of — so the floor is a real guard rather than
    /// arithmetic hygiene.
    /// </remarks>
    private const double MinReferenceScale = 0.02;

    /// <summary>
    /// Scale the held reference uniformly about a fixed point.
    /// </summary>
    /// <param name="factor">Multiplier against the scale the gesture started at.</param>
    /// <param name="anchorX">Document-space point that must not move — the opposite corner.</param>
    /// <param name="anchorY">See <paramref name="anchorX"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>Uniform, and from the corners only</b> (Q108). A sheet carries one scale
    /// for every frame by design — per-frame scale would put the character at a
    /// different size on each drawing — and the same argument refuses a
    /// non-uniform one: an artist drawing from a reference stretched on one axis
    /// is drawing the wrong proportions.
    /// </para>
    /// <para>
    /// <b>Against the gesture's starting values, never the current ones.</b>
    /// Compounding a factor per pointer event drifts, and the drift is
    /// direction-dependent, so a corner dragged out and back would not come home.
    /// </para>
    /// </remarks>
    public void ScaleReferenceAbout(double factor, double anchorX, double anchorY)
    {
        if (_referenceGesture is not { } held) return;
        if (held.Strip.CellAt(CurrentFrameIndex) is not { } cell) return;

        var scale = Math.Max(MinReferenceScale, held.Scale * factor);
        var k = scale / Math.Max(0.0001, held.Scale);

        // Where the cell's top-left was, and where it must land so the anchor
        // stays under the artist's other hand.
        var left = held.OffsetX + cell.Dx + cell.X * held.Scale;
        var top = held.OffsetY + cell.Dy + cell.Y * held.Scale;

        held.Strip.Scale = scale;
        held.Strip.OffsetX = anchorX + (left - anchorX) * k - cell.Dx - cell.X * scale;
        held.Strip.OffsetY = anchorY + (top - anchorY) * k - cell.Dy - cell.Y * scale;
        AfterReferenceViewTweak();
    }

    /// <summary>
    /// Let go: one undo step for the whole gesture, or none at all when nothing
    /// actually moved.
    /// </summary>
    public void EndReferenceGesture()
    {
        if (_referenceGesture is not { } held) return;
        _referenceGesture = null;

        var strip = held.Strip;
        var after = (strip.OffsetX, strip.OffsetY, strip.Scale);
        var before = (held.OffsetX, held.OffsetY, held.Scale);
        // A click that selected without moving is not an edit, and an undo step
        // that changes nothing is one the artist has to press through.
        if (after == before) return;

        _editor.PerformDelta(
            _ =>
            {
                strip.OffsetX = after.OffsetX;
                strip.OffsetY = after.OffsetY;
                strip.Scale = after.Scale;
            },
            _ =>
            {
                strip.OffsetX = before.OffsetX;
                strip.OffsetY = before.OffsetY;
                strip.Scale = before.Scale;
                AfterReferenceViewTweak();
            });
        AfterReferenceViewTweak();
    }
}
