using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The rig library: save this document's skeleton as a named set the project
/// keeps, and pull one back onto another document at the right size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q181, and deliberately the same three verbs as guide sets</b> — save,
/// offer, pull — because it is the same problem one record along, and an
/// artist who has learned one should not have to learn the other. What is not
/// the same is the unit: a guide set travels as a fraction of the canvas, a
/// rig travels in <em>head units</em>, and the document's own character height
/// scale is what converts. See <see cref="ArmatureFit"/>.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>A set needs a skeleton to hold and a project to live in.</summary>
    public bool CanSaveRigSet => HasArmature && ProjectDocker.Project is not null;

    /// <summary>Every set in the project, for the editor's list.</summary>
    public IReadOnlyList<RigSet> ProjectRigSets =>
        ProjectDocker.Project?.Manifest.RigSets ?? (IReadOnlyList<RigSet>)[];

    /// <summary>The sets this document's scope offers it.</summary>
    public IReadOnlyList<RigSet> OfferedRigSets => RigSetsVisibleTo(ActiveTab?.Source);

    internal IReadOnlyList<RigSet> RigSetsVisibleTo(DocumentRef? document)
    {
        if (ProjectDocker.Project is not { } project) return [];
        if (project.Manifest.RigSets is not { Count: > 0 } sets) return [];
        // Q30's migration: a project that scopes nothing offers everything.
        var visible = RigScopes.VisibleTo(project.Manifest, document);
        return visible is null ? sets : [.. sets.Where(s => visible.Contains(s.Id))];
    }

    public bool HasRigSetOffers => OfferedRigSets.Count > 0;

    public IReadOnlyList<ScopeMenuEntry> PullRigSetMenu =>
        [.. OfferedRigSets.Select(s => new ScopeMenuEntry(RigSetLabel(s), PullRigSetCommand, s))];

    /// <summary>
    /// The set's name and what it will actually do here — the head count when
    /// there is one, and how it will be sized on this document.
    /// </summary>
    /// <remarks>
    /// The label carries the head count because that is the number the artist
    /// is choosing between: "Goblin — 4.5 heads" beside "Human — 7.5 heads" is
    /// the whole feature stated in a menu, and a bare list of names is not.
    /// </remarks>
    private string RigSetLabel(RigSet set) =>
        set.Heads is > 0 ? $"{set.Name} — {set.Heads:0.##} heads" : set.Name;

    public void NotifyRigSetOffers()
    {
        OnPropertyChanged(nameof(OfferedRigSets));
        OnPropertyChanged(nameof(PullRigSetMenu));
        OnPropertyChanged(nameof(HasRigSetOffers));
        OnPropertyChanged(nameof(CanSaveRigSet));
        OnPropertyChanged(nameof(ProjectRigSets));
        OnPropertyChanged(nameof(CanPullRigSet));
    }

    // ---- what decides a pulled rig's size ---------------------------------------

    /// <summary>
    /// Which rule the next pull uses. Heads by default, because that is the
    /// one the library exists for.
    /// </summary>
    /// <remarks>
    /// A preference on the view model rather than a dialog per pull: the
    /// answer is the same nine times out of ten, and a modal in front of an
    /// action an artist repeats is a tax. Changing it changes nothing already
    /// pulled — a rig in a document is that document's from the moment it
    /// lands.
    /// </remarks>
    public RigFit RigPullFit
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RigPullFitIsHeads));
            OnPropertyChanged(nameof(RigPullFitIsCanvas));
            OnPropertyChanged(nameof(RigPullFitIsOriginal));
        }
    } = RigFit.Heads;

    // Three bindable flags rather than a converter, matching how the rest of
    // the menus express a radio group.
    public bool RigPullFitIsHeads
    {
        get => RigPullFit == RigFit.Heads;
        set { if (value) RigPullFit = RigFit.Heads; }
    }

    public bool RigPullFitIsCanvas
    {
        get => RigPullFit == RigFit.Canvas;
        set { if (value) RigPullFit = RigFit.Canvas; }
    }

    public bool RigPullFitIsOriginal
    {
        get => RigPullFit == RigFit.Original;
        set { if (value) RigPullFit = RigFit.Original; }
    }

    /// <summary>The document's character height scale, if it has one.</summary>
    private Guide? DocumentHeightScale =>
        Guides.FirstOrDefault(g => g.Kind == GuideKind.HeightScale);

    /// <summary>
    /// Whether there is anything here to measure a head count against — the
    /// editor warns before a save that cannot produce one.
    /// </summary>
    public bool DocumentHasHeightScale => DocumentHeightScale is not null;

    // ---- the trap, and the one place it is closed -------------------------------

    /// <summary>
    /// Whether anything in this document is already attached to its skeleton —
    /// a skinned stroke or a rigged layer.
    /// </summary>
    /// <remarks>
    /// <b>This is <c>docs/DESIGN-bones.md</c>'s "one trap" wearing a property
    /// name.</b> The bind pose is the coordinate space every dab dynamic seeds
    /// from, so replacing or rescaling an armature that already has art bound
    /// to it re-rolls scatter, size, flow, roundness, rotation and all three
    /// colour jitters — the character comes back boiling. Scaling a rig
    /// nothing is bound to is an ordinary authoring act; doing it afterwards
    /// is not, and refusing is the only honest answer at this layer.
    /// </remarks>
    public bool ArmatureIsBound =>
        Doc.Scene.Layers.Any(l => l.BoneId is not null)
        || Doc.Scene.Layers.Any(l =>
            l.Cels.Any(c => c.Frame?.Strokes.Any(s => s.Weights is { Count: > 0 }) == true));

    /// <summary>Whether the rig has been posed — keys that name bones about to be replaced.</summary>
    private bool ArmatureIsPosed => Doc.Scene.PoseTrack is { Keys.Count: > 0 };

    /// <summary>
    /// Whether a pull would be allowed: a document with no skeleton, or one
    /// whose skeleton nothing depends on yet.
    /// </summary>
    public bool CanPullRigSet => !ArmatureIsBound && !ArmatureIsPosed;

    // ---- the verbs --------------------------------------------------------------

    /// <summary>
    /// Save this document's skeleton as a named set in the project — into a
    /// new set, or over <paramref name="overwriteId"/>'s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copies at both ends, exactly as a guide set: re-proportioning a bone in
    /// this drawing afterwards must not silently redraw every character that
    /// pulled from the library, and vice versa.
    /// </para>
    /// <para>
    /// The head count is measured now or never — against the height scale on
    /// this document, if there is one. A rig saved without one has no head
    /// count anybody measured, writes no key, and is pulled by the canvas rule
    /// instead; inventing a number from the canvas would be a guess dressed as
    /// a proportion.
    /// </para>
    /// </remarks>
    public RigSet? SaveArmatureAsSet(string name, string? overwriteId = null)
    {
        if (ProjectDocker.Project is not { } project) return null;
        if (Doc.Armature is not { Bones.Count: > 0 } armature) return null;

        var sets = project.Manifest.RigSets ??= [];
        var set = overwriteId is null ? null : sets.FirstOrDefault(s => s.Id == overwriteId);
        if (set is null)
        {
            set = new RigSet();
            sets.Add(set);
        }
        if (name.Trim() is { Length: > 0 } trimmed) set.Name = trimmed;
        set.Armature = armature.Clone();
        set.Canvas = AuthoredCanvas.Of(Scene);
        set.Heads = ArmatureFit.HeadsOn(armature, DocumentHeightScale);
        SaveProject();
        NotifyRigSetOffers();
        AiStatus = set.Heads is > 0
            ? $"Skeleton saved as “{set.Name}”, {set.Heads:0.##} heads tall. "
              + "Share it onto a folder from the project window."
            : $"Skeleton saved as “{set.Name}”. No character height scale on this document, "
              + "so it has no head count — add one and save again to make it scale by proportion.";
        return set;
    }

    public void RenameRigSet(RigSet set, string name)
    {
        if (ProjectDocker.Project is null || name.Trim() is not { Length: > 0 } trimmed) return;
        set.Name = trimmed;
        SaveProject();
        NotifyRigSetOffers();
    }

    /// <summary>
    /// Remove a set from the project, and every declaration that shared it.
    /// </summary>
    public void DeleteRigSet(RigSet set)
    {
        if (ProjectDocker.Project is not { } project) return;
        project.Manifest.RigSets?.RemoveAll(s => s.Id == set.Id);
        // Absent, not empty: a project whose last set goes writes no key again.
        if (project.Manifest.RigSets is { Count: 0 }) project.Manifest.RigSets = null;
        ResourceScopes.Retract(project.Manifest, RigScopes.Kind, set.Id);
        SaveProject();
        NotifyRigSetOffers();
        AiStatus = $"Skeleton “{set.Name}” deleted.";
    }

    /// <summary>
    /// Put a set's skeleton on this document, sized by <see cref="RigPullFit"/>,
    /// as one undoable step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It becomes the document's armature, because a document has one — see
    /// <c>Doc.Armature</c>. Standing several characters side by side for a size
    /// comparison is the <em>other</em> landing this record was designed for
    /// and is not this verb.
    /// </para>
    /// <para>
    /// Refused outright on a document whose rig is already bound or posed. See
    /// <see cref="ArmatureIsBound"/> — that is not caution, it is the one
    /// documented way to make a rigged character boil.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void PullRigSet(RigSet? set)
    {
        if (set is null || set.Armature.Bones.Count == 0) return;
        if (!CanPullRigSet)
        {
            AiStatus = ArmatureIsBound
                ? "This drawing's skeleton already has art bound to it. Pulling another would "
                  + "re-seed every dab and the character would boil — unbind first."
                : "This drawing's skeleton is already posed. Clear the poses first, or the keys "
                  + "would name bones that are gone.";
            return;
        }

        var landed = ArmatureFit.LandedAs(set, AuthoredCanvas.Of(Scene), DocumentHeightScale, RigPullFit);
        var fitted = ArmatureFit.Onto(set, AuthoredCanvas.Of(Scene), DocumentHeightScale, RigPullFit);
        var before = Doc.Armature;
        _editor.PerformDelta(
            apply: doc => doc.Armature = fitted,
            revert: doc => doc.Armature = before);
        NotifyArmatureSurface();
        NotifyRigSetOffers();
        AiStatus = landed switch
        {
            RigFit.Heads => $"“{set.Name}” placed at {set.Heads:0.##} heads on this document's height scale.",
            RigFit.Canvas when RigPullFit == RigFit.Heads =>
                $"“{set.Name}” scaled to this canvas — no character height scale here to measure heads against.",
            RigFit.Canvas => $"“{set.Name}” scaled to this canvas.",
            _ => $"“{set.Name}” placed at the size it was saved.",
        };
    }
}
