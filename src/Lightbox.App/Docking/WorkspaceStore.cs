using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Projects;

namespace Lightbox.App.Docking;

/// <summary>One named workspace: a layout and what to call it.</summary>
/// <param name="BuiltIn">
/// A workspace that ships with the app. It can be selected and edited, but
/// not deleted — deleting the last workspace would leave nothing to fall back
/// to, and "reset" needs something to reset *to*.
/// </param>
public sealed class Workspace
{
    public string Name { get; set; } = "Workspace";

    public bool BuiltIn { get; set; }

    /// <summary>
    /// The project type this workspace is the default for, or null for a
    /// plain named one. Only ever set on built-ins.
    /// </summary>
    public ProjectType? DefaultFor { get; set; }

    public DockLayout Layout { get; set; } = DockLayout.Default();

    public Workspace Clone() => new()
    {
        Name = Name,
        BuiltIn = BuiltIn,
        DefaultFor = DefaultFor,
        Layout = Layout.Clone(),
    };
}

/// <summary>
/// Every workspace the user has, and the one they are in.
/// </summary>
/// <remarks>
/// Workspaces are <b>global</b>, never stored in a project. A layout is a
/// property of the person, not of the artwork: the same artist wants the same
/// panels in the same places whichever character they opened, and a workspace
/// travelling inside a project file would mean opening someone else's file
/// rearranges your screen.
///
/// Project type still matters, but only as a starting point: each type has a
/// built-in workspace, and creating a project of that type is a moment where
/// taking those defaults is a reasonable offer. Taking it is the user's
/// choice, made at that moment, not something the project remembers.
/// </remarks>
public sealed class WorkspaceStore
{
    public List<Workspace> Workspaces { get; set; } = [];

    /// <summary>The workspace currently applied, by name.</summary>
    public string Current { get; set; } = "";

    public Workspace? Find(string name) =>
        Workspaces.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The built-in workspace for a project type, or the general one.</summary>
    public Workspace DefaultFor(ProjectType? type) =>
        Workspaces.FirstOrDefault(w => w.BuiltIn && w.DefaultFor == type)
        ?? Workspaces.FirstOrDefault(w => w.BuiltIn && w.DefaultFor is null)
        ?? Workspaces.First();

    /// <summary>
    /// Store a layout under a name, replacing a user workspace of that name.
    /// A built-in is never overwritten <em>by this route</em> — "save as" is a
    /// request for a new workspace — so saving over one forks it as
    /// "Name (edited)". Overwriting in place is <see cref="Update"/>'s job.
    /// </summary>
    public Workspace Save(string name, DockLayout layout)
    {
        name = name.Trim();
        if (name.Length == 0) name = "Workspace";
        if (Find(name) is { BuiltIn: false } existing)
        {
            existing.Layout = layout.Clone();
            Current = existing.Name;
            return existing;
        }
        if (Find(name) is { BuiltIn: true }) name = Unique(name + " (edited)");
        var saved = new Workspace { Name = name, Layout = layout.Clone() };
        Workspaces.Add(saved);
        Current = saved.Name;
        return saved;
    }

    /// <summary>
    /// Overwrite the named workspace in place, built-ins included — what "save
    /// current workspace" means. Reset still works on an overwritten built-in
    /// because <see cref="ShippedLayout"/> answers from the code, not the file.
    /// Null when there is no workspace of that name.
    /// </summary>
    public Workspace? Update(string name, DockLayout layout)
    {
        if (Find(name) is not { } existing) return null;
        existing.Layout = layout.Clone();
        Current = existing.Name;
        return existing;
    }

    /// <summary>
    /// The layout a built-in shipped with — what reset restores — or null for
    /// a name that is not a built-in's. Always available whatever was saved
    /// over the stored copy, because it is rebuilt from <see cref="Default"/>
    /// rather than read from any file.
    /// </summary>
    public static DockLayout? ShippedLayout(string name) =>
        Default().Find(name) is { BuiltIn: true } shipped ? shipped.Layout : null;

    private string Unique(string wanted)
    {
        if (Find(wanted) is null) return wanted;
        for (var n = 2; ; n++)
        {
            var candidate = $"{wanted} {n}";
            if (Find(candidate) is null) return candidate;
        }
    }

    /// <summary>Remove a user workspace. Built-ins refuse; so does the last one.</summary>
    public bool Delete(string name)
    {
        if (Find(name) is not { BuiltIn: false } target) return false;
        if (Workspaces.Count <= 1) return false;
        Workspaces.Remove(target);
        if (string.Equals(Current, name, StringComparison.OrdinalIgnoreCase))
        {
            Current = Workspaces[0].Name;
        }
        return true;
    }

    // ---- the built-ins -------------------------------------------------------

