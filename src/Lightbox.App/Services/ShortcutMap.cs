using System.Text.Json;
using Avalonia.Input;
using Lightbox.App.Docking;

namespace Lightbox.App.Services;

/// <summary>
/// Where a shortcut is active.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of place, not a list of areas.</b> The old form named the
/// timeline and the layers docker individually, which meant a docker could only
/// own a binding if somebody had thought to add an enum member and a hover flag
/// for it — so eleven of the twelve dockers could not have one at all, and the
/// twelfth worked by hand-wiring. <see cref="Panel"/> carries the
/// <see cref="DockPanelId"/> instead, so every docker can own bindings and a new
/// docker needs nothing here.
/// </para>
/// <para>
/// <b><see cref="Canvas"/> is not "the drawing surface".</b> It is everywhere
/// that is not inside a docker — the canvas, the top bar, the tool rail, the
/// menu. That is what makes a general shortcut work in all of them, and it is
/// why the fallback below is written as it is.
/// </para>
/// </remarks>
public enum ShortcutContext
{
    /// <summary>Everywhere, unless the place you are in overrides it.</summary>
    Global,

    /// <summary>Anywhere outside a docker: the canvas, the bars, the rail, the menu.</summary>
    Canvas,

    /// <summary>One docker, named by <see cref="ShortcutDefinition.Panel"/>.</summary>
    Panel,

    /// <summary>
    /// The reference board, which is a window of its own rather than a docker.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not just another global binding</b> (B244). The board wants
    /// <c>Delete</c> for "take this off the wall" and <c>Ctrl+V</c> for "paste a
    /// picture", and both are already spoken for: <c>Delete</c> is deliberately
    /// *not* a general binding (Q82 — a destructive key must not fire on the
    /// canvas from somewhere that gives no hint the canvas is the target), and
    /// <c>Ctrl+V</c> is the timeline's. Registering them globally made two
    /// commands answer to one gesture with nothing to separate them, which is the
    /// one case this map calls unresolvable.
    /// <para>
    /// A separate top-level window <em>is</em> the separation — a key pressed
    /// there can reach nothing else — and saying so here is what lets the board's
    /// keys stay in the map, and therefore stay findable and rebindable, instead
    /// of being wired invisibly to its handler. Bindings in this scope are
    /// returned only when the board asks; nothing else can resolve them.
    /// </para>
    /// </remarks>
    Board,
}

/// <summary>
/// The place a key press happened, as the resolver needs it.
/// </summary>
/// <remarks>
/// A pair rather than a widened enum because the two halves answer different
/// questions — <em>what kind of place</em> and <em>which one</em> — and folding
/// them together is what produced an enum that had to grow per docker.
/// </remarks>
public readonly record struct ShortcutScope(ShortcutContext Context, DockPanelId Panel = default)
{
    /// <summary>Outside every docker: canvas, bars, rail, menu.</summary>
    public static readonly ShortcutScope Canvas = new(ShortcutContext.Canvas);

    /// <summary>Nowhere in particular — only general bindings apply.</summary>
    public static readonly ShortcutScope General = new(ShortcutContext.Global);

    /// <summary>Inside the reference board window.</summary>
    public static readonly ShortcutScope Board = new(ShortcutContext.Board);

    public static ShortcutScope In(DockPanelId panel) => new(ShortcutContext.Panel, panel);
}

