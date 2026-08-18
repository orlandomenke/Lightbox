using Avalonia.Headless.XUnit;
using Avalonia.Input.Raw;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The filter that drops the phantom mouse a pen tablet emits (B126, B254,
/// B255).
/// </summary>
/// <remarks>
/// The decision is separated from the Avalonia plumbing precisely so it can be
/// tested without a tablet: <see cref="PenEchoFilter.IsEcho"/> is arithmetic on
/// three facts, and everything that could be got wrong about *policy* lives
/// there. What cannot be tested here is whether the drop actually stops the
/// churn on Windows — that is the reporter's next trace, and the
/// `echo events dropped` counter in the report is what will say so.
/// </remarks>
public class PenEchoFilterTests : IDisposable
{
    public PenEchoFilterTests() => PenEchoFilter.ResetForTests();

    public void Dispose() => PenEchoFilter.ResetForTests();

    // ---- the policy ----------------------------------------------------------------

    [Fact]
    public void AMouseMoveRightAfterAPenEventIsTheEcho()
    {
        // The measured handoff is a median of 0.5 ms, so this is the case the
        // whole filter exists for.
        Assert.True(PenEchoFilter.IsEcho(isMouse: true, isMove: true, msSincePen: 0.5));
        Assert.True(PenEchoFilter.IsEcho(isMouse: true, isMove: true, msSincePen: 199));
    }

    [Fact]
    public void AMouseOnAMachineThatHasSeenNoPenIsNeverFiltered()
    {
        // The guard that keeps this from being a regression for everybody else:
        // with no pen ever seen there is no clock, so the filter cannot engage.
        Assert.False(PenEchoFilter.IsEcho(isMouse: true, isMove: true, msSincePen: null));
    }

    [Fact]
    public void AMouseLongAfterThePenIsARealMouse()
    {
        // Somebody who puts the pen down and picks up a mouse. A fifth of a
        // second later it is theirs again.
        Assert.False(PenEchoFilter.IsEcho(isMouse: true, isMove: true, msSincePen: 201));
        Assert.False(PenEchoFilter.IsEcho(isMouse: true, isMove: true, msSincePen: 5000));
    }

    [Fact]
    public void ThePenItselfIsNeverFiltered()
    {
        Assert.False(PenEchoFilter.IsEcho(isMouse: false, isMove: true, msSincePen: 0.5));
    }

    [Fact]
    public void AButtonIsNeverDroppedHoweverRecentThePenIs()
    {
        // The deliberate asymmetry: a dropped move costs a few milliseconds of
        // cursor position that the next event corrects, and a dropped press
        // costs a click the artist meant, with nothing to correct it. The churn
        // is made of moves, so refusing to touch buttons gives up nothing.
        Assert.False(PenEchoFilter.IsEcho(isMouse: true, isMove: false, msSincePen: 0.1));
    }

    [Theory]
    [InlineData(RawPointerEventType.Move, true)]
    [InlineData(RawPointerEventType.LeaveWindow, true)]
    [InlineData(RawPointerEventType.LeftButtonDown, false)]
    [InlineData(RawPointerEventType.LeftButtonUp, false)]
    [InlineData(RawPointerEventType.RightButtonDown, false)]
    [InlineData(RawPointerEventType.Wheel, false)]
    public void OnlyMoveLikeEventsAreEligible(RawPointerEventType type, bool eligible)
    {
        // LeaveWindow is in the list on purpose: it is the event the echo makes
        // fire, and the one that hands the window's arrow back.
        Assert.Equal(eligible, PenEchoFilter.IsMoveLike(type));
    }

    // ---- the plumbing, which is reflection and must not rot silently ----------------

    /// <summary>
    /// Every member the filter reaches by name still exists.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that earns the reflection.</b> Avalonia 12.1.1 ships
    /// a reference assembly that hides <c>IInputManager.PreProcess</c> and the
    /// <c>RawPointerEventArgs</c> members from the compiler while the runtime
    /// exposes them, so the only way to reach the raw stream is by name — and
    /// the failure mode of a name that stops resolving is that nothing is
    /// filtered and the 6-second freezes come back with nothing on screen to
    /// say why. An upgrade that moves any of these fails the build instead.
    /// </remarks>
    [Fact]
    public void TheRawInputHooksStillResolve()
    {
        Assert.Null(PenEchoFilter.Resolve());
    }

    /// <summary>
    /// The input manager is actually found.
    /// </summary>
    /// <remarks>
    /// <b>This is the test the first attempt did not have, and the reason it
    /// shipped broken.</b> The lookup was three lines of reflection in
    /// <c>App.axaml.cs</c> — outside anything a test reaches — and it asked
    /// <c>typeof(Application).Assembly</c> for <c>Avalonia.AvaloniaLocator</c>.
    /// <c>Application</c> is in <c>Avalonia.Controls</c> and the locator is in
    /// <c>Avalonia.Base</c>, so it returned null, the filter stood down, and the
    /// reporter's confirmation trace read <c>echo events dropped 0</c> with all
    /// three symptoms untouched. The policy was covered in full; the thing that
    /// decides whether the policy ever runs was not.
    /// </remarks>
    [AvaloniaFact]
    public void TheInputManagerIsFound()
    {
        Assert.NotNull(PenEchoFilter.FindInputManager());
    }

    [AvaloniaFact]
    public void InstallingWithAnExplicitManagerSubscribes()
    {
        PenEchoFilter.Install(PenEchoFilter.FindInputManager());

        // Nothing stopped it: no missing member, no missing manager.
        Assert.Null(PenEchoFilter.Unavailable);
    }

    [AvaloniaFact]
    public void InstallingTwiceDoesNotStackASecondSubscription()
    {
        PenEchoFilter.Install();
        var first = PenEchoFilter.Unavailable;
        PenEchoFilter.Install();

        // Two subscriptions would drop each event twice and double the counter
        // the fix is judged by.
        Assert.Equal(first, PenEchoFilter.Unavailable);
    }
}
