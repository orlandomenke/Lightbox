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
/// One colour of one effect — a glow's colour, a bevel's highlight. Hex
/// strings, the vocabulary strokes already use; not keyable in v1 (Q153).
/// </summary>
public sealed record EffectColorSpec(string Key, string Label, string Default);

/// <summary>
/// What a kind id means: its parameters, its reach, and how it joins the
/// filter chain. Definitions live here beside the other pixel code — the
/// model never renders, the App never touches pixels (DESIGN-effects.md).
/// </summary>
/// <param name="Shelf">
/// Which shelf the picker files it on — a presentation tag, never a
/// capability: any effect can be keyed whatever shelf it sits on.
/// </param>
/// <param name="Cpu">
/// A per-pixel pass run on the CPU instead of joining the Skia filter
/// chain, or null for the ordinary native effect. The escape hatch for
/// what a colour matrix cannot say and Skia's SkSL interpreter cannot
/// afford — a true HSL rotation measured ~300 ms per half-megapixel as a
/// runtime effect, ~an order less as plain C#. A CPU effect applies on the
/// backdrop path (adjustment layers, the scene grade), where the work is
/// clip-bounded; on a layer's own stack it renders as identity and the
/// docker steers it to an adjustment instead — clipped to the layer below,
/// which is per-layer use with the same pixels.
/// </param>
/// <param name="Style">
/// A layer style's decoration (Q153): two filters that read <em>only the
/// source silhouette</em> (null inputs), one drawn behind the content and
/// one over it. Not a link in the ordinary chain, twice over: every style
/// reads the original silhouette — a stroke outlines the drawing, never a
/// glow's fuzz — and a graph that re-read the previous style's subtree
/// re-evaluated it per reference, which measured seconds per compose for
/// five styles where this assembly measures the sum of its parts.
/// </param>
public sealed record EffectDefinition(
    string Kind,
    string Name,
    string Shelf,
    IReadOnlyList<EffectParamSpec> Params,
    Func<EffectUse, int, double> Reach,
    Func<EffectUse, int, float, SKImageFilter?, SKImageFilter?> Chain,
    Func<EffectUse, int, Action<SKBitmap>>? Cpu = null,
    IReadOnlyList<EffectColorSpec>? Colors = null,
    Func<EffectUse, int, float, (SKImageFilter? Behind, SKImageFilter? Over)>? Style = null)
{
    /// <summary>
    /// True when the kind only does its work on the backdrop path — a CPU
    /// pass is identity in a self stack, so offering it there would be a
    /// control wired to nothing. The docker reads this to keep the layer
    /// scope's add row honest; adjustment layers and the scene take it.
    /// </summary>
    public bool BackdropOnly => Cpu is not null;

    /// <summary>
    /// True for a layer style — self path only, the mirror of
    /// <see cref="BackdropOnly"/>: the backdrop has no silhouette to read,
    /// so the docker keeps styles off adjustment layers and the scene.
    /// Styles apply <em>after</em> the mask carve (Q155), through
    /// <see cref="EffectRegistry.StyleFor"/>, never
    /// <see cref="EffectRegistry.FilterFor"/>.
    /// </summary>
    public bool SelfOnly => Style is not null;

    /// <summary>The docker's colour rows; empty for an effect with none.</summary>
    public IReadOnlyList<EffectColorSpec> ColorSpecs => Colors ?? [];
}

/// <summary>
/// One executable step of a stack: a native Skia filter (consecutive native
/// uses merge into one), or a CPU pass.
/// </summary>
public sealed record EffectStep(SKImageFilter? Filter, Action<SKBitmap>? Cpu);

/// <summary>A stack lowered to steps, in stack order.</summary>
public sealed class EffectProgram
{
    public required IReadOnlyList<EffectStep> Steps { get; init; }

    public bool HasCpu => Steps.Any(s => s.Cpu is not null);

