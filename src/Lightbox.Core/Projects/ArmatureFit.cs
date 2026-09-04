using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>How big a pulled rig should be.</summary>
/// <remarks>
/// <b>Three, and the third one has to exist.</b> The owner's "though this
/// should be optional" is <see cref="Original"/>: the goblin being short is
/// data, not an accident to normalise away, and a tool that always fits a rig
/// to the frame cannot draw a size comparison at all.
/// </remarks>
public enum RigFit
{
    /// <summary>
    /// As many head units tall as it was saved at, measured against the
    /// document's own character height scale. The proportion answer.
    /// </summary>
    Heads,

    /// <summary>
    /// The same share of the paper's height it filled when it was saved — the
    /// guide-set rule, and the fallback when nothing can be measured in heads.
    /// </summary>
    Canvas,

    /// <summary>Exactly the bind pose that was saved, in the pixels it was saved at.</summary>
    Original,
}

/// <summary>
/// Carries a saved skeleton onto another document — Q181's second half.
/// </summary>
/// <remarks>
/// <para>
/// <b>The head unit is the currency and the height scale is the exchange.</b>
/// <see cref="GuideKind.HeightScale"/> turns out to be shaped for this without
/// being asked: <c>(X, Y)</c> is the <em>bottom</em> — the ground a character
/// stands on — and <see cref="Guide.Spacing"/> is one head. So a rig lands
/// feet-on-anchor at <c>heads × spacing</c> and is right on any paper at any
/// resolution <em>without being told the resolution</em>, which is what keeps
/// the goblin shorter than the human across a dozen files.
/// </para>
/// <para>
/// <b>Only three numbers per bone carry a length</b> — <see cref="Bone.Length"/>
/// and the origin offset <see cref="Bone.X"/>/<see cref="Bone.Y"/>. Rotations
/// are dimensionless, IK chains and spline chains name bones rather than
/// points, and a constraint's influence and offset are an amount and an angle.
/// So scaling a rig is those three numbers and nothing else, which is why this
/// can be a function rather than a traversal full of special cases.
/// </para>
/// <para>
/// <b>Pull time only.</b> <c>docs/DESIGN-bones.md</c>'s "one trap" is that the
/// bind pose is the coordinate space dab dynamics seed from, so rescaling an
/// armature that already has strokes bound to it re-rolls every dab and the
/// character boils. Scaling a rig nothing is bound to yet is an authoring act
/// and is safe; doing it afterwards is not, and the caller is what has to
/// refuse. Nothing here multiplies a stroke coordinate, so invariant 7 is not
/// in play either.
/// </para>
/// </remarks>
public static class ArmatureFit
{
    /// <summary>
    /// The box the bind pose occupies in document pixels — every bone's origin
    /// and every bone's tip.
    /// </summary>
    /// <remarks>
    /// Origins <em>and</em> tips, because a bone's frame ends at its tip and a
    /// leaf's tip is usually the lowest thing in the rig — measuring origins
    /// alone would lose a foot and stand the character in the ground.
    /// </remarks>
    public static (double MinX, double MinY, double MaxX, double MaxY)? BindBounds(Armature armature)
    {
        if (armature.Bones.Count == 0) return null;
        var placements = ArmatureOps.Solve(armature);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var bone in armature.Bones)
        {
            if (!placements.TryGetValue(bone.Id, out var at)) continue;
            var (tipX, tipY) = at.Tip(bone.Length);
            minX = Math.Min(minX, Math.Min(at.X, tipX));
            minY = Math.Min(minY, Math.Min(at.Y, tipY));
            maxX = Math.Max(maxX, Math.Max(at.X, tipX));
            maxY = Math.Max(maxY, Math.Max(at.Y, tipY));
        }
        return minX > maxX ? null : (minX, minY, maxX, maxY);
    }

    /// <summary>How tall the bind pose stands, in document pixels.</summary>
    public static double BindHeight(Armature armature) =>
        BindBounds(armature) is { } box ? box.MaxY - box.MinY : 0;

    /// <summary>
    /// How many head units tall this rig stands against that height scale, or
    /// null when either has nothing to measure.
    /// </summary>
    public static double? HeadsOn(Armature armature, Guide? heightScale)
    {
        if (heightScale is not { Kind: GuideKind.HeightScale, Spacing: > 0 } scale) return null;
        var height = BindHeight(armature);
        return height > 0 ? height / scale.Spacing : null;
    }

    /// <summary>Multiply every length in the rig. Rotations are left alone.</summary>
    public static void Scale(Armature armature, double factor)
    {
        if (factor <= 0 || Math.Abs(factor - 1) < 1e-12) return;
        foreach (var bone in armature.Bones)
        {
            bone.Length *= factor;
            bone.X *= factor;
            bone.Y *= factor;
        }
    }

    /// <summary>
    /// Move the whole rig by an offset in document pixels.
    /// </summary>
    /// <remarks>
    /// Roots only: a child's <see cref="Bone.X"/>/<see cref="Bone.Y"/> is an
    /// offset inside its parent's frame, so moving it too would move it twice.
    /// </remarks>
    public static void MoveBy(Armature armature, double dx, double dy)
    {
        foreach (var bone in armature.Bones.Where(b => b.ParentId is null))
        {
            bone.X += dx;
            bone.Y += dy;
        }
    }

    /// <summary>
    /// A copy of <paramref name="set"/>'s skeleton, sized and placed for this
    /// document.
    /// </summary>
    /// <param name="set">The library entry being pulled.</param>
    /// <param name="onto">The paper it is landing on.</param>
    /// <param name="heightScale">
    /// The document's character height scale, if it has one — what
    /// <see cref="RigFit.Heads"/> measures against and stands the rig on.
    /// </param>
    /// <param name="fit">What decides its size.</param>
    /// <remarks>
    /// <b>Asking for heads and having nothing to measure falls back rather
    /// than failing.</b> The artist asked for the size that keeps proportions;
    /// on a document with no height scale the honest nearest thing is the
    /// canvas rule, and refusing to place the rig at all would be a worse
    /// answer to a reasonable request. <see cref="LandedAs"/> says which rule
    /// actually ran, so a caller can tell the artist which one they got.
    /// </remarks>
    public static Armature Onto(RigSet set, AuthoredCanvas onto, Guide? heightScale, RigFit fit)
    {
        var rig = set.Armature.Clone();
        var mode = LandedAs(set, onto, heightScale, fit);
        if (BindBounds(rig) is not { } box) return rig;
        var height = box.MaxY - box.MinY;
        if (height <= 0) return rig;

        switch (mode)
        {
            case RigFit.Heads:
                // heads × one head, standing on the anchor. The scale's (X, Y)
                // is the ground, so the rig's feet go exactly there.
                Scale(rig, heightScale!.Spacing * set.Heads!.Value / height);
                Place(rig, heightScale.X, heightScale.Y);
                break;

            case RigFit.Canvas:
                var from = set.Canvas!;
                Scale(rig, (double)onto.Height / from.Height);
                // Where its feet stood on the old paper, as a fraction, is
                // where they stand on the new — the guide-set rule exactly.
                var fx = ((box.MinX + box.MaxX) / 2 - from.Left) / from.Width;
                var fy = (box.MaxY - from.Top) / (double)from.Height;
                Place(rig, onto.Left + fx * onto.Width, onto.Top + fy * onto.Height);
                break;
        }
        return rig;
    }

    /// <summary>
    /// Which rule a pull would actually use — the one asked for, or the
    /// fallback when it has nothing to work from.
    /// </summary>
    public static RigFit LandedAs(RigSet set, AuthoredCanvas onto, Guide? heightScale, RigFit fit)
    {
        if (fit == RigFit.Heads
            && set.Heads is > 0
            && heightScale is { Kind: GuideKind.HeightScale, Spacing: > 0 })
        {
            return RigFit.Heads;
        }
        if (fit != RigFit.Original && set.Canvas is { IsUsable: true } && onto.IsUsable)
        {
            return RigFit.Canvas;
        }
        return RigFit.Original;
    }

    /// <summary>Stand the rig's feet, centred, at this point.</summary>
    private static void Place(Armature rig, double x, double bottom)
    {
        if (BindBounds(rig) is not { } box) return;
        MoveBy(rig, x - (box.MinX + box.MaxX) / 2, bottom - box.MaxY);
    }
}