/// <summary>One rebindable command: what it's called, where it lives, what triggers it.</summary>
public sealed class ShortcutDefinition(
    string id, string name, string category, KeyGesture? @default,
    ShortcutContext context = ShortcutContext.Global,
    DockPanelId panel = default,
    ViewModels.ToolId? momentaryTool = null)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>Grouping in the editor: Tools, Canvas, Timeline, Dockers.</summary>
    public string Category { get; } = category;

    /// <summary>
    /// The tool this shortcut holds for the duration of the key, or null for an
    /// ordinary binding.
    /// </summary>
    /// <remarks>
    /// <b>B176: which shortcuts are momentary is data here, not <c>if</c>s in
    /// the key handler</b> — a momentary binding wired straight into
    /// <c>OnKeyDown</c> is exactly the shortcut the Configure window cannot see
    /// or rebind. A tap still latches the tool the way the key always did; the
    /// hold-and-release round trip is the view model's
    /// (<c>BeginMomentaryTool</c>/<c>EndMomentaryTool</c>), which is what makes
    /// it drivable by a test with no window attached.
    /// </remarks>
    public ViewModels.ToolId? MomentaryTool { get; } = momentaryTool;

    /// <summary>Where this binding fires — the same key can mean different things per area.</summary>
    public ShortcutContext Context { get; } = context;

    /// <summary>Which docker, when <see cref="Context"/> is <see cref="ShortcutContext.Panel"/>.</summary>
    public DockPanelId Panel { get; } = panel;

    /// <summary>This binding's place, as the resolver compares them.</summary>
    public ShortcutScope Scope => new(Context, Panel);

    /// <summary>
    /// Where this binding applies, in the artist's words — the heading the
    /// Configure window groups by.
    /// </summary>
    /// <remarks>
    /// Derived from the scope rather than stored, so a binding cannot be filed
    /// under a heading it does not actually belong to. That is the same reason
    /// the roadmap's checkboxes are derived: a label that can disagree with the
    /// thing it labels eventually does.
    /// </remarks>
    public string ScopeName => Context switch
    {
        ShortcutContext.Canvas => "Canvas",
        ShortcutContext.Panel => Docking.DockPanels.TitleOf(Panel),
        ShortcutContext.Board => "Reference board",
        _ => "Everywhere",
    };

    public KeyGesture? Default { get; } = @default;

    /// <summary>The active binding (null = unbound).</summary>
    public KeyGesture? Current { get; internal set; } = @default;

    public string GestureText => Current?.ToString() ?? "—";
}

/// <summary>
/// The application's rebindable shortcuts: defaults, user overrides
/// (persisted to shortcuts.json), reverse lookup for dispatch, and conflict
/// detection for the editor.
/// </summary>
public sealed class ShortcutMap
{
    public static string StorePath =>
        Path.Combine(Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "shortcuts.json");

    /// <summary>Test seam.</summary>
    public string? StorePathOverride { get; init; }

    private readonly List<ShortcutDefinition> _definitions;

    public IReadOnlyList<ShortcutDefinition> Definitions => _definitions;

