using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// The cascade Q118 chose: an element names a shared treatment and may override
/// fields on top of it. The point of these is that a shared treatment and an
/// override are the <em>same record</em> — there is no mirror type to keep in
/// step, because every field is nullable and the defaults live in one place.
/// </summary>
public class LineTreatmentTests
{
    [Fact]
    public void Nothing_Stated_Anywhere_Resolves_To_The_Defaults()
    {
        var resolved = LineTreatment.Resolve(null);
        Assert.Equal(LineTreatment.Defaults, resolved);
    }

    [Fact]
    public void A_Shared_Treatment_Beats_The_Defaults()
    {
        var shared = new LineTreatment { Bands = 5, BaseWeight = 0.9 };

        var resolved = LineTreatment.Resolve(shared);

        Assert.Equal(5, resolved.Bands);
        Assert.Equal(0.9, resolved.BaseWeight);
        Assert.Equal(LineTreatment.Defaults.Simplify, resolved.Simplify);
    }

    [Fact]
    public void An_Override_Beats_The_Shared_Treatment_Field_By_Field()
    {
        var shared = new LineTreatment { Bands = 5, BaseWeight = 0.9, Offset = 2 };
        var over = new LineTreatment { BaseWeight = 0.2 };

        var resolved = LineTreatment.Resolve(shared, over);

        Assert.Equal(0.2, resolved.BaseWeight);   // stated by the override
        Assert.Equal(5, resolved.Bands);          // falls through to the shared one
        Assert.Equal(2, resolved.Offset);         // …and so does this
        Assert.Equal(LineTreatment.Defaults.Smoothing, resolved.Smoothing);   // stated nowhere
    }

    /// <summary>
    /// Zero is a value, not an absence. A treatment that turns simplification
    /// off must not silently inherit the shared one's setting — which is the
    /// bug a non-nullable double would guarantee.
    /// </summary>
    [Fact]
    public void An_Override_Of_Zero_Still_Wins()
    {
        var shared = new LineTreatment { Simplify = 2, Offset = 3 };
        var over = new LineTreatment { Simplify = 0, Offset = 0 };

        var resolved = LineTreatment.Resolve(shared, over);

        Assert.Equal(0, resolved.Simplify);
        Assert.Equal(0, resolved.Offset);
    }

    [Fact]
    public void An_Override_With_Nothing_In_It_Changes_Nothing()
    {
        var shared = new LineTreatment
        {
            Bands = 4,
            Weights = [new WeightDriver(WeightSource.Curvature, 0.5)],
            Covers = LineCoverage.Facing,
        };

        Assert.Equal(LineTreatment.Resolve(shared), LineTreatment.Resolve(shared, new LineTreatment()));
    }

    /// <summary>
    /// The cascade's one unavoidable cost (Q118): without a way to see which
    /// fields an element states, "two places to look" reads to an artist as the
    /// app being inconsistent rather than as a cascade. This is what the UI
    /// marks and what a per-field revert clears.
    /// </summary>
    [Fact]
    public void A_Treatment_Reports_Exactly_The_Fields_It_States()
    {
        Assert.Empty(new LineTreatment().StatedFields());

        var stated = new LineTreatment { Offset = 0, Bands = 2, Weights = [] }.StatedFields().ToList();

        Assert.Equal(3, stated.Count);
        Assert.Contains(nameof(LineTreatment.Offset), stated);
        Assert.Contains(nameof(LineTreatment.Bands), stated);
        Assert.Contains(nameof(LineTreatment.Weights), stated);
        Assert.DoesNotContain(nameof(LineTreatment.Smoothing), stated);
    }

    [Fact]
    public void Cloning_Copies_The_Drivers_Rather_Than_Sharing_Them()
    {
        var original = new LineTreatment { Weights = [new WeightDriver(WeightSource.Curvature, 0.5)] };

        var copy = original.Clone();
        copy.Weights!.Add(new WeightDriver(WeightSource.ArcTaper, 0.3));

        Assert.Single(original.Weights!);
        Assert.Equal(2, copy.Weights!.Count);
    }

    /// <summary>
    /// Every distance is in stroke-widths and every direction in degrees, so a
    /// treatment means the same thing at any element scale (invariant 7) and
    /// every field is something a person can see in a drawing — which is what
    /// Q118's choice to design for style inference requires.
    /// </summary>
    [Fact]
    public void The_Defaults_Are_A_Plain_Continuous_Line()
    {
        var d = LineTreatment.Defaults;

        Assert.Equal(0, d.Offset);
        Assert.Equal(0, d.BreakLength);
        Assert.Equal(LineCoverage.Full, d.Covers);
        Assert.Empty(d.Weights);
        Assert.Equal(OutlinedBands.Silhouette, d.Outlined);
        Assert.True(d.Simplify is > 0 and < 0.25, $"simplification should hide under the line: {d.Simplify}");
    }
}
