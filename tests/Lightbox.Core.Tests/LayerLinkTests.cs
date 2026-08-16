using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Layer links — a set of layers that are one drawing (Q90).
/// </summary>
/// <remarks>
/// <para>
/// The question that produced this was "how do bones know what to move", and
/// the honest answer was: per stroke, on the current frame, by hand. For a
/// two-layer character over two hundred frames that is four hundred manual
/// binds, and nothing binds a stroke drawn after rigging.
/// </para>
/// <para>
/// A link answers it once, and because it is a property of the layer
/// structure rather than of a drawing it applies to every frame for free.
/// What travels a link is <b>opt-in per property</b>: a link can carry
/// several, one, or none — a link carrying nothing is a legal, useful thing
/// (a named set), and a link made for bones must never start sharing
/// visibility behind the artist's back.
/// </para>
/// </remarks>
public class LayerLinkTests
{
    private static (Doc Doc, Layer Lines, Layer Colour) Character(bool bones = true)
    {
        var doc = DocumentFactory.CreateDoc();
        var link = new LayerLink { Name = "Hero", Bones = bones ? true : null };
        doc.Scene.LayerLinks = [link];
        var colour = new Layer { Name = "Colour", LinkId = link.Id };
        var lines = new Layer { Name = "Lines", LinkId = link.Id };
        doc.Scene.Layers.Add(colour);
        doc.Scene.Layers.Add(lines);
        return (doc, lines, colour);
    }

    [Fact]
    public void ABoneNamedOnOneLayerReachesEveryLayerInTheLink()
    {
        var (doc, lines, colour) = Character();
        colour.BoneId = "arm";

        // The whole point: the artist rigs the drawing once, not once per
        // layer and not once per frame.
        Assert.Equal("arm", doc.Scene.RiggedBoneOf(colour));
        Assert.Equal("arm", doc.Scene.RiggedBoneOf(lines));
        Assert.True(doc.Scene.IsLayerRigged(lines));
    }

    [Fact]
    public void ALayersOwnBindingWinsOverItsLinks()
    {
        var (doc, lines, colour) = Character();
        colour.BoneId = "body";
        lines.BoneId = "head";

        // A link is a default, not an override. An artist who rigged the
        // lines to a different bone meant it, and silently overwriting that
        // would be losing authored work to a convenience.
        Assert.Equal("head", doc.Scene.RiggedBoneOf(lines));
        Assert.Equal("body", doc.Scene.RiggedBoneOf(colour));
    }

    [Fact]
    public void ALinkThatDoesNotCarryBonesRigsNothing()
    {
        var (doc, lines, colour) = Character(bones: false);
        colour.BoneId = "arm";

        // This is the per-property answer in one assertion: linking two layers
        // to hide them together must not rig one of them.
        Assert.Equal("arm", doc.Scene.RiggedBoneOf(colour));
        Assert.Null(doc.Scene.RiggedBoneOf(lines));
        Assert.False(doc.Scene.IsLayerRigged(lines));
    }

    [Fact]
    public void ALinkCanCarryNothingAtAllAndThatIsLegal()
    {
        var (doc, lines, colour) = Character(bones: false);
        colour.BoneId = "arm";
        colour.Visible = false;

        var link = doc.Scene.LinkOf(lines)!;
        Assert.False(link.CarriesBones);
        Assert.False(link.CarriesAlpha);
        Assert.False(link.CarriesVisibility);

        // It is still a link — the members know about each other — it just
        // decides nothing yet.
        Assert.Equal(2, doc.Scene.LinkedWith(lines).Count());
        Assert.Null(doc.Scene.RiggedBoneOf(lines));
        Assert.True(doc.Scene.IsLayerVisible(lines));
    }

    [Fact]
    public void VisibilityTravelsOnlyWhenTheLinkSaysSo()
    {
        var (doc, lines, colour) = Character();
        doc.Scene.LinkOf(lines)!.Visibility = true;

        colour.Visible = false;

        // Hide the colour and the lines go with it — either member hiding
        // hides them all, because there is no primary layer in a link and
        // picking one would make the answer depend on layer order.
        Assert.False(doc.Scene.IsLayerVisible(lines));
        Assert.False(doc.Scene.IsLayerVisible(colour));

        colour.Visible = true;
        Assert.True(doc.Scene.IsLayerVisible(lines));
    }

