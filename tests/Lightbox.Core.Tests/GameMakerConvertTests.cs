using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// The GameMaker naming contract, which is the whole integration.
/// </summary>
/// <remarks>
/// There is no format here to be wrong about, which is the point of choosing the
/// <c>_stripN</c> convention over writing <c>.yy</c> files. What is left is a filename,
/// and a filename is load-bearing in a way that looks trivial: GameMaker divides the
/// image's width by the number in it, so an off-by-one in this string slices every frame
/// through the middle.
/// </remarks>
public class GameMakerConvertTests
{
    // ---- the filename is the contract ----------------------------------------------

    [Fact]
    public void AStripIsNamedWithItsOwnFrameCount()
    {
        Assert.Equal("walk_strip8.png", GameMakerConvert.StripFileName("walk", 8));
        Assert.Equal("hero_idle_strip2.png", GameMakerConvert.StripFileName("hero_idle", 2));
    }

    [Fact]
    public void TheNameSurvivesARoundTripThroughTheReader()
    {
        // A forward-only test agrees with an off-by-one; a round trip does not.
        foreach (var frames in new[] { 1, 2, 7, 12, 120 })
        {
            var name = GameMakerConvert.StripFileName("run cycle", frames);
            var (read, count) = GameMakerConvert.ReadStripName(name);

            Assert.Equal("run_cycle", read);
            Assert.Equal(frames, count);
        }
    }

    [Fact]
    public void PunctuationGameMakerRejectsIsGoneFromTheName()
    {
        // The asset name GameMaker creates comes from the filename, and it will not take
        // a space or a bracket.
        Assert.Equal("Hero_v2_strip4.png", GameMakerConvert.StripFileName("Hero (v2)", 4));
        Assert.Equal("sprite_strip4.png", GameMakerConvert.StripFileName("   ", 4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AStripCannotClaimZeroFrames(int frames)
    {
        // Zero would make GameMaker divide the width by zero, or ignore the suffix — and
        // which of those it does is not something to find out on somebody's project.
        Assert.Equal("walk_strip1.png", GameMakerConvert.StripFileName("walk", frames));
    }

    [Fact]
    public void ANameWithNoSuffixReadsAsASingleFrame()
    {
        var (name, frames) = GameMakerConvert.ReadStripName("portrait.png");

        Assert.Equal("portrait", name);
        Assert.Equal(1, frames);
    }

    [Fact]
    public void ANameThatOnlyLooksLikeAStripIsNotOne()
    {
        // "_strip" with nothing after it, and "_stripfoo", are not the convention. Reading
        // them as one would report a frame count nobody wrote.
        Assert.Equal(("walk_strip", 1), GameMakerConvert.ReadStripName("walk_strip.png"));
        Assert.Equal(("walk_stripfoo", 1), GameMakerConvert.ReadStripName("walk_stripfoo.png"));
    }

    [Fact]
    public void ItReadsTheLastSuffixWhenAnEarlierOneIsPartOfTheName()
    {
        // A sprite genuinely called "strip_light" would otherwise confuse the reader.
        var (name, frames) = GameMakerConvert.ReadStripName("strip_light_strip6.png");

        Assert.Equal("strip_light", name);
        Assert.Equal(6, frames);
    }

    // ---- the origin, and the fact that two engines agree ---------------------------

    [Fact]
    public void TheOriginIsPixelsFromTheCellsTopLeft()
    {
        var (x, y) = GameMakerConvert.SpriteOrigin(pivotX: 100, pivotY: 140, cellLeft: 80, cellTop: 60);

        Assert.Equal(20, x, 6);
        Assert.Equal(80, y, 6);
    }

    [Fact]
    public void GameMakerAndUnrealHappenToAgreeAboutThePivotAndThisIsWhereThatIsRecorded()
    {
        // Both are rectangle-relative texture pixels with Y down, so one implementation
        // serves both. Asserted rather than claimed in a comment, because if either engine
        // ever changes the shared implementation stops being safe and this is the test
        // that says so.
        var gm = GameMakerConvert.SpriteOrigin(55, 90, 10, 20);
        var ue = UnrealConvert.PivotPoint(55, 90, 10, 20);

        Assert.Equal(ue.X, gm.X, 6);
        Assert.Equal(ue.Y, gm.Y, 6);
    }

    [Fact]
    public void AFeetOriginIsTheLargerYBecauseGameMakersYRunsDown()
    {
        var feet = GameMakerConvert.SpriteOrigin(50, 100, 0, 0);
        var head = GameMakerConvert.SpriteOrigin(50, 0, 0, 0);

        Assert.True(feet.Y > head.Y, $"feet {feet.Y}, head {head.Y}");
    }
}
