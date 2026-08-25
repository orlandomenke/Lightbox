using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q143: what a variant wears — the record, its resolution into ephemeral
/// placements, and the absent-until-used serialization.
/// </summary>
public sealed class VariantAttachmentTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-att-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A one-layer scene with a "hand" anchor placed at (40, 50).</summary>
    private static Scene HandAt(double x, double y, double? angle = null, string name = "hand")
    {
        var scene = new Scene { FrameCount = 2, Width = 100, Height = 100 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new Frame() });
        layer.Cels.Add(new Cel { Frame = new Frame() });
        scene.Layers.Add(layer);
        var anchor = Anchors.Declare(scene, name);
        Anchors.SetAcross(layer, 0, 1, anchor.Id, new AnchorPoint(x, y, angle));
        return scene;
    }

    private static VariantAttachment Sword(string anchor = "hand") => new()
    {
        Anchor = anchor,
        SymbolId = "sym_sword",
    };

    [Fact]
    public void AnAttachmentRidesTheAnchorByName()
    {
        // By NAME, not id — ids are per document, and the whole point is one
        // attachment reaching every animation of the subject. The sidecar
        // already made names the cross-document contract.
        var scene = HandAt(40, 50);

        var placement = Assert.Single(VariantAttachments.ResolveAt(scene, 0, [Sword()]));

        Assert.Equal("sym_sword", placement.SymbolId);
        Assert.Equal((40d, 50d), (placement.X, placement.Y));
        Assert.Equal(0, placement.Angle);
    }

    [Fact]
    public void TwoDocumentsWithDifferentAnchorIdsBothDressTheSameAttachment()
    {
        var walk = HandAt(40, 50);
        var run = HandAt(70, 20);
        Assert.NotEqual(walk.Anchors![0].Id, run.Anchors![0].Id);

        var attachment = Sword();
        Assert.Single(VariantAttachments.ResolveAt(walk, 0, [attachment]));
        Assert.Single(VariantAttachments.ResolveAt(run, 0, [attachment]));
    }

    [Fact]
    public void FollowingTheAimTurnsThePlacementAndItsOffset()
    {
        // Anchor aimed straight down (+90 on the y-down canvas): an offset
        // that points along the anchor's +X must point down with it — the
        // hilt stays in the fist as the fist turns.
        var scene = HandAt(40, 50, angle: 90);
        var attachment = Sword();
        attachment.OffsetX = 10;

        var placement = Assert.Single(VariantAttachments.ResolveAt(scene, 0, [attachment]));

        Assert.Equal(40, placement.X, 6);
        Assert.Equal(60, placement.Y, 6);
        Assert.Equal(90, placement.Angle, 6);
    }

    [Fact]
    public void NotFollowingStaysUprightHoweverTheAnchorTurns()
    {
        // The lantern on the swinging arm: position follows, rotation does not.
        var scene = HandAt(40, 50, angle: 90);
        var attachment = Sword();
        attachment.FollowAngle = false;
        attachment.OffsetX = 10;
        attachment.AngleDeg = 15;

        var placement = Assert.Single(VariantAttachments.ResolveAt(scene, 0, [attachment]));

        Assert.Equal((50d, 50d), (placement.X, placement.Y));
        Assert.Equal(15, placement.Angle);
    }

    [Fact]
    public void AbsenceIsTheOffState()
    {
        // An anchor the document does not declare, or declares but does not
        // place on this drawing, dresses nothing — the same rule a hitbox has,
        // and it is what makes "no sword where the hand is off-screen" one
        // Clear-here per drawing rather than a feature.
        var scene = HandAt(40, 50);

        Assert.Empty(VariantAttachments.ResolveAt(scene, 0, [Sword("elbow")]));
        // Frame 1 has the anchor declared but not placed.
        Assert.Empty(VariantAttachments.ResolveAt(scene, 1, [Sword()]));
    }

    [Fact]
    public void ResolutionIsDeterministic()
    {
        // Replayed by the canvas, the exporter and the AI's view of a frame —
        // two calls must be the same placements, and fresh objects each time
        // so nobody can mutate a cached one into the record.
        var scene = HandAt(40, 50, angle: 30);
        var worn = new[] { Sword() };

        var first = VariantAttachments.ResolveAt(scene, 0, worn);
        var second = VariantAttachments.ResolveAt(scene, 0, worn);

        Assert.Equal(
            (first[0].X, first[0].Y, first[0].Angle),
            (second[0].X, second[0].Y, second[0].Angle));
        Assert.NotSame(first[0], second[0]);
    }

    [Fact]
    public void AVariantThatWearsNothingWritesNoAttachmentsKey()
    {
        var project = ProjectIo.Create("Knight", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        ProjectIo.AddVariant(project, knight, "Winter");
        ProjectIo.Save(project);

        var json = File.ReadAllText(Path.Combine(_root, "project.json"));
        Assert.DoesNotContain("\"attachments\"", json);
    }

    [Fact]
    public void WhatAVariantWearsRoundTripsThroughTheProject()
    {
        var project = ProjectIo.Create("Knight", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        (winter.Attachments ??= []).Add(new VariantAttachment
        {
            Anchor = "hand",
            SymbolId = "sym_sword",
            OffsetX = 3,
            Scale = 1.5,
            AngleDeg = -10,
            FollowAngle = false,
        });
        ProjectIo.Save(project);

        var back = ProjectIo.Load(_root);
        var variant = Assert.Single(ProjectFolders.All(back.Manifest)[0].Variants!);
        var worn = Assert.Single(variant.Attachments!);
        Assert.Equal(("hand", "sym_sword", 3d, 1.5, -10d, false),
            (worn.Anchor, worn.SymbolId, worn.OffsetX, worn.Scale, worn.AngleDeg, worn.FollowAngle));
    }
}
