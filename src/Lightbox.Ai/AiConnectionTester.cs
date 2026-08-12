using Lightbox.Ai.Mcp;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

/// <summary>How hard to lean on the provider before believing it.</summary>
public enum AiTestDepth
{
    /// <summary>
    /// One inbetween of a two-point stroke on a small canvas, checked only for
    /// being well-formed. Seconds, a few hundred tokens.
    /// </summary>
    Quick,

    /// <summary>
    /// The quick test, then a real inbetween between two keyframes, checked
    /// for the things that make an inbetween usable rather than merely
    /// well-formed. Minutes on a local model.
    /// </summary>
    Thorough,
}

/// <summary>
/// The outcome of a connection test.
/// </summary>
/// <param name="Connected">
/// Something answered. Kept apart from <paramref name="Ok"/> because
/// "unreachable" and "reachable but drawing nonsense" are different problems
/// with different fixes, and one boolean would hide that.
/// </param>
public sealed record AiConnectionCheck(bool Ok, string Message, bool Connected = false);

/// <summary>
/// Proves a connection works — and that what comes back is usable — before an
/// artist depends on it.
/// </summary>
/// <remarks>
/// It asks for real work rather than pinging, because the ways this fails are
/// mostly not reachability: a key with no credit, a model name off by a
/// version, an endpoint that answers but cannot honour a JSON schema, an MCP
/// server whose tool is called something else, a small local model that
/// returns valid JSON full of nonsense. A HEAD request would say "connected"
/// to every one.
///
/// Both depths ask for an inbetween, because inbetweening is the only thing
/// the application asks a model for. A test that exercised some other capability
/// could pass on a provider that cannot do the job.
///
/// The output checks are deliberately about *usability*, not quality. Points
/// off the canvas, an empty stroke list, an inbetween that does not lie
/// between its keys — these are things no amount of prompt tuning fixes, and
/// they are what tells someone a model is the wrong tool before they spend an
/// afternoon finding out.
/// </remarks>
public static class AiConnectionTester
{
    /// <summary>
    /// Deliberately trivial: one two-point line nudged sideways. Any model
    /// that can answer the schema at all gets it, which is the point — this
    /// depth is asking "does anything usable come back", not "is it any good".
    /// </summary>
    private static InbetweenRequest Probe() => new(
        new SceneInfo(Canvas, Canvas, 12),
        [new Stroke { Label = "line", Points = [new(20, 64, 0.6), new(60, 64, 0.6)] }],
        [new Stroke { Label = "line", Points = [new(68, 64, 0.6), new(108, 64, 0.6)] }],
        [0.5],
        Easing.Linear);

    private const int Canvas = 128;

    /// <summary>
    /// An arm swinging from the top of the canvas to the bottom. Simple
    /// enough that any model that can inbetween at all gets it, and specific
    /// enough that a model which cannot is caught: the answer has to sit
    /// between the two keys, which a plausible-looking guess will not.
    /// </summary>
    internal static InbetweenRequest Swing() => new(
        new SceneInfo(Canvas, Canvas, 12),
        [new Stroke { Label = "arm", Points = [new(20, 20, 0.6), new(100, 20, 0.6)] }],
        [new Stroke { Label = "arm", Points = [new(20, 100, 0.6), new(100, 100, 0.6)] }],
        [0.5],
        Easing.Linear);

