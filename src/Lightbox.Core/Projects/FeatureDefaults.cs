namespace Lightbox.Core.Projects;

/// <summary>
/// The single source of truth for feature defaults per project type. This is
/// what the Configure window lists, the new-document dialog reads, and the
/// manual uses — three places deriving that from one table cannot disagree.
///
/// Defaults are **derived, not copied**: a document stores only what its artist
/// changed, so a default that moves in a later version moves for every document
/// that never overrode it. This is the same discipline as <see cref="BrushCostOf"/>.
/// </summary>
public sealed class FeatureDefaults
{
    private readonly Dictionary<ProjectType, Dictionary<FeatureKey, bool>> _defaults;

    public FeatureDefaults()
    {
        _defaults = new()
        {
            {
                ProjectType.Illustration,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, false },
                    { FeatureKey.Camera, false },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, false },
                }
            },
            {
                ProjectType.Animation,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, false },
                    { FeatureKey.Camera, false },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, true },
                }
            },
            {
                ProjectType.GameArt,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, true },
                    { FeatureKey.Camera, false },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, false },
                }
            },
            {
                ProjectType.Storyboard,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, false },
                    { FeatureKey.Camera, true },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, true },
                }
            },
            {
                ProjectType.Comic,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, false },
                    { FeatureKey.Camera, false },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, false },
                }
            },
            {
                ProjectType.AssetLibrary,
                new()
                {
                    { FeatureKey.FixedFrameBoundsExport, true },
                    { FeatureKey.Camera, false },
                    { FeatureKey.Layers, true },
                    { FeatureKey.ExposureSheet, false },
                }
            },
        };
    }

    /// <summary>
    /// The default for a feature in a given project type. Used by the new-document
    /// dialog to set document defaults, and by the manual to show what starts on.
    /// </summary>
    public bool GetDefault(ProjectType type, FeatureKey feature) =>
        _defaults.TryGetValue(type, out var typeDefaults) && typeDefaults.TryGetValue(feature, out var value)
            ? value
            : throw new InvalidOperationException($"No default for {feature} in {type}");

    /// <summary>
    /// All features that have a default for this project type.
    /// </summary>
    public IEnumerable<FeatureKey> FeaturesFor(ProjectType type) =>
        _defaults.TryGetValue(type, out var typeDefaults)
            ? typeDefaults.Keys
            : [];

    /// <summary>
    /// All project types that have a default for this feature.
    /// </summary>
    public IEnumerable<ProjectType> TypesFor(FeatureKey feature) =>
        _defaults
            .Where(kv => kv.Value.ContainsKey(feature))
            .Select(kv => kv.Key);
}
