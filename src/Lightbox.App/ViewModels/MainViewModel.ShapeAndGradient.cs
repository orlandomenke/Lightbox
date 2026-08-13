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
    // ---- gradient tool ------------------------------------------------------


    /// <summary>The axis the canvas overlay draws while dragging (document coordinates).</summary>
    public event Action<(double X, double Y)?, (double X, double Y)?>? GradientAxisChanged;

    internal Stroke? LiveGradient => _liveGradient;

    internal Stroke? LiveGradientForTests => _liveGradient;

    internal Stroke? LiveShapeForTests => _liveShape;

    /// <summary>
    /// Whether anything being dragged right now would actually reach the
    /// screen.
    /// </summary>
    /// <remarks>
    /// The condition the compositor tests, named once so it can be asserted.
    /// A tool that renders a preview nothing composites looks correct at every
    /// call site and shows nothing, which is how the shape tool shipped.
    /// </remarks>
    internal bool LivePreviewIsVisible =>
        _live.Scratch is not null
        && (_liveShape is not null || _liveGradient is not null || _strokeBuilder.IsActive);

    public void BeginGradient(double x, double y)
    {
        if (ActiveTool != ToolId.Gradient || IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it") || PaintTargetOrKey() is null) return;
        // A brand-new document has no gradients, and telling someone who just
        // picked the gradient tool to go and make one first is a dead end. A
        // fresh Gradient is already black to white, which is the ramp anyone
        // would have made by hand.
        if (GradientDocker.SelectedGradient is null) GradientDocker.AddGradientCommand.Execute(null);
        if (GradientDocker.SelectedGradient is not { } gradient)
        {
            AiStatus = "Could not create a gradient to paint with.";
            return;
        }
        CommitSwatchEdit();

        _liveGradient = new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Color = ColorHex,
            Brush = new BrushSettings { Opacity = GradientOpacity, AntiAlias = AntiAliasing },
            Points = [new StrokePoint(x, y, 1), new StrokePoint(x, y, 1)],
            // Stamped onto the stroke like a brush stroke's, so unlocking the
            // layer later cannot repaint what is already down.
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = "gradient",
        };
        if (PrepareClipForSelection() is { } clip) _liveGradient.ClipId = clip.Id;

        _live.EnsureScratch(Scene.Width, Scene.Height);
        RenderGradientPreview();
        PublishSnapshot();
    }

    /// <param name="snapAngle">
    /// Shift. A gradient's angle is the whole of it, and a ramp meant to be
    /// level almost never lands level by hand.
    /// </param>
    public void MoveGradient(double x, double y, bool snapAngle = false)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = GradientEnd(stroke, x, y, snapAngle);
        RenderGradientPreview();
        RequestSnapshot();
    }

    /// <summary>
    /// How far apart the snapped angles are, in degrees.
    /// </summary>
    /// <remarks>
    /// Fifteen, so the four squares and the four diagonals are all on it and
    /// there is still somewhere to put an angle between them. The same number
    /// the guide lock uses, for the same reason.
    /// </remarks>
    public const double GradientSnapDegrees = 15;

    private static StrokePoint GradientEnd(Stroke stroke, double x, double y, bool snapAngle)
    {
        if (!snapAngle) return new StrokePoint(x, y, 1);
        var ax = stroke.Points[0].X;
        var ay = stroke.Points[0].Y;
        var dx = x - ax;
        var dy = y - ay;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return new StrokePoint(x, y, 1);
        // The angle snaps; the length does not. Same division of labour as a
        // ruler — the guide decides the direction, the hand decides how far.
        var degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
        var snapped = Math.Round(degrees / GradientSnapDegrees) * GradientSnapDegrees * Math.PI / 180;
        return new StrokePoint(ax + Math.Cos(snapped) * length, ay + Math.Sin(snapped) * length, 1);
    }

    public void EndGradient(double x, double y, bool snapAngle = false)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = GradientEnd(stroke, x, y, snapAngle);
        CancelGradient(); // clears the preview; the record gets the stroke below

        if (PaintTarget() is not { } target) return;
        var dx = stroke.Points[1].X - stroke.Points[0].X;
        var dy = stroke.Points[1].Y - stroke.Points[0].Y;
        // A click with no drag has no axis. Committing it would paint a
        // degenerate shader over the whole layer, which is never the intent.
        if (dx * dx + dy * dy < 1.0)
        {
            AiStatus = "Drag to set the gradient's direction and length.";
            return;
        }

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();
        AppendToFrameRender(target, stroke);

        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Laid down “{GradientDocker.SelectedGradient?.Name}”.";
    }

    /// <summary>Abandon the drag — Escape, or capture lost.</summary>
}
