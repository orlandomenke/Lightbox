using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Where a baked element lands in the document, and what survives a re-bake.
/// Deliberately free of any solver or tracer: this is only about placement.
/// </summary>
public class SimBakeOpsTests
{
    private static SimElement Element(int first = 0, int count = 4, int exposeOn = 1) => new()
    {
        Id = "sim1",
        Kind = "fire",
        FirstFrame = first,
        FrameCount = count,
        ExposeOn = exposeOn,
    };

    private static Stroke Baked(string simId, double x = 1) => new()
    {
        SimId = simId,
        Points = [new StrokePoint(x, 1, 1), new StrokePoint(x + 5, 9, 1)],
    };

    private static Stroke Drawn(double x = 1) => new()
    {
        Points = [new StrokePoint(x, 1, 1), new StrokePoint(x + 5, 9, 1)],
    };

    private static List<(int, List<Stroke>)> OnePerFrame(SimElement element, int drawings) =>
        [.. Enumerable.Range(0, drawings)
            .Select(i => (element.FirstFrame + i * element.ExposeOn, new List<Stroke> { Baked(element.Id, i) }))];

    // ---- the element's own layer -----------------------------------------------

    [Fact]
    public void An_Element_Gets_A_Layer_Of_Its_Own_Named_After_It()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element();
        element.Name = "torch";
        var before = doc.Scene.Layers.Count;

        var layer = SimBakeOps.LayerFor(doc, element);

