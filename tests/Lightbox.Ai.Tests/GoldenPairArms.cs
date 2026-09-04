using System.Text.Json;
using Xunit.Abstractions;
using Lightbox.Ai.Golden;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Puts the golden pairs to a model <i>two ways</i> — as stroke geometry, and as
/// rendered pictures plus a stroke listing — so Q180 is answered by measurement
/// rather than by intuition.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is not called from here, and that is the design rather than a
/// limitation.</b> There is no provider key in this repo and no live-provider
/// test in the suite; adding one would make the golden set cost money to run and
/// stop it running in CI at all. So this splits into two halves that meet on
/// disk: <see cref="DumpTheTwoArms"/> writes what each arm's model would see,
/// something answers them, and <see cref="ScoreTheTwoArms"/> reads the answers
/// back through <see cref="StrokeParsing"/> and
/// <see cref="CapabilityProfiler"/> — the same parser and the same scoring a
/// real provider's reply goes through.
/// </para>
/// <para>
/// <b>What answers them is deliberately left open.</b> A provider with a key, a
/// local model, or — the case this was built for — an agent, which is a model
/// that is already here and costs nothing extra. The arms are files, so whatever
/// answers them can be blinded: an agent shown only the pictures cannot read the
/// coordinates it is supposed to be estimating, which is the one confound that
/// would make the whole comparison worthless.
/// </para>
/// <para>
/// <b>Three arms, and the third one is the reason the other two mean
/// anything.</b> Every stroke in the committed set is a straight two-point line
/// (<c>GoldenSet.Line</c>, <c>GoldenSet.Comb</c>), so a stroke's bounding box
/// *fully determines* it — a picture arm handed the listing could score
/// perfectly without ever looking at the picture, and the comparison would read
/// as "rasters work" when it had measured nothing of the kind. So a
/// <c>listing</c> arm runs with no images at all. If it matches
/// <c>picture</c>, the pictures contributed nothing and the set cannot answer
/// Q180 — which is a finding about the set, and the same gap
/// <see cref="GoldenCategory.Organic"/> already documents by shipping empty.
/// </para>
/// <para>
/// <b>Both tests are inert unless <c>LIGHTBOX_Q180_DIR</c> is set.</b> A harness
/// that wrote files or demanded answers on every CI run would be a harness
/// somebody deletes.
/// </para>
/// </remarks>
public class GoldenPairArms(ITestOutputHelper output)
{
    private static string? Dir => Environment.GetEnvironmentVariable("LIGHTBOX_Q180_DIR");

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>
    /// An artist that answers from recorded replies instead of a network.
    /// </summary>
    /// <remarks>
    /// <b>It reads the file as raw text and hands it to <see cref="StrokeParsing"/>,
    /// rather than deserializing into strokes directly.</b> That is the point of
    /// the seam: an arm's answer is graded through exactly the path a provider's
    /// reply takes, so malformed JSON, a dropped label, a coordinate outside the
    /// canvas or a missing <c>t</c> all fail here the way they would fail in the
    /// application — and an arm cannot score well by being parsed leniently.
    /// </remarks>
    private sealed class TranscriptArtist(IReadOnlyList<GoldenPair> pairs, string dir, string arm) : IAiArtist
    {
        private int _next;

        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            // ProfileAsync walks the pairs in order, one call each, so position
            // identifies the pair. Asserted rather than assumed: a mismatch here
            // would silently grade one pair's answer against another's keys.
            var pair = pairs[_next++];
            var path = Path.Combine(dir, arm, $"{pair.Id}.json");
            if (!File.Exists(path))
            {
                return Task.FromResult(AiResult<List<InbetweenFrameResult>>.Error(
                    $"no answer recorded at {path}", retryable: false));
            }
            var raw = AiResult<string>.Success(File.ReadAllText(path));
            return Task.FromResult(StrokeParsing.Inbetweens(raw, request.Scene, arm));
        }

