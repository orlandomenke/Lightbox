using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Which pixels of a finished composite the canvas is told to patch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Getting this wrong shows stale art, silently.</b> The function's own
/// comment says so: <i>"a wrong rectangle here would show stale pixels rather
/// than merely cost a repaint"</i>. Until B166's third seam it had no direct
/// test — the single assertion that touched it checked one null case, through a
/// whole pipeline, incidentally, from a test about the bake fold.
/// </para>
/// <para>
/// **Null is always safe**, because it means "repaint everything". So the tests
/// that matter most are the ones that require null, not the ones that require a
/// rectangle: a too-large rectangle costs milliseconds, and a too-small one
/// costs correctness.
/// </para>
/// </remarks>
public class SnapshotGeometryTests(ITestOutputHelper output)
{
    private static SKRectI? Changed(
        SKRectI? doc, SKRectI? covers = null, double scale = 1.0, bool camera = false) =>
        SnapshotGeometry.ChangedInImageSpace(doc, covers, scale, camera);

    /// <summary>
    /// <b>The refusal, and the important one.</b> A camera maps the document
    /// through an arbitrary matrix, so a document rectangle is not an
    /// axis-aligned image rectangle at all. There is no correct answer, and the
    /// safe one is "everything".
    /// </summary>
    [Fact]
    public void UnderACameraItRefusesToGuess()
    {
        var dab = SKRectI.Create(100, 100, 20, 20);

        Assert.NotNull(Changed(dab));
        Assert.Null(Changed(dab, camera: true));
    }

    /// <summary>
    /// A whole-canvas publish reports null — there is no smaller rectangle to
    /// name, and the canvas repaints all of it.
    /// </summary>
    [Fact]
    public void AWholeCanvasPublishReportsNull() => Assert.Null(Changed(null));

    /// <summary>
    /// <b>The rectangle is grown by a pixel on every side.</b> The composite's own
    /// edges are antialiased, so a patch exact to the rectangle leaves a seam
    /// where the new pixels meet the old.
    /// </summary>
    [Fact]
    public void ThePatchIsGrownByAPixelSoTheSeamDoesNotShow()
    {
        var dab = SKRectI.Create(100, 100, 20, 20);

        var patch = Changed(dab)!.Value;

        output.WriteLine($"{dab} -> {patch}");
        Assert.Equal(new SKRectI(99, 99, 121, 121), patch);
        // Stated as the property rather than only as the numbers: it must
        // strictly contain the rectangle it is patching.
        Assert.True(patch.Contains(dab), "the patch does not cover the region that changed");
    }

    /// <summary>
    /// <b>A culled image starts at the viewport's corner, not the document's.</b>
    /// Forgetting the offset patches the right-sized region in the wrong place,
    /// which is stale pixels where the dab was and a smear where it was not.
    /// </summary>
    [Fact]
    public void ACulledImageIsOffsetByWhatItCovers()
    {
        var dab = SKRectI.Create(700, 400, 20, 20);
        var covers = SKRectI.Create(640, 360, 640, 360);

        var patch = Changed(dab, covers)!.Value;

        output.WriteLine($"doc {dab} in image covering {covers} -> {patch}");
        Assert.Equal(new SKRectI(59, 39, 81, 61), patch);
    }

    /// <summary>
    /// The surface may be smaller than the document, so the rectangle scales
    /// with it. Rounding goes outwards — floor the near edges, ceil the far ones
    /// — because a patch that rounds inwards leaves a row of stale pixels.
    /// </summary>
    [Fact]
    public void ScalingRoundsOutwardsRatherThanToNearest()
    {
        var dab = SKRectI.Create(101, 101, 3, 3);

        var patch = Changed(dab, scale: 0.5)!.Value;

        output.WriteLine($"{dab} at 0.5 -> {patch}");
        // 101 * 0.5 = 50.5 -> floor 50, minus the seam pixel; 104 * 0.5 = 52 -> ceil 52, plus one.
        Assert.Equal(new SKRectI(49, 49, 53, 53), patch);
    }

    /// <summary>
    /// A nonsense scale is refused rather than propagated. It reaches this from
    /// a compose scale computed off a viewport, and a NaN would produce a
    /// rectangle of garbage coordinates that the patch path would act on.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnImpossibleScaleFallsBackToRepaintingEverything(double scale)
        => Assert.Null(Changed(SKRectI.Create(10, 10, 8, 8), scale: scale));

    /// <summary>
    /// An empty region comes back as the seam alone rather than as null — and
    /// that is correct, which is worth writing down because it looks like a bug.
    /// </summary>
    /// <remarks>
    /// The function ends with a <c>right &lt;= left</c> guard that reads as the
    /// handler for this case. **It is unreachable for any finite input**: the
    /// seam growth widens every rectangle by two pixels before the check, so
    /// even a zero-area document rectangle arrives 2 px wide. The guard is
    /// belt-and-braces against a scale that collapses coordinates, not the
    /// empty-region path.
    /// <para>
    /// Patching two pixels nobody changed costs nothing measurable, and the
    /// alternative — null — would repaint the whole canvas, so the behaviour is
    /// the cheaper of the two safe answers rather than an oversight.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyRegionComesBackAsTheSeamAlone()
    {
        var patch = Changed(new SKRectI(50, 50, 50, 50));

        output.WriteLine($"empty region -> {patch}");
        Assert.Equal(new SKRectI(49, 49, 51, 51), patch);
    }

    /// <summary>
    /// The patch is expressed in the image's pixels, so a document-sized change
    /// on a half-scale surface is half-sized plus the seam — not document-sized.
    /// This is the one that would be wrong if the offset and the scale were
    /// applied in the wrong order.
    /// </summary>
    [Fact]
    public void TheOffsetIsAppliedBeforeTheScale()
    {
        var dab = SKRectI.Create(700, 400, 100, 100);
        var covers = SKRectI.Create(600, 300, 800, 600);

        var patch = Changed(dab, covers, scale: 0.5)!.Value;

        // (700-600)*0.5 = 50, (800-600)*0.5 = 100. Scaling first would give
        // 350-600 = -250, which is off the surface entirely.
        output.WriteLine($"-> {patch}");
        Assert.Equal(new SKRectI(49, 49, 101, 101), patch);
    }
}
