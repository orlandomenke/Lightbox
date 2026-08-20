using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Fuel burning into heat — what lets a piece of flame that has left the column
/// go on burning instead of going out.
/// </summary>
/// <remarks>
/// <para>
/// <b>The observation this exists to serve came from watching a render</b>, not
/// from a test: real flames shed their tips near the top, and ours did not. The
/// measurement that followed found the shedding was already there and lasting
/// exactly one frame — six sheds over forty frames, every one of them gone by
/// the next drawing. So the assertions below are about a parcel's
/// <em>survival</em> rather than about whether it separates at all.
/// </para>
/// <para>
/// <b>Measured below saturation.</b> Heat accumulates the way alpha does, so the
/// tests read a parcel that has left its source rather than the column, and
/// print both numbers rather than only asserting an inequality.
/// </para>
/// </remarks>
public class CombustionTests(ITestOutputHelper output)
{
    private const int W = 40, H = 40;

    /// <summary>No weather at all, so what is measured is the burning.</summary>
    private static SimParams Still(Combustion? burning = null) => new()
    {
        Buoyancy = 0, Weight = 0, Vorticity = 0, Turbulence = 0,
        Dissipation = 0, Cooling = 0.06, Drag = 0, Burning = burning,
    };

    /// <summary>A parcel of hot fuel on its own, with nothing feeding it.</summary>
    private static FluidSolver Parcel(float heat = 1f, float fuel = 1f)
    {
        var s = new FluidSolver(W, H);
        for (var y = 18; y < 23; y++)
        {
            for (var x = 18; x < 23; x++)
            {
                s.AddDensity(x, y, fuel);
                s.AddHeat(x, y, heat);
            }
        }
        return s;
    }

    [Fact]
    public void A_Parcel_That_Is_Burning_Stays_Hot_Far_Longer_Than_One_That_Is_Only_Cooling()
    {
        var cold = Parcel();
        var lit = Parcel();

        cold.Run(64, Still());
        lit.Run(64, Still(new Combustion()));

        output.WriteLine($"after 64 steps — cooling only {cold.PeakTemperature():F4}, burning {lit.PeakTemperature():F4}");

        // Not merely warmer: the one that is only cooling has effectively gone
        // out, which is the whole complaint this answers.
        Assert.True(cold.PeakTemperature() < 0.05f, $"cooling-only parcel should be spent, was {cold.PeakTemperature():F4}");
        Assert.True(lit.PeakTemperature() > cold.PeakTemperature() * 4);
    }

    [Fact]
    public void Burning_Spends_Its_Fuel_So_It_Puts_Itself_Out()
    {
        var s = Parcel();
        var fuelAtStart = s.TotalDensity();

        var peaks = new List<float>();
        for (var i = 0; i < 12; i++)
        {
            s.Run(8, Still(new Combustion()));
            peaks.Add(s.PeakTemperature());
        }

        output.WriteLine($"fuel {fuelAtStart:F1} -> {s.TotalDensity():F1}; heat by frame: {string.Join(", ", peaks.Select(p => p.ToString("F2")))}");

        // The fuel is consumed...
        Assert.True(s.TotalDensity() < fuelAtStart * 0.8, "burning must consume the density it burns");
        // ...so the reaction is self-limiting rather than a runaway: burning
        // slows the parcel's decline while there is fuel, the fuel runs out, and
        // cooling has it again. A fireball becoming smoke is that arc, which is
        // why `Burst` needs no second mechanism.
        Assert.True(peaks[^1] < peaks[0] * 0.2, $"a parcel must still go out; ended at {peaks[^1]:F3} from {peaks[0]:F3}");
        Assert.True(s.TotalDensity() > 0, "burning out must leave the unburnt fuel behind as smoke");
    }

