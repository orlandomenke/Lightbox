using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>One bone of the pose guide as drawn: from joint to joint, how wide the part is around it, and which part.</summary>
public sealed record PoseGuideBone(StrokePoint From, StrokePoint To, double Width, int Part);

/// <summary>One joint of the pose guide as drawn.</summary>
public sealed record PoseGuideJoint(string Part, string Name, StrokePoint At, int PartIndex, int JointIndex);

/// <summary>
/// What the pose guide shows on the frame under the playhead: each part's
/// bones with their volumes, and its joints; draggable on a key while the
/// guide is being edited.
/// </summary>
public sealed record PoseGuideOverlay(IReadOnlyList<PoseGuideBone> Bones, IReadOnlyList<PoseGuideJoint> Joints, bool Editable);

/// <summary>
/// The pose guide (Q274): the model reads the pose of the subject in the keys
/// either side of the playhead; every frame of the gap shows the pose at that
/// point with each part's volume, for the artist to draw the inbetween over.
/// A guide, never a drawing: in no render and no export.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The long edge of the key pictures sent for a pose reading.</summary>
    private const int PosePictureLongEdge = 1024;

    /// <summary>Paper round the keys' ink in those pictures, in scene pixels.</summary>
    private const int PosePictureMargin = 24;

    /// <summary>Whether the pose guide is shown. On by default: there is nothing to show until a pose is read.</summary>
    [ObservableProperty]
    private bool _poseGuideVisible = true;

    /// <summary>
    /// Whether a press on a key's joint drags it. A mode rather than always on,
    /// so drawing the inbetween near a joint never moves the guide by accident.
    /// </summary>
    [ObservableProperty]
    private bool _poseEditMode;

    /// <summary>Raised when what the pose guide shows may have changed, for the window to push to the canvas.</summary>
    public event Action<PoseGuideOverlay?>? PoseGuideChanged;

    /// <summary>A joint being dragged: which, on which key (0 for A, 1 for B), and where it is now.</summary>
    private (int Part, int Joint, int Key, PosePoint At)? _poseDrag;

    partial void OnPoseGuideVisibleChanged(bool value) => RaisePoseGuideChanged();

    partial void OnPoseEditModeChanged(bool value)
    {
        _poseDrag = null;
        // The canvas only shows the mode on a key, so it says so in words.
        if (value) AiStatus = "Editing the pose guide: on either key, drag a joint to where it belongs. Esc to finish.";
        RaisePoseGuideChanged();
    }

    /// <summary>Tell the canvas the guide may have changed: the playhead moved, the document changed, a joint is being dragged.</summary>
    private void RaisePoseGuideChanged()
    {
        if (IsPlaying) return;
        PoseGuideChanged?.Invoke(PoseGuideView);
    }

    /// <summary>Push the guide whatever the clock is doing: null while playing, so the overlay goes when Play starts and returns on Stop.</summary>
    private void PushPoseGuide() => PoseGuideChanged?.Invoke(IsPlaying ? null : PoseGuideView);

    /// <summary>
    /// Each key's ink, gathered once and kept while its strokes are the same
    /// strokes (Q274 review): the guide is asked for on every playhead step,
    /// every stroke committed to an inbetween and every move of a drag, and
    /// gathering a key's ink each time was most of its cost.
    /// </summary>
    private readonly List<(Frame Frame, (Stroke Stroke, List<StrokePoint> Points, int Count)[] Strokes, PoseInk Ink)> _poseInk = [];

    private PoseInk PoseInkOf(Frame frame)
    {
        var strokes = StrokesOf(frame);
        foreach (var held in _poseInk)
        {
            if (!ReferenceEquals(held.Frame, frame) || held.Strokes.Length != strokes.Count) continue;
            var same = true;
            for (var i = 0; i < strokes.Count && same; i++)
            {
                var (stroke, points, count) = held.Strokes[i];
                same = ReferenceEquals(stroke, strokes[i]) && ReferenceEquals(points, strokes[i].Points) && count == strokes[i].Points.Count;
            }
            if (same) return held.Ink;
        }
        var ink = PoseGuides.Ink(strokes);
        _poseInk.RemoveAll(h => ReferenceEquals(h.Frame, frame));
        // Two keys make a gap: anything older is a gap the playhead has left.
        if (_poseInk.Count >= 2) _poseInk.RemoveAt(0);
        _poseInk.Add((frame, [.. strokes.Select(s => (s, s.Points, s.Points.Count))], ink));
        return ink;
    }

    /// <summary>
    /// What the guide shows on the frame under the playhead, or null: hidden,
    /// playing, or no pose read for the gap the playhead is in.
    /// </summary>
    public PoseGuideOverlay? PoseGuideView
    {
        get
        {
            if (!PoseGuideVisible || IsPlaying || PoseGapAt(CurrentFrameIndex) is not { } gap) return null;
            var (layer, a, b, pose) = gap;
            // A drag holds its joint by position; an undo mid-drag can take it away.
            if (_poseDrag is { } held && !PoseHolds(pose, held)) _poseDrag = null;
            if (_poseDrag is { } drag)
            {
                pose = pose.Clone();
                var joint = pose.Parts[drag.Part].Joints[drag.Joint];
                if (drag.Key == 0) joint.A = drag.At; else joint.B = drag.At;
            }
            var t = PoseTimeAt(layer, a, b, CurrentFrameIndex);
            var volumes = PoseGuides.Volumes(pose, PoseInkOf(layer.Cels[a].Frame!), PoseInkOf(layer.Cels[b].Frame!), t);
            var posed = PoseGuides.At(pose, t);
            var bones = volumes.SelectMany((part, p) => part.Select(v => new PoseGuideBone(v.From, v.To, v.Width, p))).ToList();
            var joints = posed.SelectMany((part, p) => part.Joints.Select((j, k) => new PoseGuideJoint(part.Name, j.Name, j.At, p, k))).ToList();
            return new PoseGuideOverlay(bones, joints, PoseEditMode && (CurrentFrameIndex == a || CurrentFrameIndex == b));
        }
    }

    private static bool PoseHolds(PoseGuide pose, (int Part, int Joint, int Key, PosePoint At) drag) =>
        drag.Part < pose.Parts.Count && drag.Joint < pose.Parts[drag.Part].Joints.Count;

    /// <summary>
    /// The gap at <paramref name="frameIndex"/> on the active layer whose first
    /// drawing holds a pose, or null. A key is the end of one gap and the start
    /// of the next: the gap it starts wins when it has a pose, and otherwise the
    /// one it ends shows there as its key B.
    /// </summary>
    private (Layer Layer, int A, int B, PoseGuide Pose)? PoseGapAt(int frameIndex)
    {
        if (MatchGapAt(frameIndex) is { } gap)
        {
            var layer = Scene.Layers.First(l => l.Id == gap.Layer);
            if (layer.Cels[gap.A].Frame?.Pose is { Parts.Count: > 0 } pose) return (layer, gap.A, gap.B, pose);
        }
        // The gap that ends here, when the frame is itself a key.
        var active = ActiveLayer;
        if (frameIndex <= 0 || frameIndex >= active.Cels.Count || active.Cels[frameIndex].Frame is not { Role: not FrameRole.Inbetween })
            return null;
        if (MatchGapAt(frameIndex - 1) is { } before && before.B == frameIndex
            && active.Cels[before.A].Frame?.Pose is { Parts.Count: > 0 } ended)
            return (active, before.A, before.B, ended);
        return null;
    }

    /// <summary>
    /// Where frame <paramref name="index"/> sits between the keys, as the
    /// inbetweener would time it: a charted gap's rung when there is one per
    /// inbetween, otherwise its place on the sheet through the bar's easing.
    /// </summary>
    private double PoseTimeAt(Layer layer, int a, int b, int index)
    {
        if (index <= a) return 0;
        if (index >= b) return 1;
        var between = Enumerable.Range(a + 1, b - a - 1).Where(i => layer.Cels[i].Frame is not null).ToList();
        if (layer.Cels[a].Frame!.Chart is { } chart && chart.Count == between.Count && between.IndexOf(index) is var k and >= 0)
        {
            return chart.Order().ElementAt(k);
        }
        return EasingOps.Ease((double)(index - a) / (b - a), TweenEasing);
    }

    /// <summary>
    /// Ask the model for the pose in the keys either side of the playhead, and
    /// keep it on the first of them for the gap, as one undo step.
    /// </summary>
    [RelayCommand]
    private async Task ReadPoseAsync()
    {
        if (_ai.Artist is null || AiBusy) return;
        var layer = ActiveLayer;
        if (!CanEdit(layer, "read a pose on it")) return;
        if (MatchGapAt(CurrentFrameIndex) is not { } gap)
        {
            AiStatus = "Read pose needs a key before and after the playhead.";
            return;
        }
        var (a, b) = (gap.A, gap.B);
        var pictures = PosePictures(layer.Cels[a].Frame!, a, layer.Cels[b].Frame!, b);
        if (pictures is not { } pics)
        {
            AiStatus = "Neither key has anything drawn to read a pose from.";
            return;
        }

        // Where the picture's pixels are in the scene, fixed now: the scene's
        // origin can move while the model is reading (a crop, a resize).
        var (originX, originY) = (this.Scene.Left, this.Scene.Top);
        var layerId = layer.Id;
        var frameId = layer.Cels[a].Frame!.Id;
        var reading = await RunAiAsync(
            $"{AiProviderLabel} is reading the pose…",
            ct => _ai.Artist.ReadPoseAsync(new PoseRequest(pics.A, pics.B, pics.Width, pics.Height), ct));
        if (reading is null) return;

        // From the pictures' pixels back to the scene's: the crop's corner,
        // the paper's own corner, and the scale the picture was made at.
        PosePoint Scene(PosePoint p) => new(
            pics.Box.Left + originX + p.X / pics.Scale,
            pics.Box.Top + originY + p.Y / pics.Scale);
        var pose = reading.Pose.Clone();
        foreach (var joint in pose.Parts.SelectMany(p => p.Joints))
        {
            joint.A = Scene(joint.A);
            joint.B = Scene(joint.B);
        }
        if (this.Scene.Layers.FirstOrDefault(l => l.Id == layerId)?.Cels.Any(c => c.Frame?.Id == frameId) != true)
        {
            AiStatus = "The key changed while the pose was being read, so nothing was kept. Read pose again.";
            return;
        }
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
            if (target?.Cels.FirstOrDefault(c => c.Frame?.Id == frameId)?.Frame is { } key) key.Pose = pose.Clone();
        }, "Read pose");
        var names = string.Join(", ", pose.Parts.Select(p => p.Name));
        AiStatus = $"Read the pose: {Plural(pose.Parts.Count, "part")} ({names}).{(reading.Notes.Length > 0 ? " " + reading.Notes : "")}";
        RaisePoseGuideChanged();
    }

    /// <summary>
    /// Both keys as pictures for a pose reading: rendered alone, on white,
    /// cropped together to where either has ink (plus a margin), at most
    /// <see cref="PosePictureLongEdge"/> on the long edge. Null when neither
    /// has ink.
    /// </summary>
    private (string A, string B, int Width, int Height, SKRectI Box, double Scale)? PosePictures(Frame keyA, int aIndex, Frame keyB, int bIndex)
    {
        var (w, h) = (Scene.Width, Scene.Height);
        using var hold = _cache.HoldFetches();
        // The cache's renders, shared: not disposed here.
        var drawnA = _cache.Get(keyA, w, h, celIndex: aIndex);
        var drawnB = _cache.Get(keyB, w, h, celIndex: bIndex);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var bitmap in new[] { drawnA, drawnB })
        {
            var pixels = bitmap.GetPixelSpan();
            var row = bitmap.RowBytes;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (pixels[y * row + x * 4 + 3] < 16) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < left) return null;
        var box = new SKRectI(
            Math.Max(0, left - PosePictureMargin), Math.Max(0, top - PosePictureMargin),
            Math.Min(w, right + 1 + PosePictureMargin), Math.Min(h, bottom + 1 + PosePictureMargin));
        var scale = Math.Min(1.0, (double)PosePictureLongEdge / Math.Max(box.Width, box.Height));
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(box.Width * scale)), Math.Max(1, (int)Math.Round(box.Height * scale)));
        return (OnPaper(drawnA), OnPaper(drawnB), size.Width, size.Height, box, scale);

        string OnPaper(SKBitmap drawn)
        {
            using var picture = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(picture))
            using (var image = SKImage.FromBitmap(drawn))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawImage(image, SKRect.Create(box.Left, box.Top, box.Width, box.Height),
                    SKRect.Create(0, 0, size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var data = picture.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("PNG encode failed.");
            return Convert.ToBase64String(data.AsSpan());
        }
    }

    /// <summary>
    /// A press on a joint of the guide, on a key: the joint under it is taken
    /// for dragging. False — leaving the press to the canvas — anywhere else.
    /// </summary>
    public bool PosePress(double x, double y, double reach)
    {
        if (PoseGuideView is not { Editable: true } view || PoseGapAt(CurrentFrameIndex) is not { } gap) return false;
        var point = new StrokePoint(x, y, 0);
        var hit = view.Joints
            .Select(j => (Joint: j, Distance: GeometryOps.Dist(j.At, point)))
            .Where(j => j.Distance <= reach)
            .OrderBy(j => j.Distance)
            .Select(j => j.Joint)
            .FirstOrDefault();
        if (hit is null) return false;
        _poseDrag = (hit.PartIndex, hit.JointIndex, CurrentFrameIndex == gap.A ? 0 : 1, new PosePoint(hit.At.X, hit.At.Y));
        return true;
    }

    /// <summary>The dragged joint follows the pointer; nothing is written until the release.</summary>
    public void PoseDrag(double x, double y)
    {
        if (_poseDrag is not { } drag) return;
        _poseDrag = drag with { At = new PosePoint(x, y) };
        RaisePoseGuideChanged();
    }

    /// <summary>The drag ends: the joint is put where it was let go, on its key, as one undo step.</summary>
    public void PoseRelease(double x, double y)
    {
        if (_poseDrag is not { } drag) return;
        _poseDrag = null;
        if (PoseGapAt(CurrentFrameIndex) is not { } gap)
        {
            RaisePoseGuideChanged();
            return;
        }
        var layerId = gap.Layer.Id;
        var frameId = gap.Layer.Cels[gap.A].Frame!.Id;
        var at = new PosePoint(x, y);
        if (!PoseHolds(gap.Pose, drag))
        {
            RaisePoseGuideChanged();
            return;
        }
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
            if (target?.Cels.FirstOrDefault(c => c.Frame?.Id == frameId)?.Frame?.Pose is not { } pose || !PoseHolds(pose, drag)) return;
            var joint = pose.Parts[drag.Part].Joints[drag.Joint];
            if (drag.Key == 0) joint.A = at; else joint.B = at;
        }, "Move pose joint");
        RaisePoseGuideChanged();
    }
}