    public ShortcutMap()
    {
        static KeyGesture G(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);
        _definitions =
        [
            // Photoshop's two, unchanged, because an artist arrives with them
            // in their hands and neither letter was taken here.
            new("image.resizeCanvas", "Resize canvas", "Image",
                G(Key.C, KeyModifiers.Control | KeyModifiers.Alt)),
            new("image.resizeImage", "Resize image", "Image",
                G(Key.I, KeyModifiers.Control | KeyModifiers.Alt)),
            // The two crops (Q128), and no default gesture on either — which is
            // allowed, for lines.recolour's stated reason: Photoshop binds
            // neither of these itself, so there is no reflex to honour, and the
            // menu is the way in. Registered anyway, because a command that is
            // not in this map cannot be found, searched or rebound — which is
            // the whole failure this map exists for.
            new("image.cropToSelection", "Crop to selection", "Image", null),
            new("image.trimToDrawing", "Trim to drawing", "Image", null),
            // B221: B, F and V join E and I as spring-loaded. The machinery has
            // been tool-agnostic since B176 and two of thirteen keys used it,
            // which made the tap-or-hold rule read as a quirk of the eraser
            // rather than as how the keyboard works. These three are the ones
            // where borrowing is a real gesture and the tool carries no modal
            // state to strand on release — holding V to shift something and
            // letting go back into the brush is the one artists reach for most.
            //
            // Deliberately not S: repeat-S cycles the selection variants, so a
            // hold would have to mean a third thing on a key that already means
            // two. And deliberately not the pen, the arrows or the width tool —
            // those carry a session a released key would leave parked, which is
            // the same reason MainViewModel.Momentary's table will not borrow
            // *from* them.
            new("tool.brush", "Brush", "Tools", G(Key.B),
                momentaryTool: ViewModels.ToolId.Brush),
            new("tool.eraser", "Eraser", "Tools", G(Key.E),
                momentaryTool: ViewModels.ToolId.Eraser),
            new("tool.fill", "Fill", "Tools", G(Key.F),
                momentaryTool: ViewModels.ToolId.Fill),
            new("tool.gradient", "Gradient", "Tools", G(Key.G)),
            // C, which is Photoshop's key for it and was free here. Not
            // spring-loaded like B, E, F, V and I: the crop carries modal state
            // — a frame that has been dragged — so a hold-and-release would
            // strand it, which is exactly the reason S and the pen are excluded
            // from that list too.
            new("tool.crop", "Crop (frame the page)", "Tools", G(Key.C)),
            new("tool.select", "Select / next variant", "Tools", G(Key.S)),
            new("tool.move", "Move (drawing and guides)", "Tools", G(Key.V),
                momentaryTool: ViewModels.ToolId.Move),
            // The only tool that had no key at all. U is Photoshop's shape tool
            // and was free here — the shape *variant* stays a tool option, like
            // the select tool's, so one letter covers all four.
            new("tool.shape", "Shape (line, rectangle, ellipse, polygon)", "Tools", G(Key.U)),
            // Illustrator's black arrow is V, which Move already has here and
            // has had for longer. A is Illustrator's other pointer and is free,
            // so the pair stays adjacent in the hand even though the letters do
            // not match Adobe's exactly.
            new("tool.arrow", "Arrow (select lines, guides, symbols)", "Tools", G(Key.A)),
            // Illustrator's white arrow is A, which the black one above already
            // has here and which the manual has documented since it shipped.
            // N for node is free and mnemonic; the letters matter less than the
            // binding being findable and reboundable, which is what this map is.
            new("tool.directselect", "Direct select (reshape a line's points)", "Tools", G(Key.N)),
            // P, which is the one letter in the vector set that does match every
            // other application — nothing here had claimed it, so there was no
            // reason to spend an artist's muscle memory.
            new("tool.pen", "Pen (draw a line by its points)", "Tools", G(Key.P)),
            // W for width, and free like P was. Illustrator puts this on Shift+W
            // because W is its own Blend tool; nothing here wanted the letter.
            // Photoshop's letter, and it was free here: Ctrl+T is the transform,
            // which is the same key meaning the same idea one modifier along.
            new("tool.text", "Text (type on the canvas)", "Tools", G(Key.T)),
            new("tool.bone", "Bone (rig, pose, weights)", "Tools", G(Key.K)),
            new("armature.weightPaint", "Toggle the weight brush (bone tool)", "Canvas", G(Key.K, KeyModifiers.Control | KeyModifiers.Shift)),
            new("armature.posingMode", "Toggle posing (bone tool: bind or pose)", "Canvas", G(Key.K, KeyModifiers.Shift)),
            // One press into a weight-paint mode from anywhere — the answer to
            // "start subtracting" costing a tool change, a toggle and a
            // dropdown. Shift'ed digits because the bare ones flip pose keys
            // (timeline.prevKey/nextKey), and 1/2/3 order matches the mode
            // dropdown's.
            new("armature.weightAdd", "Weight brush: paint influence (Add)", "Canvas",
                G(Key.D1, KeyModifiers.Shift)),
            new("armature.weightSubtract", "Weight brush: erase influence (Subtract)", "Canvas",
                G(Key.D2, KeyModifiers.Shift)),
            new("armature.weightSmooth", "Weight brush: smooth influence", "Canvas",
                G(Key.D3, KeyModifiers.Shift)),
            // No default gesture, for `lines.recolour`'s reason: Delete already
            // reaches it — `select.clear` asks the armature first while the
            // Bone tool is in hand — and being here is what lets an artist give
            // it a dedicated key that works from any tool.
            new("armature.deleteBone", "Delete the selected bone", "Canvas", null),
            // No default gesture for the same reason as the two above: the
            // button in the bone options is the way in, and being registered
            // is what lets an artist working a cycle put it under a key —
            // which is the whole point of a command pressed once per drawing.
            new("armature.insertPoseDrawing", "Keep this pose as a drawing", "Canvas", null),
            new("tool.width", "Width (make a line heavier or lighter)", "Tools", G(Key.W)),
            // No default gesture, like lines.recolour above and for the same
            // reason: the sensible letters are taken, the button in the arrow's
            // options is the way in, and being here is what lets an artist bind
            // it to whatever they have free.
            new("lines.simplify", "Simplify the isolated line", "Tools", null),
            // What the arrow can then do. Registered rather than wired straight
            // to the key handler, because a command that is not in here cannot be
            // found, searched or rebound — which is the failure this whole map
            // exists for. Nudging is absent here on purpose and is not missing:
            // `canvas.nudge*` below is already the canvas-scoped binding for "move
            // what is selected", and `NudgeSelection` asks the line selection
            // before the pixel one. A second set of keys would mean an artist
            // rebinding one and finding the other still on the old key.
            // Canvas-scoped, not global, and the test suite is what said so:
            // `docker.deleteLayer` already owns Delete over the Layers docker, and
            // a global binding on the same key shadows it. So this is another of
            // the context twins below — Delete over the canvas removes lines,
            // Delete over the layer list removes a layer, and each is what an
            // artist would expect from where their pointer is.
            // B173 took Delete off this and gave it to `select.clear`, which
            // falls back to here when no region is selected — so Delete still
            // deletes lines in the case it used to, and the entry keeps its id
            // so an artist's rebinding survives. It has no default now for
            // `lines.recolour`'s reason, stated below: that is allowed.
            new("lines.delete", "Delete the selected lines (canvas)", "Tools", null),
            // No default gesture, and that is allowed rather than an oversight:
            // every sensible letter is taken, and the button in the arrow's
            // options bar is the way in. Being here is what lets an artist bind
            // it to whatever they have free.
            new("lines.recolour", "Recolour the selected lines", "Tools", null),
            // Brush sizing by eye. It was Shift+drag on the canvas until Shift
            // became the constraint key on every tool; these are the two keys
            // every other application uses for it.
            new("brush.smaller", "Smaller brush", "Tools", G(Key.OemOpenBrackets)),
            new("brush.larger", "Larger brush", "Tools", G(Key.OemCloseBrackets)),
            new("select.all", "Select all", "Tools", G(Key.A, KeyModifiers.Control)),
            new("select.none", "Deselect", "Tools", G(Key.D, KeyModifiers.Control)),
            new("select.invert", "Invert selection", "Tools", G(Key.I, KeyModifiers.Control | KeyModifiers.Shift)),
            new("select.cancel", "Cancel polygon", "Tools", G(Key.Escape)),
            // B173. Canvas-scoped for the reason the Delete twins above are:
            // `docker.deleteLayer` and `docker.clearLayer` own the same two
            // keys over the Layers docker, and each is what an artist expects
            // from where their pointer is. Delete clears what is inside the
            // region, Backspace floods it with the background — the two differ
            // in what is left behind, which is why both exist rather than one.
            new("select.clear", "Clear the selection's contents", "Tools",
                G(Key.Delete), ShortcutContext.Canvas),
            new("select.fillBackground", "Fill the selection with the background", "Tools",
                G(Key.Back), ShortcutContext.Canvas),
            new("color.swap", "Swap foreground and background", "Tools", G(Key.X)),
            new("color.reset", "Reset to black over white", "Tools", G(Key.D)),

            new("canvas.undo", "Undo", "Canvas", G(Key.Z, KeyModifiers.Control)),
            new("canvas.redo", "Redo", "Canvas", G(Key.Y, KeyModifiers.Control)),
            // The same command on a second key, because both reflexes are real:
            // Ctrl+Y is Photoshop's Windows default and Ctrl+Shift+Z is
            // Photoshop's other binding, Krita's, and the muscle memory of
            // everyone who learned redo as "shifted undo". A second definition
            // rather than a special case in the key handler, so the Configure
            // window can see it, rebind it, or clear it like any other.
            new("canvas.redoAlt", "Redo (second key)", "Canvas",
                G(Key.Z, KeyModifiers.Control | KeyModifiers.Shift)),
            new("canvas.transform", "Transform (move/scale/rotate/perspective)", "Tools", G(Key.T, KeyModifiers.Control)),
            new("canvas.mirror", "Mirror view", "Canvas", G(Key.M)),
            // Photoshop's three, on Photoshop's keys. Rulers are where a guide
            // is made, so the one that makes them reachable comes first.
            new("canvas.rulers", "Show rulers", "Canvas", G(Key.R, KeyModifiers.Control)),
            new("canvas.showGuides", "Show guides", "Canvas", G(Key.OemSemicolon, KeyModifiers.Control)),
            new("canvas.lockGuides", "Lock guides", "Canvas",
                G(Key.OemSemicolon, KeyModifiers.Control | KeyModifiers.Alt)),
            // The guide lock's twin, on the same modifiers (Q108): both answer
            // "pin the chrome I have arranged so I stop knocking it out of
            // place", and an artist who knows one should be able to guess the
            // other.
            new("canvas.lockReferences", "Lock references", "Canvas",
                G(Key.R, KeyModifiers.Control | KeyModifiers.Alt)),
            new("canvas.resetView", "Reset view", "Canvas", G(Key.D0)),
            // No default gesture, like lines.recolour and for its reason: the
            // sensible letters are taken (M mirrors, T transforms), the
            // checkbox on the timeline bar is the way in, and being here is
            // what lets an artist bind it to whatever they have free.
            new("canvas.motionTrail", "Show motion trail (path and spacing)", "Canvas", null),
            // No default gestures either, for motionTrail's reason — and both
            // switch the trail on with them, so neither ever toggles nothing.
            new("canvas.motionArc", "Show motion arc (fitted arc and off-arc drawings)", "Canvas", null),
            new("canvas.arcPrediction", "Show arc prediction (where the next drawing goes)", "Canvas", null),

            new("timeline.playPause", "Play / pause", "Timeline", G(Key.Space)),
            new("timeline.prevFrame", "Previous frame (scrub)", "Timeline", G(Key.Left), ShortcutContext.Panel, DockPanelId.Timeline),
            new("timeline.nextFrame", "Next frame (scrub)", "Timeline", G(Key.Right), ShortcutContext.Panel, DockPanelId.Timeline),
            new("timeline.prevKey", "Flip to previous key", "Timeline", G(Key.D1)),
            new("timeline.nextKey", "Flip to next key", "Timeline", G(Key.D2)),
            new("timeline.copyCel", "Copy cel", "Timeline", G(Key.C, KeyModifiers.Control)),
            new("timeline.cutCel", "Cut cel", "Timeline", G(Key.X, KeyModifiers.Control)),
            new("timeline.pasteCel", "Paste cel", "Timeline", G(Key.V, KeyModifiers.Control)),

            // Everything the board window does (Q87), in its own scope. Here
            // rather than wired straight to the window's key handler, because a
            // command that is not in this map cannot be found, searched or
            // rebound — which is the failure the whole map exists for. The scope
            // is what lets them stay here (B244): Delete and Ctrl+V mean what
            // they obviously mean on a board without becoming a second answer to
            // a gesture the canvas or the timeline already owns.
            new("reference.arrange", "Auto-arrange the board", "Reference",
                G(Key.R, KeyModifiers.Control | KeyModifiers.Shift), ShortcutContext.Board),
            new("reference.fit", "Fit everything on screen", "Reference",
                G(Key.F, KeyModifiers.Control | KeyModifiers.Shift), ShortcutContext.Board),
            new("reference.front", "Bring the picture forward", "Reference",
                G(Key.Up, KeyModifiers.Control | KeyModifiers.Shift), ShortcutContext.Board),
            new("reference.back", "Send the picture behind", "Reference",
                G(Key.Down, KeyModifiers.Control | KeyModifiers.Shift), ShortcutContext.Board),
            new("reference.remove", "Take the picture off the board", "Reference",
                G(Key.Delete), ShortcutContext.Board),
            new("reference.paste", "Paste a picture onto the board", "Reference",
                G(Key.V, KeyModifiers.Control), ShortcutContext.Board),
            // The one that is not board-scoped, because it is how the board is
            // reached from the art in the first place.
            new("reference.board", "Open the reference board", "Reference",
                G(Key.B, KeyModifiers.Control | KeyModifiers.Shift)),

            new("docker.deleteLayer", "Delete layer", "Dockers", G(Key.Delete), ShortcutContext.Panel, DockPanelId.Layers),
            new("docker.clearLayer", "Blank layer content", "Dockers", G(Key.Back), ShortcutContext.Panel, DockPanelId.Layers),
            // Photoshop's and Krita's key, and global like theirs: merging is
            // asked for from the canvas at least as often as from the docker.
            new("docker.mergeDown", "Merge layer down", "Dockers", G(Key.E, KeyModifiers.Control)),
            // Photoshop's key, and global like the merge: clipping is asked
            // for from the canvas while colouring, not only from the docker.
            new("docker.clipToBelow", "Clip layer to the one below", "Dockers",
                G(Key.G, KeyModifiers.Control | KeyModifiers.Alt)),
            // No default gesture, the timeline.deleteColumn reason: there is
            // no convention to borrow, and guessing one costs somebody their
            // key. Bindable is the requirement.
            new("docker.editMask", "Paint the active layer's mask", "Dockers", null),
            new("docker.effects", "Show or hide the Effects panel", "Dockers", null),

            // Context twins: the same key does area-appropriate things.
            // General, not canvas-scoped. Scoped, it did nothing over any docker —
            // and the rule this map is built on says a general binding reaches
            // every docker that has no answer of its own. The timeline has one
            // (insertKey, below), so I still means "insert a key" there and
            // "eyedropper" everywhere else, which is the whole scheme in one pair.
            //
            // The id keeps the `canvas.` prefix it shipped with. Renaming it to
            // `tool.picker` would read better and would silently drop the rebind
            // of anyone who had changed it, because shortcuts.json is keyed by id.
            new("canvas.pickColor", "Color picker tool", "Tools", G(Key.I),
                momentaryTool: ViewModels.ToolId.Picker),
            new("timeline.insertKey", "Insert keyframe at playhead (timeline)", "Timeline", G(Key.I), ShortcutContext.Panel, DockPanelId.Timeline),
            // Q108. The operation is as old as DocumentEditor.DeleteFrame and was
            // reachable only from one 🗑 button, so it could not be bound,
            // searched or found — which is why it read as missing. No default
            // gesture: Delete already means four context-dependent things (the
            // twins above), and taking a fifth reading of it inside the timeline
            // would be guessing at what an artist wants there. Bindable is what
            // was actually missing.
            new("timeline.deleteColumn", "Delete column (this frame, every layer)", "Timeline", null, ShortcutContext.Panel, DockPanelId.Timeline),
            // The key clipboard crosses kinds — camera keys, pose keys, cels —
            // where Ctrl+C is the cel clipboard's. No default gesture for the
            // deleteColumn reason: taking a second reading of copy inside the
            // timeline would be guessing; bindable is the requirement.
            new("timeline.copyKeys", "Copy selected keys (camera, pose, cels)", "Timeline", null, ShortcutContext.Panel, DockPanelId.Timeline),
            new("timeline.pasteKeys", "Paste keys at playhead", "Timeline", null, ShortcutContext.Panel, DockPanelId.Timeline),
            new("canvas.nudgeLeft", "Nudge selection left", "Canvas", G(Key.Left), ShortcutContext.Canvas),
            new("canvas.nudgeRight", "Nudge selection right", "Canvas", G(Key.Right), ShortcutContext.Canvas),
            new("canvas.nudgeUp", "Nudge selection up", "Canvas", G(Key.Up), ShortcutContext.Canvas),
            new("canvas.nudgeDown", "Nudge selection down", "Canvas", G(Key.Down), ShortcutContext.Canvas),
            new("docker.layerAbove", "Select the layer above", "Dockers", G(Key.Up), ShortcutContext.Panel, DockPanelId.Layers),
            new("docker.layerBelow", "Select the layer below", "Dockers", G(Key.Down), ShortcutContext.Panel, DockPanelId.Layers),

            // Registered rather than written onto the menu items, which is where Ctrl+S
            // lived and why it could not be rebound — and why nobody noticed that Save as
            // had no gesture at all. ShortcutRegistrationTests now fails a gesture that is
            // declared in XAML and missing from here.
            new("file.save", "Save", "File", G(Key.S, KeyModifiers.Control)),
            new("file.saveAs", "Save as…", "File", G(Key.S, KeyModifiers.Control | KeyModifiers.Shift)),

            // Registered rather than written onto the menu items, for the same
            // reason as Save directly above. Ctrl+Alt+S reads as "a bigger
            // save", which a kept version is.
            new("file.saveVersion", "Save version (keep a copy in the project history)", "File",
                G(Key.S, KeyModifiers.Control | KeyModifiers.Alt)),
            new("file.versionHistory", "Version history (view and revert)", "File",
                G(Key.H, KeyModifiers.Control | KeyModifiers.Alt)),

            // Saving a picture rather than a document. Ctrl+Alt+Shift+S for two
            // reasons that agree: it continues the local family, where every save
            // is S with one more modifier than the last, and it is what Photoshop
            // binds Save for Web to — which is the ancestor of this exact command.
            // Ctrl+Shift+E, the other obvious candidate and Krita's own, is taken
            // by the fluid effects window.
            new("file.saveAsImage", "Save as image (PNG, JPEG, WebP)", "File",
                G(Key.S, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)),

            // B58. The rig had no shortcut, no menu item and no binding, so the mode
            // could not be switched on and none of the editing behind it was
            // reachable. `Ctrl+R` is taken by the rulers, so this is the next key
            // that reads as "rig" and is free.
            new("canvas.rigEditMode", "Rig edit mode (anchors and hitboxes)", "Canvas",
                G(Key.K, KeyModifiers.Control)),

            // B61. The directory watch does this by itself, so this is the way
            // out when it cannot be armed at all — a network share, or a platform
            // with no inotify. ProjectWatcher.Watch swallows that failure on
            // purpose so a project still opens, and a swallowed failure with no
            // manual path is just the bug again with better manners.
            new("project.refresh", "Re-read the project from disk", "Dockers", G(Key.F5)),

            // Q29's other surface. Registered here rather than written onto the
            // menu item, which is the trap Ctrl+S fell into: a gesture on a
            // MenuItem cannot be seen, searched or rebound, and nobody noticed
            // Save as had none at all. `Ctrl+P` is free — nothing prints.
            new("project.window", "Project window (structure, status, assets)", "File",
                G(Key.P, KeyModifiers.Control)),

            // The character library's window, on the project window's footing:
            // a registered command rather than a gesture on a button, so the
            // picker button on the project panel stays the fast path and this
            // is what an artist can search and rebind. No default gesture, for
            // canvas.motionTrail's reason — the button is the way in, and
            // being here is what lets a key be chosen freely.
            new("project.libraryWindow", "Character library (browse and import)", "File", null),

            // The fluid effects window, on the same footing as the project window
            // and the board: a window of its own, opened by a registered command
            // rather than by a gesture written onto a menu item. Ctrl+E alone is
            // Merge down; Ctrl+Shift+E is free, and reading the two as a pair is
            // no worse than any other letter.
            //
            // Named to match the menu word for word. The editor is searched, and
            // an artist who saw "Fluid effects" on the menu and types it has to
            // find this — a registry entry under a second name for the same
            // command is the same failure as no entry at all, one step later.
            new("effects.window", "Fluid effects window (fire, smoke, water)", "Effects",
                G(Key.E, KeyModifiers.Control | KeyModifiers.Shift)),

            // B126/B254, and a key rather than a menu item on purpose: the thing
            // being measured is what the pointer does while it hovers the canvas,
            // so a trace you have to reach a menu to stop records the trip to the
            // menu as its final seconds. A key leaves the pen where it is. F9 is
            // free (F5 is the project refresh, and nothing else claims a function
            // key), and it is rebindable like everything else here because a
            // tablet's own driver may have taken it.
            new("diagnostics.inputTrace", "Record an input trace (pen problems)", "Help",
                G(Key.F9)),
        ];
    }

