using Avalonia.Input;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// A stall the events contradict is not reported as one (B312).
/// </summary>
/// <remarks>
/// <para>
/// <b>The instrument claimed something it could not measure.</b> The heartbeat
/// times how late a dispatcher job runs; the report turned that into "the UI
/// thread was blocked". Those are the same sentence only while nothing else is
/// competing for the thread, and an artist drawing is the case where something
/// is: pointer events arrive at over a hundred a second, and at Background
/// priority every one of them outranked the beat.
/// </para>
/// <para>
/// <b>The reporter's capture of 2026-08-25 is the case these are built from.</b>
/// It printed <c>UI-thread stalls 3, worst 10888 ms</c> and, two lines above,
/// <c>longest silence 53 ms</c> over 1,698 delivered moves - a 10.9-second block
/// in a 15.2-second trace that never once stopped answering. The file
/// contradicted itself and the verdict line still said <i>"the application
/// really did stop answering"</i>, which is the sentence a reader acts on.
/// </para>
/// <para>
/// <b>Two halves, and this file holds the second.</b> The beat moved from
/// Background to Default so it is not queued behind the events it is timing -
/// which no headless test can prove, because it is a fact about a real
/// dispatcher under real input. What <em>can</em> be pinned is that a claim the
/// entries refute is refused, and that is what these assert.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class StallWatchTellsTheTruthTests : BrushStateIsolated
{
    public StallWatchTellsTheTruthTests() => InputTrace.ResetForTests();

    public override void Dispose()
    {
        InputTrace.ResetForTests();
        base.Dispose();
    }

    private static void Move(double seconds) =>
        InputTrace.NoteForTests(seconds, InputTrace.Kind.Move, PointerType.Pen, 1);

    /// <summary>
    /// Long enough that the report will draw a conclusion at all.
    /// </summary>
    /// <remarks>
    /// <c>Verdicts</c> refuses to judge a capture under a few seconds, on the
    /// reasoning that a short one cannot distinguish anything - correct, and it
    /// silently made the first draft of these tests assert on an empty list.
    /// So the synthetic minute is a minute.
    /// </remarks>
    private static void LongEnough() => Move(30.0);

    [Fact]
    public void AStallTheCanvasAnsweredAcrossIsOverruled()
    {
        InputTrace.Arm();

        // The reporter's shape, shrunk: events all the way through a window the
        // heartbeat then claims nothing ran in.
        for (var i = 0; i < 40; i++) Move(1.0 + i * 0.02);
        InputTrace.NoteStallForTests(1.8, 700);
        LongEnough();

        var summary = InputTrace.Summarize();

        Assert.Equal(0, summary.Stalls);
        Assert.Equal(1, summary.RefutedStalls);
        Assert.Equal(0, summary.BlockedMs);
        Assert.DoesNotContain(
            summary.Verdicts,
            v => v.Contains("really did stop answering", StringComparison.Ordinal));
    }

    /// <summary>
    /// The sensitivity half: a real block is still reported.
    /// </summary>
    /// <remarks>
    /// Without this the assertion above would pass just as well on a build that
    /// counted no stalls at all, which is the shape of mistake
    /// <c>.claude/skills/brush-measurement</c> exists for - and it is the exact
    /// failure the fix could introduce, since "report fewer stalls" is also what
    /// a broken counter looks like.
    /// </remarks>
    [Fact]
    public void AStallWithNothingAcrossItIsStillReported()
    {
        InputTrace.Arm();

        // Events, then a gap the stall covers, then events again. Nothing was
        // delivered inside the window, so the claim stands.
        Move(1.0);
        InputTrace.NoteStallForTests(2.0, 700);
        Move(2.1);
        LongEnough();

        var summary = InputTrace.Summarize();

        Assert.Equal(1, summary.Stalls);
        Assert.Equal(0, summary.RefutedStalls);
        Assert.Equal(700, summary.BlockedMs);
        Assert.Contains(
            summary.Verdicts,
            v => v.Contains("really did stop answering", StringComparison.Ordinal));
    }

    /// <summary>
    /// The trace's own entries are not evidence that the thread was answering.
    /// </summary>
    /// <remarks>
    /// A <c>Note</c> is written by <c>InputTrace</c> itself, so counting it
    /// would let the instrument refute its own measurement with its own
    /// bookkeeping - and the entries that matter are the ones the dispatcher had
    /// to run to deliver.
    /// </remarks>
    [Fact]
    public void TheTracesOwnBookkeepingDoesNotOverruleAStall()
    {
        InputTrace.Arm();

        Move(1.0);
        InputTrace.NoteForTests(1.9, InputTrace.Kind.Note, PointerType.Mouse, -1);
        InputTrace.NoteStallForTests(2.0, 700);
        LongEnough();

        var summary = InputTrace.Summarize();

        Assert.Equal(1, summary.Stalls);
        Assert.Equal(0, summary.RefutedStalls);
    }
}
