namespace Lightbox.Core.Projects;

/// <summary>
/// Two features that are mutually exclusive by construction. Not "one is off by
/// default", but "these contradict each other". Example: unbounded canvas and
/// fixed frame-bounds sprite export cannot both be true — a sprite sheet needs
/// consistent bounds, and an unbounded canvas by definition has no bounds.
///
/// A conflict is **declared between features, never on a project type**. The same
/// conflict holds in every project type; what differs is the default for each
/// feature, not the incompatibility.
///
/// Conflicts are **resolvable**: authoring the missing thing clears it. If an artist
/// enables unbounded canvas, sprite export is refused **with its reason** rather than
/// hidden or greyed out: "Sprite export needs consistent frame bounds; this canvas is
/// unbounded. Author an export region." That is what makes this a conflict, not a
/// hard limit.
/// </summary>
public sealed record FeatureConflict(FeatureKey Feature1, FeatureKey Feature2, string Reason)
{
    /// <summary>Whether this conflict involves both features (order-independent).</summary>
    public bool Involves(FeatureKey feature1, FeatureKey feature2) =>
        (Feature1 == feature1 && Feature2 == feature2)
        || (Feature1 == feature2 && Feature2 == feature1);

    /// <summary>The other feature in the conflict.</summary>
    public FeatureKey Other(FeatureKey thisFeature) =>
        thisFeature == Feature1 ? Feature2 : Feature1;
}

/// <summary>
/// The registry of feature conflicts. Same registry as <see cref="FeatureDefaults"/>,
/// but a second table: one place enumerates what can be turned on, what a type
/// starts with, and what excludes what — three questions the Configure window,
/// the new-document dialog, and the manual all ask.
///
/// A conflict is raised when both features in the pair are true. Raising the
/// conflict returns the **reason** so the UI can refuse the action and explain why.
/// </summary>
public sealed class FeatureConflicts
{
    private readonly List<FeatureConflict> _conflicts;

    public FeatureConflicts()
    {
        _conflicts =
        [
            new(
                FeatureKey.UnboundedCanvas,
                FeatureKey.FixedFrameBoundsExport,
                "Sprite export requires consistent frame bounds, which are incompatible with an unbounded canvas. Author an export region to proceed."
            ),
        ];
    }

    /// <summary>
    /// The conflict if both features are enabled (returns null if no conflict).
    /// If multiple conflicts are raised, this returns the first one found; a
    /// calling policy should probably prevent the situation rather than stacking them.
    /// </summary>
    public FeatureConflict? Raised(bool feature1Enabled, FeatureKey feature1, bool feature2Enabled, FeatureKey feature2)
    {
        if (!feature1Enabled || !feature2Enabled) return null;

        return _conflicts.FirstOrDefault(c => c.Involves(feature1, feature2));
    }

    /// <summary>
    /// All conflicts involving this feature (features that conflict with it).
    /// </summary>
    public IEnumerable<FeatureConflict> ConflictsWith(FeatureKey feature) =>
        _conflicts.Where(c => c.Feature1 == feature || c.Feature2 == feature);

    /// <summary>
    /// All declared conflicts.
    /// </summary>
    public IReadOnlyList<FeatureConflict> All => _conflicts.AsReadOnly();
}
