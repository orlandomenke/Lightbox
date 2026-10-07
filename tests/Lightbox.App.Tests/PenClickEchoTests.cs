using Avalonia.Input;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// B391: what counts as the echo of a pen tap, and — the half that matters as
/// much — what never does.
/// </summary>
public class PenClickEchoTests
{
    private long _now;
    private PenClickEcho Echo() => new(() => _now);

    [Theory]
    [InlineData(PointerType.Pen, PointerType.Mouse)]
    [InlineData(PointerType.Mouse, PointerType.Pen)]
    public void TheOtherDeviceOnTheSameTargetInsideTheWindowIsAnEcho(PointerType first, PointerType second)
    {
        var echo = Echo();
        Assert.False(echo.IsEcho(first, "a", KeyModifiers.Control));
        _now += 1;
        Assert.True(echo.IsEcho(second, "a", KeyModifiers.Control));
    }

    [Fact]
    public void TwoPressesFromOneDeviceAreTwoClicks()
    {
        var echo = Echo();
        Assert.False(echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Mouse, "b", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Mouse, "b", KeyModifiers.Control));
    }

    [Fact]
    public void WithNoPenNothingIsEverAnEcho()
    {
        var echo = Echo();
        Assert.False(echo.IsEcho(PointerType.Mouse, "a", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Touch, "a", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Mouse, "a", KeyModifiers.Control));
    }

    [Fact]
    public void ADifferentTargetModifierOrMomentIsARealClick()
    {
        var echo = Echo();
        echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control);
        Assert.False(echo.IsEcho(PointerType.Mouse, "b", KeyModifiers.Control));

        echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control);
        Assert.False(echo.IsEcho(PointerType.Mouse, "a", KeyModifiers.Shift));

        echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control);
        _now += PenClickEcho.WindowMilliseconds + 1;
        Assert.False(echo.IsEcho(PointerType.Mouse, "a", KeyModifiers.Control));
    }

    [Fact]
    public void AnEchoIsConsumed_SoAThirdPressIsAClickAgain()
    {
        var echo = Echo();
        echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control);
        Assert.True(echo.IsEcho(PointerType.Mouse, "a", KeyModifiers.Control));
        Assert.False(echo.IsEcho(PointerType.Pen, "a", KeyModifiers.Control));
    }
}
