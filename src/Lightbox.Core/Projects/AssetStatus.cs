namespace Lightbox.Core.Projects;

/// <summary>
/// Where a document is in production.
/// </summary>
/// <remarks>
/// <para>
/// Designed in <c>docs/DESIGN-project-assets.md</c>. Three things make this worth
/// more than a label:
/// </para>
/// <list type="number">
/// <item><b>It lives on the manifest, not in the document.</b> Marking something
/// Ready must not dirty the artwork file, must not touch a pixel, and must not need
/// the document open. Status is production metadata about a drawing, not part of
/// it.</item>
/// <item><b>The exporter filters on it.</b> "Export everything that is Ready" is
/// what lets work in progress live in the same project as shipped art without
/// shipping it by accident. Without status the only way to get that is a second
/// project.</item>
/// <item><b><see cref="Reopened"/> is not a synonym for
/// <see cref="InDevelopment"/>.</b> It means <em>this was Ready and is not any
/// more</em>, which is the state a linear pipeline cannot express and the one a
/// producer most needs to see.</item>
/// </list>
/// <para>
/// No approval gates, no locking, no permissions. An indie studio has none of that
/// machinery and would resent being handed it.
/// </para>
/// </remarks>
public enum AssetStatus
{
    Design,
    Draft,
    InDevelopment,
    Review,
    Ready,

    /// <summary>
    /// Was Ready, and is not any more.
    /// </summary>
    /// <remarks>
    /// Deliberately last rather than between Review and Ready: it is not a step on
    /// the way anywhere, it is a statement that the pipeline went backwards.
    /// </remarks>
    Reopened,
}

/// <summary>Reading and naming statuses without spelling the enum out everywhere.</summary>
public static class AssetStatuses
{
    /// <summary>In production order, with <see cref="AssetStatus.Reopened"/> last.</summary>
    public static IReadOnlyList<AssetStatus> InOrder { get; } =
    [
        AssetStatus.Design,
        AssetStatus.Draft,
        AssetStatus.InDevelopment,
        AssetStatus.Review,
        AssetStatus.Ready,
        AssetStatus.Reopened,
    ];

    /// <summary>What an artist reads on the row.</summary>
    public static string Label(AssetStatus status) => status switch
    {
        AssetStatus.Design => "Design",
        AssetStatus.Draft => "Draft",
        AssetStatus.InDevelopment => "In development",
        AssetStatus.Review => "Review",
        AssetStatus.Ready => "Ready",
        _ => "Reopened",
    };

    /// <summary>
    /// The dot beside the name.
    /// </summary>
    /// <remarks>
    /// Cool to warm to green along the pipeline, and <see cref="AssetStatus.Reopened"/>
    /// in the one colour that reads as "look at this": it is the only status that means
    /// something went wrong, and giving it the same amber as Review would hide exactly
    /// the state the field exists to surface.
    /// </remarks>
    public static string Color(AssetStatus status) => status switch
    {
        AssetStatus.Design => "#6a7a8a",
        AssetStatus.Draft => "#7a6a9a",
        AssetStatus.InDevelopment => "#4a7ab0",
        AssetStatus.Review => "#c08a30",
        AssetStatus.Ready => "#4a9a5e",
        _ => "#c25050",
    };
}
