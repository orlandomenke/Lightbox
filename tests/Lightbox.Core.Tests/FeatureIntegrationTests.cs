using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Integration tests for feature defaults and conflicts in the document lifecycle.
/// These tests document the expected behavior when features are:
/// 1. Loaded from a project with defaults
/// 2. Overridden at the document level
/// 3. Serialized (only overrides are written)
/// 4. Validated for conflicts before export
/// </summary>
public class FeatureIntegrationTests
{
    private readonly FeatureDefaults _defaults = new();
    private readonly FeatureConflicts _conflicts = new();

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
        var unboundedDefault = _defaults.GetDefault(ProjectType.Animation, FeatureKey.UnboundedCanvas);

        // GetFeature with no override falls back to the provided default
        var result = doc.GetFeature(FeatureKey.UnboundedCanvas, unboundedDefault);

        Assert.False(result);
    }

    [Fact]
    public void DocumentCanOverrideDefault()
    {
        var doc = new Doc();
        doc.Features = new()
        {
            { nameof(FeatureKey.UnboundedCanvas), true },
        };

        var unboundedDefault = _defaults.GetDefault(ProjectType.Animation, FeatureKey.UnboundedCanvas);
        var result = doc.GetFeature(FeatureKey.UnboundedCanvas, unboundedDefault);

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
                { nameof(FeatureKey.UnboundedCanvas), true },
            },
        };
        Assert.NotNull(changedDoc.Features);
        Assert.Single(changedDoc.Features);
    }

    [Fact]
    public void ConflictDetectionForSpriteExport()
    {
        // When both UnboundedCanvas and FixedFrameBoundsExport are requested,
        // the conflict exists regardless of project type.
        var conflict = _conflicts.Raised(
            true, FeatureKey.UnboundedCanvas,
            true, FeatureKey.FixedFrameBoundsExport
        );

        Assert.NotNull(conflict);
        Assert.Contains("Sprite export", conflict.Reason);
    }

    [Fact]
    public void DetectConflictBetweenUnboundedAndFixed()
    {
        // When a document has both features enabled, the conflict should be detectable
        var doc = new Doc
        {
            Features = new()
            {
                // Artist enabled unbounded canvas
                { nameof(FeatureKey.UnboundedCanvas), true },
            },
        };

        var unboundedEnabled = doc.GetFeature(FeatureKey.UnboundedCanvas, false);
        Assert.True(unboundedEnabled);

        // The conflict checker can detect this
        var conflict = _conflicts.ConflictsWith(FeatureKey.UnboundedCanvas).FirstOrDefault();
        Assert.NotNull(conflict);
        Assert.NotEmpty(conflict.Reason);
    }

    [Fact]
    public void NoConflictWhenUnboundedDisabled()
    {
        var doc = new Doc
        {
            Features = new()
            {
                { nameof(FeatureKey.UnboundedCanvas), false },
            },
        };

        var unboundedEnabled = doc.GetFeature(FeatureKey.UnboundedCanvas, false);
        Assert.False(unboundedEnabled);

        // No conflict should be raised
        var raised = _conflicts.Raised(unboundedEnabled, FeatureKey.UnboundedCanvas, true, FeatureKey.FixedFrameBoundsExport);
        Assert.Null(raised);
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
    public void ConflictIsDocumentedInFeatureKey()
    {
        // The conflict between unbounded canvas and fixed export bounds should be
        // in the FeatureConflicts registry, discoverable from either direction
        var fromUnbounded = _conflicts.ConflictsWith(FeatureKey.UnboundedCanvas);
        var fromFixed = _conflicts.ConflictsWith(FeatureKey.FixedFrameBoundsExport);

        Assert.NotEmpty(fromUnbounded);
        Assert.NotEmpty(fromFixed);

        // Both should find the same conflict
        var unboundedConflict = fromUnbounded.FirstOrDefault();
        var fixedConflict = fromFixed.FirstOrDefault();

        Assert.NotNull(unboundedConflict);
        Assert.NotNull(fixedConflict);
        Assert.Equal(unboundedConflict.Reason, fixedConflict.Reason);
    }
}
