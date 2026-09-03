using System.Text.Json;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
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
    /// <summary>
    /// The view model, held weakly — B281. The server's accept loop parks a
    /// native pipe read whose overlapped I/O is a strong GC handle, so it
    /// outlives everything managed, including the per-test application it was
    /// born in: a strong reference here therefore pinned the view model, the
    /// window that owned it and the window's whole visual tree for the life of
    /// the process, once per UI test. The dump that found it shows the chain
    /// verbatim: ThreadPoolBoundHandleOverlapped → AcceptLoopAsync →
    /// IpcServer → IpcDocumentApi → MainViewModel → MainWindow. Pending I/O
    /// may pin the server, which is bytes; it must not pin the application.
    /// In the running app the window disposes the server on close and the
    /// view model outlives it anyway, so nothing observable changes there.
    /// </summary>
    private readonly WeakReference<MainViewModel> _vm = new(vm);

    /// <summary>
    /// The live view model, or an <see cref="InvalidOperationException"/> the
    /// dispatcher maps to a failure response — which is what a client that
    /// outlived the window it was talking to should hear.
    /// </summary>
    private MainViewModel Vm =>
        _vm.TryGetTarget(out var target)
            ? target
            : throw new InvalidOperationException("The document this server belonged to is gone.");

    public IpcProtocol.Response Handle(IpcProtocol.Request request)
    {
        try
        {
            return request.Op switch
            {
                "get_scene" => GetScene(),
                "list_frame_strokes" => ListFrameStrokes(request),
                "get_frame_strokes" => GetFrameStrokes(request),
                "render_frame" => RenderFrame(request),
                "insert_inbetweens" => InsertInbetweens(request),
                "draw_strokes" => DrawStrokes(request),
                "list_reference_views" => ListReferenceViews(),
                "render_reference_view" => RenderReferenceView(request),
                "import_character" => ImportCharacter(request),
                "set_key" => SetKey(request),
                "extend_exposure" => ExtendExposure(request),
                "reduce_exposure" => ReduceExposure(request),
                "set_exposure_step" => SetExposureStep(request),
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
        new(Vm.Doc.Scene.Width, Vm.Doc.Scene.Height, Vm.Doc.Scene.Fps);

    private Layer ResolveLayer(string? layerId)
    {
        var layers = Vm.Doc.Scene.Layers;
        if (layerId is null) return Vm.ActiveLayerForIpc;
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
        var s = Vm.Doc.Scene;
        return IpcProtocol.Response.Success(new
        {
            AppBuild = DiagnosticLog.Build,
            s.Width,
            s.Height,
            s.Fps,
            s.FrameCount,
            CurrentFrame = Vm.CurrentFrameIndex,
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
                // Predicate-shaped like its three siblings (G12 review): the
                // model property is `Adjusts`, but `{"adjusts": true}` on the
                // wire reads as an instruction rather than a state.
                IsAdjustment = l.IsAdjustment,
                HasEffects = l.HasLiveEffects,
                KeyedFrames = Enumerable.Range(0, s.FrameCount)
                    .Where(i => ExposureSheet.FrameAtExactIndex(l, i) is not null)
                    .ToList(),
            }),
        });
    }

    private class FrameRef
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
        if (Vm.ProjectDocker.Project is not { } project)
            return IpcProtocol.Response.Fail("No project is open — an import needs somewhere to land.");
        var roots = p.Library is { Length: > 0 } one
            ? [one]
            : (IReadOnlyList<string>)Vm.Settings.Library.Roots;
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
        Vm.AfterLibraryImport(result);
        return IpcProtocol.Response.Success(new
        {
            Folder = result.Folder.Name,
            Library = matches[0].LibraryName,
            result.Added,
            result.Replaced,
            result.KeptEdited,
        });
    }

    private sealed class StrokeQuery : FrameRef
    {
        public List<string>? Labels { get; set; }
        public List<int>? Indices { get; set; }
    }

    /// <summary>
    /// The drawing at a frame, as the effective stroke list both stroke ops
    /// number from.
    /// </summary>
    /// <remarks>
    /// <b>One list, so an index means one thing.</b> <c>list_frame_strokes</c>
    /// hands an agent a position and <c>get_frame_strokes</c> takes one back, so
    /// the two must number the same strokes by the same rule — a second call to
    /// <c>EffectiveStrokes</c> under different filtering would make the round
    /// trip silently fetch the wrong line. The list is the effective record
    /// (B233): erasures and the ink they erased are not part of the drawing, so
    /// an agent can never address them.
    /// </remarks>
    private (Layer Layer, int KeyIndex, IReadOnlyList<Stroke> Strokes)? DrawingAt(int frameIndex, string? layerId)
    {
        var layer = ResolveLayer(layerId);
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, frameIndex);
        if (keyIndex < 0) return null;
        var frame = layer.Cels[keyIndex].Frame!;
        return (layer, keyIndex, Core.Inbetween.StrokeRecordCleaner.EffectiveStrokes(StrokesOf(frame)));
    }

    /// <summary>
    /// Every stroke in a drawing as one cheap line each — position, label and
    /// size, but no geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because a tool result is spent out of the agent's context,
    /// not out of a request.</b> <c>docs/DESIGN-ai-payload.md</c> costs an AI
    /// request, which is sent once and paid once; an MCP reply is different in
    /// kind, because it stays in the conversation and is re-read on every turn
    /// after it. A 120-stroke frame is ~39k tokens of geometry through
    /// <c>get_frame_strokes</c> — paid once to fetch and then again every turn —
    /// and in most tasks an agent needed it only to learn which strokes exist.
    /// This answers that question for about 8% of the tokens.
    /// </para>
    /// <para>
    /// <b>The box is why this is usable rather than merely small.</b> A list of
    /// labels says what is in the drawing; a label plus a box says which strokes
    /// a change would touch, which is the actual question in front of an agent
    /// about to redraw a limb. It comes from <see cref="TransformOps.Bounds(Stroke)"/>
    /// so it is padded by the brush radius and agrees with the transform gizmo.
    /// </para>
    /// <para>
    /// Flat <c>[x, y, w, h]</c> for the box and named keys for everything else —
    /// Q18's answer applied where it was aimed. Numbers are the volume here and
    /// carry no meaning in their names; <c>label</c> is the field whose loss
    /// costs the agent the drawing, so it keeps its key.
    /// </para>
    /// </remarks>
    private IpcProtocol.Response ListFrameStrokes(IpcProtocol.Request request)
    {
        var p = Payload<FrameRef>(request);
        if (DrawingAt(p.FrameIndex, p.LayerId) is not { } drawing)
            return IpcProtocol.Response.Fail("No drawing at or before that frame on this layer.");
        var (layer, keyIndex, strokes) = drawing;
        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            KeyIndex = keyIndex,
            StrokeCount = strokes.Count,
            Strokes = strokes.Select((s, i) => new
            {
                Index = i,
                // Absent unless it says something, the same rule the document
                // model follows: a listing whose job is to be cheap must not
                // spend bytes writing "brush" 120 times.
                Tool = s.Tool == ToolKind.Brush ? null : s.Tool.ToString().ToLowerInvariant(),
                Label = string.IsNullOrWhiteSpace(s.Label) ? null : s.Label,
                s.Color,
                // Not `points`: that key means an array of {x,y,pressure} in
                // get_frame_strokes, and one name for two shapes across two tools
                // on the same surface is a trap for whoever diffs them (G12).
                PointCount = s.Points.Count,
                Box = TransformOps.Bounds(s) is { } b
                    ? new[]
                    {
                        Math.Round(b.MinX, 1), Math.Round(b.MinY, 1),
                        Math.Round(b.MaxX - b.MinX, 1), Math.Round(b.MaxY - b.MinY, 1),
                    }
                    : null,
            }),
        });
    }

    /// <summary>
    /// The geometry of a drawing's strokes — all of them, or only the ones the
    /// caller names.
    /// </summary>
    /// <remarks>
    /// <b>Unfiltered stays the default, and that is a deliberate cost.</b>
    /// Making the cheap answer the default would save more and would change what
    /// every existing agent gets from a call it already makes; the filter is
    /// additive, and <c>list_frame_strokes</c> is what makes it usable — an
    /// agent cannot ask for labels it has not been told exist.
    /// </remarks>
    private IpcProtocol.Response GetFrameStrokes(IpcProtocol.Request request)
    {
        var p = Payload<StrokeQuery>(request);
        if (DrawingAt(p.FrameIndex, p.LayerId) is not { } drawing)
            return IpcProtocol.Response.Fail("No drawing at or before that frame on this layer.");
        var (layer, keyIndex, strokes) = drawing;

        List<(int Index, Stroke Item)>? picked = null;
        var labels = p.Labels is { Count: > 0 } ? p.Labels : null;
        var indices = p.Indices is { Count: > 0 } ? p.Indices : null;
        if (labels is not null || indices is not null)
        {
            // A filter names strokes on a *particular* layer, so it may not ride
            // on the active-layer default. `layerId` omitted resolves afresh on
            // every call, and the artist clicking a different layer between the
            // listing and this fetch is an ordinary thing to do — after which
            // index 3 silently means a different drawing's third stroke and the
            // reply is a cheerful Ok. Found by G12's ai-engineer, who reproduced
            // it rather than reasoned about it. `list_frame_strokes` returns the
            // id it numbered against for exactly this purpose.
            if (p.LayerId is null)
            {
                return IpcProtocol.Response.Fail(
                    "labels and indices name strokes on one layer, so layerId is required with them — "
                    + "pass the layerId that list_frame_strokes returned. Without it the active layer "
                    + "is resolved again now, and it may not be the layer you listed.");
            }
            // A refusal beats a silent no-op, because an agent cannot see the
            // drawing: an empty list back from a label it misspelled reads as
            // "that stroke is gone" and sends it redrawing something that is
            // already there. Naming what is present is the ImportCharacter rule
            // — the shelf's contents are what makes the retry a decision.
            // Cast through int? on purpose: FirstOrDefault over ints answers 0
            // for "nothing matched", and 0 is a perfectly good stroke index.
            if (indices?.Cast<int?>().FirstOrDefault(i => i < 0 || i >= strokes.Count) is { } bad)
            {
                return IpcProtocol.Response.Fail(
                    $"index {bad} is out of range — this drawing has {strokes.Count} strokes (0..{strokes.Count - 1}).");
            }
            if (labels is not null)
            {
                var present = strokes.Select(s => s.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
                if (labels.FirstOrDefault(l => !present.Contains(l)) is { } missing)
                {
                    return IpcProtocol.Response.Fail(
                        present.Count == 0
                            ? $"No stroke here is labelled \"{missing}\" — this drawing carries no labels at all."
                            : $"No stroke here is labelled \"{missing}\" — present: {string.Join(", ", present)}.");
                }
            }
            // Either/or rather than both: an agent asking for two labels and an
            // index wants the three strokes, not their empty intersection.
            picked = strokes.Index()
                .Where(e => (indices?.Contains(e.Index) ?? false)
                            || (labels?.Contains(e.Item.Label ?? "") ?? false))
                .ToList();
        }
        // The record's order, never the order they were asked for — and said out
        // loud, because a wire stroke carries no index of its own. G12: asking
        // for [4, 1] returns 1 then 4, and an agent zipping its request against
        // the reply (the natural thing to do) would attribute each stroke to the
        // wrong one. Adding an index to StrokeDto would have fixed it by putting
        // a field into every AI request that pays for it too, so the positions
        // ride in the envelope instead — a few bytes, once.
        //
        // Null on an unfiltered read, where it would be 0..n-1 and would say
        // nothing: this is the answer to a filter, and a reply that has not been
        // filtered should not carry a list of every number in it.
        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            KeyIndex = keyIndex,
            StrokeCount = strokes.Count,
            Indices = picked?.Select(e => e.Index).ToList(),
            Strokes = (picked?.Select(e => e.Item) ?? strokes).Select(StrokeWire.ToWire),
        });
    }

    private sealed class RenderFrameRef : FrameRef
    {
        /// <summary>Null means the default cap; 0 or less means the authored size.</summary>
        public int? LongEdge { get; set; }
    }

    /// <summary>
    /// The longest edge of a frame rendered for an agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its own constant, not <c>ReferenceViewImages.LongEdge</c>, even though
    /// both are 768 today.</b> They cap for different reasons — that one because
    /// a provider bills an AI request by area, this one because the picture is
    /// spent out of the agent's own context — and Q27's answer will make the
    /// reference cap a per-view heuristic. Sharing the number would drag
    /// <c>render_frame</c> along with a decision that was never about it, which
    /// is B31's mistake pointed the other way.
    /// </para>
    /// <para>
    /// <b>Capped by default here, uncapped by default next door, and the
    /// difference is what the tool is for.</b> <c>render_reference_view</c>
    /// answers with a view, and an agent that asks for a view should get the
    /// view — B31 recorded shrinking that reply as the bug, and
    /// <c>RenderReferenceView_ProducesDecodablePng</c> has asserted the authored
    /// width since the feature landed. <c>render_frame</c> is not that: its own
    /// description says it exists so an agent can *see* a drawing and check its
    /// own results, which is inspection. At 1080p that inspection costs ~2,764
    /// image tokens against ~442 capped, on every look. <c>longEdge: 0</c> is
    /// the way out for a caller that genuinely wants the pixels.
    /// </para>
    /// </remarks>
    internal const int RenderedFrameLongEdge = 768;

    private IpcProtocol.Response RenderFrame(IpcProtocol.Request request)
    {
        var p = Payload<RenderFrameRef>(request);
        if (p.FrameIndex < 0 || p.FrameIndex >= Vm.Doc.Scene.FrameCount)
            return IpcProtocol.Response.Fail($"frameIndex must be 0..{Vm.Doc.Scene.FrameCount - 1}.");
        // The one place the cap applies, the same shape as EncodedReferenceView:
        // the view model keeps answering at the authored size for every in-app
        // caller, and only this reply — the one that leaves the machine — is capped.
        var longEdge = p.LongEdge ?? RenderedFrameLongEdge;
        var scene = Vm.Doc.Scene;
        var longest = Math.Max(scene.Width, scene.Height);
        var scale = longEdge <= 0 || longest <= longEdge ? 1.0 : longEdge / (double)longest;
        return IpcProtocol.Response.Success(new
        {
            PngBase64 = Vm.RenderFramePng(p.FrameIndex, longEdge),
            // What the caller is actually looking at. G12's art-director measured
            // the reason: at 768 a 1080p frame keeps its pose and loses 84% of the
            // fine dark pixels on a face, and a 4K frame loses eyebrows and eyes
            // outright — so an agent checking its own inbetween would see a
            // browless head whether it drew one or not. The cap stays, because it
            // is right for the thing the tool is mostly used for; what was
            // indefensible was that the reduction was *invisible*. Q27 recorded
            // "the request shows what cap each view got" as a condition of
            // choosing a cap at all, and this is that condition, here.
            Width = (int)Math.Round(scene.Width * scale),
            Height = (int)Math.Round(scene.Height * scale),
            SceneWidth = scene.Width,
            SceneHeight = scene.Height,
            Scale = Math.Round(scale, 3),
        });
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

        var inserted = Vm.InsertExternalInbetweens(layer.Id, p.AIndex, frames.Select(f => f.Strokes).ToList());
        return IpcProtocol.Response.Success(new { Inserted = inserted });
    }

    private sealed class DrawPayload
    {
        public int FrameIndex { get; set; }
        public string? LayerId { get; set; }
        public List<StrokeWire.StrokeDto> Strokes { get; set; } = [];
    }

    private sealed class KeyRef
    {
        public int FrameIndex { get; set; }
        public string? LayerId { get; set; }
        public string? Role { get; set; }
    }

    private sealed class ExposureStepRef
    {
        public int From { get; set; }
        public int To { get; set; }
        public int Step { get; set; }
        public string? LayerId { get; set; }
    }

    /// <summary>
    /// Parse a role name, refusing an unknown one rather than defaulting to
    /// <see cref="FrameRole.Key"/>. An agent that misspells "breakdown" should
    /// be told, not quietly given a key — the reply is the only feedback it has.
    /// </summary>
    private static FrameRole? RoleOf(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        null or "" or "key" => FrameRole.Key,
        "breakdown" => FrameRole.Breakdown,
        "inbetween" => FrameRole.Inbetween,
        _ => null,
    };

    private IpcProtocol.Response SetKey(IpcProtocol.Request request)
    {
        var p = Payload<KeyRef>(request);
        var layer = ResolveLayer(p.LayerId);
        if (p.FrameIndex < 0) return IpcProtocol.Response.Fail("frameIndex must be 0 or greater.");
        if (RoleOf(p.Role) is not { } role)
            return IpcProtocol.Response.Fail(
                $"Unknown role \"{p.Role}\" — use key, breakdown or inbetween.");

        var outcome = Vm.SetExternalKey(layer.Id, p.FrameIndex, role);
        if (outcome == ExternalKeyOutcome.Refused)
            return IpcProtocol.Response.Fail($"Layer \"{layer.Name}\" cannot be edited.");

        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            Role = role.ToString().ToLowerInvariant(),
            // Which of the two things happened, because they are different
            // edits with the same op and an agent cannot see the timeline.
            Created = outcome == ExternalKeyOutcome.Created,
            FrameCount = Vm.Doc.Scene.FrameCount,
        });
    }

    private IpcProtocol.Response ExtendExposure(IpcProtocol.Request request)
    {
        var p = Payload<FrameRef>(request);
        var layer = ResolveLayer(p.LayerId);
        if (p.FrameIndex < 0) return IpcProtocol.Response.Fail("frameIndex must be 0 or greater.");
        if (ExposureSheet.KeyIndexAtOrBefore(layer, p.FrameIndex) < 0)
            return IpcProtocol.Response.Fail(
                $"No drawing at or before frame {p.FrameIndex} on layer \"{layer.Name}\" to hold.");
        if (!Vm.ExtendExternalExposure(layer.Id, p.FrameIndex))
            return IpcProtocol.Response.Fail($"Layer \"{layer.Name}\" cannot be edited.");
        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            FrameCount = Vm.Doc.Scene.FrameCount,
        });
    }

    private IpcProtocol.Response ReduceExposure(IpcProtocol.Request request)
    {
        var p = Payload<FrameRef>(request);
        var layer = ResolveLayer(p.LayerId);
        if (p.FrameIndex < 0) return IpcProtocol.Response.Fail("frameIndex must be 0 or greater.");

        // A drawing is never removed, so the honest answer when the next cel
        // is keyed is "nothing to shorten" rather than a success that did
        // nothing — an agent retiming a run needs to know which it got.
        var next = p.FrameIndex + 1;
        var shortenable = next < layer.Cels.Count && layer.Cels[next].Frame is null;
        if (!shortenable)
            return IpcProtocol.Response.Fail(
                $"Frame {p.FrameIndex} on layer \"{layer.Name}\" is not held — "
                + "there is no hold after it to remove.");
        if (!Vm.ReduceExternalExposure(layer.Id, p.FrameIndex))
            return IpcProtocol.Response.Fail($"Layer \"{layer.Name}\" cannot be edited.");
        return IpcProtocol.Response.Success(new
        {
            p.FrameIndex,
            LayerId = layer.Id,
            FrameCount = Vm.Doc.Scene.FrameCount,
        });
    }

    private IpcProtocol.Response SetExposureStep(IpcProtocol.Request request)
    {
        var p = Payload<ExposureStepRef>(request);
        var layer = ResolveLayer(p.LayerId);
        if (p.Step < 1) return IpcProtocol.Response.Fail("step must be 1 or greater.");
        if (p.From < 0 || p.To < 0) return IpcProtocol.Response.Fail("from and to must be 0 or greater.");

        var grew = Vm.RetimeExternalExposure(layer.Id, p.From, p.To, p.Step);
        if (grew < 0) return IpcProtocol.Response.Fail($"Layer \"{layer.Name}\" cannot be edited.");
        return IpcProtocol.Response.Success(new
        {
            p.From,
            p.To,
            p.Step,
            LayerId = layer.Id,
            Grew = grew,
            FrameCount = Vm.Doc.Scene.FrameCount,
        });
    }

    private IpcProtocol.Response ListReferenceViews()
    {
        // The view-model's list rather than the document's own: in a project
        // the sheets an agent should see are the ones filed above the active
        // document, the same set the docker and the AI payload use.
        return IpcProtocol.Response.Success(new
        {
            Sheets = Vm.ReferenceSheetsView.Select(s => new
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
        var view = Vm.ReferenceSheetsView.SelectMany(s => s.Views).FirstOrDefault(v => v.Id == p.ViewId)
                   ?? throw new ArgumentException($"No reference view with id \"{p.ViewId}\".");
        return IpcProtocol.Response.Success(new { PngBase64 = Vm.RenderReferenceViewPng(view) });
    }

    private IpcProtocol.Response DrawStrokes(IpcProtocol.Request request)
    {
        var p = Payload<DrawPayload>(request);
        var layer = ResolveLayer(p.LayerId);
        var strokes = StrokeWire.FromWire(p.Strokes, SceneInfo());
        if (strokes.Count == 0)
            return IpcProtocol.Response.Fail("No usable strokes in the payload.");
        var added = Vm.AppendExternalStrokes(layer.Id, p.FrameIndex, strokes);
        return added == 0
            ? IpcProtocol.Response.Fail("No drawing at or before that frame on this layer.")
            : IpcProtocol.Response.Success(new { Added = added });
    }
}
