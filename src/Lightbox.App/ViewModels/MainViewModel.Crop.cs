using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// <para>
/// Cropping: the two Image-menu commands that move the paper to a rectangle
/// somebody else worked out — the marquee, or the ink itself. Both funnel
/// through <see cref="ApplyCrop"/> and therefore through
/// <see cref="CanvasResize.CropTo"/>, so there is one answer to what a crop
/// does to the document and it is the same answer <c>Resize canvas</c> gives:
/// three fields on the scene, and nothing that is drawn.
/// </para>
/// <para>
/// <b>Q126.</b> Marks outside the new edge stay in the record. That is not
/// leniency, it is invariant 1 and <c>Hash01</c>: a stroke deleted and drawn
/// again is a different mark, so a destructive crop could not be undone by
/// growing the paper back. What it costs is written down in the question.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    // ---- cropping ----------------------------------------------------------------

    /// <summary>
    /// The layers a trim measures: everything except the paper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The background layer is excluded, and without that the whole command
    /// does nothing.</b> Paper here is real content rather than a scene
    /// property — <c>DocumentFactory.BackgroundLayer</c> is a
    /// <see cref="ToolKind.Fill"/> stroke whose contour is the four corners of
    /// the canvas — which is what makes unlocking it and painting on it work.
    /// It also means every papered document has ink covering every pixel, so a
    /// trim that counted it would measure the paper, find the paper exactly as
    /// large as the paper, and report that there is nothing to do. On every
    /// document anyone actually makes.
    /// </para>
    /// <para>
    /// Hidden and locked layers <em>are</em> counted, and only this one flag is
    /// special: both of those still render and still export, so trimming past
    /// them would lose art that is coming back. The background is not an
    /// exception to that rule — it is not artwork.
    /// </para>
    /// </remarks>
    private IEnumerable<Layer> LayersThatHoldArtwork =>
        Scene.Layers.Where(layer => !layer.IsBackground);

    /// <summary>
    /// Whether <see cref="TrimToDrawing"/> has anything to work from — any ink,
    /// symbol or baseline on a layer that is not the paper.
    /// </summary>
    /// <remarks>
    /// Asked by the menu so the entry greys out rather than being pressed and
    /// doing nothing. Cheap: it stops at the first frame that has content, and
    /// the empty-document case is the only one that walks the whole scene.
    /// </remarks>
    public bool HasAnyContent
    {
        get
        {
            foreach (var layer in LayersThatHoldArtwork)
            {
                foreach (var cel in layer.Cels)
                {
                    if (cel.Frame is not { } frame) continue;
                    if (frame.Strokes.Count > 0 || frame.HasPlacements || frame.HasBaseline) return true;
                }
            }
            return false;
        }
    }

    // ---- the crop tool's frame -----------------------------------------------------

    /// <summary>
    /// The rectangle the Crop tool is dragging, or null when the tool is not in
    /// hand.
    /// </summary>
    /// <remarks>
    /// Null rather than an idle session, so the canvas can ask one question —
    /// <em>is there a frame to draw</em> — instead of asking the tool and the
    /// session and hoping the two agree.
    /// </remarks>
    public CropSession? CropFrame { get; private set; }

    /// <summary>Raised when the frame appears, moves or goes away.</summary>
    public event Action? CropFrameChanged;

    /// <summary>Whether a crop frame is live and would change something.</summary>
    public bool CanApplyCropFrame => CropFrame is { WouldChangeAnything: true };

    /// <summary>
    /// The frame's size, for the tool options bar — or what to do when there is
    /// nothing to apply yet.
    /// </summary>
    public string CropFrameLabel => CropFrame is not { } frame
        ? string.Empty
        : frame.WouldChangeAnything
            ? $"{frame.RoundedWidth} × {frame.RoundedHeight}"
            : "Drag a frame";

    /// <summary>Open a frame on the whole page. Picking the tool does this.</summary>
    public void BeginCropFrame()
    {
        CropFrame ??= new CropSession();
        CropFrame.Begin(Scene.Left, Scene.Top, Scene.Width, Scene.Height);
        NotifyCropFrame();
    }

    /// <summary>Drop the frame. Leaving the tool does this, and so does applying.</summary>
    public void EndCropFrame()
    {
        if (CropFrame is null) return;
        CropFrame = null;
        NotifyCropFrame();
    }

    /// <summary>
    /// The frame changed under the pointer — the canvas calls this after a
    /// press, a drag or a release.
    /// </summary>
    public void NotifyCropFrame()
    {
        OnPropertyChanged(nameof(CanApplyCropFrame));
        OnPropertyChanged(nameof(CropFrameLabel));
        CropFrameChanged?.Invoke();
    }

    /// <summary>
    /// Take the paper down to the frame. Enter, or the Apply button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through <see cref="ApplyCrop"/>, which is the same route both menu
    /// commands take, so there is one answer to what a crop does to the
    /// document and the tool cannot drift from it (Q126).
    /// </para>
    /// <para>
    /// The frame is re-opened on the new page rather than dropped, because the
    /// tool is still in hand: an artist who crops and then wants a little more
    /// off should find handles there, not an empty canvas. Re-opened even when
    /// the crop was refused, since a stale frame describing the old page is the
    /// one thing that must not stay on screen.
    /// </para>
    /// </remarks>
    public bool ApplyCropFrame()
    {
        if (CropFrame is not { } frame) return false;
        if (!frame.WouldChangeAnything)
        {
            AiStatus = "Drag the frame in first — it is still the whole page.";
            return false;
        }

        var applied = ApplyCrop(
            (frame.RoundedLeft, frame.RoundedTop, frame.RoundedWidth, frame.RoundedHeight),
            "the frame");
        BeginCropFrame();
        return applied;
    }

    /// <summary>Escape, or the Cancel button: back to the whole page.</summary>
    public void CancelCropFrame()
    {
        if (CropFrame is not { } frame) return;
        frame.Reset();
        NotifyCropFrame();
    }

    /// <summary>
    /// Re-read <see cref="HasAnyContent"/>. Called from the edit funnel and
    /// from the document swap, for the reason <c>RefreshUndoRedo</c> beside it
    /// is: the first stroke on an empty document is what turns <em>Trim to
    /// drawing</em> from grey to live, and nothing else would say so.
    /// </summary>
    private void RefreshCropAvailability() => OnPropertyChanged(nameof(HasAnyContent));

    /// <summary>
    /// Crop the paper to the selection's bounding box. Returns whether anything
    /// changed, so the window knows whether to refit the view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The box, not the shape.</b> A selection can be a lasso, a polygon or
    /// a wand's traced region; paper is a rectangle. Taking the bounds is the
    /// only reading of "crop to this" that always has an answer, and it is what
    /// every application an artist arrives from does.
    /// </para>
    /// <para>
    /// <b>Clamped to the paper that is there</b>, so the word stays honest. A
    /// marquee can be dragged well outside the canvas — the contours are
    /// document coordinates and nothing stops them — and cropping to a
    /// rectangle larger than the page would <em>grow</em> it, which is the one
    /// thing the word "crop" promises not to do. Growing the canvas is
    /// <c>Resize canvas</c>, one item up the same menu.
    /// </para>
    /// </remarks>
    public bool CropToSelection()
    {
        if (!HasSelection)
        {
            AiStatus = "Select an area first — crop takes the paper to the selection's bounds.";
            return false;
        }
        if (SelectionBounds() is not { } bounds)
        {
            AiStatus = "That selection has no area to crop to.";
            return false;
        }
        return ApplyCrop(bounds, "the selection");
    }

    /// <summary>
    /// Crop the paper to the drawing — the smallest rectangle that still holds
    /// every mark, symbol and baseline in the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every frame of every layer, not the one on screen.</b> The unit of
    /// work here is a sequence: trimming to the drawing in front of you would
    /// cut the ink off frames 2 to 200, and it would do it silently because
    /// nothing about the current cel says what the others reach. Hidden and
    /// locked layers count too — both still render and still export, so
    /// trimming past them would lose art that is going to come back. The paper
    /// is the one thing that does not; see <see cref="LayersThatHoldArtwork"/>,
    /// which is the difference between this command working and this command
    /// being a no-op on every document anybody makes.
    /// </para>
    /// <para>
    /// <b>A baseline claims the whole page.</b> <c>FrameRasterizer</c> draws one
    /// stretched across the entire surface, so a frame carrying one has content
    /// everywhere by definition — and cropping the paper under it would rescale
    /// it rather than reveal anything. Including the current rectangle in the
    /// union makes the trim a no-op there, which is the honest outcome.
    /// </para>
    /// <para>
    /// The cost is one pass over every stroke's points in the document. That is
    /// real on a large painting, and it is bounded work an artist asked for
    /// once from a menu — invariant 6 is about what a pointer event may do.
    /// </para>
    /// </remarks>
    public bool TrimToDrawing()
    {
        if (DrawingBounds() is not { } bounds)
        {
            AiStatus = "Nothing is drawn yet, so there is nothing to trim to.";
            return false;
        }
        return ApplyCrop(bounds, "the drawing");
    }

    /// <summary>
    /// The selection's bounding box in document coordinates, rounded outward so
    /// a partly covered pixel is kept rather than shaved off.
    /// </summary>
    /// <remarks>
    /// Outward on both edges, because the alternative loses a row: a marquee
    /// whose edge lands at x = 10.5 covers pixel 10, and rounding to 11 would
    /// crop it away. <see cref="SelectionContours"/> is document space — the
    /// same space the canvas overlay draws them in and the marquee reports them
    /// in — so nothing is converted here.
    /// </remarks>
    private (int Left, int Top, int Width, int Height)? SelectionBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var contour in SelectionContours)
        {
            foreach (var point in contour)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                any = true;
            }
        }
        if (!any) return null;

        return ClampToPaper(
            (int)Math.Floor(minX), (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
    }

    /// <summary>
    /// The smallest document rectangle that still holds everything drawn, or
    /// null when nothing is.
    /// </summary>
    private (int Left, int Top, int Width, int Height)? DrawingBounds()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        var any = false;

        void Take(int left, int top, int right, int bottom)
        {
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
            maxX = Math.Max(maxX, right);
            maxY = Math.Max(maxY, bottom);
            any = true;
        }

        var info = SceneInfo;
        foreach (var layer in LayersThatHoldArtwork)
        {
            for (var cel = 0; cel < layer.Cels.Count; cel++)
            {
                if (layer.Cels[cel].Frame is not { } frame) continue;

                if (frame.HasBaseline)
                {
                    Take(Scene.Left, Scene.Top,
                         Scene.Left + Scene.Width, Scene.Top + Scene.Height);
                }
                foreach (var stroke in frame.Strokes)
                {
                    // ReachBounds rather than the points: a brush reaches past
                    // its path — scatter, a soft tip, a wet edge — and a trim
                    // that cut to the path would clip the mark's own edge off.
                    if (BrushEngine.ReachBounds(stroke) is { } reach)
                    {
                        Take(reach.Left, reach.Top, reach.Right, reach.Bottom);
                    }
                }
                if (frame.Placements is { } placements)
                {
                    foreach (var placement in placements)
                    {
                        if (SymbolRasterizer.PlacementBounds(placement, info, cel) is { } box)
                        {
                            Take((int)Math.Floor(box.Left), (int)Math.Floor(box.Top),
                                 (int)Math.Ceiling(box.Right), (int)Math.Ceiling(box.Bottom));
                        }
                    }
                }
            }
        }

        return any ? ClampToPaper(minX, minY, maxX, maxY) : null;
    }

    /// <summary>
    /// A document rectangle held inside the paper that is there, or null if it
    /// falls entirely outside it or has no area.
    /// </summary>
    private (int Left, int Top, int Width, int Height)? ClampToPaper(
        int left, int top, int right, int bottom)
    {
        int paperLeft = Scene.Left, paperTop = Scene.Top;
        int paperRight = paperLeft + Scene.Width, paperBottom = paperTop + Scene.Height;

        left = Math.Clamp(left, paperLeft, paperRight);
        top = Math.Clamp(top, paperTop, paperBottom);
        right = Math.Clamp(right, paperLeft, paperRight);
        bottom = Math.Clamp(bottom, paperTop, paperBottom);

        var width = right - left;
        var height = bottom - top;
        return width >= 1 && height >= 1 ? (left, top, width, height) : null;
    }

    /// <summary>
    /// Move the paper to a rectangle, as one undo step, and tell everything
    /// that has to know afterwards.
    /// </summary>
    /// <remarks>
    /// The tail is <c>ApplyResize</c>'s, and deliberately the same: the origin
    /// every pointer conversion reads has moved, the canvas is a different
    /// size, and the thumbnails are drawn against the paper. Missing any one of
    /// them leaves the tools converting against the old corner, which lands
    /// strokes exactly one crop out of place.
    /// </remarks>
    private bool ApplyCrop((int Left, int Top, int Width, int Height) rect, string what)
    {
        // Asked *before* performing, not after, and the difference is a stack
        // the artist can see. Pushing a step and discarding it would be honest
        // about the undo entry and still wrong: PushStep clears the redo stack,
        // so a crop that turned out to change nothing would silently throw away
        // everything the artist could have redone. The one thing CropTo refuses
        // besides a sub-pixel rectangle — which ClampToPaper has already ruled
        // out — is the rectangle the scene already has, so this is the whole of
        // the no-op case rather than a guess at it.
        if (rect.Left == Scene.Left && rect.Top == Scene.Top
            && rect.Width == Scene.Width && rect.Height == Scene.Height)
        {
            AiStatus = $"The canvas already fits {what}.";
            return false;
        }

        var changed = false;
        // Read before pushing, which is the contract DiscardStep documents: the
        // number is only meaningful to whoever pushes next, and that is us.
        var pushed = _editor.NextRevision;
        _editor.Perform(
            doc => changed = CanvasResize.CropTo(
                doc.Scene, rect.Left, rect.Top, rect.Width, rect.Height),
            label: "Crop");

        if (!changed)
        {
            // Unreachable through the guard above, and kept anyway: a step that
            // restores the state it was pushed from is a press that does nothing
            // and a dirty badge that never comes down (B98). If a later change to
            // CropTo adds a refusal, this is what keeps the stack honest about it.
            _editor.DiscardStep(pushed);
            AiStatus = $"The canvas already fits {what}.";
            return false;
        }

        RefreshDocumentOrigin();
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Cropped to {what} — {Scene.Width} × {Scene.Height}.";
        return true;
    }
}
