using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The camera's view of a world point, as the analysers need it (Q137):
/// pure arithmetic mirroring the compositor's matrix — translate to the
/// framed point, zoom, roll — without the output-centring offset, which is
/// the same constant on every point of one frame and says nothing about
/// motion. What varies frame to frame is exactly what "as the camera sees
/// it" means.
/// </summary>
public static class CameraView
{
    /// <summary>The framing at a frame, or null when the document has no camera — the absent-camera rule.</summary>
    public static CameraFraming? FramingAt(Scene scene, int frame) =>
        scene.Camera is { } camera
            ? CameraOps.At(camera, frame, scene.Width, scene.Height)
            : null;

    /// <summary>World to camera view.</summary>
    public static (double X, double Y) Project(in CameraFraming f, double x, double y)
    {
        var dx = (x - f.X) * f.Zoom;
        var dy = (y - f.Y) * f.Zoom;
        var r = f.RotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    /// <summary>
    /// Camera view back to world — what lets a target computed where the
    /// audience looks land as a ghost tick where the artist draws.
    /// </summary>
    public static (double X, double Y) Unproject(in CameraFraming f, double x, double y)
    {
        var r = -f.RotationDeg * Math.PI / 180;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);
        var dx = x * cos - y * sin;
        var dy = x * sin + y * cos;
        return (f.X + dx / f.Zoom, f.Y + dy / f.Zoom);
    }
}