        Assert.Equal(before + 1, doc.Scene.Layers.Count);
        Assert.Equal("torch", layer.Name);
        Assert.Equal("sim1", layer.SimId);
    }

    [Fact]
    public void Baking_Again_Reuses_The_Layer_Rather_Than_Making_A_Second()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element();

        var first = SimBakeOps.LayerFor(doc, element);
        var again = SimBakeOps.LayerFor(doc, element);

        Assert.Same(first, again);
    }

    /// <summary>
    /// Matched by id rather than by name, because renaming a layer is an
    /// ordinary thing to do and orphaning the element would show up as the next
    /// bake quietly making a second layer.
    /// </summary>
    [Fact]
    public void Renaming_The_Layer_Does_Not_Orphan_The_Element()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element();
        SimBakeOps.LayerFor(doc, element).Name = "FX — big flame";

        var again = SimBakeOps.LayerFor(doc, element);

        Assert.Equal("FX — big flame", again.Name);
        Assert.Single(doc.Scene.Layers.Where(l => l.SimId == "sim1"));
    }

    // ---- applying ----------------------------------------------------------------

    [Fact]
    public void Applying_Puts_A_Drawing_On_Every_Frame_The_Element_Covers()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(first: 2, count: 4);

        SimBakeOps.Apply(doc, element, OnePerFrame(element, 4));

        var layer = SimBakeOps.LayerFor(doc, element);
        Assert.True(doc.Scene.FrameCount >= 6, $"the scene stopped at {doc.Scene.FrameCount}");
        Assert.Null(layer.Cels[0].Frame);
        Assert.Null(layer.Cels[1].Frame);
        for (var f = 2; f < 6; f++)
        {
            Assert.Single(((Frame)layer.Cels[f].Frame!).Strokes);
        }
    }

    /// <summary>
    /// A hold is one <see cref="Frame"/> standing in several cels — which is how
    /// the timeline already says "held", so an element exposed on 2s reads as an
    /// animator's hold rather than as two identical drawings.
    /// </summary>
    [Fact]
    public void Exposing_On_Twos_Makes_A_Real_Hold()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(first: 0, count: 6, exposeOn: 2);

        SimBakeOps.Apply(doc, element, OnePerFrame(element, 3));

        var cels = SimBakeOps.LayerFor(doc, element).Cels;
        Assert.Same(cels[0].Frame, cels[1].Frame);
        Assert.Same(cels[2].Frame, cels[3].Frame);
        Assert.NotSame(cels[1].Frame, cels[2].Frame);
    }

    /// <summary>
    /// The one case a hold cannot be shared: pouring one cel's hand-drawn work
    /// into the next is a far worse bug than a duplicated stroke list.
    /// </summary>
    [Fact]
    public void A_Hold_Is_Not_Shared_Over_Cels_That_Already_Hold_Hand_Work()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(first: 0, count: 4, exposeOn: 2);
        var layer = SimBakeOps.LayerFor(doc, element);
        layer.Cels.Add(new Cel());
        layer.Cels.Add(new Cel { Frame = new Frame { Strokes = { Drawn(40) } } });

        SimBakeOps.Apply(doc, element, OnePerFrame(element, 2));

        Assert.NotSame(layer.Cels[0].Frame, layer.Cels[1].Frame);
        Assert.Single(((Frame)layer.Cels[0].Frame!).Strokes);
        Assert.Equal(2, ((Frame)layer.Cels[1].Frame!).Strokes.Count);   // the drawn one, plus a copy
    }

    // ---- re-baking -----------------------------------------------------------------

    [Fact]
    public void Re_Baking_Replaces_What_The_Element_Made()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(count: 3);

        SimBakeOps.Apply(doc, element, OnePerFrame(element, 3));
        SimBakeOps.Apply(doc, element, OnePerFrame(element, 3));

        var layer = SimBakeOps.LayerFor(doc, element);
        for (var f = 0; f < 3; f++)
        {
            Assert.Single(((Frame)layer.Cels[f].Frame!).Strokes);
        }
    }

    [Fact]
    public void Re_Baking_Leaves_Another_Elements_Work_Alone()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var mine = Element(count: 2);
        var theirs = new SimElement { Id = "sim2", FirstFrame = 0, FrameCount = 2 };

        SimBakeOps.Apply(doc, mine, OnePerFrame(mine, 2));
        SimBakeOps.Apply(doc, theirs, OnePerFrame(theirs, 2));
        SimBakeOps.Apply(doc, mine, OnePerFrame(mine, 2));

        Assert.Single(((Frame)SimBakeOps.LayerFor(doc, theirs).Cels[0].Frame!).Strokes);
    }

    /// <summary>
    /// Q123's whole answer to "what happens to hand edits": touch a baked stroke
    /// and it becomes yours, so a re-bake leaves it alone. No dialog, no merge,
    /// nothing to warn about.
    /// </summary>
    [Fact]
    public void A_Stroke_The_Artist_Touched_Survives_A_Re_Bake()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(count: 2);
        SimBakeOps.Apply(doc, element, OnePerFrame(element, 2));

        var layer = SimBakeOps.LayerFor(doc, element);
        var touched = ((Frame)layer.Cels[0].Frame!).Strokes[0];
        SimBakeOps.Detach(touched);
        touched.Points[0] = new StrokePoint(99, 99, 1);

        SimBakeOps.Apply(doc, element, OnePerFrame(element, 2));

        var strokes = ((Frame)layer.Cels[0].Frame!).Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.Contains(strokes, s => s.SimId is null && s.Points[0].X == 99);
        Assert.Contains(strokes, s => s.SimId == "sim1");
    }

    [Fact]
    public void Clearing_Counts_A_Held_Drawing_Once()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(first: 0, count: 4, exposeOn: 2);
        SimBakeOps.Apply(doc, element, OnePerFrame(element, 2));

        Assert.Equal(2, SimBakeOps.Clear(doc, element));
    }

    [Fact]
    public void Clearing_Leaves_Hand_Drawn_Work_Where_It_Is()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var element = Element(count: 1);
        var layer = SimBakeOps.LayerFor(doc, element);
        layer.Cels.Add(new Cel { Frame = new Frame { Strokes = { Drawn(), Baked("sim1"), Drawn(20) } } });

        Assert.Equal(1, SimBakeOps.Clear(doc, element));
        Assert.Equal(2, ((Frame)layer.Cels[0].Frame!).Strokes.Count);
    }

    // ---- the record -------------------------------------------------------------------

    [Fact]
    public void A_Layer_Nobody_Generated_Writes_No_SimId()
    {
        Assert.DoesNotContain("\"simId\"", DocJson.Serialize(DocumentFactory.CreateDoc(120, 80, 12)));
    }

    [Fact]
    public void A_Generated_Layer_Keeps_Its_Element_Across_A_Round_Trip()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        SimBakeOps.LayerFor(doc, Element());

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Contains(back.Scene.Layers, l => l.SimId == "sim1");
    }
}
