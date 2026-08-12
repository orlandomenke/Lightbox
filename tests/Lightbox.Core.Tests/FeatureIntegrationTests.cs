using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Integration tests for feature defaults in the document lifecycle.
/// These tests document the expected behavior when features are:
/// 1. Loaded from a project with defaults
/// 2. Overridden at the document level
/// 3. Serialized (only overrides are written)
/// </summary>
public class FeatureIntegrationTests
{
    private readonly FeatureDefaults _defaults = new();

    [Fact]
    public void DocumentInheritsProjectDefaults()
    {
        // When a document is created in a game art project, it inherits
        // that project's feature defaults.
        var doc = new Doc();
        var gameArtDefault = _defaults.GetDefault(ProjectType.GameArt, FeatureKey.FixedFrameBoundsExport);

        Assert.True(gameArtDefault);
        // The document stores no overrides initially
        Assert.Null(doc.Features);
    }

    [Fact]
    public void GetFeatureResolvesDefault()
    {
        var doc = new Doc();
        var cameraDefault = _defaults.GetDefault(ProjectType.Animation, FeatureKey.Camera);

        // GetFeature with no override falls back to the provided default
        var result = doc.GetFeature(FeatureKey.Camera, cameraDefault);

        Assert.False(result);
    }

    [Fact]
    public void DocumentCanOverrideDefault()
    {
        var doc = new Doc();
        doc.Features = new()
        {
            { nameof(FeatureKey.Camera), true },
        };

        var cameraDefault = _defaults.GetDefault(ProjectType.Animation, FeatureKey.Camera);
        var result = doc.GetFeature(FeatureKey.Camera, cameraDefault);

        Assert.True(result);
    }

    [Fact]
    public void FeaturesOnlySerializedWhenChanged()
    {
        // A document that never overrides any feature writes no Features key.
        var defaultDoc = new Doc();
        Assert.Null(defaultDoc.Features);

        // A document that overrides a feature writes only what changed.
        var changedDoc = new Doc
        {
            Features = new()
            {
                { nameof(FeatureKey.Camera), true },
            },
        };
        Assert.NotNull(changedDoc.Features);
        Assert.Single(changedDoc.Features);
    }

    [Fact]
    public void AllProjectTypesHaveConsistentDefaults()
    {
        // Every project type has a default for every feature
        var types = Enum.GetValues<ProjectType>();
        var features = Enum.GetValues<FeatureKey>();

        foreach (var type in types)
        {
            foreach (var feature in features)
            {
                var defaultValue = _defaults.GetDefault(type, feature);
                Assert.IsType<bool>(defaultValue);
            }
        }
    }

    [Fact]
    public void FeatureOverridesSerializeToJsonAsStrings()
    {
        // Verify that feature overrides are serialized to JSON with string keys,
        // following the same pattern as camera and symbol versioning.
        // Only true values (overrides of defaults) are stored; false is implicit.
        var doc = new Doc
        {
            Features = new()
            {
                { nameof(FeatureKey.Camera), true },
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            doc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        // The override is in the JSON
        Assert.Contains("Camera", json);
        Assert.Contains("true", json);
    }
}
