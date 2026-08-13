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

/// <summary>The move tool and the live transform preview.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- move tool ---------------------------------------------------------------

    /// <summary>
    /// A move in progress: where it started and how far it has come.
    /// </summary>
    /// <remarks>
    /// A move is a transform session with the handles left off. That is not a
    /// shortcut — it is the same operation, so it gets the same live preview
    /// (composite-time, one matrix, no geometry touched until the release),
    /// the same selection filter, the same scopes and the same single undo
    /// step. Re-implementing translation next to it would be a second way for
    /// the drawing to move, and the two would drift.
    /// </remarks>
    private (double X, double Y)? _moveAnchor;

    private (double X, double Y) _moveDelta;

    public bool MoveActive => _moveAnchor is not null;

    /// <summary>
    /// Pick the drawing up.
    /// </summary>
    /// <param name="wholeLayer">
    /// Move every drawing on the layer by the same amount, rather than the one
    /// under the playhead. Shifting a finished cycle sideways is a real job
    /// and doing it a frame at a time is not a job anybody finishes.
    /// </param>
    public bool BeginMove(double x, double y, bool wholeLayer)
    {
        // A placed symbol under the cursor wins, and only when the grab is on
        // the drawing rather than on the whole layer. Moving a placement is an
        // edit to two numbers on the placement; moving the drawing rewrites
        // stroke coordinates. They are different operations that happen to
        // share a gesture, and which one you get is decided by what you
        // grabbed — the same way it is in Photoshop.
        if (!wholeLayer && BeginPlacementMove(x, y)) return true;

        var scope = wholeLayer ? TransformScope.ActiveLayerAllFrames : TransformScope.ActiveCel;
        // Assigned before the session starts, so the property's change handler
        // has no live session to restart and simply records the scope.
        TransformScope = scope;
        if (!BeginTransform(gizmo: false)) return false;
        _moveAnchor = (x, y);
        _moveDelta = default;
        AiStatus = wholeLayer
            ? $"Moving every drawing on {ActiveLayer.Name}"
            : "Moving this drawing — hold Ctrl to move the whole layer";
        return true;
    }

    /// <param name="axisLock">
    /// Shift: hold the move to one axis, whichever it has gone furthest along.
    /// The same thing Shift means on every other tool here.
    /// </param>
    public void UpdateMove(double x, double y, bool axisLock)
    {
        if (PlacementMoveActive)
        {
            UpdatePlacementMove(x, y, axisLock);
            return;
        }
        if (_moveAnchor is not { } anchor) return;
        var dx = x - anchor.X;
        var dy = y - anchor.Y;
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        _moveDelta = (dx, dy);
        PreviewTransform(SKMatrix.CreateTranslation((float)dx, (float)dy));
    }

    /// <summary>Put it down. One undo step for the whole drag, or nothing at all.</summary>
    public void EndMove()
    {
        if (PlacementMoveActive)
        {
            EndPlacementMove();
            return;
        }
        if (_moveAnchor is null) return;
        var (dx, dy) = _moveDelta;
        _moveAnchor = null;
        _moveDelta = default;
        // A click that went nowhere is a click, not an edit. Committing it
        // would put an identity transform in the history for every stray tap.
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            CancelMove();
            return;
        }
        CommitTransformAffine(0, 0, 1, 1, 0, dx, dy);
        TransformActive = false;
    }

    public void CancelMove()
    {
        if (PlacementMoveActive)
        {
            CancelPlacementMove();
            return;
        }
        _moveAnchor = null;
        _moveDelta = default;
        if (TransformActive) CancelTransform();
    }

    /// <summary>Begin moving selected guides.</summary>
    public void BeginGuidesMove()
    {
        if (_selectionManager.SelectedGuideIndices.Count == 0) return;
        _guidesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedGuideIndices.Count} guide(s)";
    }

    /// <summary>
    /// Move the selected guides by the delta since the last pointer event.
    /// </summary>
    /// <remarks>
    /// Live on the record and accumulated, which is <see cref="DragGuide"/> for
    /// a group — the same reason it gives: a pointer move arrives every few
    /// milliseconds, so recording each one would bury the last real edit under
    /// fifty identical nudges. It has to be added rather than assigned; the
    /// canvas reports the change since the previous event and advances its own
    /// anchor, and assigning keeps only the final increment (B109).
    /// </remarks>
    public void UpdateGuidesMove(double dx, double dy)
    {
        foreach (var guide in SelectedGuides())
        {
            guide.X += dx;
            guide.Y += dy;
        }
        _guidesMoveDelta = (_guidesMoveDelta.X + dx, _guidesMoveDelta.Y + dy);
        NotifyGuides();
    }

    /// <summary>Close a group guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuidesMove()
    {
        var (dx, dy) = _guidesMoveDelta;
        _guidesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveGuidesBy(dx, dy);
    }

    /// <summary>
    /// The selected guides that may actually be moved.
    /// </summary>
    /// <remarks>
    /// A locked guide is skipped, the way <see cref="MoveGuide"/> and
    /// <see cref="DragGuide"/> both skip one. Locking exists so a perspective
    /// set can be leaned on without being knocked out of place, and a group
    /// move that ignored it would be the one gesture that could.
    /// </remarks>
    private List<Guide> SelectedGuides()
    {
        var guides = Scene.Guides;
        if (guides is null || guides.Count == 0) return [];
        var picked = new List<Guide>();
        foreach (var index in _selectionManager.SelectedGuideIndices)
        {
            if (index < 0 || index >= guides.Count) continue;
            if (guides[index].Locked) continue;
            picked.Add(guides[index]);
        }
        return picked;
    }

    private (double X, double Y) _guidesMoveDelta;

    /// <summary>Record a finished group guide move as one step.</summary>
    /// <remarks>
    /// Rewound first and then replayed forward, which is what
    /// <see cref="EndGuideDrag"/> does and for the same reason: the guides are
    /// already sitting at the end of the drag, so undo has to return them to
    /// where they were picked up rather than to the last pointer event. The
    /// guides are held by reference rather than by index, because an undo can
    /// replace the list and leave an index pointing at a different guide.
    /// </remarks>
    private void MoveGuidesBy(double dx, double dy)
    {
        var moved = SelectedGuides();
        if (moved.Count == 0) return;

        foreach (var guide in moved)
        {
            guide.X -= dx;
            guide.Y -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X += dx;
                    guide.Y += dy;
                }
                NotifyGuides();
            },
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X -= dx;
                    guide.Y -= dy;
                }
                NotifyGuides();
            });
        NotifyGuides();
    }

    /// <summary>Begin moving selected reference boxes.</summary>
    public void BeginRefBoxesMove()
    {
        if (_selectionManager.SelectedRefBoxIndices.Count == 0) return;
        _refBoxesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedRefBoxIndices.Count} reference box(es)";
    }

    /// <summary>
    /// Move the selected reference boxes by the delta since the last pointer
    /// event.
    /// </summary>
    /// <remarks>
    /// Live and accumulated, for the reason on <see cref="UpdateGuidesMove"/>.
    /// </remarks>
    public void UpdateRefBoxesMove(double dx, double dy)
    {
        foreach (var cell in SelectedRefBoxes())
        {
            cell.Dx += dx;
            cell.Dy += dy;
        }
        _refBoxesMoveDelta = (_refBoxesMoveDelta.X + dx, _refBoxesMoveDelta.Y + dy);
        AfterReferenceChange();
    }

    /// <summary>Close a group box drag: the whole of it becomes one undo step.</summary>
    public void EndRefBoxesMove()
    {
        var (dx, dy) = _refBoxesMoveDelta;
        _refBoxesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveRefBoxesBy(dx, dy);
    }

    private (double X, double Y) _refBoxesMoveDelta;

    /// <summary>The selected boxes on the active sheet.</summary>
    private List<ReferenceCell> SelectedRefBoxes()
    {
        if (ActiveReference?.Cells is not { Count: > 0 } cells) return [];
        var picked = new List<ReferenceCell>();
        foreach (var index in _selectionManager.SelectedRefBoxIndices)
        {
            if (index < 0 || index >= cells.Count) continue;
            picked.Add(cells[index]);
        }
        return picked;
    }

    /// <summary>Record a finished group box move as one step.</summary>
    /// <remarks>
    /// <para>
    /// <c>Dx</c>/<c>Dy</c>, which is what <see cref="MoveReferenceCell"/>
    /// moves, and the distinction is the whole of this method. A cell's
    /// <c>X</c>/<c>Y</c> are the window onto the sheet in sheet pixels — moving
    /// those scrolls the photograph inside the box and leaves the box where it
    /// was, which is a different operation and destroys the registration the
    /// pivot exists to hold. They are also <c>int</c>, so a drag reported in
    /// fractions of a pixel would be rounded away a frame at a time.
    /// </para>
    /// <para>
    /// Rewound and replayed like the guides, for the reason on
    /// <see cref="MoveGuidesBy"/>, and holding the cells by reference for the
    /// same one.
    /// </para>
    /// </remarks>
    private void MoveRefBoxesBy(double dx, double dy)
    {
        var moved = SelectedRefBoxes();
        if (moved.Count == 0) return;

        foreach (var cell in moved)
        {
            cell.Dx -= dx;
            cell.Dy -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx += dx;
                    cell.Dy += dy;
                }
                NotifyReference();
            },
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx -= dx;
                    cell.Dy -= dy;
                }
                NotifyReference();
            });
        AfterReferenceChange();
    }

    /// <summary>Begin moving selected anchors.</summary>
    public void BeginAnchorsMove()
    {
        if (_selectionManager.SelectedAnchorIds.Count == 0) return;
        _anchorsMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedAnchorIds.Count} anchor(s)";
    }

    /// <summary>Update anchor move by the delta since the last pointer event.</summary>
    public void UpdateAnchorsMove(double dx, double dy)
    {
        _anchorsMoveDelta = (_anchorsMoveDelta.X + dx, _anchorsMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit anchor moves.</summary>
    public void EndAnchorsMove()
    {
        var (dx, dy) = _anchorsMoveDelta;
        _anchorsMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveAnchorsBy(dx, dy);
    }

    private (double X, double Y) _anchorsMoveDelta;

    /// <summary>Apply movement delta to selected anchors.</summary>
    private void MoveAnchorsBy(double dx, double dy)
    {
        var selectedAnchorIds = _selectionManager.SelectedAnchorIds.ToList();
        if (selectedAnchorIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X + dx, point.Y + dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X - dx, point.Y - dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    /// <summary>Begin moving selected collision shapes.</summary>
    public void BeginShapesMove()
    {
        if (_selectionManager.SelectedShapeIds.Count == 0) return;
        _shapesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedShapeIds.Count} shape(s)";
    }

    /// <summary>Update collision shape move by the delta since the last pointer event.</summary>
    public void UpdateShapesMove(double dx, double dy)
    {
        _shapesMoveDelta = (_shapesMoveDelta.X + dx, _shapesMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit collision shape moves.</summary>
    public void EndShapesMove()
    {
        var (dx, dy) = _shapesMoveDelta;
        _shapesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveShapesBy(dx, dy);
    }

    private (double X, double Y) _shapesMoveDelta;

    /// <summary>Apply movement delta to selected collision shapes.</summary>
    private void MoveShapesBy(double dx, double dy)
    {
        var selectedShapeIds = _selectionManager.SelectedShapeIds.ToList();
        if (selectedShapeIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X + dx, box.Y + dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X - dx, box.Y - dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    partial void OnTransformScopeChanged(TransformScope value)
    {
        // Changing scope mid-session re-collects under the new scope.
        if (TransformActive) BeginTransform();
    }

    /// <summary>Distinct drawings in scope (holds share Frame instances — dedupe by id).</summary>
    private List<Frame> CollectTransformFrames()
    {
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        void Add(Frame? f)
        {
            if (f is not null && seen.Add(f.Id)) frames.Add(f);
        }
        switch (TransformScope)
        {
            case TransformScope.ActiveCel:
                Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
                break;
            case TransformScope.AllLayersAtFrame:
                foreach (var layer in Scene.Layers)
                {
                    if (Scene.IsLayerVisible(layer)) Add(ExposureSheet.ExposedFrame(layer, CurrentFrameIndex));
                }
                break;
            case TransformScope.ActiveLayerAllFrames:
                foreach (var cel in ActiveLayer.Cels) Add(cel.Frame);
                break;
            case TransformScope.CelRange:
                if (_celRange is { } r && r.Layer >= 0 && r.Layer < Scene.Layers.Count)
                {
                    var layer = Scene.Layers[r.Layer];
                    for (var i = r.Start; i <= r.End; i++) Add(ExposureSheet.ExposedFrame(layer, i));
                }
                else
                {
                    Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex)); // no range marked
                }
                break;
            case TransformScope.EntireAnimation:
                foreach (var layer in Scene.Layers)
                {
                    foreach (var cel in layer.Cels) Add(cel.Frame);
                }
                break;
        }
        return frames;
    }

    public void CancelTransform()
    {
        if (!TransformActive) return;
        EndTransformSession();
    }

    private void EndTransformSession()
    {
        TransformActive = false;
        _transformFrames.Clear();
        _transformFilter = null;
        ClearTransformPreview();
        TransformEnded?.Invoke();
    }

    // ---- live transform preview -------------------------------------------------

    /// <summary>
    /// A frame's pixels split into the part the transform moves and the part
    /// it leaves behind. With no selection everything moves and
    /// <see cref="Static"/> is null, which is the ordinary case and costs no
    /// extra render at all — <see cref="Moving"/> is the frame's own cached
    /// bitmap, borrowed rather than copied.
    /// </summary>
    private sealed record TransformParts(SKBitmap Moving, SKBitmap? Static, bool Owned);

    private readonly Dictionary<string, TransformParts> _transformParts = [];

    /// <summary>
    /// Show the drag. Null clears the preview and puts the pixels back where
    /// the record says they are.
    /// </summary>
    public void PreviewTransform(SKMatrix? matrix)
    {
        if (!TransformActive)
        {
            if (_transformPreview is null) return;
            matrix = null;
        }
        // Identity is "no preview": it renders the same and skips the split.
        if (matrix is { } m && m.IsIdentity) matrix = null;
        if (_transformPreview is null && matrix is null) return;
        _transformPreview = matrix;
        // The drawing can land anywhere on the canvas, so no dirty region is
        // safe here.
        _publish.InvalidateWholeCanvas();
        RequestSnapshot();
    }

    private void ClearTransformPreview()
    {
        _transformPreview = null;
        foreach (var parts in _transformParts.Values)
        {
            if (!parts.Owned) continue;
            parts.Moving.Dispose();
            parts.Static?.Dispose();
        }
        _transformParts.Clear();
    }

    /// <summary>
    /// The moving/static split for one in-scope frame, built once per session
    /// and reused for every pointer event of the drag. Null when this frame
    /// cannot be previewed honestly — a partial selection over a frame that is
    /// not a stroke record — in which case the gizmo alone stands in, as it
    /// did before.
    /// </summary>
    private TransformParts? PartsFor(Frame frame)
    {
        if (_transformFilter is null)
        {
            // Everything moves, so the layer bitmap already IS the moving part
            // and no render is needed. It is deliberately re-fetched rather
            // than remembered: the cache owns and disposes these, and a
            // remembered one becomes a dangling pointer the moment anything
            // invalidates the frame.
            return new TransformParts(_cache.Get(frame, Scene.Width, Scene.Height), null, Owned: false);
        }

        if (_transformParts.TryGetValue(frame.Id, out var cached)) return cached;

        TransformParts? parts;
        if (frame is Frame painted && _transformFilter is { } filter)
        {
            var moving = painted.Strokes.Where(filter).ToList();
            var rest = painted.Strokes.Where(s => !filter(s)).ToList();
            SKBitmap stay;
            if (painted.PngBase64 is { Length: > 0 })
            {
                // The baseline stays put under a region-limited transform,
                // exactly as the commit leaves it — and it goes underneath the
                // strokes that stay, because that is the order it renders in.
                stay = new SKBitmap(new SKImageInfo(
                    Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using (var canvas = new SKCanvas(stay))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var baseline = Lightbox.Raster.PngCodec.Decode(painted.PngBase64);
                    canvas.DrawBitmap(baseline, 0, 0);
                }
                foreach (var s in rest) FrameRasterizer.Append(stay, s);
            }
            else
            {
                stay = FrameRasterizer.Rasterize(rest, Scene.Width, Scene.Height);
            }
            parts = new TransformParts(
                FrameRasterizer.Rasterize(moving, Scene.Width, Scene.Height), stay, Owned: true);
        }
        else
        {
            parts = null;
        }

        if (parts is not null) _transformParts[frame.Id] = parts;
        return parts;
    }

    /// <summary>Commit a move/scale/rotate/mirror (mirror = negative scale) around a pivot.</summary>
    public void CommitTransformAffine(
        double pivotX, double pivotY,
        double scaleX, double scaleY,
        double angleRadians,
        double offsetX, double offsetY)
    {
        if (!TransformActive) return;
        var map = TransformOps.Affine(pivotX, pivotY, scaleX, scaleY, angleRadians, offsetX, offsetY);
        var sizeScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var m = SKMatrix.CreateTranslation((float)-pivotX, (float)-pivotY);
        m = m.PostConcat(SKMatrix.CreateScale((float)scaleX, (float)scaleY));
        m = m.PostConcat(SKMatrix.CreateRotation((float)angleRadians));
        m = m.PostConcat(SKMatrix.CreateTranslation((float)(pivotX + offsetX), (float)(pivotY + offsetY)));
        CommitTransformCore(map, sizeScale, m);
    }

    /// <summary>Commit a four-corner perspective transform.</summary>
    public void CommitTransformPerspective(double[] srcQuad, double[] dstQuad)
    {
        if (!TransformActive) return;
        double[] h;
        try
        {
            h = TransformOps.PerspectiveCoefficients(srcQuad, dstQuad);
        }
        catch (InvalidOperationException)
        {
            AiStatus = "That corner arrangement collapses the drawing — adjust the corners and try again.";
            return;
        }
        var map = TransformOps.Perspective(srcQuad, dstQuad);
        var m = new SKMatrix(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);
        CommitTransformCore(map, 1, m);
    }

    private void CommitTransformCore(TransformOps.PointMap map, double sizeScale, SKMatrix baselineMatrix)
    {
        var frames = _transformFrames.ToList();
        var filter = _transformFilter;
        // The preview goes first, and not only for tidiness: it borrows the
        // cache's own bitmaps, and the invalidation below disposes them.
        ClearTransformPreview();
        // Invalidate before the edit so the Changed refresh re-renders from
        // the transformed record.
        foreach (var frame in frames) InvalidateFrameRender(frame.Id);
        _editor.Perform(_ =>
        {
            foreach (var frame in frames)
            {
                TransformOps.TransformFrame(frame, map, sizeScale, filter);
                // Raster baselines resample once per commit; a region-limited
                // transform moves strokes only (baseline pixels stay put).
                if (filter is null && frame is Frame { PngBase64.Length: > 0 } painted)
                {
                    ResampleBaseline(painted, baselineMatrix);
                }
            }
        });
        // The selection outline rides along so a follow-up transform lines up.
        if (HasSelection)
        {
            foreach (var contour in _selectionContours)
            {
                for (var i = 0; i < contour.Count; i++)
                {
                    var pt = contour[i];
                    var (x, y) = map(pt.X, pt.Y);
                    contour[i] = pt with { X = x, Y = y };
                }
            }
            NotifySelection();
        }
        EndTransformSession();
        AiStatus = $"Transformed {frames.Count} drawing{(frames.Count == 1 ? "" : "s")}.";
    }

    private SKSamplingOptions SamplingFor(TransformSampling mode) => mode switch
    {
        TransformSampling.Nearest => new SKSamplingOptions(SKFilterMode.Nearest),
        TransformSampling.Bicubic => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => new SKSamplingOptions(SKFilterMode.Linear),
    };

    /// <summary>
    /// Re-render a frame's baseline pixels through a matrix. Does nothing when the
    /// frame has no baseline.
    /// </summary>
    /// <remarks>
    /// The early return rather than a <c>!</c> at the one call site: the baseline
    /// is nullable now, every caller already tests it, and a method that is correct
    /// on its own does not need the next caller to remember. Nothing to resample is
    /// not an error.
    /// </remarks>
    private void ResampleBaseline(Frame frame, SKMatrix matrix)
    {
        if (frame.PngBase64 is not { Length: > 0 } encoded) return;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return;
            var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null) return;
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            surface.Canvas.SetMatrix(matrix);
            using var image = SKImage.FromBitmap(src);
            surface.Canvas.DrawImage(image, 0, 0, SamplingFor(TransformSampling));
            using var snap = surface.Snapshot();
            using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            frame.PngBase64 = Convert.ToBase64String(data.ToArray());
        }
        catch (FormatException)
        {
            // Corrupt baseline: leave it untouched rather than destroy pixels.
        }
    }
}
