using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Constraints' editing surface: making one bone follow another, and taking
/// it back — phase 3 of <c>docs/DESIGN-bones.md</c>, after IK.
/// </summary>
/// <remarks>
/// <para>
/// <b>Picking the target is the add.</b> A "Constrain to" list that creates
/// an aim the moment a bone is chosen means one gesture rather than three
/// (press add, choose a kind, choose a target) for the constraint an artist
/// wants nine times in ten. The kind is changed afterwards, on a constraint
/// that already does something visible, which is a much easier thing to
/// understand than a kind chosen before there is anything to see.
/// </para>
/// <para>
/// <b>The target list omits what cannot be a target.</b> A bone cannot follow
/// itself or its own descendant — the solve refuses both, because the target's
/// placement would depend on the answer — and a list that offers them is a
/// list that produces a constraint doing nothing, which is the invisible-
/// failure shape this tool has already been reported for.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>Every constraint on the selected bone, newest last.</summary>
    public IReadOnlyList<BoneRow> ConstraintRows
    {
        get
        {
            if (SelectedBoneId is not { } id || Doc.Armature is not { Constraints: { } all } armature) return [];
            return all.Where(c => c.BoneId == id)
                      .Select(c => new BoneRow(
                          c.Id,
                          $"{KindLabel(c.Kind)} → {armature.BoneById(c.TargetBoneId)?.Name ?? "?"}",
                          0,
                          c.Id == SelectedConstraintId))
                      .ToList();
        }
    }

    private static string KindLabel(BoneConstraintKind kind) => kind switch
    {
        BoneConstraintKind.Aim => "Aim at",
        BoneConstraintKind.CopyRotation => "Copy turn of",
        _ => "Copy place of",
    };

    private string? _selectedConstraintId;

    /// <summary>Which of the selected bone's constraints the panel is editing.</summary>
    public string? SelectedConstraintId
    {
        get => _selectedConstraintId is { } id && Constraint(id) is not null
            ? _selectedConstraintId
            : ConstraintsOfSelectedBone().FirstOrDefault()?.Id;
        set
        {
            if (_selectedConstraintId == value) return;
            _selectedConstraintId = value;
            NotifyConstraintSurface();
        }
    }

    private BoneConstraint? SelectedConstraint =>
        SelectedConstraintId is { } id ? Constraint(id) : null;

    /// <summary>Whether the selected bone has a constraint to edit.</summary>
    public bool HasSelectedConstraint => SelectedConstraint is not null;

    /// <summary>The constraint's kind as an index, because a ComboBox speaks indices.</summary>
    public int ConstraintKindIndex
    {
        get => (int)(SelectedConstraint?.Kind ?? BoneConstraintKind.Aim);
        set
        {
            if (SelectedConstraint is not { } c) return;
            var kind = (BoneConstraintKind)Math.Clamp(value, 0, 2);
            if (c.Kind == kind) return;
            EditConstraint(c.Id, x => x.Kind = kind);
        }
    }

    /// <summary>The constraint's target bone.</summary>
    public string ConstraintTargetKey
    {
        get => SelectedConstraint?.TargetBoneId ?? "";
        set
        {
            if (SelectedConstraint is not { } c || string.IsNullOrEmpty(value) || c.TargetBoneId == value) return;
            EditConstraint(c.Id, x => x.TargetBoneId = value);
        }
    }

    /// <summary>Degrees added after the constraint resolves.</summary>
    public double ConstraintOffsetDeg
    {
        get => SelectedConstraint?.OffsetDeg ?? 0;
        set
        {
            if (SelectedConstraint is not { } c) return;
            // Back to absent at zero rather than storing a zero: the ordinary
            // constraint writes no offset key, however it got back to ordinary.
            var wanted = Math.Abs(value) < 1e-9 ? (double?)null : value;
            if (Nullable.Equals(c.OffsetDeg, wanted)) return;
            EditConstraint(c.Id, x => x.OffsetDeg = wanted);
        }
    }

    /// <summary>How far toward the constrained result the bone goes, as a percentage.</summary>
    public double ConstraintInfluencePercent
    {
        get => (SelectedConstraint?.Strength ?? 1) * 100;
        set
        {
            if (SelectedConstraint is not { } c) return;
            var fraction = Math.Clamp(value, 0, 100) / 100.0;
            var wanted = Math.Abs(fraction - 1) < 1e-9 ? (double?)null : fraction;
            if (Nullable.Equals(c.Influence, wanted)) return;
            EditConstraint(c.Id, x => x.Influence = wanted);
        }
    }

    /// <summary>
    /// The bones the selected one could legally follow, with a "pick one" row
    /// in front — and picking a row is what creates the constraint.
    /// </summary>
    public IReadOnlyList<BoneRow> ConstraintTargetRows
    {
        get
        {
            if (SelectedBoneId is not { } id || Doc.Armature is not { } armature) return [];
            return
            [
                new BoneRow("", "(constrain to…)", 0, false),
                .. BoneRows.Where(r => r.Id != id && !HangsOff(armature, r.Id, id)),
            ];
        }
    }

    /// <summary>Always the placeholder: choosing a row is an action, not a state.</summary>
    public string ConstrainToKey
    {
        get => "";
        set
        {
            if (string.IsNullOrEmpty(value) || SelectedBoneId is not { } id) return;
            AddConstraint(id, value);
            // Snap the picker back so the same target can be chosen again for
            // a second constraint of a different kind.
            OnPropertyChanged(nameof(ConstrainToKey));
        }
    }

    /// <summary>Give the selected bone an aim at a target, and select it for editing.</summary>
    private void AddConstraint(string boneId, string targetId)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(targetId) is null) return;
        var constraint = new BoneConstraint
        {
            // Aim, because it is the one that reads instantly on the canvas —
            // the bone visibly turns to look — so the artist can see what they
            // just made before deciding it should have been a copy.
            Kind = BoneConstraintKind.Aim,
            BoneId = boneId,
            TargetBoneId = targetId,
        };
        _editor.Perform(doc =>
        {
            if (doc.Armature is { } rig) (rig.Constraints ??= []).Add(constraint);
        });
        _selectedConstraintId = constraint.Id;
        NotifyConstraintSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Take the selected constraint off the bone.</summary>
    [RelayCommand]
    private void RemoveConstraint()
    {
        if (SelectedConstraint is not { } c) return;
        var id = c.Id;
        _editor.Perform(doc =>
        {
            doc.Armature?.Constraints?.RemoveAll(x => x.Id == id);
            // Absent, not empty.
            if (doc.Armature is { Constraints.Count: 0 } rig) rig.Constraints = null;
        });
        _selectedConstraintId = null;
        NotifyConstraintSurface();
        InvalidateRiggedFrames();
    }

    private IEnumerable<BoneConstraint> ConstraintsOfSelectedBone() =>
        SelectedBoneId is { } id && Doc.Armature?.Constraints is { } all
            ? all.Where(c => c.BoneId == id)
            : [];

    private BoneConstraint? Constraint(string id) =>
        ConstraintsOfSelectedBone().FirstOrDefault(c => c.Id == id);

    /// <summary>Whether <paramref name="boneId"/> hangs off <paramref name="ancestorId"/>.</summary>
    private static bool HangsOff(Armature armature, string boneId, string ancestorId)
    {
        var seen = new HashSet<string>();
        for (var walk = armature.BoneById(boneId)?.ParentId; walk is not null && seen.Add(walk);)
        {
            if (walk == ancestorId) return true;
            walk = armature.BoneById(walk)?.ParentId;
        }
        return false;
    }

    private void EditConstraint(string id, Action<BoneConstraint> edit)
    {
        _editor.Perform(doc =>
        {
            if (doc.Armature?.Constraints?.FirstOrDefault(c => c.Id == id) is { } c) edit(c);
        });
        NotifyConstraintSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Re-read everything the constraint surface derives from the document.</summary>
    private void NotifyConstraintSurface()
    {
        OnPropertyChanged(nameof(ConstraintRows));
        OnPropertyChanged(nameof(ConstraintTargetRows));
        OnPropertyChanged(nameof(SelectedConstraintId));
        OnPropertyChanged(nameof(HasSelectedConstraint));
        OnPropertyChanged(nameof(ConstraintKindIndex));
        OnPropertyChanged(nameof(ConstraintTargetKey));
        OnPropertyChanged(nameof(ConstraintOffsetDeg));
        OnPropertyChanged(nameof(ConstraintInfluencePercent));
        OnPropertyChanged(nameof(BoneChromes));
    }
}