    /// <summary>
    /// The workspaces that ship with the app: one per project type, and a
    /// general one for a document that belongs to no project.
    /// </summary>
    /// <remarks>
    /// Each is the panels that type of work actually needs. An illustration has
    /// no timeline and wants the palette; a storyboard wants the timeline and
    /// nothing else; game art wants the layer stack and the palette because a
    /// sprite is made of both. These are starting points, not rules — every one
    /// of them is editable, and the point of saving your own is that you
    /// disagreed.
    /// </remarks>
    /// <summary>
    /// The three ways of choosing a colour, in one slot.
    /// </summary>
    /// <remarks>
    /// <b>The rule these arrangements follow: tab what you use alternately,
    /// never what you use together.</b> Colour, palette and gradient are three
    /// answers to one question and you want one of them at a time — layers and
    /// the canvas are consulted *while* doing something else, so tabbing those
    /// would trade a scroll for a click on every stroke.
    ///
    /// What tabs actually buy here is not compression. It is that a workspace
    /// can now <em>offer</em> the palette and the gradient at all: both used to
    /// be hidden in most arrangements because neither was worth a slot of
    /// sidebar, and a tab costs a word in a header.
    /// </remarks>
    private static readonly DockPanelId[] Colour =
        [DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient, DockPanelId.Channels];

    /// <summary>
    /// The timeline family (Q58): the track view in front, the exposure sheet
    /// and the graph editor tabbed behind it — three views over one set of
    /// records, as the reference's strip draws them.
    /// </summary>
    /// <summary>
    /// What the work is made of and what it is made with (Q109): the project
    /// tree, the reference sheets for the subject, and the options of the tool
    /// in hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every built-in ships it</b>, Illustration included. The Project tab is
    /// already absent until a document belongs to a project, so a single-image
    /// arrangement shows reference and tool options and grows the third tab the
    /// day work is filed — rather than the arrangement itself being different
    /// for one workspace, which is the kind of rule nobody can recall later.
    /// </para>
    /// <para>
    /// <b>Tool options is docked here rather than waiting for the gear.</b> It
    /// was hidden by default on the argument that a panel should arrive when it
    /// is first wanted; a tab costs a word in a header instead of a strip of
    /// sidebar, which is the same trade that put the palette and the gradient in
    /// front of people (Q109). The gear now brings the tab forward.
    /// </para>
    /// </remarks>
    private static readonly DockPanelId[] ProjectFamily =
        [DockPanelId.Project, DockPanelId.Sheets, DockPanelId.ToolOptions];

    private static readonly DockPanelId[] TimelineFamily =
        [DockPanelId.Timeline, DockPanelId.Xsheet, DockPanelId.GraphEditor];

