using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Tilt and speed on the stroke record: absent unless recorded, carried when
/// they are.
/// </summary>
/// <remarks>
/// <b>The absence tests are the load-bearing ones.</b> These axes are optional
/// and opt-in, so the promise is not that they work — it is that a document
/// made without them is byte-for-byte the document that would have been made
/// before they existed. <c>CLAUDE.md</c> records this going wrong twice: a
/// medium block that serialized twenty-one keys per stroke for a pass nobody
/// switched on, and a convenience getter that reintroduced under a second name
/// the very key nullability had removed. Both were found by serializing a
/// document and looking, which is what these do.
/// </remarks>
public class PenAxesRecordTests
{
    private static Doc WithStroke(params StrokePoint[] points)
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        FirstFrame(doc).Strokes.Add(new Stroke { Points = [.. points] });
        return doc;
    }

    private static Frame FirstFrame(Doc doc) => (Frame)doc.Scene.Layers[0].Cels[0].Frame!;

    private static StrokePoint FirstPoint(Doc doc) => FirstFrame(doc).Strokes[0].Points[0];

    // ---- absent unless used ----------------------------------------------------

    [Fact]
    public void AStrokeWithoutPenAxesWritesNoneOfTheirKeys()
    {
        var json = DocJson.Serialize(WithStroke(
            new StrokePoint(1, 2, 0.5), new StrokePoint(3, 4, 0.6)));

        Assert.DoesNotContain("\"tiltX\"", json);
        Assert.DoesNotContain("\"tiltY\"", json);
        Assert.DoesNotContain("\"speed\"", json);
    }

    [Fact]
    public void TheDerivedGettersAreNeverSerialized()
    {
        // The BlendOrNormal trap: a public getter beside a nullable field is a
        // property, so without [JsonIgnore] these would write three more keys
        // per point — on every document, including the ones that record no
        // axes at all.
        var json = DocJson.Serialize(WithStroke(new StrokePoint(1, 2, 0.5, 30, -20, 0.4)));

        Assert.DoesNotContain("tiltAltitude", json);
        Assert.DoesNotContain("tiltAzimuth", json);
        Assert.DoesNotContain("hasPenAxes", json);
    }

    [Fact]
    public void ADocumentWithoutAxesIsUnchangedByTheFeatureExisting()
    {
        // The whole promise of "optional means absent": a drawing made by
        // somebody who never asked for tilt has to serialize exactly as it did
        // before the fields were added. Pinned by shape rather than by a
        // recorded byte count, so it survives unrelated schema growth.
        var json = DocJson.Serialize(WithStroke(new StrokePoint(10, 20, 0.75)));
        var after = json[json.IndexOf("\"points\"", StringComparison.Ordinal)..];
        var firstPoint = after[after.IndexOf('{')..(after.IndexOf('}') + 1)];

        // Exactly three keys, named — so an axis creeping back in fails here
        // rather than merely making files bigger somewhere nobody looks.
        var keys = System.Text.RegularExpressions.Regex
            .Matches(firstPoint, "\"([a-zA-Z]+)\"\\s*:")
            .Select(m => m.Groups[1].Value)
            .OrderBy(k => k)
            .ToArray();
        Assert.Equal(["pressure", "x", "y"], keys);
    }

    // ---- present when they are ---------------------------------------------------

    [Fact]
    public void RecordedAxesSurviveARoundTrip()
    {
        var json = DocJson.Serialize(WithStroke(new StrokePoint(1, 2, 0.5, 30, -20, 0.4)));
        var point = FirstPoint(DocJson.Deserialize(json));

        Assert.Equal(30, point.TiltX);
        Assert.Equal(-20, point.TiltY);
        Assert.Equal(0.4, point.Speed);
    }

    [Fact]
    public void ADocumentWrittenBeforeTheAxesExistedStillLoads()
    {
        // The compatibility promise from the other side: a file with no tilt
        // keys is not corrupt, it is a drawing made with a mouse or before the
        // feature. A missing key must read as "not recorded", never as zero —
        // zero means "upright", which is a claim about the pen.
        //
        // Written by serializing a document that records no axes rather than by
        // hand, because a hand-written fixture tests the schema I imagined
        // instead of the one the app writes.
        var old = DocJson.Serialize(WithStroke(new StrokePoint(1, 2, 0.5)));
        Assert.DoesNotContain("tilt", old);
        var point = FirstPoint(DocJson.Deserialize(old));

        Assert.Null(point.TiltX);
        Assert.Null(point.TiltY);
        Assert.Null(point.Speed);
        Assert.False(point.HasPenAxes);
    }

    // ---- what the derived values mean --------------------------------------------

    [Fact]
    public void AnUprightPenIsFlatAndALeaningOneIsNot()
    {
        Assert.Equal(0, new StrokePoint(0, 0, 1, 0, 0).TiltAltitude);
        // 90 degrees on one axis is the pen fully over.
        Assert.Equal(1, new StrokePoint(0, 0, 1, 90, 0).TiltAltitude);
        Assert.Equal(0.5, new StrokePoint(0, 0, 1, 45, 0).TiltAltitude!.Value, 3);
    }

    [Fact]
    public void TheAzimuthPointsWhereTheNibIsLeaning()
    {
        Assert.Equal(0, new StrokePoint(0, 0, 1, 10, 0).TiltAzimuthDeg!.Value, 3);
        Assert.Equal(90, new StrokePoint(0, 0, 1, 0, 10).TiltAzimuthDeg!.Value, 3);
        Assert.Equal(180, Math.Abs(new StrokePoint(0, 0, 1, -10, 0).TiltAzimuthDeg!.Value), 3);
    }

    [Fact]
    public void APointWithNoTiltHasNoDerivedTiltEither()
    {
        // Absent has to stay absent all the way through, or a brush reading the
        // altitude gets a confident 0 — "upright" — for a pen that said nothing.
        var point = new StrokePoint(1, 2, 0.5);
        Assert.Null(point.TiltAltitude);
        Assert.Null(point.TiltAzimuthDeg);
    }

    [Fact]
    public void CloningAStrokeKeepsTheAxes()
    {
        var stroke = new Stroke { Points = [new StrokePoint(1, 2, 0.5, 30, -20, 0.4)] };
        var copy = stroke.Clone();

        Assert.Equal(30, copy.Points[0].TiltX);
        Assert.Equal(0.4, copy.Points[0].Speed);
    }
}
