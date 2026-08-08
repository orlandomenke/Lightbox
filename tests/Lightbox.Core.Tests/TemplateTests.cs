using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Templates — Q12's answer. A template is an ordinary document with a flag,
/// and the rule everything rests on is that <b>a template is copied, never
/// referenced</b>.
/// </summary>
public class TemplateTests
{
    private static Doc Template()
    {
        var doc = new Doc();
        doc.Scene.Layers.Clear();
        doc.Scene.Fps = 24;
        doc.Scene.Layers.Add(new Layer { Name = "BG", Locked = true, Opacity = 1 });
        doc.Scene.Layers.Add(new Layer { Name = "Rough", Opacity = 0.5 });
        Templates.SetTemplate(doc, true);
        return doc;
    }

    private static Stroke Ink(string id) => new()
    {
        Id = id,
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(1, 1, 1), new StrokePoint(9, 9, 1)],
        Brush = new BrushSettings { Size = 4 },
    };

    // ---- the flag, and its absence ---------------------------------------------

    [Fact]
    public void AnOrdinaryDocumentCarriesNoTemplateKeys()
    {
        // "Optional means absent, not defaulted" — the camera's rule. A
        // non-nullable bool would have added isTemplate to every document ever
        // saved, which is exactly the defect the repo has been bitten by before.
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"isTemplate\"", json);
        Assert.DoesNotContain("\"templateId\"", json);
        // And the convenience getter must not reintroduce the key under a
        // second name.
        Assert.DoesNotContain("\"isTemplateDocument\"", json);
    }

    [Fact]
    public void TheFlagRoundTripsAndClearingItRemovesTheKey()
    {
        var doc = new Doc();
        Templates.SetTemplate(doc, true);
        Assert.Contains("\"isTemplate\"", DocJson.Serialize(doc));
        Assert.True(DocJson.Clone(doc).IsTemplateDocument);

        Templates.SetTemplate(doc, false);
        Assert.DoesNotContain("\"isTemplate\"", DocJson.Serialize(doc));
        Assert.False(doc.IsTemplateDocument);
    }

    // ---- new from template -----------------------------------------------------

    [Fact]
    public void ANewDocumentFromATemplateIsACopyNotALink()
    {
        var template = Template();
        template.Scene.Layers[0].Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("s1") } } });

        var copy = Templates.NewFromTemplate(template, "docref-1");

        // A copy, with the same shape...
        Assert.Equal(2, copy.Scene.Layers.Count);
        Assert.Equal(24, copy.Scene.Fps);
        // ...but not a template itself, or every animation started from one
        // would show up in the template list.
        Assert.False(copy.IsTemplateDocument);
        Assert.Equal("docref-1", copy.TemplateId);
        // Layer ids are kept: that is what makes the pull match by fact rather
        // than by name similarity.
        Assert.Equal(
            template.Scene.Layers.Select(l => l.Id),
            copy.Scene.Layers.Select(l => l.Id));
        // Nothing is shared by reference.
        Assert.NotSame(template.Scene.Layers[0], copy.Scene.Layers[0]);
    }

    [Fact]
    public void EditingATemplateLeavesEarlierCopiesAlone()
    {
        // The test the whole design rests on. If a copy were ever linked, this
        // definition collapses into a worse symbol.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "docref-1");

        template.Scene.Layers[0].Name = "Renamed in the template";
        template.Scene.Layers.Add(new Layer { Name = "Added later" });
        template.Scene.Fps = 6;

        Assert.Equal("BG", copy.Scene.Layers[0].Name);
        Assert.Equal(2, copy.Scene.Layers.Count);
        Assert.Equal(24, copy.Scene.Fps);
    }

    [Fact]
    public void ACopySurvivesTheTemplateBeingDeleted()
    {
        // TemplateId is a name, not a reference: the copy is complete on its own
        // and nothing about rendering it consults the template.
        var template = Template();
        template.Scene.Layers[0].Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("s1") } } });
        var copy = Templates.NewFromTemplate(template, "docref-1");

        var reloaded = DocJson.Clone(copy);   // as if opened with no project at all

        Assert.Equal(2, reloaded.Scene.Layers.Count);
        Assert.Single(((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes);
        Assert.Equal("docref-1", reloaded.TemplateId);
    }

    [Fact]
    public void TheProjectListsItsTemplatesApartFromItsAnimations()
    {
        var project = ProjectIo.Create("Knight", "/tmp/does-not-need-to-exist.lbproj");
        var character = ProjectFolders.Add(project.Manifest, "Knight");
        var plain = ProjectIo.AddDocument(project, "walk", new Doc(), character);
        var template = ProjectIo.AddDocument(project, "walk-start", Template(), character);

        var found = Templates.InProject(project);

        Assert.Single(found);
        Assert.Equal(template.Id, found[0].Id);
        Assert.DoesNotContain(plain.Id, found.Select(f => f.Id));
    }

    // ---- the pull: preview -----------------------------------------------------

    [Fact]
    public void ThePreviewFindsLayersTheTemplateHasAndTheDocumentDoesNot()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        template.Scene.Layers.Add(new Layer { Name = "Clean" });

        var preview = Templates.Preview(copy, template);

        Assert.True(preview.Any);
        Assert.Single(preview.NewLayers);
        Assert.Equal("Clean", preview.NewLayers[0].Layer.Name);
        Assert.Equal(2, preview.NewLayers[0].Index);
    }

    [Fact]
    public void APullNeverRemovesALayerTheArtistHas()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        copy.Scene.Layers.Add(new Layer { Name = "Mine" });
        template.Scene.Layers.RemoveAt(1);

        Templates.Apply(copy, template, new Templates.PullOptions());

        Assert.Contains("Mine", copy.Scene.Layers.Select(l => l.Name));
        Assert.Contains("Rough", copy.Scene.Layers.Select(l => l.Name));
    }

    [Fact]
    public void NothingToPullReportsNothing()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");

        var preview = Templates.Preview(copy, template);

        Assert.False(preview.Any);
        Assert.Equal(0, Templates.Apply(copy, template, new Templates.PullOptions()));
    }

    // ---- the pull: the drawn-on rule -------------------------------------------

    [Fact]
    public void ALayerTheArtistHasDrawnOnIsSkippedUnlessTicked()
    {
        // The load-bearing clause. A pull must not quietly rename or re-blend a
        // layer somebody has been painting on.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        var id = copy.Scene.Layers[1].Id;
        copy.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("mine") } } });
        template.Scene.Layers[1].Name = "Rough (renamed)";

        var preview = Templates.Preview(copy, template);
        Assert.Single(preview.LayerChanges);
        Assert.True(preview.LayerChanges[0].DrawnOnSince);

        // Skipped by default...
        Templates.Apply(copy, template, new Templates.PullOptions());
        Assert.Equal("Rough", copy.Scene.Layers[1].Name);

        // ...and applied when the artist says so.
        Templates.Apply(copy, template, new Templates.PullOptions(
            DrawnOnLayerIds: new HashSet<string> { id }));
        Assert.Equal("Rough (renamed)", copy.Scene.Layers[1].Name);
    }

    [Fact]
    public void AnUntouchedLayerTakesThePropertiesWithoutBeingTicked()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        template.Scene.Layers[1].Name = "Roughs";
        template.Scene.Layers[1].Opacity = 0.25;
        template.Scene.Layers[1].BlendMode = LayerBlendMode.Multiply;
        template.Scene.Layers[1].Locked = true;

        Templates.Apply(copy, template, new Templates.PullOptions());

        Assert.Equal("Roughs", copy.Scene.Layers[1].Name);
        Assert.Equal(0.25, copy.Scene.Layers[1].Opacity);
        Assert.Equal(LayerBlendMode.Multiply, copy.Scene.Layers[1].BlendMode);
        Assert.True(copy.Scene.Layers[1].Locked);
    }

    [Fact]
    public void ErasingCountsAsDrawnOnToo()
    {
        // Comparing stroke ids rather than counts is what catches the artist who
        // deleted one stroke and drew another — a count would call that unchanged.
        var template = Template();
        template.Scene.Layers[1].Cels.Add(new Cel
        {
            Frame = new Frame { Strokes = { Ink("a"), Ink("b") } },
        });
        var copy = Templates.NewFromTemplate(template, "t");
        var frame = (Frame)copy.Scene.Layers[1].Cels[0].Frame!;
        frame.Strokes.RemoveAt(0);
        frame.Strokes.Add(Ink("c"));
        template.Scene.Layers[1].Name = "Renamed";

        var preview = Templates.Preview(copy, template);

        Assert.True(preview.LayerChanges[0].DrawnOnSince);
    }

    [Fact]
    public void ImportedPixelsCountAsWorkEvenWithNoStrokes()
    {
        // A layer whose art arrived as a baseline PNG has no stroke provenance
        // to compare. Treating it as untouched would let a pull walk over it.
        var template = Template();
        template.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame() });
        var copy = Templates.NewFromTemplate(template, "t");
        ((Frame)copy.Scene.Layers[1].Cels[0].Frame!).PngBase64 = "aGVsbG8=";
        template.Scene.Layers[1].Name = "Renamed";

        Assert.True(Templates.Preview(copy, template).LayerChanges[0].DrawnOnSince);
    }

    // ---- the pull: what it moves and what it must not --------------------------

    [Fact]
    public void ANewLayerArrivesWithItsDrawings()
    {
        // A deliberate refinement of "never pulls drawings": nothing can be
        // overwritten by a layer that is not there, and the case the design names
        // — construction guides on their own locked layer — is worthless empty.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        var guides = new Layer { Name = "Construction", Locked = true };
        guides.Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("cross") } } });
        template.Scene.Layers.Add(guides);

        Templates.Apply(copy, template, new Templates.PullOptions());

        var landed = copy.Scene.Layers.Single(l => l.Name == "Construction");
        Assert.Single(((Frame)landed.Cels[0].Frame!).Strokes);
        // Deep-copied: editing the template's copy must not reach the document.
        Assert.NotSame(guides.Cels[0].Frame, landed.Cels[0].Frame);
    }

    [Fact]
    public void APullNeverTouchesDrawingsOnALayerTheDocumentAlreadyHas()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        copy.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("mine") } } });
        template.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink("theirs") } } });

        Templates.Apply(copy, template, new Templates.PullOptions(
            DrawnOnLayerIds: new HashSet<string> { copy.Scene.Layers[1].Id }));

        var strokes = ((Frame)copy.Scene.Layers[1].Cels[0].Frame!).Strokes;
        Assert.Equal("mine", Assert.Single(strokes).Id);
    }

    [Fact]
    public void APullNeverTouchesTheExposureSheet()
    {
        // A template's timing is what a timing preset is for — which is exactly
        // why Q11 and Q12 stayed separate mechanisms.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        for (var i = 0; i < 4; i++) copy.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame() });
        template.Scene.Layers[1].Cels.Add(new Cel { Frame = new Frame() });
        template.Scene.FrameCount = 99;
        template.Scene.Layers[1].Name = "Renamed";

        Templates.Apply(copy, template, new Templates.PullOptions(
            DrawnOnLayerIds: new HashSet<string> { copy.Scene.Layers[1].Id }));

        Assert.Equal(4, copy.Scene.Layers[1].Cels.Count);
        Assert.NotEqual(99, copy.Scene.FrameCount);
    }

    [Fact]
    public void GuidesAreReplacedWholesaleAndGridsComeWithThem()
    {
        // A grid is a GuideKind.Grid guide rather than a thing of its own, so
        // "guides and grids" is one list.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        copy.Scene.Guides = [new Guide { Kind = GuideKind.Line, Y = 10 }];
        template.Scene.Guides =
        [
            new Guide { Kind = GuideKind.Line, Y = 400 },
            new Guide { Kind = GuideKind.Grid, Spacing = 64 },
        ];

        Templates.Apply(copy, template, new Templates.PullOptions());

        Assert.Equal(2, copy.Scene.Guides!.Count);
        Assert.Contains(GuideKind.Grid, copy.Scene.Guides.Select(g => g.Kind));
        Assert.NotSame(template.Scene.Guides[0], copy.Scene.Guides[0]);
    }

    [Fact]
    public void FpsIsPulledAndSizeIsNot()
    {
        // Changing a canvas under finished drawings is a different operation
        // with its own questions.
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        template.Scene.Fps = 8;
        template.Scene.Width = 4096;
        template.Scene.Height = 2160;

        var preview = Templates.Preview(copy, template);
        Templates.Apply(copy, template, new Templates.PullOptions());

        Assert.True(preview.FpsDiffers);
        Assert.Equal(8, copy.Scene.Fps);
        Assert.Equal(960, copy.Scene.Width);
        Assert.Equal(540, copy.Scene.Height);
    }

    [Fact]
    public void ACameraIsAddedWhenAbsentAndNeverOverwritten()
    {
        var template = Template();
        template.Scene.Camera = new Camera();
        var copy = Templates.NewFromTemplate(template, "t");
        copy.Scene.Camera = null;

        Assert.True(Templates.Preview(copy, template).CameraAvailable);
        Templates.Apply(copy, template, new Templates.PullOptions());
        Assert.NotNull(copy.Scene.Camera);
        Assert.NotSame(template.Scene.Camera, copy.Scene.Camera);

        // Now it has one of its own, so the template has nothing to offer.
        var mine = copy.Scene.Camera;
        Assert.False(Templates.Preview(copy, template).CameraAvailable);
        Templates.Apply(copy, template, new Templates.PullOptions());
        Assert.Same(mine, copy.Scene.Camera);
    }

    [Fact]
    public void EachPartOfThePullCanBeDeclined()
    {
        var template = Template();
        var copy = Templates.NewFromTemplate(template, "t");
        template.Scene.Layers.Add(new Layer { Name = "Clean" });
        template.Scene.Fps = 8;
        template.Scene.Guides = [new Guide { Kind = GuideKind.Line }];

        var changed = Templates.Apply(copy, template, new Templates.PullOptions(
            NewLayers: false, LayerProperties: false, Guides: false, Fps: false, Camera: false));

        Assert.Equal(0, changed);
        Assert.Equal(2, copy.Scene.Layers.Count);
        Assert.Equal(24, copy.Scene.Fps);
        Assert.Null(copy.Scene.Guides);
    }

    [Fact]
    public void APullIntoADocumentThatIsNotACopyStillBehaves()
    {
        // Nothing stops an artist pointing an unrelated document at a template.
        // Every layer is then new, which is the honest answer rather than a throw.
        var template = Template();
        var stranger = new Doc();
        stranger.Scene.Layers.Clear();

        var preview = Templates.Preview(stranger, template);
        Templates.Apply(stranger, template, new Templates.PullOptions());

        Assert.Equal(2, preview.NewLayers.Count);
        Assert.Equal(2, stranger.Scene.Layers.Count);
    }
}
