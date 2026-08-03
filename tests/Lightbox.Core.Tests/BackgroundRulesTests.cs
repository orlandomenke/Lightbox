using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Deciding which layers an export leaves out.
/// </summary>
/// <remarks>
/// The policy is tested here, with no pixels: the caller measures coverage and this
/// decides what a measurement means. The precedence between the artist's own pin, the
/// paper flag and detection is the whole of it, and it is the part that will be got
/// wrong by a later edit, so every ordering has a case.
/// </remarks>
public class BackgroundRulesTests
{
    private static Layer Named(string name) => new() { Name = name };

    // ---- the artist's pin, and its absence ---------------------------------------

    [Fact]
    public void ADocumentThatNeverPinsALayerWritesNoKey()
    {
        // The camera's rule. A plain bool would put "omitFromExport": false on every
        // layer of every document ever saved.
        Assert.DoesNotContain("omitFromExport", DocJson.Serialize(new Doc()));
    }

    [Fact]
    public void PinningALayerOutBeatsEveryMode()
    {
        // Including Everything: that mode is a statement about backgrounds, and this
        // is a statement about one layer.
        var layer = new Layer { Name = "Notes", OmitFromExport = true };

        foreach (var mode in Enum.GetValues<BackgroundHandling>())
        {
            Assert.Equal(
                BackgroundSignal.Pinned,
                BackgroundRules.OmissionFor(layer, visible: true, mode, coversCanvas: false));
        }
    }

    [Fact]
    public void PinningALayerInSurvivesDetectionAndThePaperFlag()
    {
        // The backdrop case: the background *is* the asset. Without this, exporting a
        // sky means turning detection off for the whole document.
        var backdrop = new Layer { Name = "Sky", OmitFromExport = false, IsBackground = true };

        Assert.Null(BackgroundRules.OmissionFor(
            backdrop, visible: true, BackgroundHandling.Detected, coversCanvas: true));
        Assert.Null(BackgroundRules.OmissionFor(
            backdrop, visible: true, BackgroundHandling.PaperOnly, coversCanvas: true));
    }

    [Fact]
    public void AHiddenLayerIsReportedRatherThanJustAbsent()
    {
        // It was never going to be exported, but saying so is the point: "why is my
        // shadow missing" has a one-word answer instead of a hunt.
        Assert.Equal(
            BackgroundSignal.Hidden,
            BackgroundRules.OmissionFor(
                Named("Shadow"), visible: false, BackgroundHandling.PaperOnly, false));
    }

    [Fact]
    public void APinnedOutLayerIsReportedAsPinnedEvenWhenHidden()
    {
        // Precedence, and it matters for the message: the artist chose this, so it
        // should not read as though visibility did.
        var layer = new Layer { Name = "Ref", OmitFromExport = true };
        Assert.Equal(
            BackgroundSignal.Pinned,
            BackgroundRules.OmissionFor(layer, visible: false, BackgroundHandling.Detected, false));
    }

    // ---- the modes ---------------------------------------------------------------

    [Fact]
    public void PaperOnlyDropsThePaperLayerAndNothingElse()
    {
        var paper = new Layer { Name = "Background", IsBackground = true };
        var filled = Named("Grey");

        Assert.Equal(
            BackgroundSignal.Paper,
            BackgroundRules.OmissionFor(paper, true, BackgroundHandling.PaperOnly, false));
        // Covers the canvas, and PaperOnly still keeps it — which is exactly the gap
        // Detected exists to close, stated as a test so the default is not mistaken
        // for the whole feature.
        Assert.Null(BackgroundRules.OmissionFor(filled, true, BackgroundHandling.PaperOnly, true));
    }

    [Fact]
    public void DetectedAlsoDropsAFullCanvasFill()
    {
        Assert.Equal(
            BackgroundSignal.FullCanvasFill,
            BackgroundRules.OmissionFor(Named("Grey"), true, BackgroundHandling.Detected, true));
        // And leaves a layer that does not cover the canvas alone, whatever it is
        // called.
        Assert.Null(BackgroundRules.OmissionFor(
            Named("Background sketch"), true, BackgroundHandling.Detected, false));
    }

    [Fact]
    public void EverythingKeepsThePaperLayer()
    {
        var paper = new Layer { Name = "Background", IsBackground = true };
        Assert.Null(BackgroundRules.OmissionFor(paper, true, BackgroundHandling.Everything, true));
    }

    // ---- the advisory signal -----------------------------------------------------

    [Fact]
    public void ANameThatReadsLikeABackgroundIsAdvisoryAndNeverActedOn()
    {
        // The split the design rests on: a name is a guess, and a rule that dropped a
        // layer on a guess would eventually ship a sheet with a layer quietly missing.
        var sky = Named("Sky");

        Assert.Null(BackgroundRules.OmissionFor(sky, true, BackgroundHandling.Detected, false));

        var suspicion = BackgroundRules.SuspicionFor(sky, omission: null);
        Assert.NotNull(suspicion);
        Assert.Equal("Sky", suspicion!.Name);
    }

    [Fact]
    public void NothingIsSuspectedWhenItWasAlreadyOmittedOrPinnedIn()
    {
        // Saying "Background was left out, and by the way Background looks like a
        // background" is noise, and noise is how a warning stops being read.
        Assert.Null(BackgroundRules.SuspicionFor(Named("Background"), BackgroundSignal.Paper));
        Assert.Null(BackgroundRules.SuspicionFor(
            new Layer { Name = "Sky", OmitFromExport = false }, omission: null));
    }

    [Theory]
    [InlineData("Background", true)]
    [InlineData("backdrop", true)]
    [InlineData("BG", true)]
    [InlineData("bg 2", true)]
    [InlineData("Sky", true)]
    [InlineData("Paper", true)]
    // The discriminating cases, and the reason the match is whole-word. "Skyline" is a
    // building; a substring match would call it scenery and advise dropping it.
    [InlineData("Skyline", false)]
    [InlineData("bgroup", false)]
    [InlineData("Character", false)]
    [InlineData("Lineart", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NameMatchingIsWholeWordAndCaseInsensitive(string? name, bool expected)
    {
        Assert.Equal(expected, BackgroundRules.NameLooksLikeBackground(name));
    }

    [Fact]
    public void ThePinRoundTripsThroughTheFile()
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Name = "Ref", OmitFromExport = true });
        doc.Scene.Layers.Add(new Layer { Name = "Sky", OmitFromExport = false });

        var back = DocJson.Clone(doc);

        Assert.True(back.Scene.Layers.First(l => l.Name == "Ref").IsOmittedFromExport);
        var sky = back.Scene.Layers.First(l => l.Name == "Sky");
        Assert.True(sky.IsKeptInExport);
        Assert.False(sky.IsOmittedFromExport);

        // And the convenience getters must not reintroduce the key under a second name.
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("isOmittedFromExport", json);
        Assert.DoesNotContain("isKeptInExport", json);
    }

    [Fact]
    public void TheThresholdLeavesRoomForASoftEdgeWithoutLettingADrawingThrough()
    {
        // 0.999 of a 1920x1080 canvas allows ~2 000 stray pixels — a soft fill edge,
        // not a drawing. Stated as arithmetic because a constant with no sense of
        // scale attached is the kind that gets "tuned" later.
        const int w = 1920, h = 1080;
        var allowed = (1 - BackgroundRules.FullCanvasCoverage) * w * h;

        Assert.InRange(allowed, 1_000, 3_000);
    }
}
