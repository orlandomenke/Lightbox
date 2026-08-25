using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// A submenu cannot close the instant it opened (B255).
/// </summary>
/// <remarks>
/// <para>
/// The fourth trace from the reporter's Huion counted <b>2,763 submenu opens in
/// 45 seconds, shortest life 0 ms</b>, with the UI thread blocked for 29% of
/// the session. <c>OverlayPopups</c> had already made each cycle twenty times
/// cheaper; the rate then rose sixfold to fill the space. This caps the rate.
/// </para>
/// <para>
/// <b>The dangerous part is the recursion</b>, not the policy: this writes to
/// the property it is handling. A missing guard turns one refused close into an
/// endless fight between the handler and the control, which would be worse than
/// the thrash it replaces — so that is what most of these cover.
/// </para>
/// </remarks>
public class SubmenuCloseGraceTests : IDisposable
{
    public SubmenuCloseGraceTests() => SubmenuCloseGrace.ResetForTests();

    public void Dispose() => SubmenuCloseGrace.ResetForTests();

    // ---- the policy ----------------------------------------------------------------

    [Fact]
    public void ACloseRightAfterTheOpenIsTooSoon()
    {
        // The measured case: submenu lives of 0 ms, sixty times a second.
        Assert.True(SubmenuCloseGrace.IsTooSoon(0));
        Assert.True(SubmenuCloseGrace.IsTooSoon(16));
        Assert.True(SubmenuCloseGrace.IsTooSoon(249));
    }

    [Fact]
    public void ACloseAfterTheGraceStands()
    {
        // Somebody moving off a menu they actually looked at.
        Assert.False(SubmenuCloseGrace.IsTooSoon(250));
        Assert.False(SubmenuCloseGrace.IsTooSoon(5000));
    }

    [Fact]
    public void ASubmenuNobodySawOpenIsNotProtected()
    {
        // No open recorded means nothing to protect, and guessing would be a
        // menu held open for a reason the guard invented.
        Assert.False(SubmenuCloseGrace.IsTooSoon(null));
    }

    // ---- the behaviour, through a real MenuItem --------------------------------------

    private static MenuItem Item() => new()
    {
        Header = "Follows the rig",
        ItemsSource = new[] { new MenuItem { Header = "Child" } },
    };

    /// <summary>The close that arrives immediately is refused.</summary>
    [AvaloniaFact]
    public void AnImmediateCloseIsPutBack()
    {
        SubmenuCloseGrace.Install();
        var item = Item();

        item.IsSubMenuOpen = true;
        item.IsSubMenuOpen = false;

        // Back open, and the refusal counted so a trace can show it engaged.
        Assert.True(item.IsSubMenuOpen);
        Assert.True(SubmenuCloseGrace.Refused > 0);
    }

    /// <summary>
    /// The refusal does not recurse.
    /// </summary>
    /// <remarks>
    /// Putting the property back re-enters the handler. Without the guard that
    /// second pass would record a fresh open, and the pair would ping-pong —
    /// so this asserts the refusal happened exactly once for one close, rather
    /// than merely that the menu ended up open.
    /// <para>
    /// <b>Checked by removing the guard, and the result is worth writing down:
    /// the suite does not fail, it never finishes.</b> The handler recurses
    /// until the run is killed. So the thing being guarded against is a hang
    /// rather than a wrong answer, which is exactly why the guard is the first
    /// line of the method and not a tidy-up at the end.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void RefusingACloseDoesNotRecurse()
    {
        SubmenuCloseGrace.Install();
        var item = Item();

        item.IsSubMenuOpen = true;
        item.IsSubMenuOpen = false;

        Assert.Equal(1, SubmenuCloseGrace.Refused);
        Assert.True(item.IsSubMenuOpen);
    }

    /// <summary>A close after the grace really closes it.</summary>
    /// <remarks>
    /// The half that matters for it being a *grace* rather than a menu that
    /// will not shut: without this, the fix would be a worse bug than B255.
    /// </remarks>
    [AvaloniaFact]
    public void ACloseAfterTheGraceIsHonoured()
    {
        SubmenuCloseGrace.Install();
        var item = Item();

        item.IsSubMenuOpen = true;
        // Pretend the menu has been open long enough to have been read.
        SubmenuCloseGrace.NoteOpenedForTests(item, msAgo: SubmenuCloseGrace.GraceMs + 50);
        item.IsSubMenuOpen = false;

        Assert.False(item.IsSubMenuOpen);
        Assert.Equal(0, SubmenuCloseGrace.Refused);
    }

    /// <summary>
    /// A second close, once the grace has passed, is honoured after a refusal.
    /// </summary>
    /// <remarks>
    /// The sequence an artist actually produces on the reporter's machine: the
    /// churn tries to close it, is refused, and then they move away for real.
    /// A guard that refused the first close and then never released would trap
    /// the menu open.
    /// </remarks>
    [AvaloniaFact]
    public void AfterARefusalTheMenuStillClosesWhenItShould()
    {
        SubmenuCloseGrace.Install();
        var item = Item();

        item.IsSubMenuOpen = true;
        item.IsSubMenuOpen = false;          // refused
        Assert.True(item.IsSubMenuOpen);

        SubmenuCloseGrace.NoteOpenedForTests(item, msAgo: SubmenuCloseGrace.GraceMs + 50);
        item.IsSubMenuOpen = false;          // honoured

        Assert.False(item.IsSubMenuOpen);
    }
}
