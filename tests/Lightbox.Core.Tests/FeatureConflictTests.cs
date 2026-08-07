using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

public class FeatureConflictTests
{
    private readonly FeatureConflicts _conflicts = new();

    [Fact]
    public void UnboundedCanvasExcludesFixedFrameBoundsExport()
    {
        var conflict = _conflicts.Raised(
            true, FeatureKey.UnboundedCanvas,
            true, FeatureKey.FixedFrameBoundsExport
        );

        Assert.NotNull(conflict);
        Assert.Equal(FeatureKey.UnboundedCanvas, conflict.Feature1);
        Assert.Equal(FeatureKey.FixedFrameBoundsExport, conflict.Feature2);
    }

    [Fact]
    public void ConflictReasonIsDescriptive()
    {
        var conflict = _conflicts.Raised(
            true, FeatureKey.UnboundedCanvas,
            true, FeatureKey.FixedFrameBoundsExport
        );

        Assert.NotNull(conflict);
        Assert.Contains("Sprite export", conflict.Reason);
        Assert.Contains("consistent frame bounds", conflict.Reason);
        Assert.Contains("unbounded", conflict.Reason);
    }

    [Fact]
    public void NoConflictWhenBothFalse()
    {
        var conflict = _conflicts.Raised(
            false, FeatureKey.UnboundedCanvas,
            false, FeatureKey.FixedFrameBoundsExport
        );

        Assert.Null(conflict);
    }

    [Fact]
    public void NoConflictWhenFirstFalse()
    {
        var conflict = _conflicts.Raised(
            false, FeatureKey.UnboundedCanvas,
            true, FeatureKey.FixedFrameBoundsExport
        );

        Assert.Null(conflict);
    }

    [Fact]
    public void NoConflictWhenSecondFalse()
    {
        var conflict = _conflicts.Raised(
            true, FeatureKey.UnboundedCanvas,
            false, FeatureKey.FixedFrameBoundsExport
        );

        Assert.Null(conflict);
    }

    [Fact]
    public void ConflictInvolvesIsBothOrders()
    {
        var conflict = new FeatureConflict(
            FeatureKey.UnboundedCanvas,
            FeatureKey.FixedFrameBoundsExport,
            "Test"
        );

        Assert.True(conflict.Involves(FeatureKey.UnboundedCanvas, FeatureKey.FixedFrameBoundsExport));
        Assert.True(conflict.Involves(FeatureKey.FixedFrameBoundsExport, FeatureKey.UnboundedCanvas));
        Assert.False(conflict.Involves(FeatureKey.UnboundedCanvas, FeatureKey.Camera));
    }

    [Fact]
    public void ConflictOtherReturnsTheOtherFeature()
    {
        var conflict = new FeatureConflict(
            FeatureKey.UnboundedCanvas,
            FeatureKey.FixedFrameBoundsExport,
            "Test"
        );

        Assert.Equal(FeatureKey.FixedFrameBoundsExport, conflict.Other(FeatureKey.UnboundedCanvas));
        Assert.Equal(FeatureKey.UnboundedCanvas, conflict.Other(FeatureKey.FixedFrameBoundsExport));
    }

    [Fact]
    public void ConflictsWithReturnsAllConflictsForFeature()
    {
        var conflictsWith = _conflicts.ConflictsWith(FeatureKey.UnboundedCanvas).ToList();

        Assert.NotEmpty(conflictsWith);
        Assert.Single(conflictsWith);
        Assert.True(conflictsWith[0].Involves(FeatureKey.UnboundedCanvas, FeatureKey.FixedFrameBoundsExport));
    }

    [Fact]
    public void ConflictsWithEmptyForFeatureWithNoConflicts()
    {
        var conflictsWith = _conflicts.ConflictsWith(FeatureKey.Layers).ToList();

        Assert.Empty(conflictsWith);
    }

    [Fact]
    public void AConflictHoldsInEveryProjectType()
    {
        // The conflict between unbounded canvas and fixed export bounds holds
        // regardless of project type. Both features are reachable in every type,
        // and the incompatibility is about the features, not about which type
        // you are in.

        var conflict = _conflicts.Raised(
            true, FeatureKey.UnboundedCanvas,
            true, FeatureKey.FixedFrameBoundsExport
        );

        Assert.NotNull(conflict);
        // No project type involved in the conflict; it's universal.
    }

    [Fact]
    public void AllReturnsAllDeclaredConflicts()
    {
        var all = _conflicts.All;

        Assert.NotEmpty(all);
        // At minimum, the unbounded canvas vs fixed export conflict
        Assert.Contains(all,
            c => c.Involves(FeatureKey.UnboundedCanvas, FeatureKey.FixedFrameBoundsExport)
        );
    }
}