        public Task<AiResult<Core.Projects.SubjectTaxonomy>> ReadSubjectAsync(
            SubjectRequest request, CancellationToken ct) =>
            throw new NotSupportedException("The golden set asks for inbetweens only.");
    }

    // ---- arm 1: geometry ----------------------------------------------------

    /// <summary>Everything each arm's model is allowed to see, written to disk.</summary>
    [Fact]
    public void DumpTheTwoArms()
    {
        if (Dir is not { Length: > 0 } dir)
        {
            output.WriteLine("LIGHTBOX_Q180_DIR is not set — nothing dumped. See the class remarks.");
            return;
        }

        var pairs = GoldenSet.Short();
        Directory.CreateDirectory(Path.Combine(dir, "ask"));

        foreach (var pair in pairs)
        {
            var r = pair.Request;
            var stem = Path.Combine(dir, "ask", pair.Id);

            // Arm A sees exactly what the application sends today — not a
            // paraphrase of it, or the comparison would be against a strawman.
            File.WriteAllText($"{stem}.geometry.txt", Prompts.InbetweenUser(r));

            // Arm B sees the keys as pictures...
            File.WriteAllBytes($"{stem}.keyA.png", Png(r.KeyframeA, r.Scene));
            File.WriteAllBytes($"{stem}.keyB.png", Png(r.KeyframeB, r.Scene));

            // ...plus the identities a raster cannot carry: which strokes exist,
            // what they are called, what colour they are, and where they sit.
            // Without this an arm-B answer could not keep a label even in
            // principle, and label retention is one of the scores.
            File.WriteAllText($"{stem}.listing.json", JsonSerializer.Serialize(new
            {
                scene = new { r.Scene.Width, r.Scene.Height, r.Scene.Fps },
                easing = r.Easing.ToString().ToLowerInvariant(),
                requestedTs = r.Ts,
                keyframeA = Listing(r.KeyframeA),
                keyframeB = Listing(r.KeyframeB),
            }, Pretty));

            File.WriteAllText($"{stem}.probes.txt", $"{pair.Category}: {pair.Probes}\n");
        }

        var chars = CapabilityProfiler.EstimatedPayloadChars(pairs);
        output.WriteLine($"dumped {pairs.Count} pairs to {Path.Combine(dir, "ask")}");
        output.WriteLine($"arm A (geometry) is {chars:N0} chars of prompt across the set");
        foreach (var pair in pairs)
        {
            var stem = Path.Combine(dir, "ask", pair.Id);
            output.WriteLine(
                $"  {pair.Id,-14} geometry {new FileInfo($"{stem}.geometry.txt").Length,7:N0} B   "
                + $"pictures {new FileInfo($"{stem}.keyA.png").Length + new FileInfo($"{stem}.keyB.png").Length,6:N0} B   "
                + $"listing {new FileInfo($"{stem}.listing.json").Length,6:N0} B");
        }
    }

    /// <summary>One keyframe as a PNG a reader can actually see: white ground, black line.</summary>
    /// <remarks>
    /// The rasterizer clears to transparent, which a viewer may show as black and
    /// which would hide black line art completely. Compositing onto white here is
    /// what makes the picture arm a fair test rather than a test of nothing.
    /// </remarks>
    private static byte[] Png(IReadOnlyList<Stroke> strokes, SceneInfo scene)
    {
        using var art = FrameRasterizer.Rasterize(strokes, scene.Width, scene.Height);
        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawBitmap(art, 0, 0);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The cheap description of a drawing: what is in it, not where every point is.</summary>
    private static object[] Listing(IReadOnlyList<Stroke> strokes) =>
        [.. strokes.Select((s, i) =>
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            var reach = s.Brush.Size / 2;
            foreach (var p in s.Points)
            {
                minX = Math.Min(minX, p.X - reach);
                minY = Math.Min(minY, p.Y - reach);
                maxX = Math.Max(maxX, p.X + reach);
                maxY = Math.Max(maxY, p.Y + reach);
            }
            return (object)new
            {
                index = i,
                label = s.Label,
                color = s.Color,
                size = Math.Round(s.Brush.Size, 1),
                pointCount = s.Points.Count,
                box = new[]
                {
                    Math.Round(minX, 1), Math.Round(minY, 1),
                    Math.Round(maxX - minX, 1), Math.Round(maxY - minY, 1),
                },
            };
        })];

    // ---- scoring ------------------------------------------------------------

    /// <summary>Both arms' answers, graded by the profiler the application uses.</summary>
    [Fact]
    public async Task ScoreTheTwoArms()
    {
        if (Dir is not { Length: > 0 } dir)
        {
            output.WriteLine("LIGHTBOX_Q180_DIR is not set — nothing scored. See the class remarks.");
            return;
        }

        var pairs = GoldenSet.Short();
        // Any folder that is not the question is an answer, so a new arm is a
        // new directory rather than an edit here. The one that matters is the
        // control — see the class remarks on why `listing` exists.
        var arms = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
                .Where(a => a != "ask").OrderBy(a => a).ToList()
            : [];
        if (arms.Count == 0)
        {
            output.WriteLine($"no answer folders under {dir} — one folder per arm, beside 'ask'.");
            return;
        }

        var profiles = new List<CapabilityProfile>();
        foreach (var arm in arms)
        {
            profiles.Add(await CapabilityProfiler.ProfileAsync(
                new TranscriptArtist(pairs, dir, arm), arm, pairs, fullRun: false, null, CancellationToken.None));
        }

        foreach (var profile in profiles)
        {
            output.WriteLine($"===== arm: {profile.Subject} =====");
            foreach (var line in profile.Lines()) output.WriteLine(line);
            output.WriteLine("");
        }

        // Per pair and side by side, because the headline numbers hide the
        // thing the comparison is actually about: an arm can clear every pair
        // and still have interpolated along the chord on the one that asked for
        // an arc. DepartureFromFree is what separates those, so it is printed
        // rather than summarised away.
        output.WriteLine("===== per pair =====");
        output.WriteLine($"{"pair",-12}{"arm",-10}{"answered",-10}{"accepted",-10}{"labels",-9}{"vs free",-9}note");
        foreach (var pair in pairs)
        {
            foreach (var profile in profiles)
            {
                var o = profile.Outcomes.FirstOrDefault(x => x.PairId == pair.Id);
                if (o is null) continue;
                var dep = o.DepartureFromFree is { } d ? $"{d:F1}px" : "-";
                output.WriteLine(
                    $"{pair.Id,-12}{profile.Subject,-10}{o.Answered,-10}{o.Accepted,-10}"
                    + $"{o.LabelRetention,-9:P0}{dep,-9}{o.Note}");
            }
        }
    }
}
