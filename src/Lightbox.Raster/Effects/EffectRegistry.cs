using Lightbox.Core.Effects;
using SkiaSharp;

namespace Lightbox.Raster.Effects;

/// <summary>One slider of one effect, as the docker and the defaults see it.</summary>
/// <param name="Increment">
/// The keyboard-nudge step. Defaults to 1, which suits a 0–255 or ±180
/// range and is useless on gamma's 0.1–10 — the parameter whose useful
/// travel is narrowest is exactly the one an artist feels toward by arrow
/// key, so the spec says its own step.
/// </param>
public sealed record EffectParamSpec(
    string Key, string Label, double Default, double Min, double Max, double Increment = 1);

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
                new EffectParamSpec("gamma", "Gamma", 1, 0.1, 10, Increment: 0.1),
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
    /// One cached filter per stack instance, refreshed when the evaluated
    /// values change. The publish path asks for a stack's filter per pointer
    /// event; a static stack must answer with the same instance, or every
    /// publish allocates a chain the GC has to chase (the leak review's
    /// finding). Replaced filters are dropped to the finalizer rather than
    /// disposed: a deferred publish in flight on the render thread may still
    /// be drawing through the old one, and freeing it under that draw is the
    /// use-after-free this comment exists to prevent.
    /// </summary>
    private sealed class CachedFilter
    {
        public long Fingerprint;
        public SKImageFilter? Filter;
        public bool Built;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectStack, CachedFilter>
        Filters = [];

    /// <summary>
    /// The stack as one Skia filter at one frame, or null for a stack that
    /// currently does nothing — disabled uses and unknown kinds skip, in
    /// order, exactly as the chain composes. Cached per stack; do not
    /// dispose the result.
    /// </summary>
    public static SKImageFilter? FilterFor(EffectStack? stack, int frame, float scale = 1f)
    {
        if (stack is null) return null;
        var print = FingerprintOf(stack, frame, scale);
        var slot = Filters.GetOrCreateValue(stack);
        lock (slot)
        {
            if (slot.Built && slot.Fingerprint == print) return slot.Filter;
            SKImageFilter? chain = null;
            foreach (var use in stack.Uses)
            {
                if (!use.Applies) continue;
                if (Resolve(use.Kind) is not { } def) continue;
                chain = def.Chain(use, frame, scale, chain);
            }
            slot.Fingerprint = print;
            slot.Filter = chain;
            slot.Built = true;
            return chain;
        }
    }

    /// <summary>
    /// Everything the filter is a function of, folded to one number: the
    /// kinds, their order, what applies, the scale, and every parameter's
    /// value *evaluated at the frame* — so a keyed radius re-fingerprints as
    /// the playhead moves and a constant one never does.
    /// </summary>
    private static long FingerprintOf(EffectStack stack, int frame, float scale)
    {
        var hash = new HashCode();
        hash.Add(scale);
        foreach (var use in stack.Uses)
        {
            hash.Add(use.Kind);
            hash.Add(use.Applies);
            foreach (var (key, param) in use.Params)
            {
                hash.Add(key);
                hash.Add(param.At(frame));
            }
        }
        return hash.ToHashCode();
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

    /// <summary>
    /// A true HSL round-trip per pixel, as an SkSL colour filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the affine hue-rotation matrix, and that is the art-director's
    /// veto, verified by hand:</b> the standard luma-axis matrix keeps luma
    /// by pushing a channel negative, the filter clamps it, and a saturated
    /// flat — the exact colour a cel animator turns this on — comes out both
    /// duller and paler the further it spins (pure red +120° lands at
    /// (0,146,0) instead of green). Converting to HSL, rotating H, and
    /// converting back stays in gamut by construction. It cannot be a colour
    /// matrix, so it is a runtime effect — compiled once, uniforms per value,
    /// still a pure function of the record and the frame (invariant 2).
    /// </para>
    /// <para>
    /// Saturation scales S and lightness offsets L, both clamped — standard
    /// HSL semantics. A positive lightness still lifts blacks (the manual
    /// says so; Levels is the value tool), but a hue spin now reads as a hue
    /// spin.
    /// </para>
    /// </remarks>
    private const string HslSksl = """
        uniform float uHue;    // rotation in turns
        uniform float uSat;    // multiplier, 0..2
        uniform float uLight;  // offset on L, -1..1

        half4 main(half4 color) {
            float a = float(color.a);
            float3 rgb = a > 0.0 ? float3(color.rgb) / a : float3(color.rgb);
            float mx = max(rgb.r, max(rgb.g, rgb.b));
            float mn = min(rgb.r, min(rgb.g, rgb.b));
            float l = (mx + mn) * 0.5;
            float d = mx - mn;
            float s = 0.0;
            float h = 0.0;
            if (d > 0.00001) {
                s = d / (1.0 - abs(2.0 * l - 1.0));
                if (mx == rgb.r)      { h = mod((rgb.g - rgb.b) / d, 6.0); }
                else if (mx == rgb.g) { h = (rgb.b - rgb.r) / d + 2.0; }
                else                  { h = (rgb.r - rgb.g) / d + 4.0; }
                h = h / 6.0;
            }
            h = fract(h + uHue);
            s = clamp(s * uSat, 0.0, 1.0);
            l = clamp(l + uLight, 0.0, 1.0);
            float c = (1.0 - abs(2.0 * l - 1.0)) * s;
            float hp = h * 6.0;
            float x = c * (1.0 - abs(mod(hp, 2.0) - 1.0));
            float3 o;
            if (hp < 1.0)      { o = float3(c, x, 0.0); }
            else if (hp < 2.0) { o = float3(x, c, 0.0); }
            else if (hp < 3.0) { o = float3(0.0, c, x); }
            else if (hp < 4.0) { o = float3(0.0, x, c); }
            else if (hp < 5.0) { o = float3(x, 0.0, c); }
            else               { o = float3(c, 0.0, x); }
            o = o + (l - c * 0.5);
            return half4(half3(o * a), half(a));
        }
        """;

    private static readonly Lazy<SKRuntimeEffect> HslEffect = new(() =>
    {
        var effect = SKRuntimeEffect.CreateColorFilter(HslSksl, out var errors);
        return effect ?? throw new InvalidOperationException($"HSL SkSL failed to compile: {errors}");
    });

    private static SKColorFilter HslFilter(EffectUse use, int frame)
    {
        var uniforms = new SKRuntimeEffectUniforms(HslEffect.Value)
        {
            ["uHue"] = (float)(use.At("hue", frame, 0) / 360.0),
            ["uSat"] = (float)(1 + Math.Clamp(use.At("saturation", frame, 0), -100, 100) / 100.0),
            ["uLight"] = (float)(Math.Clamp(use.At("lightness", frame, 0), -100, 100) / 100.0),
        };
        return HslEffect.Value.ToColorFilter(uniforms)
            ?? throw new InvalidOperationException("HSL uniforms rejected.");
    }
}
