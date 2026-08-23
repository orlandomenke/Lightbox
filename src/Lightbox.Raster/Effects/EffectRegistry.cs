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
public sealed record EffectDefinition(
    string Kind,
    string Name,
    string Shelf,
    IReadOnlyList<EffectParamSpec> Params,
    Func<EffectUse, int, double> Reach,
    Func<EffectUse, int, float, SKImageFilter?, SKImageFilter?> Chain,
    Func<EffectUse, int, Action<SKBitmap>>? Cpu = null)
{
    /// <summary>
    /// True when the kind only does its work on the backdrop path — a CPU
    /// pass is identity in a self stack, so offering it there would be a
    /// control wired to nothing. The docker reads this to keep the layer
    /// scope's add row honest; adjustment layers and the scene take it.
    /// </summary>
    public bool BackdropOnly => Cpu is not null;
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
            foreach (var use in stack.Uses)
            {
                if (!use.Applies) continue;
                if (Resolve(use.Kind) is not { } def) continue;
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

            slot.Fingerprint = print;
            slot.SelfFilter = selfChain;
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
