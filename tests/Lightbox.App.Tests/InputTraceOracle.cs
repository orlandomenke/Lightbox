using Avalonia.Controls;
using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Replays a capture and compares the decisions <em>this</em> build makes
/// against the ones the capture recorded.
/// </summary>
/// <remarks>
/// <para>
/// <b>A capture already contains both halves of a test and only one was being
/// used.</b> Every trace records the input the canvas received <em>and</em> what
/// the canvas decided about it — <c>CursorDecided</c> when its mind changed,
/// <c>CursorAssigned</c> when the platform cursor it handed over actually
/// differed. <c>InputTraceReplay</c> drives the first half. This reads the
/// second as an expectation, which turns a minute of somebody's hover from an
/// input into an input <em>plus</em> an answer.
/// </para>
/// <para>
/// <b>What an exact match is worth, stated before anybody relies on it.</b> The
/// decision depends on far more than the capture holds: the active tool, a
/// gizmo being up, a guide under the pointer, the document's own geometry
/// through <c>ViewToDoc</c> — none of which a trace records. So a capture from
/// the reporter's machine is <em>not</em> expected to reproduce its recorded
/// sequence here, and a test asserting that it does would be asserting on their
/// document. Two things are worth asserting, and they are different:
/// </para>
/// <list type="bullet">
/// <item><b>Churn</b> — <see cref="Verdict.ReplayedFlipFlops"/> and the change
/// count. These do not depend on the document at all: they say whether the
/// application's mind moved while the artist's hand did not, which is the whole
/// of what B126 complains about. Comparable across machines.</item>
/// <item><b>Golden</b> — a capture whose sequence this repository's rig does
/// reproduce, checked in so a later build has to reproduce it too. This is the
/// regression guard: it catches the same input starting to produce different
/// cursor behaviour, which is precisely what a change near the hover path could
/// silently reintroduce.</item>
/// </list>
/// <para>
/// For anything else — a real capture whose sequence will not match — the
/// divergence is <b>diagnostic output for a person</b>, printed by
/// <see cref="Verdict.Report"/>, not an assertion. Confusing those two is how a
/// suite acquires a test that fails for reasons nobody can act on.
/// </para>
/// <para>
/// <b>The instrument records both sides, which is the trick.</b> The recorded
/// half is read out of the capture file; the replayed half is obtained by
/// arming <see cref="InputTrace"/> during the replay, so the current build's
/// decisions are captured by exactly the code that captured the reporter's. If
/// the two were gathered differently, a difference in the gathering would read
/// as a difference in the application.
/// </para>
/// </remarks>
internal static class InputTraceOracle
{
    /// <summary>
    /// One change of mind: the canvas went from one cursor kind to another.
    /// </summary>
    /// <param name="Assigned">
    /// The platform cursor actually changed identity too, rather than the
    /// decision merely being re-reached. Carried but not compared by default —
    /// a headless run has no real cursor surface, so this is weaker evidence
    /// here than it is in a capture from a machine with a screen.
    /// </param>
    internal sealed record Decision(double Seconds, string From, string To, bool Assigned)
    {
        public override string ToString() => $"{Seconds:F4} {From}→{To}";
    }

