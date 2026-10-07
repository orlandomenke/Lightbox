using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Folders over the agent surface (Q204): what <c>get_scene</c> says about
/// them, and the three verbs — none of which deletes anything.
/// </summary>
[Collection("BrushState")]
public sealed class IpcFolderTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static IpcProtocol.Request Req(string op, object? payload = null) => new()
    {
        Op = op,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    /// <summary>Paper, a, b — bottom first.</summary>
    private static (MainViewModel Vm, IpcDocumentApi Api, string A, string B) Open()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 3) vm.AddPaintedLayerCommand.Execute(null);
        vm.Doc.Scene.Layers[1].Name = "a";
        vm.Doc.Scene.Layers[2].Name = "b";
        return (vm, new IpcDocumentApi(vm), vm.Doc.Scene.Layers[1].Id, vm.Doc.Scene.Layers[2].Id);
    }

    private string Scene(IpcDocumentApi api)
    {
        var raw = api.Handle(Req("get_scene")).Payload!.Value.GetRawText();
        output.WriteLine(raw.Length > 600 ? raw[..600] : raw);
        return raw;
    }

    [AvaloniaFact]
    public void ADocumentWithoutFoldersReadsAsItAlwaysDid()
    {
        var (_, api, _, _) = Open();

        var json = Scene(api);

        Assert.DoesNotContain("\"folders\"", json);
        Assert.DoesNotContain("\"folderId\"", json);
    }

    [AvaloniaFact]
    public void CreateMoveAndGroup_AreEachOneUndoStep_AndShowInGetScene()
    {
        var (vm, api, a, b) = Open();

        var made = api.Handle(Req("create_folder", new { name = "Hero" }));
        Assert.True(made.Ok, made.Error);
        var hero = made.Payload!.Value.GetProperty("folderId").GetString()!;
        Assert.Contains("\"empty\":true", Scene(api));

        // "Hero" sits at the top of the stack, so filing "a" into it passes "b":
        // refused, naming "b", until the agent says it means it (Q205).
        var refused = api.Handle(Req("move_to_folder", new { id = a, folderId = hero }));
        Assert.False(refused.Ok);
        Assert.Contains("\u201cb\u201d", refused.Error);
        var moved = api.Handle(Req("move_to_folder", new { id = a, folderId = hero, reorder = true }));
        Assert.True(moved.Ok, moved.Error);
        Assert.True(moved.Payload!.Value.GetProperty("reordered").GetBoolean());
        Assert.StartsWith("Agent:", vm.UndoHistory.Rows.Single(r => r.IsCurrent).Label);
        Assert.Equal(hero, moved.Payload!.Value.GetProperty("folderId").GetString());
        Assert.Equal(hero, vm.Doc.Scene.Layers.Single(l => l.Id == a).GroupId);
        Assert.Contains("\"empty\":false", Scene(api));

        var inner = api.Handle(Req("create_folder", new { name = "Face", inFolderId = hero }));
        Assert.True(inner.Ok, inner.Error);
        Assert.Equal(hero, inner.Payload!.Value.GetProperty("parentId").GetString());

        var grouped = api.Handle(Req("group_layers", new { ids = new[] { b }, name = "Back" }));
        Assert.True(grouped.Ok, grouped.Error);
        Assert.False(grouped.Payload!.Value.GetProperty("reordered").GetBoolean());
        var back = grouped.Payload!.Value.GetProperty("folderId").GetString();
        Assert.Equal(back, vm.Doc.Scene.Layers.Single(l => l.Id == b).GroupId);

        var outOfIt = api.Handle(Req("move_to_folder", new { id = a }));
        Assert.True(outOfIt.Ok, outOfIt.Error);
        Assert.False(outOfIt.Payload!.Value.GetProperty("reordered").GetBoolean());
        Assert.Null(vm.Doc.Scene.Layers.Single(l => l.Id == a).GroupId);

        // Five edits, five undo steps, and the document is back as it was.
        for (var i = 0; i < 5; i++) vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Doc.Scene.LayerGroups);
        Assert.All(vm.Doc.Scene.Layers, l => Assert.Null(l.GroupId));
    }

    /// <summary>
    /// Grouping layers that are apart would gather them and change the picture:
    /// refused for an agent unless it says reorder, naming what is in between.
    /// </summary>
    [AvaloniaFact]
    public void GroupingScatteredLayersIsRefusedUnlessReorderIsSaid()
    {
        var (vm, api, a, _) = Open();
        vm.AddPaintedLayerCommand.Execute(null);
        var top = vm.Doc.Scene.Layers[^1].Id; // a, b, top: "b" sits between "a" and "top"
        var before = FolderTree.Signature(vm.Doc.Scene);

        var refused = api.Handle(Req("group_layers", new { ids = new[] { a, top } }));
        Assert.False(refused.Ok);
        Assert.Contains("\u201cb\u201d", refused.Error);
        Assert.Equal(before, FolderTree.Signature(vm.Doc.Scene));

        var gathered = api.Handle(Req("group_layers", new { ids = new[] { a, top }, reorder = true }));
        Assert.True(gathered.Ok, gathered.Error);
        Assert.True(gathered.Payload!.Value.GetProperty("reordered").GetBoolean());
    }

    [AvaloniaFact]
    public void LocksAreRefusedInWords_AndNothingChanges()
    {
        var (vm, api, a, _) = Open();
        var hero = api.Handle(Req("create_folder", new { name = "Hero" })).Payload!.Value.GetProperty("folderId").GetString()!;
        vm.Doc.Scene.LayerGroups.Single().Locked = true;
        var before = FolderTree.Signature(vm.Doc.Scene);

        var into = api.Handle(Req("move_to_folder", new { id = a, folderId = hero }));
        var inside = api.Handle(Req("create_folder", new { inFolderId = hero }));
        vm.Doc.Scene.Layers.Single(l => l.Id == a).Locked = true;
        var group = api.Handle(Req("group_layers", new { ids = new[] { a } }));

        Assert.False(into.Ok);
        Assert.Contains("Hero", into.Error);
        Assert.False(inside.Ok);
        Assert.False(group.Ok);
        Assert.Contains("locked", group.Error);
        Assert.Equal(before, FolderTree.Signature(vm.Doc.Scene));
    }

    [AvaloniaFact]
    public void UnknownIdsAreRefusedByName()
    {
        var (_, api, _, _) = Open();

        var move = api.Handle(Req("move_to_folder", new { id = "nope" }));
        var group = api.Handle(Req("group_layers", new { ids = new[] { "nope" } }));
        var inside = api.Handle(Req("create_folder", new { inFolderId = "nope" }));

        Assert.False(move.Ok);
        Assert.Contains("nope", move.Error);
        Assert.False(group.Ok);
        Assert.False(inside.Ok);
    }
}
