namespace Lightbox.Core.Documents;

/// <summary>
/// Root of a Lightbox document. Everything below this is plain JSON —
/// that is the "AI-native" contract: an LLM can read and write any part
/// of a document.
/// </summary>
public sealed class Doc
{
    public int Version { get; set; } = 1;

    public Scene Scene { get; set; } = new();

    /// <summary>Character sheets: reference art outside the timeline.</summary>
    public List<ReferenceSheet> ReferenceSheets { get; set; } = [];

    /// <summary>
    /// The reference board — the wall of pinned-up reference — for a document
    /// with no project behind it. Null until one is arranged, and absent from
    /// the file until then.
    /// </summary>
    /// <remarks>
    /// <b>The fallback, not the home</b> (Q87). In a project a board is filed on
    /// the scope that owns the references (<c>ProjectBoards</c>), so one wall
    /// serves every animation of the same subject. A loose file has no scope to
    /// hang one on and no directory to copy an imported image into, so it keeps
    /// its own board here with the pictures embedded — the same trade
    /// <see cref="ReferenceStrip"/> makes, for the same reason.
    /// </remarks>
    public ReferenceBoard? ReferenceBoard { get; set; }

    /// <summary>
    /// Custom brush tip shapes (id → grayscale PNG, base64). Strokes reference
    /// them by <see cref="BrushSettings.TipId"/>, so a document re-renders
    /// with no external resources.
    /// </summary>
    public Dictionary<string, string> BrushTips { get; set; } = [];

    /// <summary>
    /// Imported paper textures (id → PNG, base64), referenced by
    /// <see cref="BrushSettings.TextureId"/>.
    /// </summary>
    /// <remarks>
    /// Null until a texture is imported, and absent from the file until then —
    /// unlike <see cref="BrushTips"/>, which predates the rule and writes an
    /// empty object. Same reasoning as tips otherwise: the pixels travel with
    /// the document so it renders with nothing else installed.
    /// </remarks>
    public Dictionary<string, string>? Textures { get; set; }

    /// <summary>
    /// Selections that strokes were painted under (id → region). Referenced by
    /// <see cref="Stroke.ClipId"/> so clipped strokes re-render identically
    /// from the document alone.
    /// </summary>
    public Dictionary<string, ClipRegion> ClipRegions { get; set; } = [];

    /// <summary>
    /// The document's colour palettes. Per document rather than per
    /// application: a character's palette is part of the character, and one
    /// living in app settings could not travel with the file.
    /// </summary>
    public List<Palette> Palettes { get; set; } = [];

    /// <summary>
    /// The folders those palettes are filed under, once there are any.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, and absent from the file until a folder is
    /// made — the same rule the camera follows. A document with three palettes
    /// and no filing system should not carry the machinery for one.
    /// </remarks>
    public List<PaletteFolder>? PaletteFolders { get; set; }

    /// <summary>
    /// Gradients referenced by <see cref="Stroke.GradientId"/>, keyed by id —
    /// the same arrangement as clip regions and brush tips, so a reload
    /// re-renders from the document alone.
    /// </summary>
    public Dictionary<string, Gradient> Gradients { get; set; } = [];

    /// <summary>
    /// The text elements set in this document, keyed by id, or null — which is
    /// every document nobody has typed in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Editing state, not rendering state.</b> Every other resource in this
    /// class is here because the renderer needs it — a tip, a texture, a clip
    /// region, a gradient ramp. This one is the exception and is worth the
    /// difference being explicit: the glyph contours a text element baked to are
    /// already in the strokes, so deleting this whole block changes no pixel of
    /// any frame. What it deletes is the ability to retype the words.
    /// See <see cref="TextElement"/> for why that is the right way round.
    /// </para>
    /// </remarks>
    public Dictionary<string, TextElement>? Texts { get; set; }

    /// <summary>
    /// Fonts carried in the document itself, keyed by id, so text set in them
    /// can be retyped on a machine that does not have them installed. Null
    /// until one is carried, which needs a licence that allows it.
    /// </summary>
    /// <remarks>
    /// Referenced by <see cref="FontRef.EmbeddedId"/>. See
    /// <see cref="EmbeddedFont"/> for the licence condition and what it costs
    /// when it is not met — the short version being that the picture is never
    /// affected either way.
    /// </remarks>
    public Dictionary<string, EmbeddedFont>? Fonts { get; set; }

