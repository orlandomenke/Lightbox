using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Curves and brush blend modes on disk. Two things to prove: they survive the
/// round trip, and a document that never uses one is byte-for-byte what it was
/// before either existed.
/// </summary>
public class BrushDynamicsSerializationTests
{
    private static Doc WithBrush(Action<BrushSettings> tweak)
    {
        var brush = new BrushSettings();
        tweak(brush);
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        FirstFrame(doc).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Points = [new StrokePoint(1, 1, 1), new StrokePoint(20, 20, 1)],
            Brush = brush,
        });
        return doc;
    }

    private static Frame FirstFrame(Doc doc) => (Frame)doc.Scene.Layers[0].Cels[0].Frame!;

    private static Stroke FirstStroke(Doc doc) => FirstFrame(doc).Strokes[0];

    [Fact]
    public void ACurveRoundTripsThroughTheFile()
    {
        var doc = WithBrush(b => b.Curves = new()
        {
            [BrushDynamic.Size] = new ResponseCurve { Points = [new(0, 0.2), new(0.6, 0.9), new(1, 1)] },
            [BrushDynamic.SmudgeLength] = ResponseCurve.Linear(),
        });

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var curves = FirstStroke(back).Brush.Curves;

        Assert.NotNull(curves);
        Assert.Equal(2, curves!.Count);
        Assert.Equal(0.9, curves[BrushDynamic.Size].Points[1].Out, 1e-9);
        Assert.Equal(0.6, curves[BrushDynamic.Size].Points[1].In, 1e-9);
        Assert.True(curves.ContainsKey(BrushDynamic.SmudgeLength));
    }

    [Fact]
    public void ACurveMeansTheSameThingAfterALoadAsBefore()
    {
        // The property that actually matters. Round-tripping the handles is
        // only useful if the mark comes out the same, so compare the responses
        // rather than the numbers.
        var curve = new ResponseCurve { Points = [new(0, 0), new(0.25, 0.7), new(0.8, 0.75), new(1, 1)] };
        var doc = WithBrush(b => b.Curves = new() { [BrushDynamic.Flow] = curve });

        var back = FirstStroke(DocJson.Deserialize(DocJson.Serialize(doc))).Brush;

        for (var i = 0; i <= 100; i++)
        {
            var p = i / 100.0;
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(curve.Evaluate(p)),
                BitConverter.DoubleToInt64Bits(PressureResponse.Factor(back, BrushDynamic.Flow, p)));
        }
    }

    [Fact]
    public void ABlendModeRoundTrips()
    {
        var doc = WithBrush(b => b.Blend = LayerBlendMode.Multiply);

        Assert.Equal(LayerBlendMode.Multiply, FirstStroke(DocJson.Deserialize(DocJson.Serialize(doc))).Brush.Blend);
    }

    [Fact]
    public void ABrushThatUsesNeitherWritesNeitherKey()
    {
        // Optional means absent. A document drawn with an ordinary brush must
        // not grow keys because a feature it never touched exists.
        var json = DocJson.Serialize(WithBrush(_ => { }));

        Assert.DoesNotContain("\"curves\"", json);
        Assert.DoesNotContain("\"blend\"", json);
    }

    [Fact]
    public void AFileWrittenBeforeCurvesExistedLoadsAndPaintsTheSame()
    {
        // The compatibility claim, from the file's side: a brush with gammas
        // and no curves key comes back with its gammas doing exactly what they
        // did, and no curve invented for it.
        var doc = WithBrush(b =>
        {
            b.PressureSizeGamma = 1.4;
            b.PressureFlowGamma = 0.8;
        });
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"curves\"", json);

        var back = FirstStroke(DocJson.Deserialize(json)).Brush;

        Assert.Null(back.Curves);
        Assert.Null(back.Blend);
        Assert.Equal(LayerBlendMode.Normal, back.BlendOrNormal);
        Assert.Equal(Math.Pow(0.4, 1.4), PressureResponse.Factor(back, BrushDynamic.Size, 0.4), 1e-12);
        Assert.Equal(Math.Pow(0.4, 0.8), PressureResponse.Factor(back, BrushDynamic.Flow, 0.4), 1e-12);
    }
}
