using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// The Unreal conversions, and the factor of a hundred hiding in one of them.
/// </summary>
/// <remarks>
/// Same shape as <c>GodotConvertTests</c> and for the same reason: these are three
/// lines of arithmetic that cannot be run inside the engine from here, so each one is
/// tested against the <em>wrong</em> answer somebody would plausibly write rather than
/// only against the right one.
/// </remarks>
public class UnrealConvertTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public UnrealConvertTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    // ---- the centimetre trap -------------------------------------------------------

    [Fact]
    public void UnrealsFigureIsAHundredTimesUnitysBecauseAUnitIsACentimetre()
    {
        // The finding this class exists for. Both numbers printed, because "0.71 vs 71"
        // is the whole content and an assertion alone would hide it.
        const int height = 128;
        const double metres = 1.8;

        var unreal = UnrealConvert.PixelsPerUnrealUnit(height, metres);
        var unity = UnityConvert.PixelsPerUnit(height, metres);

        _out.WriteLine($"unreal {unreal:F4} px/uu, unity {unity:F4} px/unit, ratio {unity / unreal:F1}");

        Assert.Equal(128 / 180.0, unreal, 6);
        Assert.Equal(100, unity / unreal, 6);
    }

    [Fact]
    public void UsingUnitysFigureWouldMakeACharacterCentimetresTall()
    {
        // Stated as the consequence rather than as a ratio, because this is what the
        // artist actually sees: a character who should be 1.8 m tall arriving at 1.8 cm.
        // Nothing in Unreal will say "wrong units" — it will just look broken.
        const int height = 128;
        const double metres = 1.8;

        var right = UnrealConvert.PixelsPerUnrealUnit(height, metres);
        var wrong = UnityConvert.PixelsPerUnit(height, metres);

        // How tall the sprite ends up, in metres, for each figure.
        var withRight = height / right / UnrealConvert.UnrealUnitsPerMetre;
        var withWrong = height / wrong / UnrealConvert.UnrealUnitsPerMetre;

        _out.WriteLine($"correct gives {withRight:F3} m, Unity's figure gives {withWrong:F3} m");

        Assert.Equal(metres, withRight, 6);
        Assert.True(withWrong < 0.05, $"the wrong figure gave {withWrong:F3} m, which is not small enough for this test to mean anything");
    }

    [Fact]
    public void TheConstantSaysWhatItIsRatherThanBeingAHundredSomewhere()
    {
        // A factor of a hundred written inline reads as a typo. This is here so the
        // constant cannot be quietly "simplified" away.
        Assert.Equal(100, UnrealConvert.UnrealUnitsPerMetre);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void AnImpossibleWorldHeightDoesNotDivideByZero(double metres)
    {
        var ppuu = UnrealConvert.PixelsPerUnrealUnit(128, metres);
        Assert.False(double.IsNaN(ppuu) || double.IsInfinity(ppuu) || ppuu <= 0);
    }

    // ---- frame_run is a count, and zero deletes the frame --------------------------

    [Fact]
    public void AnOrdinaryFrameRunsForOneFlipbookFrame()
    {
        Assert.Equal(1, UnrealConvert.FrameRun(83, 12));
        Assert.Equal(1, UnrealConvert.FrameRun(42, 24));
    }

    [Fact]
    public void AHoldOn2sRunsForTwo()
    {
        Assert.Equal(2, UnrealConvert.FrameRun(166, 12));
        Assert.Equal(3, UnrealConvert.FrameRun(250, 12));
    }

    [Fact]
    public void TruncatingRatherThanRoundingWouldDeleteTheFrameEntirely()
    {
        // The discriminating case, and worse than Godot's equivalent. The sidecar writes
        // 83 ms where a 12 fps tick is 83.33, so the quotient is 0.996 — which as an int
        // is zero, and a frame_run of zero is a drawing that never appears.
        var truncated = (int)(83 / (1000.0 / 12));
        var rounded = UnrealConvert.FrameRun(83, 12);

        _out.WriteLine($"truncated {truncated}, rounded {rounded}");

        Assert.Equal(0, truncated);
        Assert.Equal(1, rounded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1)]
    public void NoDurationEverProducesAFrameThatNeverShows(int ms)
    {
        Assert.True(UnrealConvert.FrameRun(ms, 12) >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnImpossibleFrameRateStillGivesAUsableRun(int fps)
    {
        Assert.True(UnrealConvert.FrameRun(100, fps) >= 1);
    }

    // ---- the pivot, tested against both other engines' answers ---------------------

    [Fact]
    public void ThePivotIsMeasuredFromTheSpriteRectanglesOwnCorner()
    {
        var (x, y) = UnrealConvert.PivotPoint(pivotX: 100, pivotY: 140, cellLeft: 80, cellTop: 60);

        Assert.Equal(20, x, 6);
        Assert.Equal(80, y, 6);
    }

    [Fact]
    public void ItIsNeitherUnitysNormalisedPivotNorGodotsCentreOffset()
    {
        // The reason this function exists rather than a subtraction inlined in the
        // script: three engines, three conventions, and the wrong one applied here is
        // silent. Both wrong answers computed and asserted different.
        const double px = 100, py = 140;
        const int left = 80, top = 60, w = 40, h = 100;

        var unreal = UnrealConvert.PivotPoint(px, py, left, top);
        var unity = UnityConvert.Pivot(px, py, left, top, w, h);
        var godot = GodotConvert.SpriteOffset(px, py, left, top, w, h);

        _out.WriteLine($"unreal ({unreal.X}, {unreal.Y}), unity ({unity.X:F3}, {unity.Y:F3}), godot ({godot.X}, {godot.Y})");

        // Unity's is a fraction of the cell, so it is inside 0..1 and nothing like ours.
        Assert.True(unity.X is >= 0 and <= 1, $"Unity's pivot x was {unity.X}, expected normalised");
        Assert.NotEqual(unreal.X, unity.X, 3);
        Assert.NotEqual(unreal.Y, unity.Y, 3);

        // Godot's is from the centre, so it differs from ours by half the cell and
        // points the other way.
        Assert.NotEqual(unreal.X, godot.X, 3);
        Assert.NotEqual(unreal.Y, godot.Y, 3);
    }

    [Fact]
    public void UnrealsTextureSpaceYRunsDownSoAFeetPivotIsTheLargerNumber()
    {
        // Not a flip — which is exactly why it is worth writing down. The pivot is in
        // the texture's own space, so lower on the drawing is a bigger Y.
        var feet = UnrealConvert.PivotPoint(50, 100, 0, 0);
        var head = UnrealConvert.PivotPoint(50, 0, 0, 0);

        Assert.True(feet.Y > head.Y, $"feet {feet.Y}, head {head.Y}");
        Assert.Equal(0, head.Y, 6);
    }

    [Fact]
    public void ATrimmedCellMovesThePivotWithItRatherThanLeavingItBehind()
    {
        // Same property the Unity and Godot conversions have: tightening the trim must
        // not move where the drawing is anchored.
        var loose = UnrealConvert.PivotPoint(100, 140, 0, 0);
        var tight = UnrealConvert.PivotPoint(100, 140, 80, 60);

        // Shifted by exactly the cell's own origin — 80 across, 60 down.
        Assert.Equal(loose.X - 80, tight.X, 6);
        Assert.Equal(loose.Y - 60, tight.Y, 6);
    }

    // ---- asset names ---------------------------------------------------------------

    [Theory]
    [InlineData("Hero (final)", "Hero_final")]
    [InlineData("run cycle", "run_cycle")]
    [InlineData("walk", "walk")]
    [InlineData("a//b", "a_b")]
    [InlineData("__odd__", "odd")]
    public void PunctuationUnrealRejectsBecomesAnUnderscore(string given, string expected)
    {
        Assert.Equal(expected, UnrealConvert.AssetName(given));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void ANameWithNothingUsableInItFallsBackRatherThanBeingEmpty(string? given)
    {
        // An empty asset name is not an error Unreal reports usefully; it is an asset
        // you cannot find again.
        Assert.Equal("Sprite", UnrealConvert.AssetName(given));
    }

    [Fact]
    public void ARunOfPunctuationCollapsesRatherThanBecomingARowOfUnderscores()
    {
        Assert.Equal("Hero_v2", UnrealConvert.AssetName("Hero -- v2"));
    }
}
