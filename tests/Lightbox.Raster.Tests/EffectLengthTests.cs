using Lightbox.Raster.Effects;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Which effect parameters are lengths in document pixels (B466): the ones a
/// resize scales. The registry says so, and this list is the omission guard —
/// a parameter added to any effect fails here until somebody says which kind
/// of number it is.
/// </summary>
public class EffectLengthTests
{
    /// <summary>Every parameter the renderer multiplies by the device scale.</summary>
    private static readonly HashSet<(string Kind, string Key)> Lengths =
    [
        ("blur.gaussian", "radius"),
        ("detail.sharpen", "radius"),
        ("detail.edges", "radius"),
        ("anim.wiggle", "amount"),
        ("grade.grain", "size"),
        ("style.dropShadow", "distance"),
        ("style.dropShadow", "size"),
        ("style.outerGlow", "size"),
        ("style.outerGlow", "spread"),
        ("style.innerGlow", "size"),
        ("style.stroke", "width"),
        ("style.bevel", "size"),
    ];

    /// <summary>
    /// Every other parameter, by kind: levels and channel values, percentages,
    /// angles, counts of frames, seeds and switches. None of them is a
    /// distance on the paper.
    /// </summary>
    private static readonly HashSet<(string Kind, string Key)> NotLengths =
    [
        ("grade.levels", "inBlack"), ("grade.levels", "inWhite"), ("grade.levels", "gamma"),
        ("grade.levels", "outBlack"), ("grade.levels", "outWhite"),
        ("grade.hsl", "hue"), ("grade.hsl", "saturation"), ("grade.hsl", "lightness"),
        ("detail.sharpen", "amount"),
        ("grade.threshold", "level"),
        ("grade.posterize", "levels"),
        ("grade.gradientMap", "midpoint"),
        ("anim.wiggle", "hold"), ("anim.wiggle", "seed"),
        ("anim.flicker", "amount"), ("anim.flicker", "hold"), ("anim.flicker", "seed"),
        ("grade.grain", "amount"), ("grade.grain", "hold"), ("grade.grain", "seed"),
        ("style.dropShadow", "angle"), ("style.dropShadow", "opacity"),
        ("style.outerGlow", "opacity"),
        ("style.innerGlow", "opacity"),
        ("style.stroke", "position"), ("style.stroke", "opacity"),
        ("style.bevel", "depth"), ("style.bevel", "angle"), ("style.bevel", "direction"),
    ];

    [Fact]
    public void EveryEffectParameterIsEitherALengthOrNot()
    {
        var registered = EffectRegistry.All.SelectMany(d => d.Params.Select(p => (d.Kind, p.Key))).ToHashSet();

        var unclassified = registered.Except(Lengths).Except(NotLengths).ToList();
        var gone = Lengths.Concat(NotLengths).Except(registered).ToList();
        Assert.True(unclassified.Count == 0,
            "effect parameters nobody has said are lengths or not: " + string.Join(", ", unclassified)
            + ". A length in document pixels is scaled by an image resize; list it in Lengths and mark it in the registry.");
        Assert.True(gone.Count == 0, "listed here and no longer registered: " + string.Join(", ", gone));
    }

    [Fact]
    public void TheRegistryMarksExactlyTheLengths()
    {
        foreach (var definition in EffectRegistry.All)
            foreach (var param in definition.Params)
                Assert.True(
                    param.Length == Lengths.Contains((definition.Kind, param.Key)),
                    $"{definition.Kind}.{param.Key} is marked Length={param.Length}");

        Assert.True(EffectRegistry.IsLength("blur.gaussian", "radius"));
        Assert.False(EffectRegistry.IsLength("style.dropShadow", "angle"));
        Assert.False(EffectRegistry.IsLength("future.ripple", "radius"));
    }
}