    /// <summary>The whole program as one native filter, when it is one.</summary>
    public SKImageFilter? SoleFilter =>
        Steps.Count == 1 && Steps[0].Filter is { } f ? f : null;
}

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
/// (invariant 2): no RNG, no clock, no state. The catalogue is levels and
/// HSL (point; reach 0), Gaussian blur (kernel; reach derived from its
/// own radius, so the badge cannot lie — the <c>BrushCostOf</c> precedent),
/// and the five layer styles (Q153) — silhouette decorations whose reach
/// likewise follows their own sliders.
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
            // Identity in the native chain: HSL is the CPU pass below.
            Chain: (_, _, _, inner) => inner,
            Cpu: HslCpu),

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

        // ---- layer styles (Q153): decorations of the pass's silhouette.
        // Every filter below reads only the *source* (null inputs) — the
        // carved layer content of the style group (Q155) — and says whether
        // it draws behind the content or over it. Styles never read each
        // other: a stroke outlines the drawing, not a glow's fuzz, and a
        // graph that re-read another style's subtree re-evaluated it per
        // reference, which is the exponential blow-up the cost budget
        // caught (AStyledPassCostsItsChainAndNotAnOrderMore).

        ["style.dropShadow"] = new(
            "style.dropShadow", "Drop shadow", "style",
            [
                new EffectParamSpec("distance", "Distance", 6, 0, 100),
                new EffectParamSpec("size", "Size", 6, 0, 100),
                new EffectParamSpec("angle", "Angle", 120, 0, 360),
                new EffectParamSpec("opacity", "Opacity", 75, 0, 100),
            ],
            Colors: [new EffectColorSpec("color", "Colour", "#000000")],
            Reach: (use, frame) =>
                Math.Max(0, use.At("distance", frame, 6))
                + 1.5 * Math.Max(0, use.At("size", frame, 6)),
            Chain: (_, _, _, inner) => inner,
            Style: (use, frame, scale) =>
            {
                var (dx, dy) = LightOffset(use, frame, scale, use.At("distance", frame, 6));
                var sigma = SigmaOf(use, "size", 6, frame, scale);
                var color = StyleColor(use, "color", "#000000", use.At("opacity", frame, 75));
                // The shadow falls away from the light, so the offset negates.
                return (SKImageFilter.CreateDropShadowOnly(-dx, -dy, sigma, sigma, color), null);
            }),

        ["style.outerGlow"] = new(
            "style.outerGlow", "Outer glow", "style",
            [
                new EffectParamSpec("size", "Size", 8, 0, 100),
                new EffectParamSpec("spread", "Spread", 0, 0, 50),
                new EffectParamSpec("opacity", "Opacity", 75, 0, 100),
            ],
            Colors: [new EffectColorSpec("color", "Colour", "#ffffbe")],
            Reach: (use, frame) =>
                Math.Max(0, use.At("spread", frame, 0))
                + 1.5 * Math.Max(0, use.At("size", frame, 8)),
            Chain: (_, _, _, inner) => inner,
            Style: (use, frame, scale) =>
            {
                var spread = (float)Math.Max(0, use.At("spread", frame, 0)) * scale;
                var sigma = SigmaOf(use, "size", 8, frame, scale);
                var color = StyleColor(use, "color", "#ffffbe", use.At("opacity", frame, 75));
                var silhouette = spread > 0 ? Resize(spread, null) : null;
                var soft = sigma > 0
                    ? SKImageFilter.CreateBlur(sigma, sigma, silhouette)
                    : silhouette;
                // Behind: the silhouette's own interior glows too, and the
                // content covers it.
                return (Tint(color, soft), null);
            }),

        ["style.innerGlow"] = new(
            "style.innerGlow", "Inner glow", "style",
            [
                new EffectParamSpec("size", "Size", 8, 0, 100),
                new EffectParamSpec("opacity", "Opacity", 75, 0, 100),
            ],
            Colors: [new EffectColorSpec("color", "Colour", "#ffffbe")],
            Reach: (_, _) => 0, // stays inside the silhouette
            Chain: (_, _, _, inner) => inner,
            Style: (use, frame, scale) =>
            {
                var sigma = Math.Max(0.5f, SigmaOf(use, "size", 8, frame, scale));
                var color = StyleColor(use, "color", "#ffffbe", use.At("opacity", frame, 75));
                // Where the blurred silhouette is thin the pixel is near an
                // edge; content minus its own blur is the inward glow band.
                var band = Minus(null, SKImageFilter.CreateBlur(sigma, sigma));
                return (null, Tint(color, band));
            }),

        ["style.stroke"] = new(
            "style.stroke", "Stroke", "style",
            [
                new EffectParamSpec("width", "Width", 3, 1, 50),
                // 0 outside, 1 inside, 2 centred — a picker row arrives with
                // the docker's choice control; the mapping is in the manual.
                new EffectParamSpec("position", "Position", 0, 0, 2),
                new EffectParamSpec("opacity", "Opacity", 100, 0, 100),
            ],
            Colors: [new EffectColorSpec("color", "Colour", "#000000")],
            Reach: (use, frame) => Math.Max(0, use.At("width", frame, 3)),
            Chain: (_, _, _, inner) => inner,
            Style: (use, frame, scale) =>
            {
                var w = (float)Math.Max(0, use.At("width", frame, 3)) * scale;
                if (w <= 0) return (null, null);
                var color = StyleColor(use, "color", "#000000", use.At("opacity", frame, 100));
                var position = (int)Math.Round(use.At("position", frame, 0));
                var ring = position switch
                {
                    1 => Minus(null, Resize(-w, null)),
                    2 => Minus(Resize(w / 2, null), Resize(-w / 2, null)),
                    _ => Minus(Resize(w, null), null),
                };
                return (null, Tint(color, ring));
            }),

        ["style.bevel"] = new(
            "style.bevel", "Bevel", "style",
            [
                new EffectParamSpec("size", "Size", 5, 1, 50),
                new EffectParamSpec("depth", "Depth", 30, 0, 100),
                new EffectParamSpec("angle", "Angle", 120, 0, 360),
                // 0 inner (raised inside the edge), 1 outer (a ridge around it).
                new EffectParamSpec("direction", "Direction", 0, 0, 1),
            ],
            Colors:
            [
                new EffectColorSpec("highlight", "Highlight", "#ffffff"),
                new EffectColorSpec("shadow", "Shadow", "#000000"),
            ],
            Reach: (use, frame) =>
                (int)Math.Round(use.At("direction", frame, 0)) == 1
                    ? 1.5 * Math.Max(0, use.At("size", frame, 5))
                    : 0,
            Chain: (_, _, _, inner) => inner,
            Style: (use, frame, scale) =>
            {
                var size = (float)Math.Max(0, use.At("size", frame, 5)) * scale;
                if (size <= 0) return (null, null);
                var depth = Math.Clamp(use.At("depth", frame, 30), 0, 100);
                var highlight = StyleColor(use, "highlight", "#ffffff", depth);
                var shadow = StyleColor(use, "shadow", "#000000", depth);
                var outer = (int)Math.Round(use.At("direction", frame, 0)) == 1;
                var (dx, dy) = LightOffset(use, frame, 1f, size / 2);

                // The blurred silhouette shifted toward and away from the
                // light; the sliver each shift uncovers is a shaded band —
                // light-facing for the highlight, light-averted for the
                // shadow. Inner bands sit inside the silhouette, outer
                // bands outside it: the same subtraction, operands swapped.
                var sigma = size / 2;
                var blurred = SKImageFilter.CreateBlur(sigma, sigma);
                var toward = SKImageFilter.CreateOffset(dx, dy, blurred);
                var away = SKImageFilter.CreateOffset(-dx, -dy, blurred);
                var hiBand = outer ? Minus(toward, null) : Minus(null, away);
                var shBand = outer ? Minus(away, null) : Minus(null, toward);
                return (null, Over(Tint(highlight, hiBand), Tint(shadow, shBand)));
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
    private sealed class CachedProgram
    {
        public long Fingerprint;
        public SKImageFilter? SelfFilter;
        public SKImageFilter? StyleFilter;
        public EffectProgram? Program;
        public bool Built;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectStack, CachedProgram>
        Programs = [];

    /// <summary>
    /// The stack's *native* uses as one Skia filter at one frame, or null
    /// for a stack that currently draws nothing this way — disabled uses,
    /// unknown kinds and CPU effects skip. This is the self path (a layer's
    /// own stack); a CPU effect there is identity by design, see
    /// <see cref="EffectDefinition.Cpu"/>. Cached per stack; do not dispose
    /// the result.
    /// </summary>
    public static SKImageFilter? FilterFor(EffectStack? stack, int frame, float scale = 1f) =>
        stack is null ? null : Slot(stack, frame, scale).SelfFilter;

    /// <summary>
    /// The stack's layer styles as one Skia filter, or null when it has
    /// none. Styles decorate the pass <em>after</em> its mask carve (Q155),
    /// so this chain applies in a group outside the carve while
    /// <see cref="FilterFor"/>'s applies inside it. Cached per stack; do not
    /// dispose the result.
    /// </summary>
    public static SKImageFilter? StyleFor(EffectStack? stack, int frame, float scale = 1f) =>
        stack is null ? null : Slot(stack, frame, scale).StyleFilter;

    /// <summary>
    /// The whole stack as executable steps for the backdrop path — native
    /// segments merged, CPU passes in stack order — or null when nothing
    /// applies. Cached per stack; do not dispose the steps' filters.
    /// </summary>
    public static EffectProgram? ProgramFor(EffectStack? stack, int frame, float scale = 1f) =>
        stack is null ? null : Slot(stack, frame, scale).Program;

    private static CachedProgram Slot(EffectStack stack, int frame, float scale)
    {
        var print = FingerprintOf(stack, frame, scale);
        var slot = Programs.GetOrCreateValue(stack);
        lock (slot)
        {
            if (slot.Built && slot.Fingerprint == print) return slot;

            var steps = new List<EffectStep>();
            SKImageFilter? segment = null;
            SKImageFilter? selfChain = null;
            SKImageFilter? behind = null;
            SKImageFilter? overlay = null;
            foreach (var use in stack.Uses)
            {
                if (!use.Applies) continue;
                if (Resolve(use.Kind) is not { } def) continue;
                if (def.Style is { } style)
                {
                    // A layer style: its decorations join the post-carve
                    // group on the self path (Q155) and are identity on the
                    // backdrop path, which has no silhouette to read. Later
                    // uses draw over earlier ones, on both sides of the
                    // content.
                    var (b, o) = style(use, frame, scale);
                    if (b is not null) behind = behind is null ? b : Over(b, behind);
                    if (o is not null) overlay = overlay is null ? o : Over(o, overlay);
                    continue;
                }
                if (def.Cpu is { } cpu)
                {
                    if (segment is not null) steps.Add(new EffectStep(segment, null));
                    segment = null;
                    steps.Add(new EffectStep(null, cpu(use, frame)));
                    continue;
                }
                segment = def.Chain(use, frame, scale, segment);
                selfChain = def.Chain(use, frame, scale, selfChain);
            }
            if (segment is not null) steps.Add(new EffectStep(segment, null));

            // The styled picture: decorations behind, the source itself,
            // decorations over — the source referenced exactly once as a
            // graph input, so five styles cost five graphs, never a tree of
            // re-evaluated subtrees.
            SKImageFilter? styleChain = null;
            if (behind is not null || overlay is not null)
            {
                var grounded = behind is null
                    ? null
                    : SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, behind, null);
                styleChain = overlay is null
                    ? grounded
                    : SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, grounded, overlay);
            }

            slot.Fingerprint = print;
            slot.SelfFilter = selfChain;
            slot.StyleFilter = styleChain;
            slot.Program = steps.Count == 0 ? null : new EffectProgram { Steps = steps };
            slot.Built = true;
            return slot;
        }
    }

    /// <summary>
    /// Run a program over pixels: CPU steps mutate in place, native steps
    /// draw through their filter. Takes ownership of <paramref name="source"/>
    /// and returns the result — the same instance when no native step forced
    /// a copy — which the caller disposes.
    /// </summary>
    public static SKBitmap ApplyTo(SKBitmap source, EffectProgram program)
    {
        var current = source;
        foreach (var step in program.Steps)
        {
            if (step.Cpu is { } cpu)
            {
                // A CPU pass reads bytes, so it only knows the two 8888
                // layouts; anything else is converted rather than silently
                // skipped — an identity that depends on the surface format
                // is the bug this guard replaced.
                if (current.ColorType is not (SKColorType.Rgba8888 or SKColorType.Bgra8888)
                    && current.Copy(SKColorType.Rgba8888) is { } converted)
                {
                    current.Dispose();
                    current = converted;
                }
                cpu(current);
                continue;
            }
            var info = new SKImageInfo(
                current.Width, current.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var next = new SKBitmap(info);
            using (var canvas = new SKCanvas(next))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { ImageFilter = step.Filter };
                canvas.DrawBitmap(current, 0, 0, paint);
                canvas.Flush();
            }
            current.Dispose();
            current = next;
        }
        return current;
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
            if (use.Colors is { } colors)
            {
                foreach (var (key, hex) in colors)
                {
                    hash.Add(key);
                    hash.Add(hex);
                }
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

    // ---- style-chain vocabulary -------------------------------------------

    /// <summary>Colourize a silhouette: the colour everywhere, scaled by its alpha.</summary>
    private static SKImageFilter Tint(SKColor color, SKImageFilter? input) =>
        SKImageFilter.CreateColorFilter(
            SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn), input);

    /// <summary>Foreground drawn over background; null means the style group's source.</summary>
    private static SKImageFilter Over(SKImageFilter? foreground, SKImageFilter? background) =>
        SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, background, foreground);

    /// <summary>Alpha subtraction: <paramref name="from"/> where <paramref name="take"/> is not.</summary>
    private static SKImageFilter Minus(SKImageFilter? from, SKImageFilter? take) =>
        SKImageFilter.CreateBlendMode(SKBlendMode.DstOut, from, take);

    /// <summary>
    /// The silhouette grown (or shrunk, negative <paramref name="radius"/>)
    /// by about that many pixels: a Gaussian at sigma = r/2, then an alpha
    /// ramp at the value the blur takes ~r past (or before) a straight edge.
    /// <b>Deliberately not Skia's morphology filter</b>, which measured
    /// ~740 ms per 960×540 compose on the CPU backend where this graph
    /// measures ~15 — and the blur rounds corners where a true dilation
    /// squares them off, which reads better on a drawn line anyway. The
    /// cheap approximation that looks right is the correct one (charter).
    /// </summary>
    private static SKImageFilter Resize(float radius, SKImageFilter? input)
    {
        var sigma = Math.Max(0.5f, Math.Abs(radius) / 2f);
        var blurred = SKImageFilter.CreateBlur(sigma, sigma, input);
        // For sigma = r/2 a straight edge's blurred alpha is ~2.3% at r
        // outside and ~97.7% at r inside; the ramp spans a few counts for
        // an anti-aliased edge instead of a hard step.
        var alpha = new byte[256];
        var identity = new byte[256];
        int low = radius >= 0 ? 3 : 245, high = radius >= 0 ? 11 : 253;
        for (var i = 0; i < 256; i++)
        {
            identity[i] = (byte)i;
            alpha[i] = (byte)Math.Clamp((i - low) * 255 / (high - low), 0, 255);
        }
        return SKImageFilter.CreateColorFilter(
            SKColorFilter.CreateTable(alpha, identity, identity, identity), blurred);
    }

    /// <summary>A blur sigma from a size-in-pixels parameter — the radius/2 convention.</summary>
    private static float SigmaOf(EffectUse use, string key, double fallback, int frame, float scale) =>
        (float)(Math.Max(0, use.At(key, frame, fallback)) / 2.0) * scale;

    /// <summary>
    /// The authored colour with the style's opacity folded into its alpha.
    /// An unparseable hex falls back to the spec's default rather than to a
    /// surprise colour — the same forgiveness a stroke's colour gets.
    /// </summary>
    private static SKColor StyleColor(EffectUse use, string key, string fallback, double opacityPct)
    {
        var color = BrushEngine.ParseColor(use.ColorAt(key, fallback));
        var alpha = (byte)Math.Round(Math.Clamp(opacityPct, 0, 100) / 100.0 * color.Alpha);
        return color.WithAlpha(alpha);
    }

    /// <summary>
    /// A distance along the light direction, in device pixels, screen-y
    /// down. The angle convention is Photoshop's — degrees anticlockwise
    /// from the right, so the default 120° lights from the upper left.
    /// </summary>
    private static (float Dx, float Dy) LightOffset(
        EffectUse use, int frame, float scale, double distance)
    {
        var radians = use.At("angle", frame, 120) * Math.PI / 180.0;
        var d = Math.Max(0, distance) * scale;
        return ((float)(Math.Cos(radians) * d), (float)(-Math.Sin(radians) * d));
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
    /// A true HSL round-trip per pixel, as a CPU pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the affine hue-rotation matrix, and that is the art-director's
    /// veto, verified by hand:</b> the standard luma-axis matrix keeps luma
    /// by pushing a channel negative, the clamp eats it, and a saturated
    /// flat — the exact colour a cel animator turns this on — comes out both
    /// duller and paler the further it spins (pure red +120° lands at
    /// (0,146,0) instead of green). Converting to HSL, rotating H, and
    /// converting back stays in gamut by construction.
    /// </para>
    /// <para>
    /// <b>And not an SkSL runtime effect, which was the first fix:</b> Skia's
    /// CPU interpreter measured ~300 ms per half-megapixel for it — three
    /// hundred times a native colour matrix — where this loop measures in
    /// single-digit milliseconds. It is still a pure function of the record
    /// and the frame (invariant 2): no randomness, no state, the same pixel
    /// in gives the same pixel out.
    /// </para>
    /// <para>
    /// Saturation scales S and lightness offsets L, both clamped — standard
    /// HSL semantics. A positive lightness still lifts blacks (the manual
    /// says so; Levels is the value tool), but a hue spin now reads as a hue
    /// spin.
    /// </para>
    /// </remarks>
    private static Action<SKBitmap> HslCpu(EffectUse use, int frame)
    {
        var hue = (float)(use.At("hue", frame, 0) / 360.0);
        var sat = (float)(1 + Math.Clamp(use.At("saturation", frame, 0), -100, 100) / 100.0);
        var light = (float)(Math.Clamp(use.At("lightness", frame, 0), -100, 100) / 100.0);
        return bitmap => ApplyHsl(bitmap, hue, sat, light);
    }

    private static void ApplyHsl(SKBitmap bitmap, float hue, float sat, float light)
    {
        // Both byte orders a compose surface hands us: raster tests build
        // Rgba8888, the app's surfaces snapshot as the platform's N32, which
        // is Bgra8888 here. Hue rotation cares which byte is red, so the
        // offset is looked up rather than assumed — and a layout this loop
        // does not know stays untouched (ApplyTo converts those first).
        var red = bitmap.ColorType switch
        {
            SKColorType.Rgba8888 => 0,
            SKColorType.Bgra8888 => 2,
            _ => -1,
        };
        if (red < 0) return;
        var blue = 2 - red;
        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null) return;
        var pixels = pixmap.GetPixelSpan<byte>();
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 0) continue;
            // Premultiplied in, premultiplied out.
            var inv = 1f / a;
            var r = pixels[i + red] * inv;
            var g = pixels[i + 1] * inv;
            var b = pixels[i + blue] * inv;

            var mx = MathF.Max(r, MathF.Max(g, b));
            var mn = MathF.Min(r, MathF.Min(g, b));
            var l = (mx + mn) * 0.5f;
            var d = mx - mn;
            float h = 0f, s = 0f;
            if (d > 1e-5f)
            {
                s = d / (1f - MathF.Abs(2f * l - 1f));
                if (mx == r) h = ((g - b) / d % 6f + 6f) % 6f;
                else if (mx == g) h = (b - r) / d + 2f;
                else h = (r - g) / d + 4f;
                h *= 1f / 6f;
            }

            h += hue;
            h -= MathF.Floor(h);
            s = Math.Clamp(s * sat, 0f, 1f);
            l = Math.Clamp(l + light, 0f, 1f);

            var c = (1f - MathF.Abs(2f * l - 1f)) * s;
            var hp = h * 6f;
            var x = c * (1f - MathF.Abs(hp % 2f - 1f));
            float or, og, ob;
            if (hp < 1f) { or = c; og = x; ob = 0f; }
            else if (hp < 2f) { or = x; og = c; ob = 0f; }
            else if (hp < 3f) { or = 0f; og = c; ob = x; }
            else if (hp < 4f) { or = 0f; og = x; ob = c; }
            else if (hp < 5f) { or = x; og = 0f; ob = c; }
            else { or = c; og = 0f; ob = x; }
            var m = l - c * 0.5f;

            pixels[i + red] = (byte)Math.Clamp((int)MathF.Round((or + m) * a), 0, 255);
            pixels[i + 1] = (byte)Math.Clamp((int)MathF.Round((og + m) * a), 0, 255);
            pixels[i + blue] = (byte)Math.Clamp((int)MathF.Round((ob + m) * a), 0, 255);
        }
    }
}
