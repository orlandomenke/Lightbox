using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Documents;

/// <summary>
/// One authored framing at a point on the timeline. <see cref="X"/> and
/// <see cref="Y"/> are the document point the camera is centred on, so a pan
/// is a move in the same coordinates the artist draws in.
/// </summary>
public sealed class CameraKey
{
    public int Frame { get; set; }

    /// <summary>Document x the frame is centred on.</summary>
    public double X { get; set; }

    /// <summary>Document y the frame is centred on.</summary>
    public double Y { get; set; }

    /// <summary>1 shows the output size one-for-one; 2 is a 2x push in.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Camera roll, clockwise.</summary>
    public double RotationDeg { get; set; }

    /// <summary>
    /// How this key eases into the NEXT one — the same easing vocabulary the
    /// inbetweener uses, so a camera move and a drawing inbetween are
    /// described the same way.
    /// </summary>
    public Easing Ease { get; set; } = Easing.EaseInOut;
}

/// <summary>
/// A keyframed camera over the scene.
///
/// This is a <em>document</em> transform, unlike the editing view transform of
/// invariant 5: it is authored, saved and exported. It still never touches a
/// stroke, so invariant 1 holds — the record stays the record and the camera
/// only decides what part of it a render shows.
///
/// A scene has one of these only if the artist asked for one
/// (<see cref="Scene.Camera"/> is null by default). Character animation for a
/// game never needs it and must not pay for it.
/// </summary>
public sealed class Camera
{
    /// <summary>Rendered size of what the camera sees.</summary>
    public int OutputWidth { get; set; } = 1920;

    public int OutputHeight { get; set; } = 1080;

    /// <summary>Authored framings. Order is not guaranteed; read through <see cref="CameraOps"/>.</summary>
    public List<CameraKey> Keys { get; set; } = [];
}

/// <summary>The camera's framing on one frame, after interpolation.</summary>
public readonly record struct CameraFraming(double X, double Y, double Zoom, double RotationDeg)
{
    /// <summary>Dead centre at 1:1 — what a scene without a camera is framed as.</summary>
    public static CameraFraming Centred(int sceneWidth, int sceneHeight) =>
        new(sceneWidth / 2.0, sceneHeight / 2.0, 1.0, 0.0);
}

public static class CameraOps
{
    /// <summary>Keys in timeline order. The stored list is not kept sorted by callers.</summary>
    public static IReadOnlyList<CameraKey> Ordered(Camera? camera) =>
        camera is null ? [] : camera.Keys.OrderBy(k => k.Frame).ToList();

    /// <summary>The framing to render <paramref name="frame"/> through.</summary>
    public static CameraFraming At(Camera? camera, int frame, int sceneWidth, int sceneHeight)
    {
        var keys = Ordered(camera);
        if (keys.Count == 0) return CameraFraming.Centred(sceneWidth, sceneHeight);
        // Outside the authored range the camera holds, rather than
        // extrapolating: a shot that runs past its last key should sit still,
        // not keep drifting.
        if (frame <= keys[0].Frame) return Framing(keys[0]);
        if (frame >= keys[^1].Frame) return Framing(keys[^1]);

        for (var i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (frame < a.Frame || frame > b.Frame) continue;

            var span = b.Frame - a.Frame;
            var t = span <= 0 ? 1.0 : (frame - a.Frame) / (double)span;
            var e = EasingOps.Ease(t, a.Ease);
            return new CameraFraming(
                Lerp(a.X, b.X, e),
                Lerp(a.Y, b.Y, e),
                LerpZoom(a.Zoom, b.Zoom, e),
                // Linear on the stored angle, deliberately: an author who
                // writes 350 means 350, and taking the short way round would
                // silently turn a long roll into a short one.
                Lerp(a.RotationDeg, b.RotationDeg, e));
        }
        return Framing(keys[^1]);
    }

    /// <summary>The key exactly on a frame, if there is one.</summary>
    public static CameraKey? KeyAt(Camera? camera, int frame) =>
        camera?.Keys.FirstOrDefault(k => k.Frame == frame);

    /// <summary>Set the framing at a frame, replacing any key already there.</summary>
    public static CameraKey SetKey(Camera camera, int frame, CameraFraming framing, Easing ease = Easing.EaseInOut)
    {
        var key = KeyAt(camera, frame);
        if (key is null)
        {
            key = new CameraKey { Frame = frame, Ease = ease };
            camera.Keys.Add(key);
        }
        key.X = framing.X;
        key.Y = framing.Y;
        key.Zoom = framing.Zoom;
        key.RotationDeg = framing.RotationDeg;
        return key;
    }

    /// <summary>Remove the key at a frame. False when there was none.</summary>
    public static bool ClearKey(Camera camera, int frame) =>
        camera.Keys.RemoveAll(k => k.Frame == frame) > 0;

    private static CameraFraming Framing(CameraKey k) => new(k.X, k.Y, Zoom(k.Zoom), k.RotationDeg);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Zoom interpolates geometrically, because zoom is a ratio. A linear
    /// push from 1x to 4x covers three quarters of its magnification in the
    /// first half of the move and visibly decelerates; a geometric one holds
    /// a constant apparent rate, which is what a camera move looks like.
    /// Easing then shapes that rate, rather than fighting it.
    /// </summary>
    private static double LerpZoom(double a, double b, double t)
    {
        var (za, zb) = (Zoom(a), Zoom(b));
        return za * Math.Pow(zb / za, t);
    }

    /// <summary>A zoom of zero or less would divide the framing away.</summary>
    private static double Zoom(double z) => z is > 0.0001 and < 10000 ? z : 1.0;
}