    /// <summary>
    /// Symbols this document carries itself, keyed by id — or null, which is
    /// the ordinary case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A symbol normally lives on the <em>project</em>, above the animations
    /// that place it; that is what makes editing the sword once change every
    /// animation holding it. This key exists for the one moment that stops
    /// being true: <c>ProjectIo.Flatten</c> copies the symbols an exported
    /// document references into it, so the file that leaves the application
    /// renders from its own record. It is the same job
    /// <see cref="Palettes"/> and <see cref="Gradients"/> already do, and the
    /// place invariant 1 is repaid.
    /// </para>
    /// <para>
    /// Nothing in the app writes this while a document is being drawn, so a
    /// working file carries no symbol key at all.
    /// </para>
    /// </remarks>
    public Dictionary<string, Symbol>? Symbols { get; set; }

    /// <summary>Whether this document carries symbols of its own. Derived; not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSymbols => Symbols is { Count: > 0 };

    /// <summary>
    /// The effects elements authored in this document, keyed by id, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Absent until authored</b>, the camera's rule and the medium block's
    /// lesson: a document that never makes an effect is byte-identical to one
    /// from before the feature existed, and shows no effects UI.
    /// </para>
    /// <para>
    /// These are <em>authoring parameters</em>, never the drawing. Baking writes
    /// ordinary strokes into the frames, each tagged with
    /// <see cref="Stroke.SimId"/>; deleting every element here changes no pixel,
    /// because the strokes are ordinary strokes. That is what keeps invariant 1
    /// intact and is why nothing downstream — renderer, picker, transform, undo,
    /// export, the AI payload — needed to learn what an element is.
    /// </para>
    /// </remarks>
    public Dictionary<string, SimElement>? Sims { get; set; }

    /// <summary>Whether this document authors any effects elements. Derived; not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSims => Sims is { Count: > 0 };

    /// <summary>
    /// Elements that are one effect, keyed by id, or null on a document where
    /// nobody has grouped any.
    /// </summary>
    /// <remarks>
    /// Absent until authored, like <see cref="Sims"/> itself: a document with
    /// three ungrouped elements writes no <c>simGroups</c> key. Membership lives
    /// here rather than as a field on the element for the reason layer folders
    /// took the same shape — an element in no group is the ordinary case and
    /// should carry nothing saying so.
    /// </remarks>
    public Dictionary<string, SimGroup>? SimGroups { get; set; }

    /// <summary>The group an element belongs to, or null. Linear; there are never many.</summary>
    public SimGroup? GroupOf(SimElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (SimGroups is null) return null;
        foreach (var group in SimGroups.Values)
        {
            if (group.ElementIds.Contains(element.Id)) return group;
        }
        return null;
    }

    /// <summary>
    /// Shared line treatments, keyed by id, or null — the show's looks, which an
    /// element names and may override field by field (Q118).
    /// </summary>
    /// <remarks>
    /// On the document for now, in the manner <see cref="Palettes"/> began: the
    /// scoping that makes one look serve a whole production is the same step
    /// palettes took, and is a separate change from having the record at all.
    /// </remarks>
    public Dictionary<string, LineTreatment>? LineTreatments { get; set; }

    /// <summary>
    /// The treatment an element follows, with its own overrides applied over it.
    /// </summary>
    /// <remarks>
    /// The one place the cascade is resolved, so nothing else has to remember
    /// the order. A missing id resolves to the defaults rather than throwing — a
    /// treatment deleted out from under an element should leave a plain line,
    /// not an unopenable document.
    /// </remarks>
    public ResolvedTreatment TreatmentFor(SimElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        LineTreatment? shared = null;
        if (element.TreatmentId is { } id) LineTreatments?.TryGetValue(id, out shared);
        return LineTreatment.Resolve(shared, element.Treatment);
    }

    /// <summary>
    /// The document's bone hierarchy, or null — and null is the default and
    /// the common case, exactly as <see cref="Documents.Scene.Camera"/> is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bones are the camera's precedent applied to rigging: authored, saved,
    /// and never a mutation of a stroke — posing decides where marks are
    /// re-stamped, the stroke record stays the record. A document that never
    /// rigs writes no key, shows no rig UI, and pays nothing.
    /// </para>
    /// <para>
    /// On the document rather than the scene because the rig is a property of
    /// the <em>character</em>, like <see cref="Palettes"/>: the hierarchy and
    /// bind pose are what every scene shares, while the poses struck against a
    /// timeline live on <see cref="Documents.Scene.PoseTrack"/>. The design and
    /// its decisions are <c>docs/DESIGN-bones.md</c>.
    /// </para>
    /// </remarks>
    public Armature? Armature { get; set; }

