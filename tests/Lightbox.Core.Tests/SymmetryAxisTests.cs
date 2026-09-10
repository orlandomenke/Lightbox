using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The symmetry record and the placements it asks for.
/// </summary>
/// <remarks>
/// The geometry is decided in Core and tested without a canvas, which is the
/// reason <see cref="SymmetryPlacement"/> is a pair of numbers rather than a
/// matrix. What lands on pixels is guarded separately in the Raster suite; what
/// is guarded here is that the arrangement is the one Q185 asked for.
/// </remarks>
public class SymmetryAxisTests
{
    private static SymmetryAxis Vertical(int order = 1, bool mirror = true) => new()
    {
        CenterX = 100,
        CenterY = 100,
        AngleDeg = 90,
        Order = order,
        Mirror = mirror,
    };

    // ---- the three behaviours Q185 chose one record for -------------------

    [Fact]
    public void OrderOneWithAMirrorIsThePlainTwoHalvedMirror()
    {
        var placements = Vertical().Placements();
        Assert.Equal(2, placements.Length);
        Assert.True(placements[0].IsIdentity);
        Assert.True(placements[1].Mirrored);
    }

    [Fact]
    public void OrderSixWithoutAMirrorIsSixTurnedCopies()
    {
        var placements = Vertical(order: 6, mirror: false).Placements();
        Assert.Equal(6, placements.Length);
        Assert.DoesNotContain(placements, p => p.Mirrored);
        Assert.Equal([0, 60, 120, 180, 240, 300], placements.Select(p => p.RotationDeg));
    }

    [Fact]
    public void OrderSixWithAMirrorIsTwelveCopiesHalfOfThemReflected()
    {
        var placements = Vertical(order: 6).Placements();
        Assert.Equal(12, placements.Length);
        Assert.Equal(6, placements.Count(p => p.Mirrored));
        Assert.Equal(6, placements.Count(p => !p.Mirrored));
    }

