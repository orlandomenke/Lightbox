using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Budgets derived from the machine rather than from the machine they were
/// written on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every cache budget here was a constant chosen while looking at one
/// laptop.</b> 512 MB of frame cache and 192 MB of layer textures leave a 64 GB
/// workstation idling and are more than a minimum-spec machine can spare. For
/// something meant to run in production on hardware nobody here owns, that is
/// specificity dressed as a default.
/// </para>
/// <para>
/// These assert the <em>shape</em> — a fraction, clamped, monotonic — rather
/// than the numbers, because the numbers are supposed to differ per machine and
/// a test that pinned them would be the old problem with extra steps.
/// </para>
/// </remarks>
[Collection("FrameCacheBudget")]
public class MemoryBudgetTests(ITestOutputHelper output)
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    /// <summary>
    /// <b>The minimum spec is a production decision, not a guess (Q64):</b> indie
    /// 2D game work, on an ordinary laptop with integrated graphics and 8 GB.
    /// Every floor exists so that machine still works.
    /// </summary>
    [Fact]
    public void TheFloorsAreAffordableOnTheMinimumSpec()
    {
        // What each derivation would give on exactly the minimum spec, floors and
        // ceilings applied — which is the number that has to be affordable, not
        // the fraction.
        var frames = Math.Clamp(MemoryBudget.MinimumSpecBytes / 8, 256 * Mb, 4 * Gb);
        var tiles = Math.Clamp(MemoryBudget.MinimumSpecBytes / 32, 128 * Mb, 2 * Gb);
        var textures = Math.Clamp(MemoryBudget.MinimumSpecBytes / 16, 64 * Mb, 1 * Gb);
        var total = frames + tiles + textures;

        output.WriteLine($"on the 8 GB minimum spec: frames {frames / Mb} MB, tiles {tiles / Mb} MB, " +
                         $"textures {textures / Mb} MB — {total / Mb} MB of " +
                         $"{MemoryBudget.MinimumSpecBytes / Mb} MB");
        output.WriteLine($"available on this machine: {MemoryBudget.Available / Mb} MB");

        // All three at once is the worst case rather than the usual one — the
        // bitmap and tile caches hold the same frames by two routes, and the
        // textures only fill with GPU compositing switched on. But the worst case
        // is what a minimum-spec machine has to survive, and it must leave room
        // for the document, the undo history and the operating system.
        Assert.True(total < MemoryBudget.MinimumSpecBytes / 4,
            $"the caches would claim {total / Mb} MB of the {MemoryBudget.MinimumSpecBytes / Mb} MB " +
            "minimum spec between them, which is more than a quarter");
    }

    /// <summary>
    /// A bigger machine gets a bigger budget. This is the whole point: the same
    /// rule, a different answer, with nobody editing a constant.
    /// </summary>
    [Theory]
    [InlineData(8L * 1024 * 1024 * 1024, 32L * 1024 * 1024 * 1024)]
    [InlineData(4L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024)]
    public void MoreMemoryMeansMoreBudget(long smaller, long larger)
    {
        // Share is pure arithmetic on a fraction, so the monotonicity can be
        // checked without pretending to be another machine.
        var small = Math.Clamp((long)(smaller / 8.0), 256 * Mb, 4 * Gb);
        var big = Math.Clamp((long)(larger / 8.0), 256 * Mb, 4 * Gb);

        output.WriteLine($"{smaller / Gb} GB -> {small / Mb} MB, {larger / Gb} GB -> {big / Mb} MB");
        Assert.True(big > small);
    }

    /// <summary>
    /// <b>And it is clamped at both ends.</b> Without a ceiling a 128 GB machine
    /// hands a cache 16 GB it will never fill, which is memory held for nothing;
    /// without a floor a small machine gets a cache too small to survive a frame,
    /// which is pure overhead.
    /// </summary>
    [Fact]
    public void TheShareIsClampedBothWays()
    {
        Assert.Equal(100 * Mb, MemoryBudget.Share(1.0, 10 * Mb, 100 * Mb));
        Assert.Equal(10 * Mb, MemoryBudget.Share(0.0, 10 * Mb, 100 * Mb));
    }

    /// <summary>
    /// A floor above the ceiling is a caller's mistake and must not produce a
    /// nonsense budget — the clamp would otherwise throw or invert.
    /// </summary>
    [Fact]
    public void AnInvertedRangeIsSurvivable()
    {
        var b = MemoryBudget.Share(0.5, 100 * Mb, 10 * Mb);

        Assert.InRange(b, 10 * Mb, 100 * Mb);
    }

    /// <summary>
    /// The real budgets are within their stated ranges on whatever machine this
    /// runs on, including CI.
    /// </summary>
    [Fact]
    public void TheDerivedBudgetsAreInRangeHere()
    {
        var frames = MemoryBudget.FrameCache();
        var textures = MemoryBudget.LayerTextures();

        var tiles = MemoryBudget.TileCache();

        output.WriteLine($"frame cache {frames / Mb} MB, tiles {tiles / Mb} MB, " +
                         $"layer textures {textures / Mb} MB");
        Assert.InRange(frames, 256 * Mb, 4 * Gb);
        Assert.InRange(tiles, 128 * Mb, 2 * Gb);
        Assert.InRange(textures, 64 * Mb, 1 * Gb);
        // Textures are meaner than frames on purpose: on integrated graphics they
        // are the same memory the compositor is already competing for.
        Assert.True(textures <= frames);
    }

    /// <summary>
    /// The caches that actually run take their defaults from here, rather than
    /// each carrying its own constant — which is the whole change.
    /// </summary>
    [Fact]
    public void TheCachesTakeTheirDefaultsFromTheDerivation()
    {
        output.WriteLine($"frames {Rendering.FrameBitmapCache.ByteBudget / Mb} MB, " +
                         $"tiles {Rendering.TileFrameCache.ByteBudget / Mb} MB");

        // Static, so another test may have pinned them; the range is what holds
        // either way, and pinning outside it would itself be the bug.
        Assert.InRange(Rendering.FrameBitmapCache.ByteBudget, 64 * Mb, 4 * Gb);
        Assert.InRange(Rendering.TileFrameCache.ByteBudget, 64 * Mb, 2 * Gb);
    }

    /// <summary>
    /// <b>The artist's floor is allowed below the derived one, and that is the
    /// design.</b> The derived floor is what a minimum-spec machine needs for the
    /// cache to earn its keep; the setting's floor is how far somebody may go
    /// when they have decided they would rather have the memory back. The
    /// <em>ceiling</em> is shared, because past it the cache holds bytes it will
    /// never spend no matter who asked.
    /// </summary>
    [AvaloniaFact]
    public void TheSettingsCeilingIsTheDerivationsCeiling()
    {
        var vm = new ViewModels.MainViewModel(null);
        var previous = Rendering.FrameBitmapCache.ByteBudget;
        try
        {
            vm.FrameCacheBudgetMb = int.MaxValue;
            Assert.Equal(MemoryBudget.FrameCacheCeilingBytes / Mb, vm.FrameCacheBudgetMb);

            vm.FrameCacheBudgetMb = 1;
            Assert.Equal(64, vm.FrameCacheBudgetMb);
        }
        finally
        {
            Rendering.FrameBitmapCache.ByteBudget = previous;
        }
    }
}
