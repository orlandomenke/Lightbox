namespace Lightbox.Core.Projects;

/// <summary>
/// The enumeration of features that can have defaults per project type and may
/// have conflicts with other features. Each feature is reachable in every
/// project type; a type only sets the default, never the availability.
///
/// Adding a feature means:
/// 1. Add the key here
/// 2. Update <see cref="FeatureDefaults"/> constructor with the default for each type
/// 3. If it conflicts with anything, declare that in <see cref="FeatureConflicts"/>
/// 4. Add a test to <see cref="FeatureDefaultsTests"/>
/// </summary>
public enum FeatureKey
{
    /// <summary>Whether the canvas is unbounded (infinite canvas mode).</summary>
    UnboundedCanvas,

    /// <summary>Whether export uses fixed frame bounds (sprite sheet generation).</summary>
    FixedFrameBoundsExport,

    /// <summary>Whether the camera is available and can be keyframed.</summary>
    Camera,

    /// <summary>Whether the project has multiple layers support.</summary>
    Layers,

    /// <summary>Whether the project has exposure sheet (onion skin, holds).</summary>
    ExposureSheet,
}