    /// <summary>Whether this document has a rig. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasArmature => Armature is { Bones.Count: > 0 };

    /// <summary>
    /// The timeline frame the artist was parked on when the document was
    /// saved, or null — and null is every document saved at frame 0, which is
    /// most of them. Restored on open, so a scene reopens showing what it
    /// showed when it was put down (Q111): a rig posed at frame 30 stands at
    /// frame 30's pose again instead of snapping to rest at frame 0, which
    /// read as "the bones load in a different spot".
    /// </summary>
    /// <remarks>
    /// Nullable for <see cref="IsTemplate"/>'s serializer reason: optional
    /// means absent, and frame 0 is the default nobody needs written down.
    /// Stamped by the save funnel rather than tracked live — the playhead is
    /// view state (invariant 5) and only crosses into the record at the
    /// moment the record is written.
    /// </remarks>
    public int? PlayheadFrame { get; set; }

    /// <summary>
    /// Marks this document as a template: a starting point other animations are
    /// copied from. Q12's answer, and deliberately the whole of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A template is an ordinary document with a flag, not a new file type — so
    /// an artist can turn work they have already done into one, which is where
    /// real templates come from. See <c>docs/DESIGN-templates.md</c>.
    /// </para>
    /// <para>
    /// Nullable rather than a plain <c>bool</c>, and that is not fussiness: the
    /// serializer drops nulls and writes <c>false</c>, so a non-nullable flag
    /// would add <c>"isTemplate": false</c> to every document ever saved. The
    /// camera's rule — optional means <em>absent</em>, not defaulted. Read it
    /// through <see cref="IsTemplateDocument"/> rather than comparing to true.
    /// </para>
    /// </remarks>
    public bool? IsTemplate { get; set; }

    /// <summary>Whether this document is a template. Derived; never serialized.</summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> because a convenience getter beside a nullable field
    /// is still a property: without it this would write <c>"isTemplateDocument":
    /// false</c> on every document and reintroduce, under a second name, exactly
    /// the key making <see cref="IsTemplate"/> nullable existed to remove.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTemplateDocument => IsTemplate == true;

    /// <summary>
    /// The id of the template this document was copied from, if it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one link in the whole template design, and note which way it points:
    /// <b>the document names the template, never the reverse</b>. A template has
    /// no idea who copied it, so deleting one cannot break anything and nothing
    /// can traverse from a template to its copies. That asymmetry is what makes
    /// <em>Update from template</em> safe where a push from the template would
    /// not be.
    /// </para>
    /// <para>
    /// It is not a live reference. The copy is complete on its own and renders
    /// with the template deleted; this exists only so the artist can ask to pull
    /// changes forward, and a document whose template is gone simply cannot be
    /// asked.
    /// </para>
    /// </remarks>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Feature overrides: which features are enabled/disabled in this document,
    /// if they differ from the project's defaults. Null and absent from the file
    /// when the artist has not changed anything — a document that never touched
    /// a feature carries no key, following the camera's rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived defaults, not copied ones. A document stores only what its artist
    /// changed, so a default that moves in a later version moves for every
    /// document that never overrode it. The project supplies the defaults via
    /// <see cref="Lightbox.Core.Projects.FeatureDefaults"/>.
    /// </para>
    /// </remarks>
    public Dictionary<string, bool>? Features { get; set; }

    /// <summary>Get the effective feature state, considering project defaults.</summary>
    /// <remarks>
    /// This is a helper for reading; the actual resolution logic lives in the
    /// view model or wherever a document is being used and a project context
    /// is available.
    /// </remarks>
    public bool GetFeature(Lightbox.Core.Projects.FeatureKey feature, bool projectDefault) =>
        Features?.TryGetValue(feature.ToString(), out var value) == true ? value : projectDefault;

