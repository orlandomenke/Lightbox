using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The DragonBones converter: a view of the rig schema, written to the
/// stable core of the 5.5 format. Structure is asserted off the parsed JSON —
/// the file is for other people's importers, so its shape is the contract.
/// </summary>
public class DragonBonesConvertTests
{
    private static Doc Rigged()
    {
        var parent = new Bone { Name = "arm", X = 100, Y = 100, RotationDeg = 10, Length = 50 };
        var child = new Bone { Name = "hand", ParentId = parent.Id, X = 50, Length = 30 };
        var doc = new Doc { Armature = new Armature { Bones = [parent, child] } };
        doc.Scene.Fps = 24;
        doc.Scene.FrameCount = 4;
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0 },
                new PoseKey { Frame = 3, Bones = { [parent.Id] = new BonePose { RotationDeg = 45 } } },
            ],
        };
        return doc;
    }

    private static JsonElement Convert(Doc doc)
    {
        var rig = RigExport.Build(doc);
        return JsonDocument.Parse(DragonBonesConvert.Convert(rig, "character")).RootElement;
    }

    [Fact]
    public void TheSkeletonFileCarriesTheFormatsCore()
    {
        var root = Convert(Rigged());

        Assert.Equal("5.5", root.GetProperty("version").GetString());
        Assert.Equal(24, root.GetProperty("frameRate").GetInt32());

        var armature = root.GetProperty("armature").EnumerateArray().Single();
        Assert.Equal("Armature", armature.GetProperty("type").GetString());

        var bones = armature.GetProperty("bone").EnumerateArray().ToList();
        Assert.Equal(2, bones.Count);
        Assert.Equal("arm", bones[0].GetProperty("name").GetString());
        Assert.False(bones[0].TryGetProperty("parent", out _), "a root bone writes no parent key");
        Assert.Equal("arm", bones[1].GetProperty("parent").GetString());
        // A rigid bone's rest rotation is the skX/skY pair, equal.
        var transform = bones[0].GetProperty("transform");
        Assert.Equal(10, transform.GetProperty("skX").GetDouble(), 9);
        Assert.Equal(transform.GetProperty("skX").GetDouble(), transform.GetProperty("skY").GetDouble(), 12);

        var animation = armature.GetProperty("animation").EnumerateArray().Single();
        Assert.Equal(4, animation.GetProperty("duration").GetInt32());
        Assert.Equal(0, animation.GetProperty("playTimes").GetInt32());
    }

    [Fact]
    public void TheAnimationIsOffsetsFromRestOnePerFrame()
    {
        var root = Convert(Rigged());
        var armature = root.GetProperty("armature").EnumerateArray().Single();
        var timeline = armature.GetProperty("animation").EnumerateArray().Single()
            .GetProperty("bone").EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == "arm");

        var rotate = timeline.GetProperty("rotateFrame").EnumerateArray().ToList();
        Assert.Equal(4, rotate.Count);
        // Frame 0 sits at rest: a zero offset, not the absolute 10°.
        Assert.Equal(0, rotate[0].GetProperty("rotate").GetDouble(), 6);
        // The last key asks for +45° from rest, and the offset says exactly that.
        Assert.Equal(45, rotate[3].GetProperty("rotate").GetDouble(), 6);
        Assert.All(rotate, f => Assert.Equal(1, f.GetProperty("duration").GetInt32()));
    }

    [Fact]
    public void MotionOnlyNoSlotsSkinsOrDisplays()
    {
        var root = Convert(Rigged());
        var armature = root.GetProperty("armature").EnumerateArray().Single();

        // Pixels travel by sprite sheet; this file is the motion. Writing
        // empty slot/skin blocks would be surface for mixed-quality importers
        // to disagree about, so they are absent rather than empty.
        Assert.False(armature.TryGetProperty("slot", out _));
        Assert.False(armature.TryGetProperty("skin", out _));
    }

    [Fact]
    public void AccumulatedTurnsFoldIntoTheShortWayRound()
    {
        Assert.Equal(0, DragonBonesConvert.Normalized(360), 9);
        Assert.Equal(-90, DragonBonesConvert.Normalized(270), 9);
        Assert.Equal(170, DragonBonesConvert.Normalized(-190), 9);
        Assert.Equal(180, DragonBonesConvert.Normalized(180), 9);
    }
}