    public static Task<AiConnectionCheck> TestAsync(
        AiConnection connection,
        AiTestDepth depth = AiTestDepth.Quick,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => TestAsync(connection, depth, artist: null, progress, ct);

    /// <param name="artist">
    /// Test seam: skip the factory and drive a prepared artist. Lets the
    /// output checks be exercised against scripted replies, which is the half
    /// of this class that has real logic in it.
    /// </param>
    internal static async Task<AiConnectionCheck> TestAsync(
        AiConnection connection,
        AiTestDepth depth,
        IAiArtist? artist,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Say what is missing before spending a call finding out.
        if (connection.Missing() is { Count: > 0 } missing)
        {
            var names = string.Join(", ", missing.Select(f => f.Label.ToLowerInvariant()));
            return new AiConnectionCheck(false, $"Still needs: {names}.");
        }

        try
        {
            artist ??= AiArtistFactory.Create(connection, ignoreSwitch: true);
        }
        catch (McpException e)
        {
            return new AiConnectionCheck(false, e.Message);
        }
        if (artist is null)
            return new AiConnectionCheck(false, "Lightbox could not build a connection from these values.");

        try
        {
            if (await CheckToolNameAsync(artist, connection, ct) is { } toolProblem) return toolProblem;

            progress?.Report("Asking for one inbetween of a short line…");
            var probed = await artist.GenerateInbetweensAsync(Probe(), ct);
            if (Interpret(probed.Outcome, probed.Message, connection) is { } failure) return failure;

            // Well-formedness only at this depth: the strokes would mark.
            // Whether the drawing lands between the keys is the thorough
            // test's question. A success always carries at least one frame —
            // the parser reports an empty list as an error rather than a
            // value — so indexing here needs no guard of its own.
            var first = probed.Value![0];
            if (BadStrokes(first.Strokes) is { } strokeProblem)
            {
                return new AiConnectionCheck(false,
                    $"Connected, but the strokes are not usable: {strokeProblem}", Connected: true);
            }

            var name = connection.Provider.Name;
            var drewLine = $"{name} drew {Count(first.Strokes.Count, "stroke")}";
            if (depth == AiTestDepth.Quick)
                return new AiConnectionCheck(true, $"Connected. {drewLine}.", Connected: true);

            progress?.Report("Asking for an inbetween between two keyframes…");
            var swing = Swing();
            var tweened = await artist.GenerateInbetweensAsync(swing, ct);
            if (Interpret(tweened.Outcome, tweened.Message, connection) is { } tweenFailure) return tweenFailure;

            if (BadInbetween(swing, tweened.Value!) is { } tweenProblem)
            {
                // Connected and well-formed but not competent: a real result,
                // and the one a small local model most often lands on.
                return new AiConnectionCheck(false,
                    $"Connected, and {drewLine}, but the inbetween is not usable: {tweenProblem} "
                    + "The connection is fine — this model may be too small for inbetweening.",
                    Connected: true);
            }

            return new AiConnectionCheck(true,
                $"Connected. {drewLine}, and put the inbetween where it belongs.", Connected: true);
        }
        catch (McpException e)
        {
            return new AiConnectionCheck(false, e.Message);
        }
        catch (OperationCanceledException)
        {
            return new AiConnectionCheck(false, "Canceled.");
        }
        finally
        {
            // An MCP test leaves a child process behind otherwise.
            if (artist is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    /// <summary>
    /// An MCP server that is up but offers a different tool is the likeliest
    /// mistake on that path, and the one worth naming precisely.
    /// </summary>
    private static async Task<AiConnectionCheck?> CheckToolNameAsync(
        IAiArtist artist, AiConnection connection, CancellationToken ct)
    {
        if (artist is not McpArtist mcp) return null;
        var wanted = connection.Value("tool") ?? McpArtist.DefaultTool;
        var tools = await mcp.ListToolsAsync(ct);
        if (tools.Count == 0 || tools.Contains(wanted)) return null;
        return new AiConnectionCheck(false,
            $"The server started, but offers no tool called “{wanted}”. It has: "
            + string.Join(", ", tools) + ".", Connected: true);
    }

    /// <summary>Map a non-success outcome; null when the call succeeded.</summary>
    private static AiConnectionCheck? Interpret(AiOutcome outcome, string? message, AiConnection connection) =>
        outcome switch
        {
            AiOutcome.Success => null,
            // A refusal proves the connection: something read the prompt and
            // decided. That is reachability confirmed, and a caveat.
            AiOutcome.Refused => new AiConnectionCheck(false,
                $"Connected, but {connection.Provider.Name} declined the test drawing. "
                + "The connection is fine; the model may be a poor fit for drawing tasks.",
                Connected: true),
            AiOutcome.Truncated => new AiConnectionCheck(false,
                "The reply was cut off before it finished — the model's output limit may be very low.",
                Connected: true),
            _ => new AiConnectionCheck(false, message ?? "Failed."),
        };

    /// <summary>
    /// What is wrong with these strokes, or null if nothing is.
    /// </summary>
    /// <remarks>
    /// Notably absent: an off-canvas check. <c>StrokeWire.FromWire</c> already
    /// clamps every point into the scene plus a margin, so by the time a
    /// stroke reaches here it cannot be off-canvas — a check for it would have
    /// been reassurance that could never fire, and a test caught it as such.
    /// The checks themselves live on <see cref="InbetweenVerifier"/>, because
    /// the pipeline judges every real request with them and a connection test
    /// that judged differently would certify a model the pipeline then refuses.
    /// </remarks>
    internal static string? BadStrokes(IReadOnlyList<Stroke> strokes) =>
        InbetweenVerifier.Unusable(strokes);

    /// <summary>What is wrong with this inbetween, or null if nothing is.</summary>
    /// <remarks>
    /// The verifier in miniature: the same checks a real request will face,
    /// pointed at the test drawing. A model that fails here fails on the
    /// gentlest keys it will ever be shown, which is exactly what tells
    /// someone it is the wrong tool before they spend an afternoon finding out.
    /// </remarks>
    internal static string? BadInbetween(InbetweenRequest request, IReadOnlyList<InbetweenFrameResult> frames)
    {
        if (frames.Count == 0) return "no frames came back.";
        var frame = frames[0];
        var judged = InbetweenVerifier.Verify(
            request.KeyframeA,
            request.KeyframeB,
            [new CandidateInbetween(frame.T, frame.Strokes)],
            request.Easing);
        return judged.Frames[0].Refusal;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