    /// <summary>
    /// A copy holding no reference in common with this one — the undo path's
    /// snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists rather than <c>DocJson.Clone</c> (B142).</b> Every
    /// structural edit pushes a snapshot, and routing that through
    /// serialize-then-parse made adding a layer to a 5 000-stroke painting cost
    /// 615 ms warm and ~1.1 s cold, allocating 72.5 MB — the slowest possible way
    /// to copy an object graph, paid on an ordinary action. This walks the graph
    /// directly.
    /// </para>
    /// <para>
    /// <b>Built on <c>MemberwiseClone</c> at every level on purpose.</b> That copies
    /// every value field automatically, so a new scalar property cannot be silently
    /// dropped by a clone nobody updated; only reference members need deepening
    /// here, and <c>DocCloneTests</c> walks both graphs to prove none was missed.
    /// The alternative — writing out every field by hand — fails silently in the
    /// direction that corrupts a document rather than slowing it.
    /// </para>
    /// <para>
    /// <c>DocJson.Clone</c> stays: it is the reference implementation the tests
    /// compare against, and the right tool anywhere a copy must also prove it
    /// survives serialization.
    /// </para>
    /// </remarks>
    /// <summary>
    /// This document with the drawings, and the reference material, taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What a stroke render resolves that is not in the stroke.</b> A mark
    /// reaches for a brush tip, a texture, a clip region, a palette swatch, a
    /// gradient ramp, a line treatment, a simulation — all of them by id, all of
    /// them stored here, and every one of them editable in place while the id
    /// stays put. Recolour a swatch and the pixels of a stroke drawn years ago
    /// change without the stroke changing at all (B151). So a checkpoint's
    /// fingerprint has to cover this, and covering it by listing the registries
    /// would be a list somebody has to remember to extend.
    /// </para>
    /// <para>
    /// <b>Hence subtraction rather than enumeration.</b> Everything is in unless
    /// it is taken out here, so a registry added next year is fingerprinted
    /// without anybody thinking about it. The cost of the coarseness is stated
    /// and accepted: editing a swatch no stroke uses drops every checkpoint in
    /// the document, which is one slow reopen. Being slow is the direction this
    /// is allowed to be wrong in.
    /// </para>
    /// <para>
    /// Three things come out, and each has to earn it. <b>Layers</b> hold the
    /// drawings, which the fingerprint hashes directly and precisely — leaving
    /// them in would hash every stroke of every cel to validate one.
    /// <b>Reference material</b> is imported photographs and boards: megabytes
    /// that no render of a drawing reads. <b>The playhead</b> is stamped into the
    /// document by every save (<c>StampPlayhead</c>), so leaving it in would
    /// change the fingerprint on the very act that writes the checkpoint, and
    /// nothing would ever validate.
    /// </para>
    /// </remarks>
    internal Doc RenderShell()
    {
        var shell = (Doc)MemberwiseClone();
        shell.Scene = Scene.RenderShell();
        shell.ReferenceSheets = [];
        shell.ReferenceBoard = null;
        shell.PlayheadFrame = null;
        return shell;
    }

    public Doc Clone()
    {
        var copy = (Doc)MemberwiseClone();
        copy.Scene = Scene.Clone();
        copy.ReferenceSheets = ReferenceSheets.Select(s => s.Clone()).ToList();
        copy.ReferenceBoard = ReferenceBoard?.Clone();
        copy.BrushTips = new Dictionary<string, string>(BrushTips);
        copy.Textures = Textures is null ? null : new Dictionary<string, string>(Textures);
        copy.ClipRegions = ClipRegions.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Palettes = Palettes.Select(p => p.Clone()).ToList();
        copy.PaletteFolders = PaletteFolders?.Select(f => f.Clone()).ToList();
        copy.Gradients = Gradients.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Texts = Texts?.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Fonts = Fonts?.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Symbols = Symbols?.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Sims = Sims?.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.SimGroups = SimGroups?.ToDictionary(g => g.Key, g => g.Value.Clone());
        copy.LineTreatments = LineTreatments?.ToDictionary(e => e.Key, e => e.Value.Clone());
        copy.Armature = Armature?.Clone();
        copy.Features = Features is null ? null : new Dictionary<string, bool>(Features);
        return copy;
    }
}

/// <summary>A recorded selection: closed contours (even-odd) plus edge feather.</summary>
public sealed class ClipRegion
{
    public List<List<StrokePoint>> Contours { get; set; } = [];

    /// <summary>Gaussian edge softness in pixels (0 = hard edge).</summary>
    public double Feather { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public ClipRegion Clone()
    {
        var copy = (ClipRegion)MemberwiseClone();
        copy.Contours = Contours.Select(c => new List<StrokePoint>(c)).ToList();
        return copy;
    }
}