    [Fact]
    public void Fuel_Below_The_Ignition_Point_Does_Not_Burn()
    {
        // Warm, but not as warm as the threshold asks for — the case that keeps
        // heated smoke from spontaneously catching fire.
        var damp = Parcel(heat: 0.2f);
        var hot = Parcel(heat: 0.8f);
        var settings = Still(new Combustion { Ignition = 0.5 });

        var dampFuel = damp.TotalDensity();
        var hotFuel = hot.TotalDensity();
        damp.Run(24, settings);
        hot.Run(24, settings);

        output.WriteLine($"fuel spent — below ignition {dampFuel - damp.TotalDensity():F4}, above {hotFuel - hot.TotalDensity():F4}");

        Assert.Equal(dampFuel, damp.TotalDensity(), 5);
        Assert.True(hotFuel - hot.TotalDensity() > 0.5, "fuel above the ignition point must actually burn");
    }

    [Fact]
    public void An_Element_That_Does_Not_Burn_Is_Untouched_By_This()
    {
        var a = Parcel();
        var b = Parcel();

        a.Run(32, Still());
        b.Run(32, Still(new Combustion { Rate = 0 }));

        // A rate of zero and no block at all are the same fluid, to the bit.
        Span<float> left = new float[W * H], right = new float[W * H];
        a.ReadTemperature(left);
        b.ReadTemperature(right);
        Assert.True(left.SequenceEqual(right));
    }

    [Fact]
    public void Two_Burning_Solves_Are_Identical()
    {
        var a = Parcel();
        var b = Parcel();
        var settings = Still(new Combustion());

        a.Run(40, settings);
        b.Run(40, settings);

        Span<float> heatA = new float[W * H], heatB = new float[W * H];
        Span<float> fuelA = new float[W * H], fuelB = new float[W * H];
        a.ReadTemperature(heatA);
        b.ReadTemperature(heatB);
        a.ReadDensity(fuelA);
        b.ReadDensity(fuelB);

        Assert.True(heatA.SequenceEqual(heatB));
        Assert.True(fuelA.SequenceEqual(fuelB));
    }

    /// <summary>
    /// The whole point, measured the way the complaint was: how many frames a
    /// detached piece survives on the flame the window actually makes.
    /// </summary>
    [Fact]
    public void A_Detached_Piece_Of_Flame_Survives_More_Frames_When_It_Can_Burn()
    {
        output.WriteLine($"longest shed — cooling only {Longest(null)} frames, burning {Longest(new Combustion())} frames");
        Assert.True(Longest(new Combustion()) > Longest(null));
    }

    /// <summary>Frames the longest-lived detached piece of 5+ cells lasted.</summary>
    private static int Longest(Combustion? burning)
    {
        var element = new SimElement
        {
            Id = "t", Kind = "fire", FrameCount = 40,
            GridWidth = 48, GridHeight = 88, Scale = 4, PreRoll = 16, Substeps = 8,
            BandsFromHeat = true, BandColors = ["#3a1200", "#d95f18", "#ffe9a8"],
            Params = new SimParams { Vorticity = 0.7, Burning = burning },
        };
        element.Emitters.Add(new Emitter
        {
            Shape = EmitterShape.Disc, X = 24, Y = 84, Radius = 4, Density = 1, Heat = 1,
        });

        var solved = new SimBaker().Solve(element);
        var t = LineTreatment.Resolve(null, element.Treatment);
        var level = FieldTracer.LevelOf(0, t.Bands, (float)(element.BandLow * solved.PeakBand),
            (float)(element.BandHigh * solved.PeakBand), t.Spacing);

        int run = 0, longest = 0;
        foreach (var frame in solved.Frames)
        {
            var parts = Pieces(frame.Band, solved.Width, solved.Height, level);
            var loose = parts.Count > 1 && parts.Order().Reverse().Skip(1).Any(p => p >= 5);
            run = loose ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary>Sizes of the connected pieces of field above a level.</summary>
    private static List<int> Pieces(float[] field, int w, int h, float level)
    {
        var seen = new bool[w * h];
        var sizes = new List<int>();
        for (var start = 0; start < w * h; start++)
        {
            if (seen[start] || field[start] <= level) continue;

            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            var n = 0;
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                int x = i % w, y = i / w;
                n++;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var j = ny * w + nx;
                    if (seen[j] || field[j] <= level) continue;
                    seen[j] = true;
                    stack.Push(j);
                }
            }
            sizes.Add(n);
        }
        return sizes;
    }
}
