using Lightbox.Core.Effects;
using SkiaSharp;

namespace Lightbox.Raster.Effects;

/// <summary>One slider of one effect, as the docker and the defaults see it.</summary>
public sealed record EffectParamSpec(
    string Key, string Label, double Default, double Min, double Max);

/// <summary>
/// What a kind id means: its parameters, its reach, and how it joins the
/// filter chain. Definitions live here beside the other pixel code — the
/// model never renders, the App never touches pixels (DESIGN-effects.md).
/// </summary>
/// <param name="Shelf">
/// Which shelf the picker files it on — a presentation tag, never a
/// capability: any effect can be keyed whatever shelf it sits on.
/// </param>
public sealed record EffectDefinition(
    string Kind,
    string Name,
    string Shelf,
    IReadOnlyList<EffectParamSpec> Params,
    Func<EffectUse, int, double> Reach,
    Func<EffectUse, int, float, SKImageFilter?, SKImageFilter?> Chain);

/// <summary>
/// The kind-id registry and the one place a stack becomes a Skia filter.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unknown kind renders as identity and is never dropped</b> — a
/// document from a newer build keeps its stranger through save and load
/// (<c>EffectRecordTests.AnUnknownKindIsPreservedNotDropped</c>) and simply
/// does not apply it here. <see cref="HasUnknown"/> is the UI's flag.
/// </para>
/// <para>
/// <b>Everything is a pure function of the record and the frame</b>
/// (invariant 2): no RNG, no clock, no state. The v1 catalogue is levels and
/// HSL (point; reach 0) and Gaussian blur (kernel; reach derived from its
/// own radius, so the badge cannot lie — the <c>BrushCostOf</c> precedent).
/// </para>
/// <para>
/// <b><paramref name="scale"/> is device pixels per document unit.</b> Reach
/// and radius are declared in document pixels (invariant 7); the caller says
/// how many device pixels that currently is, because filters attached to an
/// identity-transform snapshot draw cannot read a canvas matrix.
/// </para>
/// </remarks>
public static class EffectRegistry
{
    private static readonly Dictionary<string, EffectDefinition> ByKind = new()
    {
        ["grade.levels"] = new(
            "grade.levels", "Levels", "grade",
            [
                new EffectParamSpec("inBlack", "Input black", 0, 0, 254),
                new EffectParamSpec("inWhite", "Input white", 255, 1, 255),
                new EffectParamSpec("gamma", "Gamma", 1, 0.1, 10),
                new EffectParamSpec("outBlack", "Output black", 0, 0, 255),
                new EffectParamSpec("outWhite", "Output white", 255, 0, 255),
            ],
            Reach: (_, _) => 0,
            Chain: (use, frame, _, inner) =>
                SKImageFilter.CreateColorFilter(LevelsFilter(use, frame), inner)),

        ["grade.hsl"] = new(
            "grade.hsl", "Hue / Saturation", "grade",
            [
                new EffectParamSpec("hue", "Hue", 0, -180, 180),
                new EffectParamSpec("saturation", "Saturation", 0, -100, 100),
                new EffectParamSpec("lightness", "Lightness", 0, -100, 100),
            ],
            Reach: (_, _) => 0,
            Chain: (use, frame, _, inner) =>
                SKImageFilter.CreateColorFilter(HslFilter(use, frame), inner)),

        ["blur.gaussian"] = new(
            "blur.gaussian", "Gaussian blur", "blur",
            [new EffectParamSpec("radius", "Radius", 4, 0, 100)],
            // The visible spill of a Gaussian is ~3 sigma, and sigma is
            // radius/2 — the feather convention the selection clip already
            // uses, so one word means one thing across the app.
            Reach: (use, frame) => 1.5 * Math.Max(0, use.At("radius", frame, 4)),
            Chain: (use, frame, scale, inner) =>
            {
                var sigma = (float)(Math.Max(0, use.At("radius", frame, 4)) / 2.0) * scale;
                return sigma <= 0 ? inner : SKImageFilter.CreateBlur(sigma, sigma, inner);
            }),
    };

    public static EffectDefinition? Resolve(string kind) =>
        ByKind.TryGetValue(kind, out var def) ? def : null;

