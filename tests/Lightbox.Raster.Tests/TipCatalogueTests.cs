using Lightbox.Core.Documents;
using Lightbox.Raster.Tips;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The eight tips that are there before the artist has made any.
/// </summary>
public class TipCatalogueTests
{
    [Fact]
    public void ThereAreEightBuiltInsAndEveryOneBakes()
    {
        var all = TipCatalogue.All;

        Assert.Equal(8, all.Count);
        Assert.All(all, tip => Assert.NotEmpty(tip.Png));
        Assert.All(all, tip => Assert.NotNull(tip.Recipe));
    }

    [Fact]
    public void EveryBuiltInIsADistinctShape()
    {
        // No "soft round 2". A size slider and a hardness slider already make
        // those, and a library an artist has to read through is worse than one
        // they can see at a glance — so every entry has to be a shape the
        // others cannot reach.
        var shapes = TipCatalogue.Recipes.Select(r => r.Recipe.Shape).ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [Fact]
    public void TheIdsAreFrozen()
    {
        // A document that painted with a built-in copied the raster into
        // Doc.BrushTips under this id, so the id is part of that file's record
        // forever. Renaming one is a compatibility break, which is exactly the
        // kind of change that is easy to make by accident and impossible to
        // notice — so the list is written down here.
        Assert.Equal(
        [
            "tip-builtin-soft-round",
            "tip-builtin-hard-round",
            "tip-builtin-paintbrush",
            "tip-builtin-bristle",
            "tip-builtin-marker-nib",
            "tip-builtin-cut-nib",
            "tip-builtin-spatter",
            "tip-builtin-wet-edge",
        ], TipCatalogue.All.Select(t => t.Id));

        Assert.All(TipCatalogue.All, t => Assert.True(TipCatalogue.IsBuiltIn(t.Id)));
        Assert.False(TipCatalogue.IsBuiltIn("tip-abc123"));
        Assert.False(TipCatalogue.IsBuiltIn(null));
    }

    [Fact]
    public void ThePixelsBehindABuiltInDoNotMoveBetweenRuns()
    {
        // The other half of the frozen id. Same id, different pixels would
        // silently repaint every drawing that used one, which is invariant 1
        // read from the wrong end.
        foreach (var (_, name, recipe) in TipCatalogue.Recipes)
        {
            var again = TipGenerator.Create(recipe, name);
            var baked = TipCatalogue.All.Single(t => t.Name == name);
            Assert.Equal(baked.Png, again.Png);
        }
    }

    [Fact]
    public void TheCatalogueIsBakedOnceAndSharedAfterwards()
    {
        // A tip is an asset. Re-deriving one per library open is the same
        // mistake as re-deriving one per dab, only slower to notice.
        Assert.Same(TipCatalogue.All, TipCatalogue.All);
        Assert.Same(TipCatalogue.All[0], TipCatalogue.All[0]);
    }

    [Fact]
    public void TheThreeStaplesAreTheOnesAnArtistExpectsToFindFirst()
    {
        // Order is the UI's, and someone opening the library for the first time
        // should meet a round brush before they meet a spatter.
        Assert.Equal(["Soft round", "Hard round", "Paintbrush"],
            TipCatalogue.All.Take(3).Select(t => t.Name));

        Assert.Equal(TipShape.SoftCircle, TipCatalogue.All[0].Recipe!.Shape);
        Assert.Equal(TipShape.HardCircle, TipCatalogue.All[1].Recipe!.Shape);
    }

    [Fact]
    public void APaintbrushIsFlatEnoughToReadAsOne()
    {
        // The point of the third staple: with AngleFollowsDirection on it is
        // the cheapest thing in the engine that reads as a loaded brush rather
        // than a pen, and that only works if it is actually flattened.
        var recipe = TipCatalogue.Recipes.Single(r => r.Id == "paintbrush").Recipe;

        Assert.Equal(TipShape.Chisel, recipe.Shape);
        Assert.InRange(recipe.Roundness, 0.15, 0.6);
    }
}
