namespace Lightbox.Core.Documents;

/// <summary>
/// The multiplane arithmetic: how much of a camera move a layer at a given
/// depth actually shows. Stage 1 of <c>docs/DESIGN-3d-space.md</c> (Q72) — the
/// camera stays today's 2D record, and parallax is nothing but a per-layer
/// attenuation of its framing.
/// </summary>
/// <remarks>
/// <para>
/// Pure arithmetic on <see cref="CameraFraming"/>, deliberately free of any
/// rendering type: the matrix that puts it on a canvas lives beside
/// <c>CameraTransform</c> in the App project. Invariant 2 needs nothing here —
/// the factor is a pure function of depth, so there is nothing stochastic to
/// seed — and invariant 7 is kept by the caller applying the result to the
/// layer's rasterised surface, never to stroke geometry.
/// </para>
/// <para>
/// <b>The focal length is a constant, not a knob.</b> Depth is authored in
/// document units and the factor is <c>f / (f + depth)</c>, so the artist's
/// control over how strong the parallax feels is the depth itself — a second
/// dial multiplying the first would give two ways to say one thing. 1000 puts
/// useful factors on hand-sized numbers: depth 250 ≈ 0.8, 1000 = 0.5,
/// 4000 = 0.2.
/// </para>
/// </remarks>
public static class LayerDepth
{
    /// <summary>The picture-plane distance the factor is measured against.</summary>
    public const double Focal = 1000;

    /// <summary>
    /// How much of a camera move a layer at <paramref name="depth"/> shows:
    /// 1 on the picture plane, toward 0 far away, above 1 floating in front.
    /// </summary>
    /// <remarks>
    /// Clamped to [0.05, 4]: a depth at or past <c>-Focal</c> would put the
    /// layer at or behind the camera, where the division blows up, and a layer
    /// showing four times the camera's move is already past anything an artist
    /// means by "foreground".
    /// </remarks>
    public static double Factor(double? depth)
    {
        if (depth is not { } d || d == 0) return 1;
        return Math.Clamp(Focal / (Focal + d), 0.05, 4);
    }

    /// <summary>
    /// The framing a layer at <paramref name="depth"/> is rendered through:
    /// the camera's pan taken proportionally from <paramref name="home"/> (the
    /// centred, unzoomed framing a scene without a camera has), its zoom
    /// attenuated geometrically, its roll untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pan is linear in the factor — a pan of Δ moves a plane at depth d by
    /// Δ·k on screen, which is the perspective projection of a translating
    /// camera. Zoom attenuates geometrically (<c>zoom^k</c>) because zoom is a
    /// ratio, the same reason <see cref="CameraOps"/> interpolates it
    /// geometrically. Roll is untouched: a camera rolling about its own axis
    /// turns every plane by the same angle, however far away.
    /// </para>
    /// <para>
    /// At factor 1 this returns <paramref name="framing"/> unchanged, which is
    /// what makes a depthless layer render byte-identically to today.
    /// </para>
    /// </remarks>
    public static CameraFraming Attenuate(CameraFraming framing, CameraFraming home, double? depth)
    {
        var k = Factor(depth);
        if (k == 1) return framing;
        return new CameraFraming(
            home.X + (framing.X - home.X) * k,
            home.Y + (framing.Y - home.Y) * k,
            // The general geometric form rather than framing.Zoom^k, so this
            // stays right if home ever stops being the unzoomed centre.
            home.Zoom * Math.Pow(framing.Zoom / home.Zoom, k),
            framing.RotationDeg);
    }
}