    /// <summary>
    /// The command a key event triggers in the given context, or null. A
    /// context-specific binding beats a global one for the same keys.
    /// </summary>
    /// <summary>
    /// The command a key press means, in the place it happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The place wins if it has an answer; otherwise the wider place does.</b>
    /// That one sentence is the whole scheme, and it is what makes <c>I</c>
    /// insert a keyframe over the timeline and pick a colour everywhere else —
    /// including over every docker that has no <c>I</c> of its own, which is the
    /// case the old form could not express because a docker had to be named in
    /// an enum before it could have bindings at all.
    /// </para>
    /// <para>
    /// <b>The chain over a docker is panel → canvas → general (Q82).</b> The
    /// canvas rung is the part that was added, and it was a decision rather than
    /// an oversight: it used to stop at the docker's edge, so <c>Delete</c> over
    /// the Colour docker did nothing at all. It now clears the selection's
    /// contents, because the artist's model is "the docker overrides, everything
    /// else still works". What that costs is written down in
    /// <c>QUESTIONS.md</c> → Q82: a destructive key now fires on the canvas from
    /// a place that gives no hint the canvas is the target.
    /// </para>
    /// <para>
    /// <b>Only a docker gets the canvas rung.</b> <see cref="ShortcutScope.General"/>
    /// means "nowhere in particular", and letting canvas bindings answer there
    /// would make a marquee-clearing <c>Delete</c> reachable from a scope that is
    /// deliberately the narrowest one there is.
    /// </para>
    /// </remarks>
    public string? IdFor(KeyEventArgs e, ShortcutScope scope)
    {
        ShortcutDefinition? canvas = null, general = null;
        foreach (var d in _definitions)
        {
            if (d.Current is not { } g || g.Key != e.Key || g.KeyModifiers != e.KeyModifiers) continue;
            if (d.Scope == scope) return d.Id;
            // A board binding is reachable from the board and nowhere else: its
            // window is the separation that lets it share a gesture at all
            // (B244). Collecting it as a fallback would undo exactly that.
            if (d.Context == ShortcutContext.Board) continue;
            if (d.Context == ShortcutContext.Canvas) canvas ??= d;
            else if (d.Context == ShortcutContext.Global) general ??= d;
        }
        if (scope.Context == ShortcutContext.Panel && canvas is not null) return canvas.Id;
        return general?.Id;
    }

