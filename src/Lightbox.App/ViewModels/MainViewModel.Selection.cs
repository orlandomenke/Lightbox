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
    // ---- selection --------------------------------------------------------------


    /// <summary>
    /// Current selection outlines for the canvas overlay, in <b>surface</b>
    /// coordinates — indexed from the paper's own corner, not from the document
    /// origin.
    /// </summary>
    /// <remarks>
    /// This said "document space" and was wrong, which is worse than saying
    /// nothing: the selection is a surface mask (see
    /// <c>PrepareClipForSelection</c>, which adds the origin back at the one
    /// boundary it crosses into the record), and a reader who believed the
    /// label would "fix" <see cref="SelectAll"/> — the one part of this that was
    /// always right.
    /// </remarks>
    public IReadOnlyList<List<StrokePoint>> SelectionContours => _selectionContours;

    public bool HasSelection => _selectionContours.Count > 0;

    /// <summary>Raised when the selection outline (or polygon-in-progress) changes.</summary>
    public event Action? SelectionChanged;

    [ObservableProperty]
    private double _selectionFeather;

    /// <summary>Pixels for the Grow/Shrink selection buttons.</summary>
    [ObservableProperty]
    private double _selectionAdjustPx = 2;

    private readonly List<StrokePoint> _polygonPoints = [];

    public IReadOnlyList<StrokePoint> PolygonInProgress => _polygonPoints;

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        SelectionChanged?.Invoke();
        PublishSnapshot();
    }

    /// <summary>Combine a closed shape into the selection (Shift adds, Alt subtracts).</summary>
    internal IReadOnlyList<List<StrokePoint>> SelectionContoursForTests => _selectionContours;

    public void ApplySelectionShape(List<StrokePoint> contour, bool add, bool subtract)
    {
        if (contour.Count < 3) return;
        int w = Scene.Width, h = Scene.Height;
        ApplySelectionMask(MaskFromContours([ToSurface(contour)], w, h), add, subtract);
    }

    /// <summary>
    /// A contour the canvas reported, in surface coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The canvas reports a marquee in document coordinates</b> — its
    /// <c>_dragShape</c> is built from <c>ViewToDoc</c> — and the selection is a
    /// surface mask, a <c>w × h</c> array of booleans whose contours are indexed
    /// from the bitmap's own corner. This is the boundary between those two
    /// facts, and it was missing: the contour went straight into
    /// <see cref="MaskFromContours"/>, which rasterizes into a surface-sized
    /// bitmap, so on a page whose origin is not zero every hand-drawn selection
    /// landed one origin away from the hand that drew it.
    /// </para>
    /// <para>
    /// <b>Measured before the fix</b>, on a page cropped to (100, 60): a marquee
    /// drawn at document x 200–400 was recorded as a clip at x 300–500. Paint
    /// was therefore stopped a hundred pixels from the marching ants — and
    /// because the clip is part of the record (invariant 3), the mistake was
    /// saved with the document rather than merely displayed.
    /// </para>
    /// <para>
    /// <b><see cref="SelectAll"/> does not come through here and must not.</b>
    /// It writes the surface rectangle directly, which is already correct;
    /// that asymmetry is why this survived, since the command every test
    /// reaches for first is the one that was never wrong.
    /// </para>
    /// </remarks>
    private List<StrokePoint> ToSurface(IReadOnlyList<StrokePoint> contour)
    {
        int dx = Scene.Left, dy = Scene.Top;
        if (dx == 0 && dy == 0) return [.. contour];
        return [.. contour.Select(p => p with { X = p.X - dx, Y = p.Y - dy })];
    }

    /// <summary>Combine any shape mask into the selection with the standard modifiers.</summary>
    private void ApplySelectionMask(bool[] shape, bool add, bool subtract)
    {
        int w = Scene.Width, h = Scene.Height;
        bool[] mask;
        if (!HasSelection || (!add && !subtract))
        {
            mask = subtract ? new bool[w * h] : shape;
        }
        else
        {
            mask = MaskFromContours(_selectionContours, w, h);
            for (var i = 0; i < mask.Length; i++)
            {
                if (add) mask[i] |= shape[i];
                else if (subtract) mask[i] &= !shape[i];
            }
        }
        SetSelectionFromMask(mask, w, h);
    }

    /// <summary>
    /// Ctrl+click on a layer thumbnail: select the layer's visible pixels
    /// (the exposed drawing at the playhead). Shift adds, Alt subtracts.
    /// </summary>
    public void SelectLayerAlpha(LayerRow row, bool add, bool subtract)
    {
        var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
        if (frame is null)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        int w = Scene.Width, h = Scene.Height;
        var bmp = _cache.Get(frame, w, h);
        var pixels = bmp.Pixels;
        var shape = new bool[w * h];
        var any = false;
        for (var i = 0; i < shape.Length; i++)
        {
            // Low threshold keeps soft brush edges inside the selection.
            if (pixels[i].Alpha > 25)
            {
                shape[i] = true;
                any = true;
            }
        }
        if (!any)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        ApplySelectionMask(shape, add, subtract);
    }

    /// <summary>Place one corner of a polygon selection.</summary>
    /// <remarks>
    /// <b>B216: snapped, unlike the lasso beside it.</b> A polygon vertex is
    /// clicked rather than traced — it is a point an artist is aiming, the same
    /// as a pen node or a shape corner — so it belongs to the set that snaps. A
    /// freehand lasso is a traced line and stays unsnapped, because pulling
    /// every sample onto a lattice fights the hand the whole way round, which
    /// is the rule the brush already follows for a stroke's middle.
    /// </remarks>
    public void AddPolygonVertex(double x, double y)
    {
        // Snapped first, in document coordinates, because that is where the
        // guides are — then converted, because that is where the mask is. The
        // two cannot swap: snapping a surface coordinate to a document guide
        // would pull the vertex by the origin.
        (x, y) = SnappedPoint(x, y);
        _polygonPoints.Add(new StrokePoint(x - Scene.Left, y - Scene.Top, 1));
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Close the polygon and combine it in.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not through <see cref="ApplySelectionShape"/>.</b> That
    /// one converts a document-space contour the canvas reported;
    /// <see cref="_polygonPoints"/> has already been converted, vertex by
    /// vertex, because the in-progress rubber band is drawn from the same list
    /// and the overlay is surface-space. Routing this through the converter
    /// again subtracts the origin twice — which is exactly what it did, and the
    /// polygon landed at surface x 2 instead of 100.
    /// </remarks>
    public void CompletePolygon(bool add, bool subtract)
    {
        var contour = new List<StrokePoint>(_polygonPoints);
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
        if (contour.Count < 3) return;
        int w = Scene.Width, h = Scene.Height;
        ApplySelectionMask(MaskFromContours([contour], w, h), add, subtract);
    }

    public void CancelPolygon() => CancelPolygonInProgress();

    private void CancelPolygonInProgress()
    {
        if (_polygonPoints.Count == 0) return;
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Whether Select All and Deselect should mean the objects on the canvas
    /// rather than a region of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B168.</b> Both commands used to ask <c>ActiveTool == ToolId.Select</c>,
    /// which names the wrong tool — and not by a near miss. <see cref="ToolId.Select"/>
    /// is the <em>marquee</em>: the tool whose entire job is making the region
    /// these commands then could not touch. The tools that pick objects are the
    /// two arrows, exactly as <c>ToolId.Arrow</c>'s own documentation says
    /// ("picks <b>things</b> … not areas of pixels").
    /// </para>
    /// <para>
    /// So the old condition was inverted in effect: Ctrl+D with the marquee in
    /// hand cleared object selections and left the marquee up, and Ctrl+A took
    /// all the objects instead of the canvas. One property, asked by both, so a
    /// fix to one cannot drift from the other.
    /// </para>
    /// </remarks>
    private bool ObjectSelectionIsTheSubject =>
        ActiveTool is ToolId.Arrow or ToolId.DirectSelect;

    [RelayCommand]
    private void SelectAll()
    {
        // With an arrow in hand, "all" means the objects; otherwise the canvas.
        if (ObjectSelectionIsTheSubject)
        {
            SelectEveryObject();
        }
        else
        {
            // Pixel/stroke selection (existing behavior)
            _selectionContours =
            [
                [new(0, 0, 1), new(Scene.Width, 0, 1), new(Scene.Width, Scene.Height, 1), new(0, Scene.Height, 1)],
            ];
            NotifySelection();
        }
    }

    /// <summary>
    /// Take every object on the canvas the arrows can pick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B221: every kind, in one selection.</b> This was a cascade of
    /// early returns — placements, or else guides, or else reference boxes, or
    /// else anchors, or else collision shapes — so "select all" meant "select
    /// all of whichever kind happens to be first and non-empty". On a drawing
    /// with a symbol on it, Ctrl+A took the symbol and left every line.
    /// </para>
    /// <para>
    /// <b>And lines were not in the list at all</b>, which is the half that
    /// makes it a defect rather than an inconsistency: strokes are the Arrow's
    /// primary subject — the thing its own documentation leads with — so on an
    /// ordinary drawing with no symbols and no guides, Ctrl+A fell through
    /// every arm and selected nothing.
    /// </para>
    /// <para>
    /// Cleared once at the top rather than per kind, because every
    /// <c>Select*</c> on the manager clears everything before it adds: doing it
    /// inside the loop is what made the cascade's early returns necessary in
    /// the first place, since the second kind would have wiped the first.
    /// </para>
    /// </remarks>
    private void SelectEveryObject()
    {
        _selectionManager.ClearAllSelections();

        // The lines, through the same list the Arrow's own picking uses, so
        // "all" and "the one under the pointer" cannot disagree about what is
        // pickable — a hidden or locked layer's strokes are neither.
        //
        // And through the picker's own filter (B243): this took the raw list,
        // so Ctrl+A selected the erasures and the wholly-erased lines that a
        // click and a marquee had just been taught to refuse (B232) — and
        // Delete then resurrected erased ink through the one door left open.
        var strokes = PickableStrokes();
        foreach (var position in Lightbox.Raster.StrokePicker.AllSelectable(strokes))
        {
            _selectionManager.AddStrokeToSelection(strokes[position].Id);
        }

        if (PaintTargetOrKey() is Frame { Placements: { Count: > 0 } placements })
        {
            foreach (var placement in placements)
            {
                _selectionManager.AddPlacementToSelection(placement.Id);
            }
        }

        if (Doc?.Scene?.Guides is { Count: > 0 } guides)
        {
            foreach (var guide in guides) _selectionManager.AddGuideToSelection(guide.Id);
        }

        if (ActiveReference?.Cells is { Count: > 0 } cells)
        {
            for (var i = 0; i < cells.Count; i++) _selectionManager.AddRefBoxToSelection(i);
        }

        // The rig's own objects only while the rig is being edited. Off, they
        // are not on screen and not pickable, so taking them would be a
        // selection of things the artist cannot see.
        if (!RigEditMode) return;

        if (Doc?.Scene?.Anchors is { Count: > 0 } anchors)
        {
            foreach (var anchor in anchors) _selectionManager.AddAnchorToSelection(anchor.Id);
        }

        if (Doc?.Scene?.Shapes is { Count: > 0 } shapes)
        {
            foreach (var shape in shapes) _selectionManager.AddShapeToSelection(shape.Id);
        }
    }

    /// <summary>
    /// Deselect: afterwards, nothing is selected.
    /// </summary>
    /// <remarks>
    /// <b>B168.</b> Unlike <see cref="SelectAll"/> this asks no question at all,
    /// and the asymmetry is the point. "Select all" has to know what *all*
    /// means, so it consults <see cref="ObjectSelectionIsTheSubject"/>; "none"
    /// means none, and a deselect that clears one kind of selection while
    /// leaving another live is the bug rather than a subtlety. It also means
    /// the artist can never be left holding a selection they cannot see how to
    /// remove — which is what made this a P1: a marquee clips painting, so the
    /// symptom of a failed deselect is <em>the application stopped drawing</em>.
    /// </remarks>
    [RelayCommand]
    private void Deselect()
    {
        // Already a no-op when nothing is picked — it guards its own event.
        _selectionManager.ClearAllSelections();
        if (!HasSelection && _polygonPoints.Count == 0) return;
        _selectionContours = [];
        _polygonPoints.Clear();
        NotifySelection();
    }

    /// <summary>
    /// Drop every selection without announcing it — for a document swap, where
    /// the announcement belongs to the swap rather than to the selection.
    /// </summary>
    /// <remarks>
    /// <b>B171.</b> Deliberately not <see cref="Deselect"/>: that is a command
    /// an artist issued and it publishes a snapshot, which during
    /// <c>AttachEditor</c> would publish a frame from a half-swapped document.
    /// </remarks>
    private void ClearSelectionState()
    {
        _selectionManager.ClearAllSelections();
        _selectionContours = [];
        _polygonPoints.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Arrow keys over the canvas: shift the selection outline by whole pixels.</summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        // A line selection wins, because the two cannot both be live in a way
        // that matters: the Arrow holds lines, the Select tools hold an area, and
        // only one of them is what the artist is looking at. Asked first rather
        // than given its own keys — `canvas.nudge*` is already the registered,
        // rebindable, canvas-scoped binding for "move what is selected", and
        // adding a second set would mean an artist rebinding one and finding the
        // other still on the old key.
        if (NudgeSelectionFromKeyboard(dx, dy, coarse: false)) return;
        if (!HasSelection) return;
        foreach (var contour in _selectionContours)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var p = contour[i];
                contour[i] = p with { X = p.X + dx, Y = p.Y + dy };
            }
        }
        NotifySelection();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        int w = Scene.Width, h = Scene.Height;
        var mask = HasSelection ? MaskFromContours(_selectionContours, w, h) : new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = !mask[i];
        SetSelectionFromMask(mask, w, h);
    }

    [RelayCommand]
    private void GrowSelection() => AdjustSelection(Math.Abs(SelectionAdjustPx));

    [RelayCommand]
    private void ShrinkSelection() => AdjustSelection(-Math.Abs(SelectionAdjustPx));

    private void AdjustSelection(double px)
    {
        if (!HasSelection || Math.Abs(px) < 0.5) return;
        int w = Scene.Width, h = Scene.Height;
        // Boundary included: these contours were traced off a mask, and
        // without their own boundary ring back the shape walks into its
        // top-left corner a little on every adjustment.
        var mask = MaskFromContours(_selectionContours, w, h, includeBoundary: true);
        var r = (int)Math.Round(Math.Abs(px));
        mask = px > 0 ? FloodFill.Dilate(mask, w, h, r) : FloodFill.Erode(mask, w, h, r);
        SetSelectionFromMask(mask, w, h);
    }

    private void SetSelectionFromMask(bool[] mask, int w, int h)
    {
        _selectionContours = FloodFill.TraceAllContours(mask, w, h);
        NotifySelection();
    }

    /// <summary>Rasterize contours (even-odd) to a boolean mask.</summary>
    /// <param name="includeBoundary">
    /// Also paint the boundary pixels the contour runs through.
    /// </param>
    /// <remarks>
    /// <para>
    /// Off for an ordinary mask: a contour drawn by hand is a geometric
    /// outline and filling it is exactly right.
    /// </para>
    /// <para>
    /// On when the contour <i>came from</i> a mask. <c>TraceBoundary</c> walks
    /// pixel centres, so the polygon it returns runs down the middle of the
    /// boundary ring rather than around the outside of it; filling that back
    /// keeps only the pixels whose centres are strictly inside, and Skia's
    /// fill rule resolves the exact-half case towards the bottom right. The
    /// top and left rings are therefore lost on every round trip, and Shrink
    /// followed by Grow walked a selection two pixels into its own top-left
    /// corner each time. Stroking the same path with a one-pixel pen puts the
    /// ring back, which makes the round trip stable — the property Grow and
    /// Shrink actually need.
    /// </para>
    /// </remarks>
    private static bool[] MaskFromContours(
        IReadOnlyList<List<StrokePoint>> contours, int w, int h, bool includeBoundary = false)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create mask surface.");
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        using (var path = BrushEngine.PathFromContours(contours))
        using (var paint = new SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = false })
        {
            surface.Canvas.DrawPath(path, paint);
            if (includeBoundary)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1;
                surface.Canvas.DrawPath(path, paint);
            }
        }
        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var pixels = bmp.Pixels;
        var mask = new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = pixels[i].Alpha > 127;
        return mask;
    }

    /// <summary>The selection as a flood-fill constraint (null when nothing is selected).</summary>
    private bool[]? SelectionMask(int w, int h) =>
        HasSelection ? MaskFromContours(_selectionContours, w, h) : null;

    /// <summary>
    /// Freeze the active selection as a document clip region (content-hashed,
    /// deduped) so strokes painted under it re-render identically forever.
    /// </summary>
    /// <summary>
    /// Where the paper's top-left sits in stroke coordinates, for the canvas
    /// control's pointer conversions.
    /// </summary>
    public Avalonia.PixelPoint DocumentOrigin => new(Scene.Left, Scene.Top);

    /// <summary>
    /// A stroke coordinate as a pixel in a document-sized bitmap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three tools index pixels rather than reasoning about the record</b> —
    /// flood fill, the wand and the colour picker — and they are the only place
    /// in this file that needs this. Everything else works in stroke
    /// coordinates, which is what <c>CanvasControl.ViewToDoc</c> now hands over.
    /// </para>
    /// <para>
    /// The two spaces are the same until the canvas is grown or cropped on the
    /// left or top, which is exactly why this is easy to forget and impossible
    /// to notice: every test written on an unresized document passes either way.
    /// </para>
    /// </remarks>
    private (int X, int Y) ToSurface(double x, double y) =>
        ((int)Math.Round(x - Scene.Left), (int)Math.Round(y - Scene.Top));

    /// <summary>Surface-space contours as stroke coordinates.</summary>
    /// <remarks>
    /// The other direction, for the two results that become part of the record:
    /// a fill's outline (invariant 1) and a selection's clip region
    /// (invariant 3). Both come out of <c>FloodFill</c> indexed from a bitmap's
    /// own corner and have to be told where that corner is.
    /// </remarks>
    private List<List<StrokePoint>> ToDocument(IEnumerable<List<StrokePoint>> contours)
    {
        int dx = Scene.Left, dy = Scene.Top;
        return [.. contours.Select(c => dx == 0 && dy == 0
            ? new List<StrokePoint>(c)
            : [.. c.Select(pt => pt with { X = pt.X + dx, Y = pt.Y + dy })])];
    }

    private (string Id, ClipRegion Region)? PrepareClipForSelection()
    {
        if (!HasSelection) return null;
        var region = new ClipRegion
        {
            // The selection is kept as a surface mask, because that is what it
            // is; a clip region is part of the record, so it crosses here.
            Contours = ToDocument(_selectionContours),
            Feather = SelectionFeather,
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(region);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)))[..12];
        var id = $"clip_{hash.ToLowerInvariant()}";
        // The renderer resolves clips by id, so it must know this one before
        // the stroke's first re-render — not only after a document reload.
        ClipRegionRegistry.Register(id, region);
        return (id, region);
    }

    // Onion skin's settings live in AppSettings, not here and not in the
    // document: it is a drawing aid that never reaches pixels, and an
    // animator's depth and falloff are how they work rather than a property of
    // each scene. These forward, so the views and the existing tests keep the
    // names they had.

    public bool OnionSkin
    {
        get => Onion.Enabled;
        set => SetOnion(value, v => Onion.Enabled = v, Onion.Enabled);
    }

    /// <summary>Drawings shown behind and ahead. One number sets both.</summary>
    public int OnionDepth
    {
        get => Math.Max(Onion.Before, Onion.After);
        set
        {
            var depth = Math.Clamp(value, 0, 10);
            if (Onion.Before == depth && Onion.After == depth) return;
            Onion.Before = Onion.After = depth;
            AfterOnionChange();
            OnPropertyChanged(nameof(OnionBefore));
            OnPropertyChanged(nameof(OnionAfter));
        }
    }

    public int OnionBefore
    {
        get => Onion.Before;
        set => SetOnion(Math.Clamp(value, 0, 10), v => Onion.Before = v, Onion.Before, nameof(OnionDepth));
    }

    public int OnionAfter
    {
        get => Onion.After;
        set => SetOnion(Math.Clamp(value, 0, 10), v => Onion.After = v, Onion.After, nameof(OnionDepth));
    }

    public double OnionOpacity
    {
        get => Onion.Opacity;
        set => SetOnion(Math.Clamp(value, 0.02, 1), v => Onion.Opacity = v, Onion.Opacity);
    }

    /// <summary>
    /// Setting this marks the falloff as chosen, so no future default revises
    /// it. See <see cref="Services.OnionSettings.FalloffChosen"/>.
    /// </summary>
    public double OnionFalloff
    {
        get => Onion.Falloff;
        set => SetOnion(Math.Clamp(value, 0.05, 1), v =>
        {
            Onion.Falloff = v;
            Onion.FalloffChosen = true;
        }, Onion.Falloff);
    }

    public bool OnionKeysOnly
    {
        get => Onion.KeysOnly;
        set => SetOnion(value, v => Onion.KeysOnly = v, Onion.KeysOnly);
    }

    public bool OnionDrawOver
    {
        get => Onion.DrawOver;
        set => SetOnion(value, v => Onion.DrawOver = v, Onion.DrawOver);
    }

    public string OnionPreviousTint
    {
        get => Onion.PreviousTint;
        set => SetOnion(value, v => Onion.PreviousTint = v, Onion.PreviousTint);
    }

    public string OnionNextTint
    {
        get => Onion.NextTint;
        set => SetOnion(value, v => Onion.NextTint = v, Onion.NextTint);
    }

    public IReadOnlyList<Services.OnionMode> OnionModeChoices { get; } =
        Enum.GetValues<Services.OnionMode>();

    public Services.OnionMode OnionMode
    {
        get => Onion.Mode;
        set => SetOnion(value, v => Onion.Mode = v, Onion.Mode, nameof(IsLightTable));
    }

    /// <summary>
    /// A light table shows the other layers at this instant rather than this
    /// layer's own neighbours in time — a genuinely different question, and
    /// the reason it is a mode rather than another depth.
    /// </summary>
    public bool IsLightTable => Onion.Mode == Services.OnionMode.LightTable;

    private void SetOnion<T>(T value, Action<T> apply, T current, string? also = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        apply(value);
        OnPropertyChanged(name);
        if (also is not null) OnPropertyChanged(also);
        AfterOnionChange();
    }

    /// <summary>
    /// Repaint, and remember. Onion settings persist across sessions because
    /// setting them up again every morning is the kind of small friction that
    /// makes a tool feel like it is not paying attention.
    /// </summary>
    private void AfterOnionChange()
    {
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        // The armature's ghosts read the same switch and depths the drawing's
        // ghosts do, so the skeleton chrome moves when the onion bar does —
        // and the ghost memo is keyed by playhead alone, so a depth change
        // has to drop it by hand.
        _ghostChromeCache = null;
        OnPropertyChanged(nameof(BoneChromes));
        Settings.Save();
    }
}
