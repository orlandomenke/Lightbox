using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// IK's editing surface: making a chain, aiming it, and taking it away —
/// phase 3 of <c>docs/DESIGN-bones.md</c>, under the Q86 decisions.
/// </summary>
/// <remarks>
/// <para>
/// <b>One button makes a working limb.</b> "Add IK" on a selected bone
/// creates the target bone as well as the chain and leaves the target
/// selected, so the next thing the artist does — switch to Pose and drag —
/// is the thing IK is for. Handing over an empty chain with two combo boxes
/// to fill in would be the same feature and nobody would find it, which is
/// the failure this tool has already been reported for three times.
/// </para>
/// <para>
/// <b>Nothing here is silently inert.</b> A chain-driven bone's rotation is
/// the solver's, so an FK drag on one would write a key that the next solve
/// overwrites — a gesture that visibly does nothing. Pose drags on chain
/// bones are therefore redirected to the target (<c>PoseBoneTo</c>), which is
/// the gesture the artist meant anyway.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The chain the selected bone belongs to or aims, or null.</summary>
    public IkChain? SelectedBoneChain =>
        SelectedBoneId is { } id ? ChainTouching(id) : null;

    /// <summary>Whether the selected bone has anything to do with an IK chain.</summary>
    public bool HasSelectedChain => SelectedBoneChain is not null;

    /// <summary>Whether "Add IK" would do anything: a bone, and not one already driven.</summary>
    public bool CanAddIk => SelectedBoneId is not null && SelectedBoneChain is null;

    /// <summary>How many bones up from the tip the selected chain may rotate.</summary>
    public int SelectedChainBoneCount
    {
        get => SelectedBoneChain?.Bones ?? 2;
        set
        {
            if (SelectedBoneChain is not { } chain) return;
            var wanted = Math.Clamp(value, 1, 16);
            if (chain.Bones == wanted) return;
            EditChain(chain.Id, c => c.Bones = wanted);
        }
    }

    /// <summary>The pole bone's id, or null for "let it fall where it falls".</summary>
    public string? SelectedChainPoleId
    {
        get => SelectedBoneChain?.PoleBoneId;
        set
        {
            if (SelectedBoneChain is not { } chain || chain.PoleBoneId == value) return;
            EditChain(chain.Id, c => c.PoleBoneId = value);
        }
    }

    /// <summary>
    /// The bone list with a "none" row in front, for the pole picker — a
    /// nullable choice needs a way to say null.
    /// </summary>
    public IReadOnlyList<BoneRow> PoleRows =>
        [new BoneRow("", "(no pole)", 0, false), .. BoneRows];

    /// <summary>The pole picker's value, which speaks "" rather than null.</summary>
    public string SelectedChainPoleKey
    {
        get => SelectedChainPoleId ?? "";
        set => SelectedChainPoleId = string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Give the selected bone an IK chain, target bone and all, and leave the
    /// target selected so it can be dragged straight away.
    /// </summary>
    /// <remarks>
    /// The target is created as a <em>root</em> bone at the chain tip's tip.
    /// A root, because a target parented into the limb it drives is the circle
    /// the solver refuses — and an artist who wants the foot to follow a prop
    /// re-parents it afterwards with the control that already exists.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAddIk))]
    private void AddIkChain()
    {
        if (SelectedBoneId is not { } tipId || Doc.Armature is not { } armature) return;
        if (armature.BoneById(tipId) is not { } tip || ChainTouching(tipId) is not null) return;

        var at = ArmatureOps.Solve(armature)[tipId];
        var (tx, ty) = at.Tip(tip.Length);
        var target = new Bone
        {
            Name = tip.Name + ".ik",
            X = tx,
            Y = ty,
            // Short: it is a handle, not a limb, and a handle the length of a
            // thigh is chrome an artist has to look past.
            Length = 16,
        };
        var chain = new IkChain
        {
            Name = tip.Name + " IK",
            TipBoneId = tipId,
            TargetBoneId = target.Id,
            // Two is the limb — upper arm and forearm, thigh and shin — and
            // the count an artist changes rather than one they have to set.
            Bones = Math.Min(2, DepthOf(armature, tip)),
        };
        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } rig) return;
            rig.Bones.Add(target);
            (rig.Chains ??= []).Add(chain);
        });

        SelectedBoneId = target.Id;
        NotifyArmatureSurface();
        NotifyIkSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Take the chain away, and its target bone with it — the target was
    /// created by the chain and has no other job, so leaving it behind would
    /// leave a stray bone the artist has to notice and delete.
    /// </summary>
    [RelayCommand]
    private void RemoveIkChain()
    {
        if (SelectedBoneChain is not { } chain) return;
        var targetId = chain.TargetBoneId;
        var chainId = chain.Id;
        var tipId = chain.TipBoneId;
        // Read before the edit: the moment the handle is gone,
        // NotifyArmatureSurface clears the selection that pointed at it.
        var wasOnTheHandle = SelectedBoneId == targetId;

        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } rig) return;
            rig.Chains?.RemoveAll(c => c.Id == chainId);
            // Absent, not empty: a rig with no IK writes no chains key.
            if (rig.Chains is { Count: 0 }) rig.Chains = null;
            // Only if nothing else uses it, and only if it is childless —
            // a target somebody re-parented a foot under is load-bearing.
            if (rig.Chains?.Any(c => c.TargetBoneId == targetId || c.PoleBoneId == targetId) != true
                && !rig.Bones.Any(b => b.ParentId == targetId))
                rig.Bones.RemoveAll(b => b.Id == targetId);
            if (doc.Scene.PoseTrack is { } track)
                foreach (var key in track.Keys) key.Bones.Remove(targetId);
        });

        if (wasOnTheHandle) SelectedBoneId = tipId;
        NotifyArmatureSurface();
        NotifyIkSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>The chain that drives this bone, targets it, or poles by it.</summary>
    private IkChain? ChainTouching(string boneId)
    {
        if (Doc.Armature is not { Chains: { Count: > 0 } chains } armature) return null;
        foreach (var chain in chains)
        {
            if (chain.TargetBoneId == boneId || chain.PoleBoneId == boneId) return chain;
            if (DrivenBy(armature, chain, boneId)) return chain;
        }
        return null;
    }

    /// <summary>Whether a chain's solve writes this bone's rotation.</summary>
    private static bool DrivenBy(Armature armature, IkChain chain, string boneId)
    {
        var walk = armature.BoneById(chain.TipBoneId);
        var seen = new HashSet<string>();
        for (var i = 0; walk is not null && i < Math.Max(1, chain.Bones) && seen.Add(walk.Id); i++)
        {
            if (walk.Id == boneId) return true;
            walk = walk.ParentId is { } p ? armature.BoneById(p) : null;
        }
        return false;
    }

    /// <summary>How many bones this one has, counting itself and its ancestors.</summary>
    private static int DepthOf(Armature armature, Bone bone)
    {
        var count = 0;
        var seen = new HashSet<string>();
        for (var walk = bone; walk is not null && seen.Add(walk.Id); count++)
            walk = walk.ParentId is { } p ? armature.BoneById(p) : null;
        return Math.Max(1, count);
    }

    private void EditChain(string chainId, Action<IkChain> edit)
    {
        _editor.Perform(doc =>
        {
            if (doc.Armature?.Chains?.FirstOrDefault(c => c.Id == chainId) is { } chain) edit(chain);
        });
        NotifyIkSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// The IK and constraint panels both answer about the selected bone, so
    /// they move with it. One implementation because a partial method can only
    /// have one — the constraint half lives in its own file otherwise.
    /// </summary>
    partial void OnSelectedBoneIdChanged(string? value)
    {
        NotifyIkSurface();
        NotifyConstraintSurface();
    }

    /// <summary>Re-read everything the IK surface derives from the document.</summary>
    private void NotifyIkSurface()
    {
        OnPropertyChanged(nameof(SelectedBoneChain));
        OnPropertyChanged(nameof(HasSelectedChain));
        OnPropertyChanged(nameof(CanAddIk));
        OnPropertyChanged(nameof(SelectedChainBoneCount));
        OnPropertyChanged(nameof(SelectedChainPoleId));
        OnPropertyChanged(nameof(SelectedChainPoleKey));
        OnPropertyChanged(nameof(PoleRows));
        AddIkChainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Move a bone to a document point as a pose <em>translation</em>, keyed
    /// at the playhead.
    /// </summary>
    /// <remarks>
    /// This is the gesture for an IK handle and a pole: both are placed
    /// rather than aimed, so the FK drag — which only ever writes a rotation —
    /// would leave them exactly where they were. It is also where a pose drag
    /// on a chain-driven bone lands, since the artist dragging a forearm is
    /// asking the hand to end up under the pointer.
    /// </remarks>
    internal void PoseTranslateTo(string boneId, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(boneId) is not { } target) return;

        var frame = CurrentFrameIndex;
        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, frame);
        var placements = ArmatureOps.Solve(armature, pose);
        // The target's own offset lives in its parent's frame, so the pointer
        // has to be taken there before it can be written as a delta.
        var (px, py, prot) = target.ParentId is { } pid && placements.TryGetValue(pid, out var pp)
            ? (pp.X, pp.Y, pp.RotationDeg)
            : (0.0, 0.0, 0.0);
        var rad = -prot * Math.PI / 180.0;
        var (dx, dy) = (x - px, y - py);
        var localX = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
        var localY = Math.Sin(rad) * dx + Math.Cos(rad) * dy;

        KeyPose(frame, target.Id, p =>
        {
            p.X = localX - target.X;
            p.Y = localY - target.Y;
        });
        InvalidateRiggedFrames();
    }
}
