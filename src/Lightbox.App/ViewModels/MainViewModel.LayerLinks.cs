using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>Which piece of a link's bracket a docker row draws.</summary>
public enum LayerLinkMark
{
    /// <summary>Not in a link — no bracket, and no space taken for one.</summary>
    None,

    /// <summary>Topmost of a run: the corner, with the line running down.</summary>
    Top,

    /// <summary>Inside a run: the line, with a tick into the row.</summary>
    Middle,

    /// <summary>Last of a run: the line coming down into a corner.</summary>
    Bottom,

    /// <summary>In a link, but with neither neighbour in it — a tick, joining nothing.</summary>
    Detached,
}

/// <summary>
/// Layer links: declaring that several layers are one drawing, and choosing
/// what travels between them (Q90).
/// </summary>
/// <remarks>
/// <para>
/// <b>Adjacency is the gesture, never the addressing.</b> "Link to the layer
/// above" reads the neighbour once, at the moment the artist asks, and what it
/// writes is a membership by id. Reordering afterwards is safe — where a link
/// that meant "follow whatever is above me" would silently retarget, which is
/// the invisible-failure shape this area keeps producing.
/// </para>
/// <para>
/// <b>Joining is a merge, not a move.</b> Linking a layer to a neighbour that
/// is already in a link puts it in <em>that</em> link rather than making a new
/// pair, so building a four-layer character is three gestures and ends with
/// one link — not two links and a layer that quietly left the first.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The active layer's link, or null.</summary>
    /// <remarks>Declared here so the docker's bracket and this file read together.</remarks>
    public LayerLink? ActiveLayerLink =>
        ActiveLayer is { } layer ? Doc.Scene.LinkOf(layer) : null;

    /// <summary>Whether the active layer is in a link.</summary>
    public bool HasActiveLayerLink => ActiveLayerLink is not null;

    /// <summary>Link the layer to the one above it in the docker's order.</summary>
    [RelayCommand]
    private void LinkLayerAbove() => LinkActiveToNeighbour(above: true);

    /// <summary>Link the layer to the one below it.</summary>
    [RelayCommand]
    private void LinkLayerBelow() => LinkActiveToNeighbour(above: false);

    /// <summary>Take the active layer out of its link.</summary>
    [RelayCommand]
    private void UnlinkLayer()
    {
        if (ActiveLayer is not { LinkId: { } linkId } layer) return;
        var id = layer.Id;
        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == id) is { } target) target.LinkId = null;
            PruneLinks(doc);
        });
        NotifyLinkSurface();
    }

    /// <summary>
    /// Put the active layer and its neighbour in one link, creating it if
    /// neither is in one.
    /// </summary>
    /// <remarks>
    /// A new link carries <b>bones</b> and nothing else. Rigging a character
    /// is why the gesture exists, and a link that arrived carrying everything
    /// would be the general-mechanism trap Q90 accepted the cost of avoiding:
    /// each property is switched on deliberately.
    /// </remarks>
    private void LinkActiveToNeighbour(bool above)
    {
        if (ActiveLayer is not { } layer) return;
        var layers = Doc.Scene.Layers;
        var index = layers.IndexOf(layer);
        if (index < 0) return;
        // Scene.Layers is bottom-first; the docker shows it topmost-first, so
        // "above" in the docker is the next index up.
        var neighbourIndex = above ? index + 1 : index - 1;
        if (neighbourIndex < 0 || neighbourIndex >= layers.Count) return;

        var ids = (layer.Id, layers[neighbourIndex].Id);
        var existing = layer.LinkId ?? layers[neighbourIndex].LinkId;

        _editor.Perform(doc =>
        {
            var a = doc.Scene.Layers.FirstOrDefault(l => l.Id == ids.Item1);
            var b = doc.Scene.Layers.FirstOrDefault(l => l.Id == ids.Item2);
            if (a is null || b is null) return;

            var linkId = existing;
            if (linkId is null || doc.Scene.LayerLinks?.Any(l => l.Id == linkId) != true)
            {
                var link = new LayerLink { Name = LinkName(doc), Bones = true };
                (doc.Scene.LayerLinks ??= []).Add(link);
                linkId = link.Id;
            }
            a.LinkId = linkId;
            b.LinkId = linkId;
            PruneLinks(doc);
        });
        NotifyLinkSurface();
    }

    /// <summary>Switch one of the link's travelling properties on or off.</summary>
    public void SetLinkCarries(string property, bool on)
    {
        if (ActiveLayerLink is not { } link) return;
        var linkId = link.Id;
        _editor.Perform(doc =>
        {
            if (doc.Scene.LayerLinks?.FirstOrDefault(l => l.Id == linkId) is not { } target) return;
            // Back to absent when switched off, not a stored false — the same
            // rule the constraint panel follows for its offset and strength.
            bool? value = on ? true : null;
            switch (property)
            {
                case "bones": target.Bones = value; break;
                case "alpha": target.Alpha = value; break;
                case "visibility": target.Visibility = value; break;
            }
        });
        NotifyLinkSurface();
    }

    /// <summary>Whether the active layer's link carries bones.</summary>
    public bool LinkCarriesBones
    {
        get => ActiveLayerLink is { CarriesBones: true };
        set => SetLinkCarries("bones", value);
    }

    /// <summary>Whether the active layer's link carries alpha lock.</summary>
    public bool LinkCarriesAlpha
    {
        get => ActiveLayerLink is { CarriesAlpha: true };
        set => SetLinkCarries("alpha", value);
    }

    /// <summary>Whether the active layer's link carries visibility.</summary>
    public bool LinkCarriesVisibility
    {
        get => ActiveLayerLink is { CarriesVisibility: true };
        set => SetLinkCarries("visibility", value);
    }

    /// <summary>How many layers are in the active layer's link.</summary>
    public int ActiveLinkSize =>
        ActiveLayer is { } layer ? Doc.Scene.LinkedWith(layer).Count() : 0;

    /// <summary>What the active layer's strokes follow, for the panel to say out loud.</summary>
    public string ActiveLayerRigSummary
    {
        get
        {
            if (ActiveLayer is not { } layer) return "";
            if (Doc.Scene.RiggedBoneOf(layer) is not { } bone) return "not rigged";
            if (bone.Length == 0) return "follows the whole skeleton";
            var name = Doc.Armature?.BoneById(bone)?.Name ?? bone;
            return layer.BoneId is null ? $"follows {name} (through the link)" : $"follows {name}";
        }
    }

    /// <summary>
    /// A link with fewer than two members is not a link. Dropped rather than
    /// kept, so unlinking the second-to-last layer cannot leave a one-member
    /// link the docker draws a bracket around.
    /// </summary>
    private static void PruneLinks(Doc doc)
    {
        if (doc.Scene.LayerLinks is not { } links) return;
        links.RemoveAll(link => doc.Scene.Layers.Count(l => l.LinkId == link.Id) < 2);
        foreach (var layer in doc.Scene.Layers)
            if (layer.LinkId is { } id && links.All(l => l.Id != id))
                layer.LinkId = null;
        // Absent, not empty.
        if (links.Count == 0) doc.Scene.LayerLinks = null;
    }

    private static string LinkName(Doc doc) =>
        $"Link {(doc.Scene.LayerLinks?.Count ?? 0) + 1}";

    /// <summary>
    /// Rig the active layer: to one bone, to the whole skeleton (empty
    /// string), or to nothing (null).
    /// </summary>
    /// <remarks>
    /// This is the layer's <em>own</em> answer, which beats the link's — so an
    /// artist can rig the effects layer to a different bone than the lines it
    /// is linked to, and the link stays a default rather than becoming a thing
    /// that overwrites authored work.
    /// </remarks>
    public void SetLayerBone(string? boneId)
    {
        if (ActiveLayer is not { } layer || layer.BoneId == boneId) return;
        var id = layer.Id;
        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == id) is { } target) target.BoneId = boneId;
        });
        NotifyLinkSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Rig the active layer to the bone selected in the Bone tool.</summary>
    [RelayCommand]
    private void RigLayerToSelectedBone() => SetLayerBone(SelectedBoneId ?? "");

    /// <summary>Rig the active layer to the whole skeleton, auto-bound by distance.</summary>
    [RelayCommand]
    private void RigLayerToSkeleton() => SetLayerBone("");

    /// <summary>Take the active layer off the rig.</summary>
    [RelayCommand]
    private void UnrigLayer() => SetLayerBone(null);

    /// <summary>
    /// Which piece of the link bracket a docker row draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the <em>neighbours</em> rather than of the link's membership
    /// order, and that is the whole reason this is a method rather than an
    /// index. A link's members need not be adjacent — an artist can link the
    /// lines and the effects with somebody else's layer sitting between them —
    /// and a bracket drawn from membership order would then be a continuous
    /// line down a row that is not in the link.
    /// </para>
    /// <para>
    /// <see cref="LayerLinkMark.Detached"/> is the honest answer for a member
    /// whose neighbours are not in the link: a tick rather than a corner,
    /// because there is nothing above or below it to join to.
    /// </para>
    /// </remarks>
    internal LayerLinkMark LinkMarkOf(Layer layer)
    {
        if (layer.LinkId is not { } id) return LayerLinkMark.None;
        // Scene.Layers is bottom-first; the docker shows it the other way up,
        // so "above" in the docker is the next index up.
        var layers = Doc.Scene.Layers;
        var index = layers.IndexOf(layer);
        if (index < 0) return LayerLinkMark.None;
        var above = index + 1 < layers.Count && layers[index + 1].LinkId == id;
        var below = index - 1 >= 0 && layers[index - 1].LinkId == id;
        return (above, below) switch
        {
            (false, true) => LayerLinkMark.Top,
            (true, true) => LayerLinkMark.Middle,
            (true, false) => LayerLinkMark.Bottom,
            _ => LayerLinkMark.Detached,
        };
    }

    /// <summary>A layer's link colour, or transparent when it is in none.</summary>
    internal string LinkColorOf(Layer layer) =>
        Doc.Scene.LinkOf(layer)?.Color ?? "#00000000";

    /// <summary>
    /// The short badge a docker row shows for the rig — empty when the layer
    /// is not rigged, so an unrigged document's docker gains nothing.
    /// </summary>
    internal string RigBadgeOf(Layer layer)
    {
        if (Doc.Scene.RiggedBoneOf(layer) is not { } bone) return "";
        if (bone.Length == 0) return "rig";
        return Doc.Armature?.BoneById(bone)?.Name ?? bone;
    }

    /// <summary>Re-read everything the link surface derives from the document.</summary>
    internal void NotifyLinkSurface()
    {
        // A link that starts or stops carrying bones changes which drawings
        // the rig moves, and nothing else on this path would notice.
        RebuildRigIndex();
        OnPropertyChanged(nameof(ActiveLayerLink));
        OnPropertyChanged(nameof(HasActiveLayerLink));
        OnPropertyChanged(nameof(LinkCarriesBones));
        OnPropertyChanged(nameof(LinkCarriesAlpha));
        OnPropertyChanged(nameof(LinkCarriesVisibility));
        OnPropertyChanged(nameof(ActiveLinkSize));
        OnPropertyChanged(nameof(ActiveLayerRigSummary));
        foreach (var row in LayerRows) row.SyncLinkFromModel();
    }
}
