using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Several elements that are one effect, driven the way the window drives them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The group's numbers are not stored anywhere</b>, which is what most of
/// these guard. "Starts on frame 30" reads the earliest member and writes the
/// difference to all of them, so the property that must hold is that the
/// document afterwards is one an artist could have made element by element — no
/// offset waiting to be applied, nothing for the bake path to know about.
/// </para>
/// </remarks>
public class FluidEffectsGroupTests(Xunit.ITestOutputHelper output)
{
    private static (FluidEffectsWindow Window, MainViewModel Vm) AnExplosion()
    {
        var vm = new MainViewModel(null);
        var window = new FluidEffectsWindow(vm);
        var model = window.Model;

        var flash = model.NewElement("fire").Element!;
        flash.Name = "flash";
        model.NewGroup("Explosion");

        var smoke = model.NewElement("smoke").Element!;
        smoke.Name = "smoke";
        smoke.FirstFrame = 4;
        model.AddToGroup();

        return (window, vm);
    }

    [AvaloniaFact]
    public void An_Element_Can_Start_An_Effect_And_Others_Join_It()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;

        var group = Assert.Single(vm.Doc.SimGroups!.Values);
        Assert.Equal("Explosion", group.Name);
        Assert.Equal(2, group.ElementIds.Count);
        Assert.Equal(2, model.Groups.Count > 0 ? group.ElementIds.Count : 0);
        Assert.Equal("Explosion", model.Elements.First().GroupName);

        window.Close();
    }

    /// <summary>
    /// Moving and retiming shift every member and keep the spacing between
    /// them, because that spacing is the effect's timing.
    /// </summary>
    [AvaloniaFact]
    public void Placing_The_Effect_Moves_Every_Member_And_Keeps_Their_Timing()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;
        var members = vm.Doc.SimGroups!.Values.Single().ElementIds
            .Select(id => vm.Doc.Sims![id]).ToList();

        foreach (var e in members) e.OriginX = 100;
        members[1].OriginX = 60;

        model.GroupOriginX = 200;
        model.GroupFirstFrame = 30;

        output.WriteLine($"origins {string.Join(", ", members.Select(e => e.OriginX))}; " +
                         $"frames {string.Join(", ", members.Select(e => e.FirstFrame))}");

        // The leftmost lands where it was asked to; the other keeps its 40px lead.
        Assert.Equal(200, members.Min(e => e.OriginX));
        Assert.Equal(40, members[0].OriginX - members[1].OriginX);
        // And the smoke is still four frames behind the flash.
        Assert.Equal(30, members[0].FirstFrame);
        Assert.Equal(34, members[1].FirstFrame);

        window.Close();
    }

    /// <summary>
    /// Nothing about the group reaches the elements' own records beyond their
    /// placement — no stored offset, so a document read back describes exactly
    /// what will bake.
    /// </summary>
    [AvaloniaFact]
    public void A_Group_Stores_No_Geometry_Of_Its_Own()
    {
        var (window, vm) = AnExplosion();
        window.Model.GroupOriginX = 320;
        window.Model.GroupFirstFrame = 12;

        var group = vm.Doc.SimGroups!.Values.Single();
        var json = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);

        Assert.DoesNotContain("\"originX\":0,\"originY\":0,\"frameOffset\"", json);
        foreach (var name in new[] { "frameOffset", "offsetX", "offsetY" })
        {
            Assert.DoesNotContain($"\"{name}\"", json);
        }
        Assert.Equal(2, group.ElementIds.Count);

        window.Close();
    }

    [AvaloniaFact]
    public void Copying_An_Effect_Copies_Its_Elements()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;

        var copy = model.DuplicateGroup()!;
        model.GroupOriginX = 500;

        Assert.Equal(4, vm.Doc.Sims!.Count);
        Assert.Equal(2, vm.Doc.SimGroups!.Count);
        Assert.Equal(500, SimGroupOps.Members(vm.Doc, copy.Group!).Min(e => e.OriginX));
        // The original did not follow it.
        var original = vm.Doc.SimGroups.Values.First(g => g.Id != copy.Id);
        Assert.NotEqual(500, SimGroupOps.Members(vm.Doc, original).Min(e => e.OriginX));

        window.Close();
    }

    [AvaloniaFact]
    public void Ungrouping_Keeps_The_Elements_And_Deleting_Does_Not()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;

        model.Ungroup();
        Assert.Empty(vm.Doc.SimGroups!);
        Assert.Equal(2, vm.Doc.Sims!.Count);
        Assert.Null(model.Elements.First().GroupName);

        model.NewGroup("Again");
        model.DeleteGroup();
        Assert.Empty(vm.Doc.SimGroups!);
        Assert.Single(vm.Doc.Sims);

        window.Close();
    }

    /// <summary>
    /// Baking the effect bakes every member and folds their layers together, in
    /// the group's own order.
    /// </summary>
    [AvaloniaFact]
    public void Baking_An_Effect_Folds_Its_Layers_Into_One_Folder()
    {
        var (window, vm) = AnExplosion();

        window.Model.BakeGroup();

        var group = vm.Doc.SimGroups!.Values.Single();
        var folder = Assert.Single(vm.Doc.Scene.LayerGroups);
        Assert.Equal("Explosion", folder.Name);
        Assert.Equal(folder.Id, group.LayerGroupId);

        var ours = vm.Doc.Scene.Layers.Where(l => l.SimId is not null).ToList();
        Assert.Equal(2, ours.Count);
        Assert.Equal(group.ElementIds, ours.Select(l => l.SimId));
        Assert.All(ours, l => Assert.Equal(folder.Id, l.GroupId));
        output.WriteLine($"folder “{folder.Name}” holds {ours.Count} layers");

        window.Close();
    }

    /// <summary>
    /// An element belongs to one effect at a time. Two groups that each thought
    /// they could retime it would move it twice.
    /// </summary>
    [AvaloniaFact]
    public void Joining_A_Second_Effect_Leaves_The_First()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;

        // A third element, in a group of its own, then moved into the first.
        model.NewElement("smoke");
        var second = model.NewGroup("Chimney")!;
        model.SelectedGroup = model.Groups.First(g => g.Label == "Explosion");
        model.AddToGroup();

        Assert.Equal(3, vm.Doc.SimGroups!.Values.Single(g => g.Name == "Explosion").ElementIds.Count);
        Assert.Empty(vm.Doc.SimGroups.Values.Single(g => g.Name == "Chimney").ElementIds);
        Assert.Equal(second.Id, second.Id);

        window.Close();
    }

    /// <summary>Moving a group must drop its members' cached solves — placement is what the solver reads.</summary>
    [AvaloniaFact]
    public void Moving_An_Effect_Forces_Its_Members_To_Simulate_Again()
    {
        var (window, vm) = AnExplosion();
        var model = window.Model;

        Assert.NotNull(model.Group);
        model.Resolve();
        Assert.True(model.LastBakeResolved, "the first solve did not happen");
        model.Preview(model.PreviewFrame);
        Assert.False(model.LastBakeResolved, "the cache is not warm, so the next assertion proves nothing");

        model.GroupOriginX = 400;
        model.Preview(model.PreviewFrame);

        output.WriteLine($"{model.Element!.Name} moved to {model.Element.OriginX}, " +
                         $"re-simulated: {model.LastBakeResolved}");
        Assert.True(model.LastBakeResolved, "a moved element served a stale solve");
        window.Close();
    }
}