    internal sealed record Verdict(
        IReadOnlyList<Decision> Recorded,
        IReadOnlyList<Decision> Replayed,
        int MatchedPrefix,
        string? FirstDivergence,
        int RecordedFlipFlops,
        int ReplayedFlipFlops,
        InputTraceReplay.Result Replay)
    {
        /// <summary>The two sequences agree, kind for kind, all the way down.</summary>
        internal bool Matches => FirstDivergence is null && Recorded.Count == Replayed.Count;

        /// <summary>
        /// Both sides side by side, for a failure a person has to read.
        /// </summary>
        /// <remarks>
        /// Prints every decision rather than only the divergence: the question
        /// after "these differ" is always "differ how", and a message that
        /// answers only the first sends the reader back to the file.
        /// </remarks>
        internal string Report()
        {
            var lines = new List<string>
            {
                $"recorded {Recorded.Count} decisions ({RecordedFlipFlops} flip-flops), "
                + $"replayed {Replayed.Count} ({ReplayedFlipFlops} flip-flops)",
                FirstDivergence ?? "no divergence in the shared prefix",
            };
            for (var i = 0; i < Math.Max(Recorded.Count, Replayed.Count); i++)
            {
                var was = i < Recorded.Count ? Recorded[i].ToString() : "—";
                var now = i < Replayed.Count ? Replayed[i].ToString() : "—";
                lines.Add($"  [{i}] recorded {was}   replayed {now}");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Replay the capture with the instrument armed, and compare the two sets of
    /// decisions.
    /// </summary>
    /// <remarks>
    /// Arming resets the ring, so the capture must already be in hand — which it
    /// is, having been read from a file. The trace is disarmed again on the way
    /// out even if the replay throws, because a trace left armed records the
    /// next test's work into this one's.
    /// </remarks>
    internal static Verdict Run(
        InputTraceLog.Capture capture, Window window, CanvasControl canvas)
    {
        var recorded = DecisionsIn(capture.Entries);

        InputTrace.Arm();
        InputTraceReplay.Result replay;
        IReadOnlyList<Decision> replayed;
        try
        {
            replay = InputTraceReplay.Replay(capture, window, canvas);
        }
        finally
        {
            replayed = DecisionsIn(InputTrace.EntriesInOrder());
            InputTrace.Disarm();
        }

        var (matched, divergence) = Compare(recorded, replayed);
        return new Verdict(
            recorded, replayed, matched, divergence,
            FlipFlops(recorded), FlipFlops(replayed), replay);
    }

    /// <summary>
    /// The decisions out of a run of entries, with each assignment folded onto
    /// the decision it belongs to.
    /// </summary>
    /// <remarks>
    /// <c>CursorAssigned</c> is written immediately after the
    /// <c>CursorDecided</c> it accompanies and carries the same kind, so the
    /// pairing is positional rather than guessed. A decision with no assignment
    /// behind it is the ordinary case: the canvas re-reached the same answer and
    /// Avalonia was handed the cursor it already had.
    /// </remarks>
    internal static IReadOnlyList<Decision> DecisionsIn(IReadOnlyList<InputTrace.Entry> entries)
    {
        var decisions = new List<Decision>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind != InputTrace.Kind.CursorDecided) continue;
            var (from, to) = Split(entries[i].Detail);
            var assigned = i + 1 < entries.Count
                && entries[i + 1].Kind == InputTrace.Kind.CursorAssigned;
            decisions.Add(new Decision(entries[i].Seconds, from, to, assigned));
        }
        return decisions;
    }

    /// <summary>
    /// A transition as the trace writes it: <c>from→to</c>, with <c>start</c>
    /// standing in for "nothing decided yet".
    /// </summary>
    private static (string From, string To) Split(string? detail)
    {
        if (detail is null) return ("?", "?");
        var arrow = detail.IndexOf('→');
        return arrow < 0
            ? ("?", detail)
            : (detail[..arrow], detail[(arrow + 1)..]);
    }

    /// <summary>
    /// How far the two sequences agree, and what parted them.
    /// </summary>
    /// <remarks>
    /// Compared by destination rather than by the whole transition. The
    /// <em>from</em> half is a consequence of what came before, so a single
    /// early difference would otherwise be reported again on every later line
    /// and bury the one that mattered.
    /// </remarks>
    private static (int Matched, string? Divergence) Compare(
        IReadOnlyList<Decision> recorded, IReadOnlyList<Decision> replayed)
    {
        var shared = Math.Min(recorded.Count, replayed.Count);
        for (var i = 0; i < shared; i++)
        {
            if (recorded[i].To == replayed[i].To) continue;
            return (i, $"decision {i}: recorded {recorded[i].To}, replayed {replayed[i].To}");
        }
        if (recorded.Count != replayed.Count)
        {
            return (shared,
                $"agreed for {shared}, then recorded {recorded.Count} decisions in all "
                + $"and this build made {replayed.Count}");
        }
        return (shared, null);
    }

    /// <summary>
    /// How many times the answer went away and came back.
    /// </summary>
    /// <remarks>
    /// <b>This is the strobe, counted.</b> A cursor that changes once because
    /// the pointer moved onto a guide has changed for a reason; one that goes
    /// A→B→A has changed its mind about a hand that did not move, which is what
    /// the artist sees flickering. Independent of the document, so it is the one
    /// figure comparable between the reporter's machine and this one.
    /// </remarks>
    internal static int FlipFlops(IReadOnlyList<Decision> decisions)
    {
        var count = 0;
        for (var i = 2; i < decisions.Count; i++)
        {
            if (decisions[i].To == decisions[i - 2].To) count++;
        }
        return count;
    }
}
