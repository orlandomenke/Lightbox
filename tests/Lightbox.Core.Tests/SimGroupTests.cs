using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Several elements that are one effect.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is under test is that a group changes nothing about how an element
/// bakes.</b> A group carries no geometry — it moves and retimes by writing the
/// members' own records — so every one of these leaves a document that would
/// bake identically to one edited element by element. That property is what
/// lets the whole bake path stay ignorant of groups, and it is the thing that
/// breaks first if somebody later adds an offset to the group record.
/// </para>
/// </remarks>
public class SimGroupTests(ITestOutputHelper output)
{
    private static SimElement Element(string id, double x, double y, int first, int frames = 24)
    {
        var e = new SimElement
        {
            Id = id, Kind = "smoke", OriginX = x, OriginY = y,
            FirstFrame = first, FrameCount = frames,
        };
        e.Emitters.Add(new Emitter { Id = $"em_{id}", X = 10, Y = 10, Radius = 3 });
        return e;
    }

    private static (Doc Doc, SimGroup Group) Explosion()
    {
        var doc = new Doc { Sims = [] };
        var flash = Element("flash", 100, 200, 10, 8);
        var fire = Element("fire", 100, 200, 11, 16);
        var smoke = Element("smoke", 90, 190, 14, 30);
        foreach (var e in new[] { flash, fire, smoke }) doc.Sims![e.Id] = e;

        return (doc, SimGroupOps.Create(doc, "Explosion", [flash, fire, smoke]));
    }

    // ---- the record ---------------------------------------------------------------

