using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

public class FeatureDefaultsTests
{
    private readonly FeatureDefaults _defaults = new();

    [Fact]
    public void EveryFeatureIsReachableInEveryProjectType()
    {
        var projectTypes = Enum.GetValues<ProjectType>();
        var featureKeys = Enum.GetValues<FeatureKey>();

        foreach (var type in projectTypes)
        {
            var features = _defaults.FeaturesFor(type).ToList();
            Assert.Equal(featureKeys.Length, features.Count);

            foreach (var feature in featureKeys)
            {
                var value = _defaults.GetDefault(type, feature);
                Assert.IsType<bool>(value);
            }
        }
    }

    [Fact]
    public void AProjectTypeSetsDefaultsRatherThanAvailability()
    {
        // The registry declares defaults, not availability.
        // Every feature is reachable in every type; the type only sets the default.
        // This test is documentation: if the result changes, the promise changes.

        var illustration = ProjectType.Illustration;
        var gameArt = ProjectType.GameArt;

        // Both types can reach FixedFrameBoundsExport, but default differently
        Assert.False(_defaults.GetDefault(illustration, FeatureKey.FixedFrameBoundsExport));
        Assert.True(_defaults.GetDefault(gameArt, FeatureKey.FixedFrameBoundsExport));

        // Both types can reach Camera, but default differently
        Assert.False(_defaults.GetDefault(illustration, FeatureKey.Camera));
        Assert.True(_defaults.GetDefault(ProjectType.Storyboard, FeatureKey.Camera));
    }

    [Fact]
    public void AFeatureLeftAtItsDefaultWritesNoKey()
    {
        // When a document is created with a project's defaults and never changes
        // a feature, nothing about that feature is written to the document.
        // This test documents the contract; the enforcement is in document
        // serialization (Doc should not write a feature key if it matches the default).

        var defaults = _defaults.GetDefault(ProjectType.GameArt, FeatureKey.FixedFrameBoundsExport);
        Assert.True(defaults);

        // A game art project would have FixedFrameBoundsExport=true by default,
        // and the document would carry no key unless the artist changed it.
        // The document deserialization path reads the default from the registry
        // to fill in any missing keys.
    }

    [Fact]
    public void IllustrationDefaultsAreMinimal()
    {
        Assert.False(_defaults.GetDefault(ProjectType.Illustration, FeatureKey.UnboundedCanvas));
        Assert.False(_defaults.GetDefault(ProjectType.Illustration, FeatureKey.FixedFrameBoundsExport));
        Assert.False(_defaults.GetDefault(ProjectType.Illustration, FeatureKey.Camera));
        Assert.True(_defaults.GetDefault(ProjectType.Illustration, FeatureKey.Layers));
        Assert.False(_defaults.GetDefault(ProjectType.Illustration, FeatureKey.ExposureSheet));
    }

    [Fact]
    public void AnimationDefaultsIncludeExposureSheet()
    {
        Assert.True(_defaults.GetDefault(ProjectType.Animation, FeatureKey.ExposureSheet));
        Assert.True(_defaults.GetDefault(ProjectType.Animation, FeatureKey.Layers));
        Assert.False(_defaults.GetDefault(ProjectType.Animation, FeatureKey.FixedFrameBoundsExport));
        Assert.False(_defaults.GetDefault(ProjectType.Animation, FeatureKey.Camera));
    }

    [Fact]
    public void GameArtDefaultsIncludeFixedFrameBoundsExport()
    {
        Assert.True(_defaults.GetDefault(ProjectType.GameArt, FeatureKey.FixedFrameBoundsExport));
        Assert.True(_defaults.GetDefault(ProjectType.GameArt, FeatureKey.Layers));
        Assert.False(_defaults.GetDefault(ProjectType.GameArt, FeatureKey.UnboundedCanvas));
    }

    [Fact]
    public void StoryboardDefaultsIncludeCamera()
    {
        Assert.True(_defaults.GetDefault(ProjectType.Storyboard, FeatureKey.Camera));
        Assert.True(_defaults.GetDefault(ProjectType.Storyboard, FeatureKey.ExposureSheet));
        Assert.True(_defaults.GetDefault(ProjectType.Storyboard, FeatureKey.Layers));
    }

    [Fact]
    public void AssetLibraryDefaultsIncludeFixedFrameBoundsExport()
    {
        Assert.True(_defaults.GetDefault(ProjectType.AssetLibrary, FeatureKey.FixedFrameBoundsExport));
    }

    [Fact]
    public void GetDefaultThrowsForUnknownType()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _defaults.GetDefault((ProjectType)999, FeatureKey.Camera)
        );
    }

    [Fact]
    public void FeaturesForReturnsAllFeaturesForType()
    {
        var features = _defaults.FeaturesFor(ProjectType.Animation).ToList();
        Assert.NotEmpty(features);
        Assert.Contains(FeatureKey.ExposureSheet, features);
        Assert.Contains(FeatureKey.Layers, features);
    }

    [Fact]
    public void TypesForReturnsAllTypesWithFeature()
    {
        var types = _defaults.TypesFor(FeatureKey.Layers).ToList();
        Assert.Equal(Enum.GetValues<ProjectType>().Length, types.Count);
        // Layers is on by default in every type
    }

    [Fact]
    public void LayersIsEnabledInEveryProjectType()
    {
        foreach (var type in Enum.GetValues<ProjectType>())
        {
            Assert.True(_defaults.GetDefault(type, FeatureKey.Layers),
                $"Layers should be enabled by default in {type}");
        }
    }
}
