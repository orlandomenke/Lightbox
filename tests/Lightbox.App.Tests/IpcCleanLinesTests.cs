using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// <c>clean_lines</c> over the pipe (Q292–Q295): a preview on the artist's
/// canvas unless the agent asks to apply, the artist's keys refused unless
/// named, and the reply saying what would change.
/// </summary>
[Collection("BrushState")]
public sealed class IpcCleanLinesTests : BrushStateIsolated
{
    private static IpcProtocol.Request Req(string op, object payload) => new()
    {
        Op = op,
        Payload = JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    private static void Draw(MainViewModel vm, double y)
    {
        vm.BeginStroke(100, y, 0.5);
        for (var i = 1; i <= 200; i++) vm.MoveStroke(100 + i, y + 1.5 * Math.Sin(i / 2.0), 0.5);
        vm.EndStroke();
    }

    /// <summary>Two shaky keys and one inbetween between them.</summary>
    private static MainViewModel KeysAndAnInbetween()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, 200);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 240);
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        vm.InsertInbetweensCommand.Execute(null);
        return vm;
    }

    private static List<StrokePoint> PointsAt(MainViewModel vm, int cel) =>
        [.. vm.PaintLayer().Cels[cel].Frame!.Strokes.SelectMany(s => s.Points)];

    [AvaloniaFact]
    public void ByDefaultItPreviewsOnTheCanvasAndWritesNothing()
    {
        var vm = KeysAndAnInbetween();
        var api = new IpcDocumentApi(vm);
        var before = PointsAt(vm, 0);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 0, mode = "frame", includeKeys = true }));

        Assert.True(resp.Ok, resp.Error);
        var json = resp.Payload!.Value;
        Assert.False(json.GetProperty("applied").GetBoolean());
        Assert.True(json.GetProperty("pending").GetBoolean());
        Assert.NotEqual(0, json.GetProperty("strokes").GetArrayLength());
        Assert.Equal(0, json.GetProperty("truncated").GetInt32());
        Assert.Equal(before, PointsAt(vm, 0));
        Assert.True(vm.IsCleanupPending);
    }

    [AvaloniaFact]
    public void ApplyOnAnInbetweenWritesOneUndoStep()
    {
        var vm = KeysAndAnInbetween();
        var api = new IpcDocumentApi(vm);
        var before = PointsAt(vm, 1);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 1, mode = "frame", strength = "strong", apply = true }));

        Assert.True(resp.Ok, resp.Error);
        var json = resp.Payload!.Value;
        Assert.True(json.GetProperty("applied").GetBoolean());
        Assert.Equal("inbetween", json.GetProperty("role").GetString());
        Assert.True(json.GetProperty("smoothed").GetInt32() > 0);
        Assert.NotEqual(before, PointsAt(vm, 1));
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, PointsAt(vm, 1));
    }

    [AvaloniaFact]
    public void AnAgentCanPreviewAKeyButNeverApplyIt()
    {
        var vm = KeysAndAnInbetween();
        var api = new IpcDocumentApi(vm);
        var before = PointsAt(vm, 0);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 0, mode = "frame", includeKeys = true, apply = true }));

        Assert.False(resp.Ok);
        Assert.Contains("artist applies it", resp.Error);
        Assert.Equal(before, PointsAt(vm, 0));
    }

    [AvaloniaFact]
    public void AnAgentDoesNotReplaceAPreviewTheArtistHasOpen()
    {
        var vm = KeysAndAnInbetween();
        vm.CurrentFrameIndex = 0;
        Assert.True(vm.PreviewCleanup(guided: false) > 0);
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 1 }));

        Assert.False(resp.Ok);
        Assert.Contains("artist has a cleanup preview open", resp.Error);
        Assert.True(vm.IsCleanupPendingFromArtist);
    }

    [AvaloniaFact]
    public void AKeyIsRefusedUnlessTheAgentSaysItMeansIt()
    {
        var vm = KeysAndAnInbetween();
        var api = new IpcDocumentApi(vm);
        var before = PointsAt(vm, 0);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 0, mode = "frame", apply = true }));

        Assert.False(resp.Ok);
        Assert.Contains("includeKeys", resp.Error);
        Assert.Equal(before, PointsAt(vm, 0));
        Assert.False(vm.IsCleanupPending);
    }

    [AvaloniaFact]
    public void AnInbetweenIsCleanedTowardItsKeysByDefault()
    {
        var vm = KeysAndAnInbetween();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("clean_lines", new { frameIndex = 1 }));

        Assert.True(resp.Ok, resp.Error);
        Assert.Equal("keys", resp.Payload!.Value.GetProperty("mode").GetString());
    }

    [AvaloniaFact]
    public void AnUnknownModeOrStrengthIsRefusedByName()
    {
        var api = new IpcDocumentApi(KeysAndAnInbetween());

        var mode = api.Handle(Req("clean_lines", new { frameIndex = 1, mode = "everything" }));
        var strength = api.Handle(Req("clean_lines", new { frameIndex = 1, mode = "frame", strength = "max" }));

        Assert.False(mode.Ok);
        Assert.Contains("everything", mode.Error);
        Assert.False(strength.Ok);
        Assert.Contains("max", strength.Error);
    }
}
