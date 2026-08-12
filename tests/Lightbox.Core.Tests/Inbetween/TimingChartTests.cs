using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// The timing chart (Q58): the ladder on an extreme saying where its
/// inbetweens sit. The promises: absent unless authored (no key, no
/// migration), the rungs place the inbetweens exactly, and the intended
/// spacing curve reads the same ladder — one authored object, every consumer.
/// </summary>
public class TimingChartTests
{
    [Fact]
    public void ADocumentWithoutAChartWritesNoKey()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);

        Assert.DoesNotContain("\"chart\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void AChartRoundTripsAndTravelsWithItsClone()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Chart = [0.25, 0.5, 0.75];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var reloaded = back.Scene.Layers[^1].Cels[0].Frame!;
        Assert.Equal([0.25, 0.5, 0.75], reloaded.Chart);

        var copy = frame.Clone();
        copy.Chart![0] = 0.1;
        Assert.Equal(0.25, frame.Chart[0]);   // the clone shares nothing
    }

    [Fact]
    public void NormaliseSortsDedupesAndDropsTheImpossible()
    {
        var chart = TimingChart.Normalise([0.75, 0.25, 0.2500001, -0.2, 0.5, 1.4, 0.0, 1.0]);

        Assert.Equal([0.25, 0.5, 0.75], chart);
        Assert.Null(TimingChart.Normalise([]));
        Assert.Null(TimingChart.Normalise([-1.0, 2.0]));
        Assert.Null(TimingChart.Normalise(null));
    }

    [Fact]
    public void PresetsAreTheEasingTheyName()
    {
        Assert.Equal([0.25, 0.5, 0.75], TimingChart.Even(3));

        var easeIn = TimingChart.FromEasing(3, Easing.EaseIn);
        Assert.True(easeIn[0] < 0.25, $"ease-in bunches rungs at the start, got {easeIn[0]:F3}");
        Assert.True(easeIn[2] < 0.75);

        var easeOut = TimingChart.FromEasing(3, Easing.EaseOut);
        Assert.True(easeOut[0] > 0.25, $"ease-out bunches rungs at the end, got {easeOut[0]:F3}");
    }

    [Fact]
    public void TheChartPlacesTheInbetweensExactlyOnItsRungs()
    {
        // One straight stroke moving 100 units right between the keys, so an
        // inbetween's x IS its t.
        var a = new List<Stroke> { StrokeAt(0) };
        var b = new List<Stroke> { StrokeAt(100) };

        var series = Inbetweener.InbetweenSeries(a, b, [0.2, 0.9]);

        Assert.Equal(2, series.Count);
        Assert.Equal(20, MeanX(series[0]), 1);
        Assert.Equal(90, MeanX(series[1]), 1);
    }

    [Fact]
    public void TheIntendedSpacingReadsTheExtremesOwnChart()
    {
        // Four drawings 30 units apart: key, tween, tween, key. Total travel
        // 90. A chart [0.1, 0.2] says: tiny step, tiny step, huge last step.
        var doc = DocumentFactory.CreateDoc(200, 60, 12);
        var layer = doc.Scene.Layers[^1];
        PutDrawing(layer, 0, 0, FrameRole.Key);
        PutDrawing(layer, 1, 30, FrameRole.Inbetween);
        PutDrawing(layer, 2, 60, FrameRole.Inbetween);
        PutDrawing(layer, 3, 90, FrameRole.Key);
        layer.Cels[0].Frame!.Chart = [0.1, 0.2];

        var intended = SpacingChart.Intended(layer, Easing.Linear);

        Assert.Equal(3, intended.Count);
        Assert.Equal(9, intended[0].Distance, 1);    // 10% of 90
        Assert.Equal(9, intended[1].Distance, 1);    // next 10%
        Assert.Equal(72, intended[2].Distance, 1);   // the remaining 80%
    }

    [Fact]
    public void AChartForTheWrongCountIsIgnoredRatherThanMisread()
    {
        // Two inbetweens in the run, a chart with one rung: the ladder no
        // longer describes this run, so the easing decides as if it were
        // absent — silently redistributing 2 drawings by a 1-rung chart
        // would place them somewhere nobody asked for.
        var doc = DocumentFactory.CreateDoc(200, 60, 12);
        var layer = doc.Scene.Layers[^1];
        PutDrawing(layer, 0, 0, FrameRole.Key);
        PutDrawing(layer, 1, 30, FrameRole.Inbetween);
        PutDrawing(layer, 2, 60, FrameRole.Inbetween);
        PutDrawing(layer, 3, 90, FrameRole.Key);
        layer.Cels[0].Frame!.Chart = [0.5];

        var intended = SpacingChart.Intended(layer, Easing.Linear);

        Assert.Equal(30, intended[0].Distance, 1);   // linear thirds
        Assert.Equal(30, intended[1].Distance, 1);
        Assert.Equal(30, intended[2].Distance, 1);
    }

    private static Stroke StrokeAt(double x)
    {
        var stroke = new Stroke();
        stroke.Points.Add(new StrokePoint(x, 10, 1));
        stroke.Points.Add(new StrokePoint(x + 4, 20, 1));
        return stroke;
    }

    private static double MeanX(IReadOnlyList<Stroke> strokes)
    {
        double sum = 0;
        var n = 0;
        foreach (var s in strokes)
        {
            foreach (var p in s.Points) { sum += p.X; n++; }
        }
        return n == 0 ? 0 : sum / n - 2;   // the stroke's own half-width backed out
    }

    private static void PutDrawing(Layer layer, int index, double x, FrameRole role)
    {
        while (layer.Cels.Count <= index) layer.Cels.Add(new Cel());
        var frame = new Frame { Role = role };
        frame.Strokes.Add(StrokeAt(x));
        layer.Cels[index].Frame = frame;
    }
}
