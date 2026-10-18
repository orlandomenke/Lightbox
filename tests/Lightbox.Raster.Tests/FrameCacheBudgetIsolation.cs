using System.Reflection;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Tests that set <c>FrameBitmapCache.ByteBudget</c>, which is process-wide.
/// </summary>
/// <remarks>
/// <para>
/// Naming <c>[Collection("FrameCacheBudget")]</c> on a class is not enough by
/// itself: with no <c>[CollectionDefinition]</c> behind the name, xUnit makes the
/// collection but still runs it beside every other collection. The budget tests
/// serialised with each other and not with, say,
/// <c>StrokeGroupRenderTests.OnlyAnEffectThatChangesOverTimeMakesADrawingDifferentAtEachCel</c>,
/// whose second <c>Get</c> evicted its first while a neighbour had the budget
/// set to a few frames — "a constant effect was keyed by cel", once in a full
/// run, never alone (B488). A try/finally restore does not help: the damage is
/// done while the small budget is set.
/// </para>
/// <para>
/// <c>Lightbox.App.Tests</c> has had this definition all along
/// (<c>BrushStateIsolation.cs</c>); this assembly never did.
/// </para>
/// </remarks>
[CollectionDefinition("FrameCacheBudget", DisableParallelization = true)]
public class FrameCacheBudgetCollection;

public sealed class FrameCacheBudgetIsolationTests
{
    [Fact]
    public void TheFrameCacheBudgetCollectionRunsAloneNotBesideOtherClasses()
    {
        var definition = typeof(FrameCacheBudgetCollection).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributesData())
            .SingleOrDefault(d => d.AttributeType == typeof(CollectionDefinitionAttribute)
                && d.ConstructorArguments.Any(c => c.Value as string == "FrameCacheBudget"));

        Assert.NotNull(definition);
        Assert.Contains(definition.NamedArguments,
            n => n.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization) && n.TypedValue.Value is true);
    }
}
