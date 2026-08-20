using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The ground the analysers measure against: an origin, a direction along it,
/// and which way is up. Authored when the artist placed it, derived when not.
/// </summary>
/// <param name="Authored">
/// True when this came from the artist's own guide — the trail's filled-tick
/// rule: a statement, not a guess.
/// </param>
public readonly record struct GroundFrame(
    double OriginX, double OriginY,
    double AlongX, double AlongY,
    double UpX, double UpY,
    bool Authored)
{
    /// <summary>Height above the ground, up-positive — feet live near the minimum.</summary>
    public double Elevation(double x, double y) => (x - OriginX) * UpX + (y - OriginY) * UpY;

    /// <summary>Distance along the ground — what a planted foot must hold.</summary>
    public double Along(double x, double y) => (x - OriginX) * AlongX + (y - OriginY) * AlongY;
}

/// <summary>
/// Resolves the ground for a scene (Q137): a <see cref="GuideKind.Line"/>
/// guide named "ground" at any angle — slopes and tilted layouts measure
/// along its normal — else the canvas-horizontal ground the asset lens always
/// assumed. The pivot-vs-centroid pattern: the artist's statement wins, the
/// derived answer fills in, and the record never changes shape for it.
/// </summary>
public static class Ground
{
    /// <summary>The name that makes a line guide the ground. Case-blind, like the contact marker's label.</summary>
    public const string GuideName = "ground";

    public static GroundFrame Resolve(Scene scene)
    {
        var guide = (scene.Guides ?? []).FirstOrDefault(g =>
            g.Kind == GuideKind.Line
            && string.Equals(g.Name?.Trim(), GuideName, StringComparison.OrdinalIgnoreCase));
        if (guide is null)
        {
            // Screen Y grows downward, so the derived up is -Y — exactly the
            // "lowest ink" reading the analysers started with.
            return new GroundFrame(0, 0, 1, 0, 0, -1, Authored: false);
        }

        var radians = guide.Angle * Math.PI / 180;
        var ax = Math.Cos(radians);
        var ay = Math.Sin(radians);
        // The normal that points away from gravity: of the two, the one with
        // the negative screen Y. A vertical "ground" is nobody's floor, but
        // the tie-break keeps the arithmetic deterministic either way.
        var ux = ay;
        var uy = -ax;
        if (uy > 0 || (uy == 0 && ux < 0))
        {
            ux = -ux;
            uy = -uy;
        }
        return new GroundFrame(guide.X, guide.Y, ax, ay, ux, uy, Authored: true);
    }
}