    public static WorkspaceStore Default()
    {
        var store = new WorkspaceStore();
        store.Workspaces.Add(new Workspace
        {
            Name = "Default",
            BuiltIn = true,
            Layout = DockLayout.Default(),
        });
        store.Workspaces.Add(Built("Illustration", ProjectType.Illustration,
            right: [[DockPanelId.Navigator], ProjectFamily, [DockPanelId.Layers], Colour],
            bottom: [],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.EraserOptions, QuickBarCatalog.SelectOptions,
                    QuickBarCatalog.FillOptions, QuickBarCatalog.GradientOptions,
                    QuickBarCatalog.ShapeOptions, QuickBarCatalog.GuideOptions]));
        store.Workspaces.Add(Built("Animation", ProjectType.Animation,
            right: [[DockPanelId.Navigator], ProjectFamily, [DockPanelId.Layers], Colour],
            bottom: [TimelineFamily],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.EraserOptions, QuickBarCatalog.SelectOptions,
                    QuickBarCatalog.Transport, QuickBarCatalog.AddFrame,
                    QuickBarCatalog.GuideOptions]));
        store.Workspaces.Add(Built("Game art", ProjectType.GameArt,
            // The colour family rather than a hand-picked three: Gradient was
            // missing here and in Asset library for no reason anybody recorded,
            // so those two workspaces could not reach it at all (Q109).
            right: [[DockPanelId.Navigator], ProjectFamily, [DockPanelId.Layers], Colour],
            bottom: [TimelineFamily],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.EraserOptions, QuickBarCatalog.SelectOptions,
                    QuickBarCatalog.FillOptions, QuickBarCatalog.Transport,
                    QuickBarCatalog.AddFrame, QuickBarCatalog.GuideOptions]));
        store.Workspaces.Add(Built("Storyboard", ProjectType.Storyboard,
            right: [[DockPanelId.Navigator], ProjectFamily, Colour],
            bottom: [TimelineFamily],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.EraserOptions, QuickBarCatalog.Transport,
                    QuickBarCatalog.AddFrame]));
        store.Workspaces.Add(Built("Comic", ProjectType.Comic,
            right: [[DockPanelId.Navigator], ProjectFamily, [DockPanelId.Layers], Colour],
            bottom: [],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.EraserOptions, QuickBarCatalog.SelectOptions,
                    QuickBarCatalog.FillOptions, QuickBarCatalog.ShapeOptions,
                    QuickBarCatalog.GuideOptions]));
        store.Workspaces.Add(Built("Asset library", ProjectType.AssetLibrary,
            right: [[DockPanelId.Navigator], ProjectFamily, Colour],
            // A sprite sheet is a character cycle, which is animation by
            // another name — so the timeline family opens here too (Q109).
            bottom: [TimelineFamily],
            quick: [QuickBarCatalog.BrushOptions,
                    QuickBarCatalog.SelectOptions]));
        store.Current = "Default";
        return store;
    }

    /// <summary>
    /// A built-in arrangement. Each inner array is one slot; several panels in
    /// it are tabbed together.
    /// </summary>
    /// <param name="quick">
    /// The Quick options bar's contents for this kind of work (Q70, sharpened
    /// 2026-08-13: the bar is the workspace's, not the tool's). Animation and
    /// its kin take the transport; the single-image types take the paint kit.
    /// The "Default" workspace passes nothing and falls back to
    /// <see cref="QuickBarCatalog.ToolDefaults"/>.
    /// </param>
    private static Workspace Built(
        string name, ProjectType type, DockPanelId[][] right, DockPanelId[][] bottom,
        string[]? quick = null)
    {
        var layout = new DockLayout();
        Fill(layout, DockSide.Right, right);
        Fill(layout, DockSide.Bottom, bottom);
        layout.AreaExtents[DockSide.Right] = 300;
        layout.AreaExtents[DockSide.Bottom] = 280;
        layout.QuickBar = quick?.ToList();
        return new Workspace { Name = name, BuiltIn = true, DefaultFor = type, Layout = layout };
    }

    /// <summary>Dock a strip's slots, tabbing the ones that share a slot.</summary>
    /// <remarks>
    /// The first of each group is docked and the rest join it, so the leader is
    /// the tab that shows. That ordering is the whole of what "which one is in
    /// front" means in a built-in, and it is why these arrays are written with
    /// the panel an artist reaches for first at the head.
    /// </remarks>
    private static void Fill(DockLayout layout, DockSide side, DockPanelId[][] slots)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            layout.Dock(slots[i][0], side, i);
            foreach (var tabbed in slots[i].Skip(1)) layout.JoinGroup(tabbed, slots[i][0]);
            layout.Activate(slots[i][0]);
        }
    }

    // ---- persistence ---------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Where <see cref="Load"/> reads from and a loaded store writes back to.</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lightbox", "workspaces.json");

    /// <summary>
    /// Where this particular store persists, or null for one that does not.
    /// </summary>
    /// <remarks>
    /// Per instance rather than static, because a store built in memory —
    /// <see cref="Default"/>, or one a test made — genuinely has nowhere to
    /// save, and writing to a global path anyway is how one test's saved
    /// workspace became the next test's starting layout.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? File { get; set; }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Read the saved workspaces, adding any built-in the file predates.
    /// A store written before a project type existed must not leave that type
    /// without a default.
    /// </summary>
    public static WorkspaceStore Deserialize(string json)
    {
        WorkspaceStore? store;
        try
        {
            store = JsonSerializer.Deserialize<WorkspaceStore>(json, Json);
        }
        catch (JsonException)
        {
            store = null;
        }
        if (store is null || store.Workspaces.Count == 0) return Default();

        foreach (var builtIn in Default().Workspaces)
        {
            if (store.Find(builtIn.Name) is not { } saved)
            {
                store.Workspaces.Add(builtIn);
                continue;
            }
            // The store saves built-ins beside the user's own, so a file
            // written before the quick bar could be chosen carries them with
            // no quickBar key — a null the app wrote, not the artist. Left
            // alone it shadows the built-in's choice forever (B203):
            // Animation never gets its transport and the bar reads as the
            // old tool-options bar on every install that predates the
            // feature. Filling only a null keeps any list the artist did
            // choose, including one that dropped a default entry.
            if (saved.BuiltIn && saved.Layout.QuickBar is null)
            {
                saved.Layout.QuickBar = builtIn.Layout.QuickBar?.ToList();
            }
        }
        if (store.Find(store.Current) is null) store.Current = store.Workspaces[0].Name;
        return store;
    }

    public static WorkspaceStore Load()
    {
        WorkspaceStore store;
        try
        {
            store = System.IO.File.Exists(Path)
                ? Deserialize(System.IO.File.ReadAllText(Path))
                : Default();
        }
        catch (IOException)
        {
            store = Default();
        }
        store.File = Path;
        return store;
    }

    /// <summary>
    /// Write the store. Failures are swallowed: losing a panel arrangement is
    /// an annoyance, and it must never be the reason a save or a close fails.
    /// </summary>
    public void Save()
    {
        if (File is not { Length: > 0 } path) return;
        try
        {
            Lightbox.Core.Serialization.DocJson.WriteAtomic(path, Serialize());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