    /// <summary>
    /// <c>CopyCount</c> agrees with what <c>Placements</c> actually produces.
    /// </summary>
    /// <remarks>
    /// Two ways to answer the same question is a bug report waiting to happen
    /// (charter O5), and this one is load-bearing: the engine sizes work by the
    /// count and the artist is shown it, so a disagreement is a wrong number on
    /// screen or a copy nobody stamped.
    /// </remarks>
    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 2)]
    [InlineData(4, false, 4)]
    [InlineData(6, true, 12)]
    [InlineData(0, false, 1)]
    [InlineData(-3, true, 2)]
    public void TheCopyCountIsWhatThePlacementsCome(int order, bool mirror, int expected)
    {
        var axis = new SymmetryAxis { Order = order, Mirror = mirror };
        Assert.Equal(expected, axis.CopyCount);
        Assert.Equal(expected, axis.Placements().Length);
    }

    // ---- the identity, which the fast path and the fingerprint depend on ---

    /// <summary>
    /// The first placement is exactly the identity, so the mark the artist drew
    /// is stamped through no transform at all.
    /// </summary>
    /// <remarks>
    /// This is what lets <c>DrawingCostBaselineTests</c>' render fingerprint
    /// stay still: if the original copy went through a matrix that was merely
    /// <em>close</em> to identity, an antialiased edge would move by a fraction
    /// of a pixel and every one of those hashes would change.
    /// </remarks>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(8, true)]
    public void TheFirstPlacementIsAlwaysTheUntransformedMark(int order, bool mirror)
    {
        var placements = new SymmetryAxis { Order = order, Mirror = mirror, AngleDeg = 37 }.Placements();
        Assert.True(placements[0].IsIdentity);
        Assert.Equal(0, placements[0].RotationDeg);
        Assert.False(placements[0].Mirrored);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(0, false, true)]
    public void AnAxisThatWouldChangeNothingSaysSo(int order, bool mirror, bool identity)
    {
        Assert.Equal(identity, new SymmetryAxis { Order = order, Mirror = mirror }.IsIdentity);
    }

    /// <summary>An identity axis asks for exactly one placement, not none.</summary>
    /// <remarks>
    /// Returning an empty array would make a stroke with a pointless axis
    /// disappear, which is a far worse failure than the pointless axis.
    /// </remarks>
    [Fact]
    public void AnIdentityAxisStillAsksForTheOriginalMark()
    {
        var placements = new SymmetryAxis { Order = 1, Mirror = false }.Placements();
        Assert.Single(placements);
        Assert.True(placements[0].IsIdentity);
    }

    // ---- the axis angle is the thing that rotates -------------------------

    /// <summary>
    /// Turning the axis turns where the reflection lands, and leaves the
    /// original mark alone.
    /// </summary>
    /// <remarks>
    /// <c>reflect(β) = rotate(2β) ∘ flipY</c>, so the reflection's rotation
    /// moves at twice the axis angle. A version that used <c>β</c> rather than
    /// <c>2β</c> reflects across the wrong line for every angle except 0 and 90,
    /// which is exactly the bug an artist would report as "the mirror is off
    /// when I rotate it".
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 90)]
    [InlineData(90, 180)]
    [InlineData(30, 60)]
    public void AReflectionTurnsAtTwiceTheAxisAngle(double axisAngle, double expectedRotation)
    {
        var placements = new SymmetryAxis { AngleDeg = axisAngle, Order = 1, Mirror = true }.Placements();
        var reflection = Assert.Single(placements.Where(p => p.Mirrored));
        Assert.Equal(expectedRotation, reflection.RotationDeg, 9);
    }

    /// <summary>
    /// The reflections of an order-N axis are evenly spaced between its
    /// rotations, which is what makes a kaleidoscope look like one.
    /// </summary>
    [Fact]
    public void TheReflectionsSitBetweenTheRotations()
    {
        var placements = new SymmetryAxis { AngleDeg = 0, Order = 4, Mirror = true }.Placements();
        var reflections = placements.Where(p => p.Mirrored).Select(p => p.RotationDeg).ToArray();
        // Axis lines at 0, 22.5, 45, 67.5 degrees — doubled to 0, 45, 90, 135.
        Assert.Equal([0, 90, 180, 270], reflections);
    }

    // ---- absent unless used ----------------------------------------------

    /// <summary>
    /// A document painted without symmetry writes no <c>symmetry</c> key.
    /// </summary>
    /// <remarks>
    /// The check <c>CLAUDE.md</c> asks for by name under *"Optional has two
    /// halves"*, and the one that is made by dumping the JSON rather than by
    /// reading the model. The medium block was behaviourally optional and
    /// written anyway — twenty-one keys on every stroke of every document.
    /// </remarks>
    [Fact]
    public void AStrokeDrawnWithoutSymmetrySerializesNoSymmetryKey()
    {
        var doc = DocumentFactory.CreateDoc(100, 100);
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(10, 10, 1), new StrokePoint(20, 20, 1)] });

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"symmetry\"", json);
        Assert.DoesNotContain("\"centerX\"", json);
        Assert.DoesNotContain("\"copyCount\"", json);
        Assert.DoesNotContain("\"isIdentity\"", json);
    }

    /// <summary>
    /// And a stroke that <em>does</em> use it round-trips every field.
    /// </summary>
    /// <remarks>
    /// The other half: absence is only correct if presence still works. A
    /// nullable field nobody serializes passes the test above perfectly.
    /// </remarks>
    [Fact]
    public void AStrokePaintedWithSymmetryKeepsItAcrossASaveAndReload()
    {
        var doc = DocumentFactory.CreateDoc(100, 100);
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(20, 20, 1)],
            Symmetry = new SymmetryAxis
            {
                CenterX = 640, CenterY = 360, AngleDeg = 90, Order = 6, Mirror = true,
            },
        });

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"symmetry\"", json);

        var back = DocJson.Deserialize(json);
        var axis = back!.Scene.Layers[^1].Cels[0].Frame!.Strokes[0].Symmetry;
        Assert.NotNull(axis);
        Assert.Equal(640, axis!.CenterX);
        Assert.Equal(360, axis.CenterY);
        Assert.Equal(90, axis.AngleDeg);
        Assert.Equal(6, axis.Order);
        Assert.True(axis.Mirror);
        Assert.Equal(12, axis.CopyCount);
    }

    /// <summary>Editing an axis never reaches a stroke already painted.</summary>
    [Fact]
    public void CloningAnAxisSeparatesItFromTheOneItCameFrom()
    {
        var original = Vertical(order: 4);
        var copy = original.Clone();
        copy.Order = 9;
        copy.CenterX = -1;

        Assert.Equal(4, original.Order);
        Assert.Equal(100, original.CenterX);
        Assert.Equal(9, copy.Order);
    }
}