    /// <summary>Every definition, for the picker, shelf-tagged.</summary>
    public static IReadOnlyCollection<EffectDefinition> All => ByKind.Values;

    /// <summary>Whether the stack carries a kind this build cannot render.</summary>
    public static bool HasUnknown(EffectStack stack) =>
        stack.Uses.Exists(u => Resolve(u.Kind) is null);

    /// <summary>
    /// The stack as one Skia filter at one frame, or null for a stack that
    /// currently does nothing — disabled uses and unknown kinds skip, in
    /// order, exactly as the chain composes.
    /// </summary>
    public static SKImageFilter? FilterFor(EffectStack? stack, int frame, float scale = 1f)
    {
        if (stack is null) return null;
        SKImageFilter? chain = null;
        foreach (var use in stack.Uses)
        {
            if (!use.Applies) continue;
            if (Resolve(use.Kind) is not { } def) continue;
            chain = def.Chain(use, frame, scale, chain);
        }
        return chain;
    }

    /// <summary>
    /// How far, in document pixels, a pixel's output can depend on its input
    /// across the whole stack — what a dirty region must inflate by
    /// (invariant 6, the brush-reach rule applied to effects).
    /// </summary>
    public static double ReachOf(EffectStack? stack, int frame)
    {
        if (stack is null) return 0;
        var reach = 0.0;
        foreach (var use in stack.Uses)
        {
            if (!use.Applies) continue;
            if (Resolve(use.Kind) is not { } def) continue;
            reach += def.Reach(use, frame);
        }
        return reach;
    }

    private static SKColorFilter LevelsFilter(EffectUse use, int frame)
    {
        var inBlack = use.At("inBlack", frame, 0);
        var inWhite = Math.Max(inBlack + 1, use.At("inWhite", frame, 255));
        var gamma = Math.Clamp(use.At("gamma", frame, 1), 0.1, 10);
        var outBlack = use.At("outBlack", frame, 0);
        var outWhite = use.At("outWhite", frame, 255);

        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var v = Math.Clamp((i - inBlack) / (inWhite - inBlack), 0, 1);
            v = Math.Pow(v, 1.0 / gamma);
            table[i] = (byte)Math.Clamp(Math.Round(outBlack + v * (outWhite - outBlack)), 0, 255);
        }
        var identity = new byte[256];
        for (var i = 0; i < 256; i++) identity[i] = (byte)i;
        return SKColorFilter.CreateTable(identity, table, table, table);
    }

    private static SKColorFilter HslFilter(EffectUse use, int frame)
    {
        var hue = use.At("hue", frame, 0) * Math.PI / 180.0;
        var sat = 1 + Math.Clamp(use.At("saturation", frame, 0), -100, 100) / 100.0;
        var light = Math.Clamp(use.At("lightness", frame, 0), -100, 100) / 100.0 * 255.0;

        // Rec. 601 luma, the vector saturation pivots around and hue spins
        // about — the standard hue-rotation matrix, composed with saturation
        // and a lightness offset into one 4x5.
        const double lr = 0.299, lg = 0.587, lb = 0.114;
        var cos = Math.Cos(hue);
        var sin = Math.Sin(hue);

        double[,] h =
        {
            { lr + cos * (1 - lr) + sin * -lr, lg + cos * -lg + sin * -lg, lb + cos * -lb + sin * (1 - lb) },
            { lr + cos * -lr + sin * 0.143, lg + cos * (1 - lg) + sin * 0.140, lb + cos * -lb + sin * -0.283 },
            { lr + cos * -lr + sin * -(1 - lr), lg + cos * -lg + sin * lg, lb + cos * (1 - lb) + sin * lb },
        };

        var m = new float[20];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var luma = col == 0 ? lr : col == 1 ? lg : lb;
                // Saturation lerps each hue-rotated channel toward luma.
                m[row * 5 + col] = (float)(luma * (1 - sat) + h[row, col] * sat);
            }
            m[row * 5 + 4] = (float)light;
        }
        m[3 * 5 + 3] = 1; // alpha untouched
        return SKColorFilter.CreateColorMatrix(m);
    }
}
