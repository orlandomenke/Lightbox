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
    // ---- active tool ----------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEraser))]
    [NotifyPropertyChangedFor(nameof(ShowsEffectOptions))]
    [NotifyPropertyChangedFor(nameof(PointerIntent))]
    [NotifyPropertyChangedFor(nameof(PointerRefusal))]
    [NotifyPropertyChangedFor(nameof(ActiveToolIcon))]
    [NotifyPropertyChangedFor(nameof(IsBrushTool))]
    [NotifyPropertyChangedFor(nameof(IsEraserTool))]
    [NotifyPropertyChangedFor(nameof(IsFillTool))]
    [NotifyPropertyChangedFor(nameof(IsSelectTool))]
    [NotifyPropertyChangedFor(nameof(IsPickerTool))]
    [NotifyPropertyChangedFor(nameof(IsGradientTool))]
    [NotifyPropertyChangedFor(nameof(IsMoveTool))]
    [NotifyPropertyChangedFor(nameof(ReachesGuides))]
    // Missing, and it cost the whole shape options group: nothing ever told
    // the bar the tool had changed, so IsVisible stayed false and there was no
    // way to pick a shape.
    [NotifyPropertyChangedFor(nameof(IsShapeTool))]
    [NotifyPropertyChangedFor(nameof(IsPaintTool))]
    [NotifyPropertyChangedFor(nameof(MakesSizedMarks))]
    [NotifyPropertyChangedFor(nameof(IsArrowTool))]
    [NotifyPropertyChangedFor(nameof(IsDirectSelectTool))]
    [NotifyPropertyChangedFor(nameof(IsPenTool))]
    [NotifyPropertyChangedFor(nameof(IsWidthTool))]
    [NotifyPropertyChangedFor(nameof(IsBoneTool))]
    [NotifyPropertyChangedFor(nameof(ActiveToolLabel))]
    [NotifyPropertyChangedFor(nameof(ActiveToolHasNoPanelOptions))]
    private ToolId _activeTool = ToolId.Brush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectVariantGlyph))]
    [NotifyPropertyChangedFor(nameof(IsFreehandVariant))]
    [NotifyPropertyChangedFor(nameof(IsPolygonVariant))]
    [NotifyPropertyChangedFor(nameof(IsBoxVariant))]
    [NotifyPropertyChangedFor(nameof(IsEllipseVariant))]
    [NotifyPropertyChangedFor(nameof(IsWandVariant))]
    private SelectVariant _activeSelectVariant = SelectVariant.Freehand;

    public bool IsFreehandVariant => ActiveSelectVariant == SelectVariant.Freehand;

    public bool IsPolygonVariant => ActiveSelectVariant == SelectVariant.Polygon;

    public bool IsBoxVariant => ActiveSelectVariant == SelectVariant.Box;

    public bool IsEllipseVariant => ActiveSelectVariant == SelectVariant.Ellipse;

    public bool IsWandVariant => ActiveSelectVariant == SelectVariant.Wand;

    public bool IsBrushTool => ActiveTool == ToolId.Brush;

    /// <summary>The active tool's icon, for the Quick options bar's left edge.</summary>
    public Avalonia.Media.Geometry? ActiveToolIcon =>
        Rendering.IconSet.Resolve(Rendering.IconSet.ForTool(ActiveTool));

    /// <summary>
    /// What the pointer should say the active tool will do over the active
    /// layer — bound straight onto the canvas control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision itself is <see cref="Rendering.CanvasCursor"/>, which is
    /// pure and tested without a window; this only gathers the facts. Keeping
    /// the gathering here and the rule there is what stops the canvas control
    /// and anything else that reports the same refusal from drifting apart.
    /// </para>
    /// <para>
    /// <b>Two of the five facts are still assumed, and they are the two that
    /// vary per pixel rather than per layer:</b> whether the pointer is inside
    /// the selection and whether there is paint under it for an alpha-locked
    /// layer. Both need a hover position the view model is not told about yet,
    /// so they are left at their permissive default — the cursor under-reports
    /// rather than lying, which is the right way round for a refusal.
    /// </para>
    /// </remarks>
    public Rendering.CanvasCursorKind PointerIntent =>
        // The Bone tool is the one whose gesture changes under the pointer,
        // so it asks what is beneath rather than what is in hand. Everything
        // else does one thing wherever it presses.
        ActiveTool == ToolId.Bone
            ? Rendering.CanvasCursor.ForBone(HoveredBoneGrab, PosingMode, WeightPainting)
            : Rendering.CanvasCursor.For(ActiveTool, CurrentTarget, _hoverModifiers);

    /// <summary>What the pointer is over, through the same hit-test the gesture uses.</summary>
    private Rendering.BoneGrab HoveredBoneGrab =>
        _hoverPoint is { } p
            ? Rendering.ArmatureOverlay.Hit(BoneChromes, p.X, p.Y, _hoverScale).Grab
            : Rendering.BoneGrab.None;

    private double _hoverScale = 1.0;

    /// <summary>Why the active tool would do nothing here, or null.</summary>
    /// <remarks>
    /// Nothing shows this yet: the application has no status line, and inventing
    /// one to carry a sentence is a bigger decision than this change. The
    /// pointer already refuses, which is the half an artist notices; this is the
    /// half that says why, and it is ready for the surface that will hold it.
    /// </remarks>
    public string? PointerRefusal =>
        Rendering.CanvasCursor.Refusal(ActiveTool, CurrentTarget, _hoverModifiers);

    /// <summary>Whether the refusal has anything to say, for the status line.</summary>
    public bool HasPointerRefusal => PointerRefusal is not null;

    private Rendering.CanvasTarget CurrentTarget
    {
        get
        {
            var alphaLocked = ActiveLayer.AlphaLocked;
            return new Rendering.CanvasTarget(
                LayerHidden: !ActiveLayer.Visible,
                LayerLocked: ActiveLayer.Locked,
                AlphaLocked: alphaLocked,
                // The two that need a place. With the pointer off the canvas
                // there is no place, so both stay permissive — the cursor
                // under-reports rather than inventing a refusal.
                NothingUnderPointer:
                    alphaLocked && _hoverPoint is { } a && !PaintUnder(a.X, a.Y),
                OutsideSelection:
                    _hoverPoint is { } b && !InsideSelection(b.X, b.Y));
        }
    }

    /// <summary>
    /// Re-ask the mapping, because something it reads has changed underneath it.
    /// </summary>
    /// <remarks>
    /// <b>Needed because a layer's flags are plain properties on a document
    /// model that raises nothing.</b> `Layer.Visible` and `Layer.Locked` are set
    /// through the editor so they undo correctly, and neither notifies — so
    /// hiding the layer you are drawing on would otherwise leave the pointer
    /// still promising a stroke until you happened to switch tools.
    /// </remarks>
    public void RefreshPointerIntent()
    {
        OnPropertyChanged(nameof(PointerIntent));
        OnPropertyChanged(nameof(PointerRefusal));
        OnPropertyChanged(nameof(HasPointerRefusal));
    }

    private Avalonia.Input.KeyModifiers _hoverModifiers;
    private (double X, double Y)? _hoverPoint;

    /// <summary>
    /// The pointer moved: remember where, and re-ask what the tool would do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what lets the last two refusals be true rather than assumed.</b>
    /// Whether the pointer is inside the selection and whether there is paint
    /// under an alpha-locked brush are the only two facts that vary pixel by
    /// pixel rather than per layer, so they need a position and nothing else did.
    /// </para>
    /// <para>
    /// <b>Bounded, as invariant 6 requires.</b> The selection test is
    /// point-in-polygon over the outline — proportional to the contour, not to
    /// the canvas — where <c>SelectionMask</c> would rasterise a full
    /// <c>w × h</c> mask on every move. The alpha read is one pixel, and only
    /// when the layer is alpha-locked, because that is the only case whose
    /// answer is used.
    /// </para>
    /// </remarks>
    public void UpdatePointerContext(
        double x, double y, Avalonia.Input.KeyModifiers modifiers, double scale = 1.0)
    {
        _hoverPoint = (x, y);
        _hoverModifiers = modifiers;
        _hoverScale = scale;
        RefreshPointerIntent();
        RefreshPickPreview();
    }

    /// <summary>The pointer left the canvas: stop claiming to know where it is.</summary>
    public void ClearPointerContext()
    {
        if (_hoverPoint is null) return;
        _hoverPoint = null;
        RefreshPointerIntent();
        RefreshPickPreview();
    }

    /// <summary>
    /// Re-read the colour under the pointer, or drop it when nothing would be
    /// picked there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called from the pointer paths and from the tool change, and from
    /// nowhere else.</b> The canvas already coalesces hover reports to one per
    /// document pixel crossed, so this runs when the answer can actually have
    /// changed rather than per pointer event — which is what makes a composite
    /// read affordable here at all. The tool change is the other moment: an
    /// artist who presses <c>I</c> without moving the pointer, or borrows the
    /// eyedropper by holding Ctrl mid-stroke, has changed the answer without
    /// moving anything.
    /// </para>
    /// <para>
    /// Not folded into <see cref="RefreshPointerIntent"/>, which is called from
    /// layer edits and stroke commits where a bitmap read would be a cost paid
    /// for nothing.
    /// </para>
    /// </remarks>
    private void RefreshPickPreview() =>
        PickPreviewHex =
            PointerIntent == Rendering.CanvasCursorKind.Pick && _hoverPoint is { } p
                ? PickedColorHexAt(p.X, p.Y)
                : null;

    /// <summary>Whether a stroke coordinate falls inside the active selection.</summary>
    /// <remarks>
    /// Even-odd, matching <c>BrushEngine.PathFromContours</c> — a hole in a
    /// selection is outside it, and the cursor has to agree with the renderer
    /// about that or it forbids in the wrong places.
    /// </remarks>
    private bool InsideSelection(double x, double y)
    {
        if (!HasSelection) return true;
        // Surface space: the contours are the mask's, and so is the question.
        var (px, py) = (x - Scene.Left, y - Scene.Top);
        var inside = false;
        foreach (var contour in _selectionContours)
        {
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var a = contour[i];
                var b = contour[j];
                if (a.Y > py != b.Y > py &&
                    px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }
        }
        return inside;
    }

    /// <summary>Whether the active layer has any paint at a stroke coordinate.</summary>
    /// <remarks>
    /// Only asked when the layer is alpha-locked, because that is the only time
    /// the answer changes what the pointer says — and it costs a bitmap read, so
    /// asking it otherwise would be paying per pointer event for nothing.
    /// </remarks>
    private bool PaintUnder(double x, double y)
    {
        var px = (int)Math.Round(x - Scene.Left);
        var py = (int)Math.Round(y - Scene.Top);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return false;
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return false;
        try
        {
            return _cache.Get(frame, Scene.Width, Scene.Height).GetPixel(px, py).Alpha > 0;
        }
        catch
        {
            // A cache miss mid-resize is not worth a crash on a hover; assume
            // paint, which is the permissive answer and never forbids wrongly.
            return true;
        }
    }

    /// <summary>
    /// Tell the view the paper moved, after a resize changed the origin.
    /// </summary>
    /// <remarks>
    /// <c>Scene.OriginX</c> is a plain field on the document model, so nothing
    /// is raised when a resize changes it — and every pointer conversion in the
    /// canvas reads it. Without this the tools would keep converting against the
    /// old corner, which lands strokes exactly one resize out of place.
    /// </remarks>
    public void RefreshDocumentOrigin() => OnPropertyChanged(nameof(DocumentOrigin));

    /// <summary>
    /// Apply a resize the artist confirmed, as one undo step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One <c>Perform</c> around the whole thing</b>, and that is not
    /// bookkeeping: an image resize walks every stroke, clip contour, guide,
    /// camera key, symbol placement and collision box in the document. An artist
    /// who had to press undo once per stroke would have lost the drawing.
    /// </para>
    /// <para>
    /// Here rather than in the window because it changes the document, and
    /// because everything that has to be told afterwards — the origin the
    /// pointer converts against, the caches, the snapshot — is already this
    /// type's to notify. The window's remaining job is the window: show the
    /// dialog, then refit the view to paper that is now a different size.
    /// </para>
    /// </remarks>
    public bool ApplyResize(ResizeDialogViewModel choice)
    {
        var changed = false;
        _editor.Perform(doc => changed = choice.Apply(doc, new Lightbox.Raster.PixelResampler()));
        if (!changed) return false;

        RefreshDocumentOrigin();
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = choice.IsImage
            ? $"Resized the image to {Scene.Width} × {Scene.Height}."
            : $"Resized the canvas to {Scene.Width} × {Scene.Height}.";
        return true;
    }

    /// <summary>The black arrow — picks things (lines, guides, symbols) rather than an area of pixels.</summary>
    public bool IsArrowTool => ActiveTool == ToolId.Arrow;

    /// <summary>The white arrow — nodes and handles on one isolated line.</summary>
    public bool IsDirectSelectTool => ActiveTool == ToolId.DirectSelect;

    /// <summary>The pen — places nodes and draws the curve between them.</summary>
    public bool IsPenTool => ActiveTool == ToolId.Pen;

    /// <summary>The width tool — drag off a line to fatten it, towards it to thin it.</summary>
    public bool IsWidthTool => ActiveTool == ToolId.Width;

    public bool IsEraserTool => ActiveTool == ToolId.Eraser;

    public bool IsFillTool => ActiveTool == ToolId.Fill;

    public bool IsSelectTool => ActiveTool == ToolId.Select;

    public bool IsPickerTool => ActiveTool == ToolId.Picker;

    public bool IsGradientTool => ActiveTool == ToolId.Gradient;

    public bool IsMoveTool => ActiveTool == ToolId.Move;

    /// <summary>
    /// Whether the tool in hand reaches for guides — picks them, moves them,
    /// and shows their numbers.
    /// </summary>
    /// <remarks>
    /// <b>B215.</b> Two tools, one answer, asked by everything that used to ask
    /// <see cref="IsMoveTool"/> about guides: the grab gate on the canvas, the
    /// emphasis the rig is drawn with, and the options bar and panel. They had
    /// drifted apart — the Arrow could select a guide through a hit test of its
    /// own while the bar stayed blank and the drag gate stayed shut — and one
    /// property is what stops them drifting again.
    ///
    /// <para>
    /// Not the whole answer on its own: hiding or locking the guides overrides
    /// it, whatever tool is in hand. That half lives with the workspace, which
    /// is where those two switches are.
    /// </para>
    /// </remarks>
    public bool ReachesGuides => ActiveTool is ToolId.Move or ToolId.Arrow;

    public bool IsBoneTool => ActiveTool == ToolId.Bone;

    /// <summary>Brush or eraser — the tools whose strokes the brush-parameter flyout edits.</summary>
    public bool IsPaintTool => ActiveTool is ToolId.Brush or ToolId.Eraser;

    /// <summary>
    /// Whether the tool in hand makes a mark that takes the pinned Size and
    /// Opacity — brush, eraser, and shape, because a shape is a stroke drawn
    /// with the loaded brush. The Quick options bar disables (never hides)
    /// its fixed section on this, per Q70.
    /// </summary>
    public bool MakesSizedMarks => ActiveTool is ToolId.Brush or ToolId.Eraser or ToolId.Shape;

    /// <summary>The active tool, named for the Tool options panel's header.</summary>
    public string ActiveToolLabel => ActiveTool switch
    {
        ToolId.Brush => "Brush",
        ToolId.Eraser => "Eraser",
        ToolId.Fill => "Fill",
        ToolId.Select => "Select",
        ToolId.Gradient => "Gradient",
        ToolId.Shape => "Shape",
        ToolId.Move => "Move",
        ToolId.Picker => "Colour picker",
        ToolId.Arrow => "Arrow",
        ToolId.DirectSelect => "Direct select",
        ToolId.Pen => "Pen",
        ToolId.Width => "Width",
        // B221. It fell through to ToString() and read "Bone", which is the
        // right word by luck rather than by decision — the fallback is there so
        // a tool added tomorrow shows *something*, not so a shipped tool can
        // skip the table. Every other tool whose label differs from its enum
        // name is in here; the one that agreed by accident was the one nobody
        // noticed was missing.
        ToolId.Bone => "Bone",
        _ => ActiveTool.ToString(),
    };

    /// <summary>
    /// Tools whose whole vocabulary already fits on the bar — the Tool options
    /// panel has nothing to add for them, and says so instead of going blank.
    /// </summary>
    public bool ActiveToolHasNoPanelOptions => ActiveTool is
        ToolId.Move or ToolId.Picker or ToolId.Arrow or
        ToolId.DirectSelect or ToolId.Pen or ToolId.Width;

    /// <summary>Eyedropper click: the color under the cursor (what the eye sees, incl. paper).</summary>
    public void PickColorAt(double x, double y)
    {
        if (PickedColorHexAt(x, y) is { } hex) ColorHex = hex;
    }

    /// <summary>
    /// The colour the eyedropper would take at a document position, or null
    /// where it would take none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One definition, because two things ask.</b> The click sets the colour
    /// and the ring around the pointer promises what the click will set — and a
    /// promise computed separately from the thing it promises is a promise that
    /// will eventually be broken, quietly, in the case nobody tested. The paper
    /// fallback below is exactly such a case: transparent pixels resolve to the
    /// paper colour, and a preview that had not been told would show the ring's
    /// top half empty over most of a fresh drawing.
    /// </para>
    /// <para>
    /// Null for "off the paper" rather than a colour, so the caller can tell
    /// nothing-to-pick apart from picked-something-transparent. The click uses
    /// it to leave the colour alone; the ring uses it to not appear.
    /// </para>
    /// </remarks>
    public string? PickedColorHexAt(double x, double y)
    {
        var (px, py) = ToSurface(x, y);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return null;
        var color = SampleVisibleComposite(px, py);
        return color.Alpha == 0
            ? Scene.TransparentBackground ? "#ffffff" : Scene.BackgroundColor
            : $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";
    }

    /// <summary>
    /// The colour under the pointer while the eyedropper is armed, for the ring
    /// the canvas draws around it — null whenever there is no ring to draw.
    /// </summary>
    /// <remarks>
    /// Absent rather than stale. It is cleared the moment the pointer leaves the
    /// canvas or the tool stops being an eyedropper, so the canvas never has to
    /// decide whether what it was handed is still true.
    /// </remarks>
    [ObservableProperty]
    private string? _pickPreviewHex;

    /// <summary>
    /// What marking on a held cel does.
    /// </summary>
    /// <remarks>
    /// A preference, not document data, and the default is the animator's
    /// answer: a hold is somebody else's drawing shown again, and drawing on
    /// it silently rewrites the frame you were holding — every mark you make
    /// at frame 4 appears at frame 3 as well, which is a very confusing way to
    /// ruin a hold. Keying first means the timeline shows a new drawing where
    /// you made one.
    /// </remarks>
    public HoldDrawing DrawingOnAHold
    {
        get => Enum.TryParse<HoldDrawing>(Settings.DrawingOnAHold, out var v) ? v : HoldDrawing.StartANewDrawing;
        set
        {
            if (DrawingOnAHold == value) return;
            Settings.DrawingOnAHold = value.ToString();
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<HoldDrawing> HoldDrawingChoices { get; } = Enum.GetValues<HoldDrawing>();

    /// <summary>Timeline-context shortcut: key the active layer's cel at the playhead.</summary>
    public void InsertKeyframeAtPlayhead() =>
        _editor.SetKeyAt(ActiveLayer.Id, CurrentFrameIndex, FrameRole.Key);

    /// <summary>The tool button's icon, so it says which selection it will make.</summary>
    /// <remarks>
    /// The wand variant used to be U+1FA84, an emoji: full colour beside eleven
    /// monoline glyphs on the platforms that had the character, and an empty box
    /// on the ones that did not.
    /// </remarks>
    public Avalonia.Media.Geometry? SelectVariantGlyph =>
        Rendering.IconSet.Resolve(Rendering.IconSet.ForSelect(ActiveSelectVariant));

    /// <summary>Compat view of the tool (old XAML/tests): eraser vs everything else painting as brush.</summary>
    public bool IsEraser
    {
        get => ActiveTool == ToolId.Eraser;
        set => ActiveTool = value ? ToolId.Eraser : ToolId.Brush;
    }

    partial void OnActiveToolChanged(ToolId value)
    {
        // The bound sliders edit the active tool's brush configuration.
        NotifyBrushProperties();
        OnPropertyChanged(nameof(LazyRadiusForCursor));
        // Display, so it belongs above the borrow guard below: picking up the
        // eyedropper without moving the pointer still has to put the ring on.
        RefreshPickPreview();

        // A borrow is not a decision — see MainViewModel.Momentary.cs. Everything
        // below is about an artist choosing to leave a tool, and holding Ctrl is
        // not that. The two lines above are display, which a borrow does want:
        // the picker's cursor is the whole reason the borrow is legible.
        //
        // Everything that throws modal work away lives below this line, the
        // half-drawn lasso included. It was above it, which was harmless only
        // because the Select tool is not borrowable — and "harmless because of
        // a fact recorded somewhere else" is what makes adding a row to the
        // table a thing to reason about instead of a thing to do.
        if (_suppressToolSideEffects) return;

        LeaveToolStateBehind(value);
    }

    /// <summary>
    /// What choosing a tool does to the one being left: modal work is let go.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OnActiveToolChanged"/> because a momentary
    /// key's tap-latch (B176) needs to run these consequences *after* the
    /// switch: the press borrowed without side effects — it could not know yet
    /// whether it was a tap or a hold — and the release finding a tap makes the
    /// switch a decision retroactively.
    /// </remarks>
    private void LeaveToolStateBehind(ToolId value)
    {
        // B208. Reaching for a tool means you are done transforming, so the
        // session goes — and it discards rather than applies, because the
        // preview was never an edit (invariant 1) and committing the document
        // from a gesture that never said "apply" is the worse mistake of the
        // two. Enter is still the only thing that writes.
        //
        // Unconditional on the tool being chosen, unlike everything below it:
        // there is no tool that can go on working a gizmo session, the Move
        // tool included — that one opens a session of its own on the press.
        //
        // Worth knowing that the comment below has claimed this behaviour since
        // B147 ("Every other modal thing here behaves the same way ... the
        // transform session") and the line was never here. Ctrl+T then survived
        // every tool change while the canvas quietly went back to painting,
        // which is the other half of what B208 reported.
        if (TransformActive) CancelTransform();

        CancelPolygonInProgress();

        // B147: leaving the arrow lets the lines go. A stroke selection is only
        // reachable from the arrow — nothing else moves, recolours or deletes it
        // — so a highlight that outlives the tool is drawn state pointing at a
        // capability the artist no longer has. Every other modal thing here
        // behaves the same way (the polygon above it, the transform session),
        // which is why this is one line rather than a mode.
        if (value != ToolId.Arrow) ClearStrokeSelection();

        // B215: the same rule, applied to the category it had been leaving out.
        // Guides are picked by the Arrow and by the Move tool, so a selection
        // survives between those two — that is one capability handed between
        // two tools that both have it, not drawn state outliving its tool. Any
        // other tool and it goes, for the reason above it: a guide left lit
        // while the brush paints over it is pointing at something the artist
        // can no longer do.
        if (!ReachesGuides) Selection.ClearGuideSelection();

        // Same one-line rule for the hover preview: it is drawn state that only
        // the white arrow can act on, so it must not outlive the tool. Without
        // this the last-hovered line keeps its points on screen while the brush
        // is painting over them.
        if (value != ToolId.DirectSelect) ClearPathHover();

        // B217: the same one-line rule for the whole-line preview. It belongs
        // to the two tools that take a whole line, and outside them it is an
        // outline drawn over artwork nothing is about to pick up.
        if (value is not (ToolId.Arrow or ToolId.Width)) ClearStrokeHover();

        // The pen parks rather than committing: the path in progress survives
        // the switch, stays traced on screen, and the pen resumes it. It used
        // to finish here — which kept the work (right) but ended a path the
        // artist meant to come back to (wrong). Enter and Escape are still the
        // deliberate finish, and neither discards.
        if (value != ToolId.Pen) ParkPen();

        // B147's shape one tool along, and phase 2 shipped it: the node overlay
        // is drawn whatever the tool is, so leaving isolation for the brush left
        // glyphs on screen over a line nothing could reshape any more. Only the
        // tools that can still work the session keep it — the white arrow that
        // edits nodes and the width tool that shares it. The black arrow used to
        // keep it too, and that read as stuck: its clicks answer to isolation's
        // lock, so the artist saw nodes they could not drag and other lines that
        // would not select. Choosing a tool that cannot work the session is
        // leaving it.
        if (value is not (ToolId.DirectSelect or ToolId.Width)) EndPathEdit();
    }

    [RelayCommand]
    private void SelectTool(ToolId tool)
    {
        if (tool == ToolId.Select && ActiveTool == ToolId.Select)
        {
            CycleSelectVariant();
            return;
        }
        ActiveTool = tool;
    }

    /// <summary>Pressing the selection shortcut repeatedly cycles its variants.</summary>
    public void CycleSelectVariant()
    {
        ActiveSelectVariant = ActiveSelectVariant switch
        {
            SelectVariant.Freehand => SelectVariant.Polygon,
            SelectVariant.Polygon => SelectVariant.Box,
            SelectVariant.Box => SelectVariant.Ellipse,
            SelectVariant.Ellipse => SelectVariant.Wand,
            _ => SelectVariant.Freehand,
        };
    }

    [RelayCommand]
    private void SelectVariantOf(SelectVariant variant)
    {
        ActiveSelectVariant = variant;
        ActiveTool = ToolId.Select;
    }

    // ---- magic wand -------------------------------------------------------------

    [ObservableProperty]
    private double _wandTolerance = 32;

    /// <summary>Openings up to this many pixels read as closed for the wand.</summary>
    [ObservableProperty]
    private double _wandGapPx;

    /// <summary>Sample the composited visible layers instead of only the active one.</summary>
    [ObservableProperty]
    private bool _wandSampleAllLayers = true;

    /// <summary>Magic-wand click: select the connected color region at a document position.</summary>
    public void WandSelectAt(double x, double y, bool add, bool subtract)
    {
        if (ActiveTool != ToolId.Select || IsPlaying) return;
        int w = Scene.Width, h = Scene.Height;
        SKBitmap? owned = null;
        try
        {
            SKBitmap sample;
            if (WandSampleAllLayers)
            {
                owned = CompositeVisibleLayers();
                sample = owned;
            }
            else
            {
                var frame = ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex);
                if (frame is null)
                {
                    AiStatus = "The active layer has nothing drawn to select here.";
                    return;
                }
                sample = _cache.Get(frame, w, h);
            }

            var (wandX, wandY) = ToSurface(x, y);
            var result = FloodFill.Fill(
                sample, wandX, wandY,
                new FloodFill.Options(WandTolerance, WandGapPx));
            if (result is null)
            {
                AiStatus = "Nothing selectable at that spot.";
                return;
            }
            var contours = new List<List<StrokePoint>> { result.Outer };
            contours.AddRange(result.Holes);
            ApplySelectionMask(MaskFromContours(contours, w, h), add, subtract);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    // ---- transform tool (Ctrl+T) ------------------------------------------------

    [ObservableProperty]
    private TransformScope _transformScope = TransformScope.ActiveCel;

    public IReadOnlyList<TransformScope> TransformScopeChoices { get; } = Enum.GetValues<TransformScope>();

    /// <summary>Pixel solver for raster baselines (strokes never resample).</summary>
    [ObservableProperty]
    private TransformSampling _transformSampling = TransformSampling.Bilinear;

    public IReadOnlyList<TransformSampling> TransformSamplingChoices { get; } = Enum.GetValues<TransformSampling>();

    [ObservableProperty]
    private bool _transformActive;


    /// <summary>Session started/restarted: bounds of the transformable content (doc space).</summary>
    public event Action<double, double, double, double>? TransformBegun;

    public event Action? TransformEnded;

    /// <summary>Start (or restart) a transform session over the current scope.</summary>
    /// <param name="gizmo">
    /// Raise <see cref="TransformBegun"/> so the canvas puts a handled box
    /// round the drawing. The Move tool passes false: it is one drag with no
    /// handles, and a gizmo appearing under the pointer for the length of a
    /// nudge is noise.
    /// </param>
    /// <param name="filter">
    /// What moves, overriding the selection the session would derive. Only the
    /// line drag passes this (B223): a drag on a line you are holding moves
    /// that line, whatever else happens to be selected elsewhere, because
    /// direct manipulation cannot mean something other than the thing under
    /// your hand.
    /// </param>
    public bool BeginTransform(bool gizmo = true, Func<Stroke, bool>? filter = null)
    {
        if (!CanEdit(ActiveLayer, "transform it")) return false;
        var frames = CollectTransformFrames();
        filter ??= DerivedTransformFilter();
        var bounds = TransformOps.Bounds(frames, filter);
        if (frames.Count == 0 || bounds is null)
        {
            AiStatus = "Nothing to transform in this scope.";
            if (TransformActive) CancelTransform();
            return false;
        }
        _transform.Begin(frames, filter);
        _transform.MovingBounds = PreviewMovingBounds(frames, filter);
        _transform.HeldFrameIdToKey = HeldCelNeedingKey();
        TransformActive = true;
        // The session's controls live in the Tool options docker now (Q70), so
        // starting a transform with the docker closed must open it — Apply and
        // Cancel have keys, but scope, sampling and perspective would otherwise
        // be reachable only through a panel the artist cannot see.
        OpenToolOptions();
        var b = bounds.Value;
        if (gizmo) TransformBegun?.Invoke(b.MinX, b.MinY, b.MaxX, b.MaxY);
        return true;
    }

    /// <summary>
    /// What a transform started from a menu or a key should move, or null for
    /// the whole scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B223. The marquee wins when both are up</b> — the owner's call
    /// against the recommendation, recorded in Q97 with what it costs. The
    /// recommendation was to let the tool in hand decide, reusing
    /// <c>ObjectSelectionIsTheSubject</c>, which <c>Ctrl+A</c> and <c>Ctrl+D</c>
    /// already share. The cost of this order is precise and worth naming here
    /// rather than only in the question: a marquee left up somewhere off screen
    /// silently outranks the lines you can see highlighted, and that is the
    /// state hardest to notice you are in. <see cref="TransformSubject"/> is
    /// what pays it back — the session says which one it took.
    /// </para>
    /// <para>
    /// <b>A line selection is not consulted at all when a marquee is up</b>,
    /// rather than being intersected with it. Two filters ANDed would mean a
    /// transform that moves neither what you marqueed nor what you picked, and
    /// there is no way to show that on a canvas.
    /// </para>
    /// </remarks>
    private Func<Stroke, bool>? DerivedTransformFilter()
    {
        if (HasSelection)
        {
            int w = Scene.Width, h = Scene.Height;
            var mask = MaskFromContours(_selectionContours, w, h);
            return s => TransformOps.MajorityInside(s, mask, w, h);
        }
        return StrokeSelectionFilter();
    }

    /// <summary>The picked lines as a filter, or null when nothing is picked.</summary>
    /// <remarks>
    /// By id, because the session outlives the list it was built from — the
    /// same reason <c>SelectionManager</c> holds ids, one layer up.
    /// </remarks>
    private Func<Stroke, bool>? StrokeSelectionFilter() =>
        HasStrokeSelection ? s => Selection.IsStrokeSelected(s.Id) : null;

    /// <summary>
    /// What the live session is moving, in the artist's words.
    /// </summary>
    /// <remarks>
    /// The visible half of the precedence above. A transform that quietly took
    /// the marquee when the lines were the thing on screen is the failure the
    /// chosen order makes possible, so the session says which it took instead
    /// of leaving it to be discovered on release.
    /// </remarks>
    public string TransformSubject =>
        !TransformActive ? ""
        : HasSelection ? "the selection"
        : HasStrokeSelection ? (Selection.SelectedStrokeIds.Count == 1
            ? "the selected line" : $"{Selection.SelectedStrokeIds.Count} selected lines")
        : "this drawing";
}