    /// <inheritdoc cref="IdFor(KeyEventArgs, ShortcutScope)"/>
    public string? IdFor(KeyEventArgs e) => IdFor(e, ShortcutScope.General);

    public ShortcutDefinition? Find(string id) => _definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// The OTHER command already bound to this gesture in the SAME scope —
    /// the case <see cref="IdFor(KeyEventArgs, ShortcutScope)"/> cannot resolve,
    /// because both are reachable from one place with nothing to separate them.
    /// </summary>
    /// <remarks>
    /// A general binding and a scoped one sharing a gesture are deliberately
    /// <i>not</i> a conflict: the resolver picks the more specific, so the
    /// scoped command wins in its own area and the general one applies
    /// everywhere else. That is the whole rule this map is built on — general
    /// <c>I</c> is the eyedropper, and over the timeline it is insert-key — so
    /// reporting it as something to resolve would ask an artist to undo the
    /// design. Only an unresolvable tie is a conflict.
    /// <para>
    /// A canvas binding and a panel one are not a conflict either, for the same
    /// reason and now for a second one: since Q82 the canvas is a rung in the
    /// fallback chain, so <c>Delete</c> deletes a layer over the Layers docker
    /// and clears the selection everywhere else. Both are reachable, which is
    /// what "resolvable" means.
    /// </para>
    /// </remarks>
    public ShortcutDefinition? ConflictWith(string id, KeyGesture gesture)
    {
        if (Find(id) is not { } self) return null;
        return _definitions.FirstOrDefault(d =>
            d.Id != id
            && d.Current is { } g && g.Key == gesture.Key && g.KeyModifiers == gesture.KeyModifiers
            && d.Scope == self.Scope);
    }