    [Fact]
    public void AnUnlinkedLayerIsItsOwnCompanyAndIsUnaffected()
    {
        var (doc, lines, _) = Character();
        var loose = new Layer { Name = "Notes" };
        doc.Scene.Layers.Add(loose);

        Assert.Single(doc.Scene.LinkedWith(loose));
        Assert.Null(doc.Scene.LinkOf(loose));
        Assert.Null(doc.Scene.RiggedBoneOf(loose));
        // And the link's members do not include it.
        Assert.DoesNotContain(loose, doc.Scene.LinkedWith(lines));
    }

    [Fact]
    public void TheEmptyStringMeansTheWholeSkeletonRatherThanNoBone()
    {
        var (doc, lines, colour) = Character();
        colour.BoneId = "";

        // Three states of one question, not a bool beside a string: unrigged,
        // rigged to one bone, rigged to the whole skeleton by distance.
        Assert.True(colour.IsRigged);
        Assert.True(colour.FollowsWholeSkeleton);
        Assert.Equal("", doc.Scene.RiggedBoneOf(lines));
        Assert.True(doc.Scene.IsLayerRigged(lines));

        var unrigged = new Layer();
        Assert.False(unrigged.IsRigged);
        Assert.False(unrigged.FollowsWholeSkeleton);
    }

    [Fact]
    public void ALinkSurvivesReorderingWhichIsWhyItIsNotAdjacency()
    {
        var (doc, lines, colour) = Character();
        colour.BoneId = "arm";

        // The reason layer-above/below was declined: dragging a layer between
        // the two must not retarget or break anything.
        doc.Scene.Layers.Insert(1, new Layer { Name = "Interloper" });
        doc.Scene.Layers.Reverse();

        Assert.Equal("arm", doc.Scene.RiggedBoneOf(lines));
        Assert.Equal(2, doc.Scene.LinkedWith(lines).Count());
    }

    [Fact]
    public void ADocumentThatNeverLinkedWritesNoKeys()
    {
        var json = DocJson.Serialize(DocumentFactory.CreateDoc());

        Assert.DoesNotContain("\"layerLinks\"", json);
        Assert.DoesNotContain("\"linkId\"", json);
        Assert.DoesNotContain("\"boneId\"", json);
        // The derived getters must not leak either.
        Assert.DoesNotContain("\"isRigged\"", json);
        Assert.DoesNotContain("\"carriesBones\"", json);
        Assert.DoesNotContain("\"followsWholeSkeleton\"", json);
    }

    [Fact]
    public void ALinkCarryingOneThingWritesOnlyThatThing()
    {
        var (doc, _, colour) = Character();
        colour.BoneId = "arm";

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"bones\": true", json);
        Assert.DoesNotContain("\"alpha\"", json);
        Assert.DoesNotContain("\"visibility\"", json);
    }

    [Fact]
    public void LinksSurviveASaveAndLoad()
    {
        var (doc, lines, colour) = Character();
        colour.BoneId = "arm";
        doc.Scene.LinkOf(lines)!.Alpha = true;

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var link = Assert.Single(back.Scene.LayerLinks!);
        Assert.Equal("Hero", link.Name);
        Assert.True(link.CarriesBones);
        Assert.True(link.CarriesAlpha);
        Assert.False(link.CarriesVisibility);
        Assert.Equal("arm", back.Scene.RiggedBoneOf(back.Scene.Layers.First(l => l.Name == "Lines")));
    }

    [Fact]
    public void CloningADocumentCopiesItsLinks()
    {
        var (doc, _, colour) = Character();
        colour.BoneId = "arm";

        var copy = doc.Clone();
        copy.Scene.LayerLinks![0].Name = "Changed";
        copy.Scene.Layers.First(l => l.Name == "Colour").BoneId = "leg";

        Assert.Equal("Hero", doc.Scene.LayerLinks![0].Name);
        Assert.Equal("arm", colour.BoneId);
    }
}
