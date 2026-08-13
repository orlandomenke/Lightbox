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

/// <summary>Guides and grids, and snapping a stroke to them.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q75, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
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

    /// <summary>The grid guides on this document, if any.</summary>
    public IReadOnlyList<Guide> GridGuides =>
        Guides.Where(g => g.Kind == GuideKind.Grid).ToList();

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
        NotifyGuides();
    }

    /// <summary>Change a placed grid's angle, as one undoable step.</summary>
    public void SetGridAngle(Guide guide, double angle)
    {
        var before = guide.Angle;
        if (Math.Abs(before - angle) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Angle = angle, _ => guide.Angle = before);
        NotifyGuides();
    }

    /// <summary>Turn a placed guide's drawing or snapping on or off, undoably.</summary>
    public void SetGuideFlags(Guide guide, bool visible, bool snaps)
    {
        var before = (guide.Visible, guide.Snaps);
        if (before == (visible, snaps)) return;
        _editor.PerformDelta(
            _ => { guide.Visible = visible; guide.Snaps = snaps; },
            _ => { guide.Visible = before.Visible; guide.Snaps = before.Snaps; });
        NotifyGuides();
    }

    /// <summary>Put a raw point where the guides say it belongs.</summary>
    private (double X, double Y) Guided(double x, double y) =>
        !SnapToGuides || Scene.Guides is not { Count: > 0 } guides
            ? (x, y)
            : _guideSnap.Apply(guides, x, y, SnapTolerance);

    /// <summary>Add a guide. The first one brings the machinery into being.</summary>
    public Guide AddGuide(GuideKind kind, double x, double y, double angle = 0, double spacing = 32)
    {
        var guide = new Guide { Kind = kind, X = x, Y = y, Angle = angle, Spacing = spacing };
        _editor.Perform(doc => (doc.Scene.Guides ??= []).Add(guide));
        NotifyGuides();
        return guide;
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
        NotifyGuides();
    }

    /// <summary>Move a guide's anchor, in document pixels.</summary>
    public void MoveGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        _editor.PerformDelta(
            _ => { guide.X += dx; guide.Y += dy; },
            _ => { guide.X -= dx; guide.Y -= dy; });
        NotifyGuides();
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
        NotifyGuides();
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
        NotifyGuides();
    }

    private void NotifyGuides()
    {
        OnPropertyChanged(nameof(Guides));
        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(GridGuides));
        GuidesChanged?.Invoke();
        PublishSnapshot();
        MarkDocumentEdited();
    }

    /// <summary>The guides changed; the canvas redraws its chrome from this.</summary>
    public event Action? GuidesChanged;
}
