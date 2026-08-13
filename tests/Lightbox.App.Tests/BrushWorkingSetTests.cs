using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The guard that fences applying a preset comes down whatever happens.
/// </summary>
/// <remarks>
/// <para>
/// <c>BrushWorkingSet</c> owns the two working brush configurations, the saved
/// presets, and the re-entrancy guard. The guard is the reason it is a class:
/// applying a preset assigns the bound properties, and every one of their setters
/// writes back into the working settings, so the assignment has to be fenced or
/// choosing a preset immediately edits it.
/// </para>
/// <para>
/// <b>The fence was nine hand-written raise/lower pairs and none of them was in a
/// try/finally.</b> The fenced region reaches <c>Settings.Save()</c>, which is file
/// I/O and can throw. One throw and the guard stays raised for the rest of the
/// session — at which point every bound brush property silently does nothing: the
/// size slider moves and the brush does not change, with no error anywhere. That is
/// B39's shape, a stale flag quietly suppressing behaviour, and it is what these
/// tests exist for.
/// </para>
/// </remarks>
public class BrushWorkingSetTests(Xunit.ITestOutputHelper output)
{
    /// <summary>The guard is down before and after an ordinary apply.</summary>
    [Fact]
    public void TheGuardIsRaisedOnlyForTheDurationOfTheApply()
    {
        var brushes = new BrushWorkingSet();
        Assert.False(brushes.IsApplying);

        var sawItRaised = false;
        brushes.Applying(() => sawItRaised = brushes.IsApplying);

        Assert.True(sawItRaised, "the body must see the guard raised, or it fences nothing");
        Assert.False(brushes.IsApplying);
    }

    /// <summary>
    /// A throw inside the apply still lowers the guard — the failure the old
    /// hand-written pairs had.
    /// </summary>
    [Fact]
    public void AThrowInsideTheApplyStillLowersTheGuard()
    {
        var brushes = new BrushWorkingSet();

        Assert.Throws<InvalidOperationException>(
            () => brushes.Applying(() => throw new InvalidOperationException("Settings.Save failed")));

        output.WriteLine($"guard after the throw: {brushes.IsApplying} (must be False)");
        Assert.False(brushes.IsApplying);

        // And the object is still usable rather than wedged for the session.
        var sawItRaised = false;
        brushes.Applying(() => sawItRaised = brushes.IsApplying);
        Assert.True(sawItRaised);
    }

    /// <summary>
    /// Nesting runs the body without lowering the guard early.
    /// </summary>
    /// <remarks>
    /// An inner apply that lowered the guard on its way out would unfence the rest of
    /// the outer one, which is the same silent write-back the guard exists to stop —
    /// just harder to see.
    /// </remarks>
    [Fact]
    public void ANestedApplyDoesNotLowerTheGuardEarly()
    {
        var brushes = new BrushWorkingSet();
        var raisedAfterInner = false;

        brushes.Applying(() =>
        {
            brushes.Applying(() => { });
            raisedAfterInner = brushes.IsApplying;
        });

        Assert.True(raisedAfterInner, "the inner apply lowered the guard the outer one still needed");
        Assert.False(brushes.IsApplying);
    }

    /// <summary>The brush and the eraser keep their own tuning.</summary>
    /// <remarks>
    /// Two configurations rather than one because switching tools must not lose
    /// either one's settings — an eraser sized for cleanup and a brush sized for
    /// drawing are not the same number.
    /// </remarks>
    [Fact]
    public void TheBrushAndTheEraserAreSeparateConfigurations()
    {
        var brushes = new BrushWorkingSet();
        brushes.Brush.Size = 40;
        brushes.Eraser.Size = 14;

        Assert.Equal(40, brushes.For(isEraser: false).Size);
        Assert.Equal(14, brushes.For(isEraser: true).Size);
        Assert.NotSame(brushes.Brush, brushes.Eraser);
    }
}
