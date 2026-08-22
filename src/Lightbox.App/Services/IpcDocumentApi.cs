using System.Text.Json;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Services;

/// <summary>
/// Maps IPC requests onto the document through the same validated paths the
/// in-app AI uses: <see cref="StrokeWire"/> DTOs in and out (clamped, never
/// trusted), <see cref="MainViewModel"/> operations for mutation (undoable).
/// Must be called on the UI thread — <see cref="IpcServer"/> marshals.
/// </summary>
public sealed class IpcDocumentApi(MainViewModel vm)
{
    public IpcProtocol.Response Handle(IpcProtocol.Request request)
    {
        try
        {
            return request.Op switch
            {
                "get_scene" => GetScene(),
                "get_frame_strokes" => GetFrameStrokes(request),
                "render_frame" => RenderFrame(request),
                "insert_inbetweens" => InsertInbetweens(request),
                "draw_strokes" => DrawStrokes(request),
                "list_reference_views" => ListReferenceViews(),
                "render_reference_view" => RenderReferenceView(request),
                "import_character" => ImportCharacter(request),
                _ => IpcProtocol.Response.Fail($"Unknown op \"{request.Op}\"."),
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return IpcProtocol.Response.Fail(e.Message);
        }
    }

    private T Payload<T>(IpcProtocol.Request request) where T : class =>
        (request.Payload is { } el ? el.Deserialize<T>(IpcProtocol.Json) : null)
        ?? throw new ArgumentException($"Op \"{request.Op}\" needs a payload.");

    private SceneInfo SceneInfo() =>
        new(vm.Doc.Scene.Width, vm.Doc.Scene.Height, vm.Doc.Scene.Fps);

    private Layer ResolveLayer(string? layerId)
    {
        var layers = vm.Doc.Scene.Layers;
        if (layerId is null) return vm.ActiveLayerForIpc;
        return layers.FirstOrDefault(l => l.Id == layerId)
               ?? throw new ArgumentException($"No layer with id \"{layerId}\".");
    }

    private static List<Stroke> StrokesOf(Frame frame) => frame.Strokes;

    // ---- ops ----------------------------------------------------------------

    /// <summary>
    /// The running application's build, answered on <c>get_scene</c> so an agent
    /// learns it in the call it already makes first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It lives on this side of the pipe on purpose.</b> B195 was a bug in
    /// the MCP server that stayed fixed in source and broken in practice,
    /// because Claude Desktop launches a <em>published</em>
    /// <c>Lightbox.Mcp.exe</c> and nothing tells an agent which build that is.
    /// Putting the app's build here means even a server too old to know about
    /// this feature still relays it — <c>LightboxTools</c> forwards the payload
    /// verbatim.
    /// </para>
    /// <para>
    /// So the pair reads as a diagnosis rather than as trivia. Both keys
    /// present and equal is the healthy case; <c>mcpBuild</c> <b>absent</b>
    /// means the server predates this and is certainly stale; the two
    /// disagreeing means one half was republished and the other was not, which
    /// is the shape of every bug that is "already fixed" and still happening.
    /// </para>
    /// </remarks>
    private IpcProtocol.Response GetScene()
    {
        var s = vm.Doc.Scene;
        return IpcProtocol.Response.Success(new
        {
            AppBuild = DiagnosticLog.Build,
            s.Width,
            s.Height,
            s.Fps,
            s.FrameCount,
            CurrentFrame = vm.CurrentFrameIndex,
            Layers = s.Layers.Select(l => new
            {
                l.Id,
                l.Name,
                Kind = l.Kind.ToString().ToLowerInvariant(),
                l.Visible,
                // Both false on every ordinary layer, so an agent reading the
                // scene sees the carve state without a second call — and
                // RenderFramePng already applies both, so what it reads and
                // what it renders agree.
                HasMask = l.IsMasked,
                Clipped = l.IsClipped,
                KeyedFrames = Enumerable.Range(0, s.FrameCount)
                    .Where(i => ExposureSheet.FrameAtExactIndex(l, i) is not null)
                    .ToList(),
            }),
        });
    }

    private sealed class FrameRef
    {
        public int FrameIndex { get; set; }
        public string? LayerId { get; set; }
    }

    private sealed class ImportCharacterRef
    {
        public string? Library { get; set; }
        public string Character { get; set; } = "";
        public bool ReplaceEdited { get; set; }
    }

    /// <summary>
    /// The character library's agent surface: the same scan, the same merge,
    /// the same after-path the two UI surfaces use — and the edited-copy gate
    /// reshaped for a caller that has no dialog: with <c>replaceEdited</c>
    /// unset the edited copies are kept and reported, exactly the UI default,
    /// so nothing is destroyed by an agent that did not say so.
    /// </summary>
    private IpcProtocol.Response ImportCharacter(IpcProtocol.Request request)
    {
        var p = Payload<ImportCharacterRef>(request);
        if (vm.ProjectDocker.Project is not { } project)
            return IpcProtocol.Response.Fail("No project is open — an import needs somewhere to land.");
        var roots = p.Library is { Length: > 0 } one
            ? [one]
            : (IReadOnlyList<string>)vm.Settings.Library.Roots;
        if (roots.Count == 0)
        {
            return IpcProtocol.Response.Fail(
                "No library given, and no library folders are configured. Pass a path in "
                + "\"library\", or have the artist add one under the library window.");
        }
        var entries = Core.Projects.CharacterLibrary.Scan(roots);
        // An empty shelf is a path problem, not a name problem — saying "no
        // character named X" here sends an agent retrying name variations
        // against a folder that was never a library.
        if (entries.Count == 0)
        {
            return IpcProtocol.Response.Fail(
                $"Nothing is offered under {string.Join(", ", roots)}. A library is a project "
                + "whose type is Asset library — check the path points at one, or at a folder "
                + "holding several.");
        }
        // Named rather than counted, the ConfirmDiscard rule for agents: the
        // shelf's contents are what makes the retry a decision. And a name two
        // libraries offer refuses rather than silently taking whichever the
        // scan met first — the UI path never guesses (the artist clicked one
        // specific entry), so the agent path must not either.
        var matches = entries.Where(e => e.Name == p.Character).ToList();
        if (matches.Count == 0)
        {
            return IpcProtocol.Response.Fail(
                $"No character named \"{p.Character}\" — offered: "
                + $"{string.Join(", ", entries.Select(e => e.Name))}.");
        }
        if (matches.Count > 1)
        {
            return IpcProtocol.Response.Fail(
                $"\"{p.Character}\" is offered by more than one library "
                + $"({string.Join(", ", matches.Select(e => e.LibraryName))}) — pass the one "
                + "you mean as \"library\".");
        }
        var result = Core.Projects.CharacterLibrary.Import(matches[0], project, p.ReplaceEdited);
        vm.AfterLibraryImport(result);
        return IpcProtocol.Response.Success(new
        {
            Folder = result.Folder.Name,
            Library = matches[0].LibraryName,
            result.Added,
            result.Replaced,
            result.KeptEdited,
        });
    }

    private IpcProtocol.Response GetFrameStrokes(IpcProtocol.Request request)
    {
        var p = Payload<FrameRef>(request);
        var layer = ResolveLayer(p.LayerId);
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, p.FrameIndex);
        if (keyIndex < 0) return IpcProtocol.Response.Fail("No drawing at or before that frame on this layer.");
        var frame = layer.Cels[keyIndex].Frame!;
        // Effective record only: erased strokes are not part of the drawing.
        var strokes = Core.Inbetween.StrokeRecordCleaner.EffectiveStrokes(StrokesOf(frame));
        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            KeyIndex = keyIndex,
            Strokes = strokes.Select(StrokeWire.ToWire),
        });
    }

    private IpcProtocol.Response RenderFrame(IpcProtocol.Request request)
    {
        var p = Payload<FrameRef>(request);
        if (p.FrameIndex < 0 || p.FrameIndex >= vm.Doc.Scene.FrameCount)
            return IpcProtocol.Response.Fail($"frameIndex must be 0..{vm.Doc.Scene.FrameCount - 1}.");
        return IpcProtocol.Response.Success(new { PngBase64 = vm.RenderFramePng(p.FrameIndex) });
    }

    private sealed class InsertPayload
    {
        public int AIndex { get; set; }
        public string? LayerId { get; set; }
        public List<StrokeWire.InbetweenFrameDto> Frames { get; set; } = [];
    }

    private IpcProtocol.Response InsertInbetweens(IpcProtocol.Request request)
    {
        var p = Payload<InsertPayload>(request);
        var layer = ResolveLayer(p.LayerId);
        if (ExposureSheet.FrameAtExactIndex(layer, p.AIndex) is null)
            return IpcProtocol.Response.Fail($"aIndex {p.AIndex} is not a keyed frame on layer \"{layer.Name}\".");

        var scene = SceneInfo();
        var frames = p.Frames
            .Where(f => f.T is > 0 and < 1)
            .OrderBy(f => f.T)
            .Select(f => (f.T, Strokes: StrokeWire.FromWire(f.Strokes, scene)))
            .Where(f => f.Strokes.Count > 0)
            .ToList();
        if (frames.Count == 0)
            return IpcProtocol.Response.Fail("No usable inbetween frames in the payload (each needs 0<t<1 and at least one valid stroke).");

        var inserted = vm.InsertExternalInbetweens(layer.Id, p.AIndex, frames.Select(f => f.Strokes).ToList());
        return IpcProtocol.Response.Success(new { Inserted = inserted });
    }

    private sealed class DrawPayload
    {
        public int FrameIndex { get; set; }
        public string? LayerId { get; set; }
        public List<StrokeWire.StrokeDto> Strokes { get; set; } = [];
    }

    private IpcProtocol.Response ListReferenceViews()
    {
        // The view-model's list rather than the document's own: in a project
        // the sheets an agent should see are the ones filed above the active
        // document, the same set the docker and the AI payload use.
        return IpcProtocol.Response.Success(new
        {
            Sheets = vm.ReferenceSheetsView.Select(s => new
            {
                s.Id,
                s.Name,
                Views = s.Views.Select(v => new { v.Id, v.Name, v.Width, v.Height }),
            }),
        });
    }

    private sealed class ViewRef
    {
        public string ViewId { get; set; } = "";
    }

    private IpcProtocol.Response RenderReferenceView(IpcProtocol.Request request)
    {
        var p = Payload<ViewRef>(request);
        var view = vm.ReferenceSheetsView.SelectMany(s => s.Views).FirstOrDefault(v => v.Id == p.ViewId)
                   ?? throw new ArgumentException($"No reference view with id \"{p.ViewId}\".");
        return IpcProtocol.Response.Success(new { PngBase64 = vm.RenderReferenceViewPng(view) });
    }

    private IpcProtocol.Response DrawStrokes(IpcProtocol.Request request)
    {
        var p = Payload<DrawPayload>(request);
        var layer = ResolveLayer(p.LayerId);
        var strokes = StrokeWire.FromWire(p.Strokes, SceneInfo());
        if (strokes.Count == 0)
            return IpcProtocol.Response.Fail("No usable strokes in the payload.");
        var added = vm.AppendExternalStrokes(layer.Id, p.FrameIndex, strokes);
        return added == 0
            ? IpcProtocol.Response.Fail("No drawing at or before that frame on this layer.")
            : IpcProtocol.Response.Success(new { Added = added });
    }
}