    /// <summary>Bind a gesture (stealing it from a conflicting command must be the CALLER's explicit choice).</summary>
    public void Assign(string id, KeyGesture? gesture, bool unbindConflicts = false)
    {
        if (Find(id) is not { } def) return;
        if (gesture is not null && unbindConflicts && ConflictWith(id, gesture) is { } other)
        {
            other.Current = null;
        }
        def.Current = gesture;
        Save();
    }

    public void ResetToDefaults()
    {
        foreach (var def in _definitions) def.Current = def.Default;
        Save();
    }

    public void Load()
    {
        try
        {
            var path = StorePathOverride ?? StorePath;
            if (!File.Exists(path)) return;
            var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (overrides is null) return;
            foreach (var (id, text) in overrides)
            {
                if (Find(id) is not { } def) continue;
                def.Current = string.IsNullOrWhiteSpace(text) ? null : TryParse(text);
            }
        }
        catch
        {
            // a corrupt store must never block input — defaults stay active
        }
    }

    private void Save()
    {
        try
        {
            var path = StorePathOverride ?? StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var overrides = _definitions
                .Where(d => !Equals(d.Current?.ToString(), d.Default?.ToString()))
                .ToDictionary(d => d.Id, d => d.Current?.ToString() ?? "");
            File.WriteAllText(path, JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best-effort persistence
        }
    }

    private static KeyGesture? TryParse(string text)
    {
        try
        {
            return KeyGesture.Parse(text);
        }
        catch
        {
            return null;
        }
    }
}