    /// <summary>
    /// A document where nobody has grouped anything writes no key, which is the
    /// half of "optional" that is easy to miss.
    /// </summary>
    [Fact]
    public void Grouping_Nothing_Writes_Nothing()
    {
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["a"] = Element("a", 0, 0, 0) } };

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"sims\"", json);
        Assert.DoesNotContain("\"simGroups\"", json);
        Assert.DoesNotContain("\"layerGroupId\"", json);

        var (grouped, _) = Explosion();
        Assert.Contains("\"simGroups\"", DocJson.Serialize(grouped));
    }

    [Fact]
    public void A_Group_Survives_A_Round_Trip()
    {
        var (doc, group) = Explosion();

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var restored = Assert.Single(back.SimGroups!.Values);
        Assert.Equal("Explosion", restored.Name);
        Assert.Equal(group.ElementIds, restored.ElementIds);
        Assert.Equal(3, SimGroupOps.Members(back, restored).Count);
    }

    /// <summary>
    /// Cloning has to copy the membership list, or an undo snapshot and the live
    /// document share it and adding a member rewrites history.
    /// </summary>
    [Fact]
    public void Cloning_A_Document_Copies_Its_Groups()
    {
        var (doc, _) = Explosion();

        var copy = doc.Clone();
        copy.SimGroups!.Values.Single().ElementIds.Add("intruder");

        Assert.Equal(3, doc.SimGroups!.Values.Single().ElementIds.Count);
    }

    [Fact]
    public void An_Element_Belongs_To_One_Group_At_A_Time()
    {
        var (doc, first) = Explosion();
        var smoke = doc.Sims!["smoke"];

        var second = SimGroupOps.Create(doc, "Chimney", [smoke]);

        Assert.DoesNotContain("smoke", first.ElementIds);
        Assert.Contains("smoke", second.ElementIds);
        Assert.Same(second, doc.GroupOf(smoke));
    }

    // ---- moving and retiming --------------------------------------------------------

    [Fact]
    public void Moving_A_Group_Moves_Every_Member_And_Keeps_Their_Spacing()
    {
        var (doc, group) = Explosion();

        SimGroupOps.Move(doc, group, 40, -15);

        Assert.Equal(140, doc.Sims!["flash"].OriginX);
        Assert.Equal(130, doc.Sims["smoke"].OriginX);
        Assert.Equal(185, doc.Sims["flash"].OriginY);
        Assert.Equal(175, doc.Sims["smoke"].OriginY);
        // The offset between members is the effect's shape and must survive.
        Assert.Equal(10, doc.Sims["flash"].OriginX - doc.Sims["smoke"].OriginX);
    }

    /// <summary>
    /// The smoke starting four frames after the flash is what makes it read as
    /// one event, so retiming shifts the whole thing rather than lining members
    /// up on a common frame.
    /// </summary>
    [Fact]
    public void Retiming_Shifts_Everything_And_Keeps_The_Internal_Timing()
    {
        var (doc, group) = Explosion();

        SimGroupOps.Retime(doc, group, 20);

        Assert.Equal(30, doc.Sims!["flash"].FirstFrame);
        Assert.Equal(31, doc.Sims["fire"].FirstFrame);
        Assert.Equal(34, doc.Sims["smoke"].FirstFrame);
        Assert.Equal(30, SimGroupOps.FirstFrame(doc, group));
        Assert.Equal(64, SimGroupOps.EndFrame(doc, group));
    }

    /// <summary>
    /// Dragged before the start, the group stops at frame zero with its spacing
    /// intact — a member pushed to a negative frame would silently stop being
    /// drawn.
    /// </summary>
    [Fact]
    public void Retiming_Past_The_Start_Stops_At_Zero_Rather_Than_Losing_A_Member()
    {
        var (doc, group) = Explosion();

        SimGroupOps.Retime(doc, group, -1000);

        Assert.Equal(0, doc.Sims!["flash"].FirstFrame);
        Assert.Equal(1, doc.Sims["fire"].FirstFrame);
        Assert.Equal(4, doc.Sims["smoke"].FirstFrame);
        Assert.All(doc.Sims.Values, e => Assert.True(e.FirstFrame >= 0));
    }

    // ---- duplicating and taking apart -----------------------------------------------

    /// <summary>
    /// Duplicating has to copy the elements too. A shallow copy is two groups
    /// naming one set, and editing either edits both — reported as "duplicating
    /// my explosion changed the first one".
    /// </summary>
    [Fact]
    public void Duplicating_Copies_The_Elements_Rather_Than_Sharing_Them()
    {
        var (doc, group) = Explosion();

        var copy = SimGroupOps.Duplicate(doc, group);
        SimGroupOps.Move(doc, copy, 300, 0);
        SimGroupOps.Retime(doc, copy, 12);

        Assert.Equal(6, doc.Sims!.Count);
        Assert.Empty(copy.ElementIds.Intersect(group.ElementIds));

        // The original is where it was.
        Assert.Equal(100, doc.Sims["flash"].OriginX);
        Assert.Equal(10, doc.Sims["flash"].FirstFrame);
        output.WriteLine($"original at {doc.Sims["flash"].OriginX}, copy at " +
                         $"{SimGroupOps.Members(doc, copy)[0].OriginX}");
        Assert.Equal(400, SimGroupOps.Members(doc, copy)[0].OriginX);
        Assert.Equal(22, SimGroupOps.Members(doc, copy)[0].FirstFrame);
    }

    /// <summary>
    /// A duplicate has baked nothing, so it must not claim the original's layer
    /// folder — the first bake would fold the original's layers into it.
    /// </summary>
    [Fact]
    public void A_Duplicate_Claims_No_Layer_Folder()
    {
        var (doc, group) = Explosion();
        group.LayerGroupId = "group_original";

        var copy = SimGroupOps.Duplicate(doc, group);

        Assert.Null(copy.LayerGroupId);
    }

    [Fact]
    public void Ungrouping_Leaves_Every_Element_Exactly_Where_It_Was()
    {
        var (doc, group) = Explosion();
        var before = doc.Sims!.Values.Select(e => (e.Id, e.OriginX, e.OriginY, e.FirstFrame)).ToList();

        SimGroupOps.Ungroup(doc, group);

        Assert.Empty(doc.SimGroups!);
        Assert.Equal(3, doc.Sims.Count);
        Assert.Equal(before, doc.Sims.Values.Select(e => (e.Id, e.OriginX, e.OriginY, e.FirstFrame)).ToList());
    }

    [Fact]
    public void Deleting_A_Group_Takes_Its_Elements_With_It()
    {
        var (doc, group) = Explosion();

        SimGroupOps.Delete(doc, group);

        Assert.Empty(doc.Sims!);
        Assert.Empty(doc.SimGroups!);
    }

    // ---- the layer folder -----------------------------------------------------------

    private static void Bake(Doc doc, SimElement element)
    {
        var strokes = new List<Stroke> { new() { Tool = ToolKind.Fill, SimId = element.Id, Points = [new(0, 0, 1), new(1, 0, 1), new(1, 1, 1)] } };
        SimBakeOps.Apply(doc, element, [(element.FirstFrame, strokes)]);
    }

    /// <summary>
    /// The baked layers land in a folder of the group's name, contiguous and in
    /// the group's own order — which is the order they composite in.
    /// </summary>
    [Fact]
    public void Folding_Puts_The_Baked_Layers_In_One_Folder_In_Order()
    {
        var (doc, group) = Explosion();
        // Baked out of order on purpose: the stack must follow the group, not
        // whichever element happened to be baked first.
        Bake(doc, doc.Sims!["smoke"]);
        Bake(doc, doc.Sims["flash"]);
        Bake(doc, doc.Sims["fire"]);

        var folder = SimGroupOps.Fold(doc, group);

        Assert.NotNull(folder);
        Assert.Equal("Explosion", folder!.Name);
        Assert.Equal(folder.Id, group.LayerGroupId);

        var ours = doc.Scene.Layers.Where(l => l.SimId is not null).ToList();
        Assert.Equal(["flash", "fire", "smoke"], ours.Select(l => l.SimId));
        Assert.All(ours, l => Assert.Equal(folder.Id, l.GroupId));

        // Contiguous, which is what a layer folder requires.
        var indices = ours.Select(l => doc.Scene.Layers.IndexOf(l)).ToList();
        Assert.Equal(indices.Max() - indices.Min() + 1, indices.Count);
        output.WriteLine($"layers at {string.Join(", ", indices)}");
    }

    /// <summary>
    /// Folding twice reuses the folder rather than making a second one, and
    /// renaming the group renames it. Found by id, not by name, so renaming the
    /// folder in the docker does not orphan it.
    /// </summary>
    [Fact]
    public void Folding_Again_Reuses_The_Folder()
    {
        var (doc, group) = Explosion();
        foreach (var e in SimGroupOps.Members(doc, group)) Bake(doc, e);

        var first = SimGroupOps.Fold(doc, group);
        first!.Name = "renamed by hand";
        group.Name = "Big explosion";
        var second = SimGroupOps.Fold(doc, group);

        Assert.Same(first, second);
        Assert.Single(doc.Scene.LayerGroups);
        Assert.Equal("Big explosion", second!.Name);
    }

    /// <summary>A group nobody has baked has nothing to fold, and says so rather than making an empty folder.</summary>
    [Fact]
    public void Folding_An_Unbaked_Group_Makes_No_Folder()
    {
        var (doc, group) = Explosion();

        Assert.Null(SimGroupOps.Fold(doc, group));
        Assert.Empty(doc.Scene.LayerGroups);
        Assert.Null(group.LayerGroupId);
    }

    /// <summary>A group whose elements have been deleted underneath it is not a crash.</summary>
    [Fact]
    public void A_Group_Naming_A_Missing_Element_Skips_It()
    {
        var (doc, group) = Explosion();
        doc.Sims!.Remove("fire");

        Assert.Equal(2, SimGroupOps.Members(doc, group).Count);
        SimGroupOps.Move(doc, group, 5, 5);
        Assert.Equal(105, doc.Sims["flash"].OriginX);
    }
}
