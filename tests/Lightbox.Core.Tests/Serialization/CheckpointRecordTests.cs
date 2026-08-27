using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// The record half of B30's raster checkpoint: what reaches the file, and what
/// makes stored pixels stop being trusted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Invalidation is the half that fails by showing stale art</b>, which this
/// ledger ranks worse than being slow, so most of what is here is about the
/// fingerprint refusing rather than accepting. The design that would have been
/// easier — every edit path calls a "drop the checkpoint" method — fails
/// whenever a new edit path forgets to, and forgetting is silent. Recomputing
/// what the pixels were made from cannot be forgotten.
/// </para>
/// <para>
/// The two acceptance tests matter just as much in the other direction: a
/// fingerprint that changed when it did not need to would be correct and
/// useless, because a checkpoint invalidated on every save is a checkpoint
/// nobody ever gets to use.
/// </para>
/// </remarks>
public class CheckpointRecordTests
{
    private static Doc DocWith(Frame frame)
    {
        var doc = DocumentFactory.CreateDoc(320, 240);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });
        return doc;
    }

    private static Frame FrameOf(int strokes)
    {
        var frame = new Frame();
        for (var i = 0; i < strokes; i++)
        {
            frame.Strokes.Add(new Stroke
            {
                Color = "#334455",
                Points = [new StrokePoint(i, i * 2, 0.5), new StrokePoint(i + 9, i * 2 + 7, 0.8)],
            });
        }
        return frame;
    }

    private static StrokeCheckpoint Stored(Doc doc, Frame frame) => new()
    {
        Strokes = frame.Strokes.Count,
        Fingerprint = CheckpointFingerprint.Of(doc, frame, frame.Strokes.Count),
        PixelsBase64 = "cGl4ZWxz",
        Width = doc.Scene.Width,
        Height = doc.Scene.Height,
    };

    // ---- what reaches the file -----------------------------------------------

    /// <summary>
    /// A document that has never been checkpointed writes no checkpoint key.
    /// </summary>
    /// <remarks>
    /// "Optional means absent, not disabled" — and this is the cheap version of
    /// the check that the <c>optional-settings</c> skill exists to insist on:
    /// dump the JSON and look, rather than read the model and believe.
    /// </remarks>
    [Fact]
    public void ADocumentWithNoCheckpointWritesNoCheckpointKey()
    {
        var json = DocJson.Serialize(DocWith(FrameOf(3)));
        Assert.DoesNotContain("\"checkpoint\"", json);
    }

    [Fact]
    public void ACheckpointSurvivesBeingWrittenAndReadBack()
    {
        var frame = FrameOf(4);
        var doc = DocWith(frame);
        frame.Checkpoint = Stored(doc, frame);

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var back = reloaded.Scene.Layers[0].Cels[0].Frame!;

        Assert.NotNull(back.Checkpoint);
        Assert.Equal(4, back.Checkpoint!.Strokes);
        Assert.Equal(frame.Checkpoint.Fingerprint, back.Checkpoint.Fingerprint);
        Assert.True(CheckpointFingerprint.Matches(reloaded, back, back.Checkpoint));
    }

    /// <summary>
    /// A checkpoint block that will not parse loads as no checkpoint, and the
    /// document opens.
    /// </summary>
    /// <remarks>
    /// B137's rule: derived state that will not read is a slow open, never a
    /// failed one. The strokes are untouched by construction, which is what
    /// makes degrading silently the right answer here and the wrong answer for
    /// anything that is content.
    /// </remarks>
    [Fact]
    public void ACheckpointThatWillNotParseIsDroppedRatherThanFatal()
    {
        var frame = FrameOf(4);
        var json = DocJson.Serialize(DocWith(frame))
            .Replace("\"strokes\":", "\"checkpoint\": 17, \"strokes\":");

        var reloaded = DocJson.Deserialize(json);
        var back = reloaded.Scene.Layers[0].Cels[0].Frame!;

        Assert.Null(back.Checkpoint);
        Assert.Equal(4, back.Strokes.Count);
    }

    /// <summary>A half-written checkpoint is not one, so it is never written out.</summary>
    [Fact]
    public void AnIncompleteCheckpointIsNotWritten()
    {
        var frame = FrameOf(3);
        var doc = DocWith(frame);
        frame.Checkpoint = new StrokeCheckpoint { Strokes = 3, Fingerprint = "abc" };

        Assert.DoesNotContain("\"checkpoint\"", DocJson.Serialize(doc));
    }

    // ---- what the fingerprint accepts ----------------------------------------

    /// <summary>
    /// Painting on does not invalidate — the property the whole feature rests on.
    /// </summary>
    /// <remarks>
    /// A checkpoint covers a <em>prefix</em>. Painting appends, so an artist's
    /// next thousand strokes leave the covered ones exactly as they were, and the
    /// checkpoint keeps earning its keep for the whole session. Were this to fail,
    /// a checkpoint would be valid only until the next mark and the feature would
    /// be a rounding error.
    /// </remarks>
    [Fact]
    public void PaintingOnLeavesTheCoveredPrefixAlone()
    {
        var frame = FrameOf(6);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        frame.Strokes.Add(new Stroke { Points = [new StrokePoint(1, 1, 1)] });
        frame.Strokes.Add(new Stroke { Points = [new StrokePoint(2, 2, 1)] });

        Assert.True(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>
    /// Saving moves the playhead into the document, and that must not invalidate.
    /// </summary>
    /// <remarks>
    /// <b>The trap that would have made this feature never work.</b>
    /// <c>StampPlayhead</c> writes the artist's timeline position into the
    /// document on every save, so a fingerprint over the whole document would
    /// change on the very act that stores the checkpoint — every checkpoint
    /// written would be invalid the moment it was read back, and the failure
    /// would look like the feature simply doing nothing. <c>Doc.RenderShell</c>
    /// takes the playhead out for exactly this reason.
    /// </remarks>
    [Fact]
    public void MovingThePlayheadDoesNotInvalidateAnything()
    {
        var frame = FrameOf(5);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        doc.PlayheadFrame = 42;

        Assert.True(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    // ---- what the fingerprint refuses ----------------------------------------

    [Fact]
    public void EditingACoveredStrokeInvalidatesIt()
    {
        var frame = FrameOf(6);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        frame.Strokes[2].Color = "#ff0000";

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    [Fact]
    public void RemovingAStrokeInvalidatesIt()
    {
        var frame = FrameOf(6);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        frame.Strokes.RemoveAt(5);

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>
    /// Recolouring a swatch reaches art painted with it, so it invalidates.
    /// </summary>
    /// <remarks>
    /// <b>B151's shape, and the case a fingerprint over the strokes alone would
    /// miss.</b> A stroke records the swatch rather than the colour, precisely so
    /// that recolouring the swatch recolours the art — which means pixels change
    /// while every byte of every stroke stays exactly as it was. The fingerprint
    /// covers the document's registries for this reason, by subtraction rather
    /// than by a list somebody has to remember to extend.
    /// </remarks>
    [Fact]
    public void RecolouringASwatchInvalidatesTheArtPaintedWithIt()
    {
        var frame = FrameOf(5);
        var doc = DocWith(frame);
        var swatch = new Swatch { Color = "#112233" };
        doc.Palettes.Add(new Palette { Swatches = { swatch } });
        frame.Strokes[1].SwatchId = swatch.Id;
        var checkpoint = Stored(doc, frame);

        swatch.Color = "#445566";

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>The same argument one registry along.</summary>
    [Fact]
    public void EditingAGradientRampInvalidatesTheStrokesThatUseIt()
    {
        var frame = FrameOf(5);
        var doc = DocWith(frame);
        var gradient = new Gradient();
        doc.Gradients[gradient.Id] = gradient;
        frame.Strokes[0].GradientId = gradient.Id;
        var checkpoint = Stored(doc, frame);

        gradient.Stops[1].Color = "#00ff00";

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>
    /// Undoing past the checkpoint leaves fewer strokes than it covers, and that
    /// is refused before anything is hashed.
    /// </summary>
    [Fact]
    public void ACheckpointCoveringMoreStrokesThanTheDrawingHasIsRefused()
    {
        var frame = FrameOf(6);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        frame.Strokes.RemoveRange(4, 2);

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>Pixels drawn for a different canvas are refused.</summary>
    [Fact]
    public void ResizingTheCanvasInvalidatesIt()
    {
        var frame = FrameOf(5);
        var doc = DocWith(frame);
        var checkpoint = Stored(doc, frame);

        doc.Scene.Width += 40;

        Assert.False(CheckpointFingerprint.Matches(doc, frame, checkpoint));
    }

    /// <summary>
    /// Two drawings with identical strokes hash the same, and that is correct.
    /// </summary>
    /// <remarks>
    /// Worth pinning rather than leaving to chance: the fingerprint answers "do
    /// these pixels describe this record", not "which frame is this". A duplicated
    /// cel really can use its twin's pixels, because they are the same picture —
    /// and nothing keys a checkpoint by frame id, so nothing here needs the two to
    /// differ.
    /// </remarks>
    [Fact]
    public void TwoIdenticalDrawingsFingerprintTheSame()
    {
        var first = FrameOf(4);
        var doc = DocWith(first);
        var second = new Frame { Strokes = first.Strokes.Select(s => s.Clone(newId: false)).ToList() };
        doc.Scene.Layers[0].Cels.Add(new Cel { Frame = second });

        Assert.Equal(
            CheckpointFingerprint.Of(doc, first, 4),
            CheckpointFingerprint.Of(doc, second, 4));
    }
}
