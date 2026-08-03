using Lightbox.Core.Documents;

namespace Lightbox.Raster.Tips;

/// <summary>
/// The tips that are there before the artist has made any: eight shapes that
/// between them cover what a round brush, a flat brush, a nib, a sponge and a
/// wet edge leave behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recipes, not files.</b> A built-in is the recipe plus a fixed id; the
/// raster is baked on first use and then held. Shipping eight PNGs would put a
/// megabyte of pixels in the repository to hold information that eight lines of
/// arithmetic already contain, and would make "the same tip" mean two different
/// things depending on whether the file was found.
/// </para>
/// <para>
/// <b>The ids are frozen.</b> A document that painted with a built-in copied the
/// raster into <c>Doc.BrushTips</c> under this id, so the id is part of that
/// file's record forever. Renaming one is a compatibility break; changing the
/// pixels behind one silently repaints old drawings, which is invariant 1 read
/// from the wrong end. Adjusting a built-in therefore means adding a new one.
/// </para>
/// <para>
/// The catalogue is deliberately small. Every entry is a shape the others
/// cannot reach — there is no "soft round 2" here, because a size slider and a
/// hardness slider already make those, and a library an artist has to read
/// through is worse than one they can see at a glance.
/// </para>
/// </remarks>
public static class TipCatalogue
{
    /// <summary>The prefix every built-in id carries, so scope is readable off the id.</summary>
    public const string IdPrefix = "tip-builtin-";

    /// <summary>Baked size. Big enough for any dab the engine will ask for, and downscaled from.</summary>
    private const int Size = 256;

    private static readonly Lazy<IReadOnlyList<BrushTip>> Baked = new(Bake, isThreadSafe: true);

    /// <summary>
    /// The recipes, in the order they are shown. Data rather than code so a new
    /// shape is a row here and nothing else.
    /// </summary>
    public static IReadOnlyList<(string Id, string Name, TipRecipe Recipe)> Recipes { get; } =
    [
        // ---- the three staples --------------------------------------------
        ("soft-round", "Soft round", new TipRecipe
        {
            Shape = TipShape.SoftCircle, Size = Size, Hardness = 0.3,
        }),

        ("hard-round", "Hard round", new TipRecipe
        {
            Shape = TipShape.HardCircle, Size = Size,
        }),

        // A flat brush seen head-on: long one way, narrow the other. With
        // AngleFollowsDirection on it is the cheapest thing in the engine that
        // reads as a loaded brush rather than a pen.
        ("paintbrush", "Paintbrush", new TipRecipe
        {
            Shape = TipShape.Chisel, Size = Size, Roundness = 0.38, Angle = 0,
        }),

        // ---- five shapes a circle and a chisel cannot reach ----------------
        ("bristle", "Bristle round", new TipRecipe
        {
            Shape = TipShape.Bristle, Size = Size, Count = 14, Sharpness = 0.45,
        }),

        // n ≈ 6.5: squarish with the corners still rounded, which is what a
        // chisel marker lays down and what an ellipse cannot do at any flatness.
        ("marker-nib", "Marker nib", new TipRecipe
        {
            Shape = TipShape.Superellipse, Size = Size, Roundness = 0.6, Sharpness = 0.8,
        }),

        // Six sides, mostly sharp: a cut nib, and the only tip here with corners.
        ("cut-nib", "Cut nib", new TipRecipe
        {
            Shape = TipShape.Polygon, Size = Size, Count = 6, Sharpness = 0.8, Angle = 15,
        }),

        ("spatter", "Spatter", new TipRecipe
        {
            Shape = TipShape.Spatter, Size = Size, Count = 9, Sharpness = 0.45,
        }),

        // The dried-puddle profile, stamped. The fast counterpart to a fluid
        // simulation, for a brush that is not paying for one.
        ("wet-edge", "Wet edge", new TipRecipe
        {
            Shape = TipShape.Halo, Size = Size, Sharpness = 0.85,
        }),
    ];

    /// <summary>
    /// The baked built-ins. Baked once per process and shared — a tip is an
    /// asset, and re-deriving one per library open would be the same mistake as
    /// re-deriving one per dab, only slower to notice.
    /// </summary>
    public static IReadOnlyList<BrushTip> All => Baked.Value;

    /// <summary>True if this id names a built-in, so the UI can refuse to delete it.</summary>
    public static bool IsBuiltIn(string? id) => id?.StartsWith(IdPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// Bake the catalogue on a background thread, so the first thing that asks
    /// for it does not wait.
    /// </summary>
    /// <remarks>
    /// Eight 256² shapes plus their PNGs measured 162 ms cold on the slow dev
    /// container — once per process, but on the UI thread that lands on window
    /// open, which the charter gives 100 ms. It is a fixed constant with no
    /// input, so there is nothing to wait for it with: start it at launch and
    /// it is done long before a library is opened. <see cref="Lazy{T}"/> is
    /// thread-safe here, so a caller that beats the warm-up simply blocks on
    /// the same bake rather than doing a second one.
    /// </remarks>
    public static void Warm() => Task.Run(() => _ = Baked.Value);

    private static IReadOnlyList<BrushTip> Bake()
    {
        var tips = new List<BrushTip>(Recipes.Count);
        foreach (var (id, name, recipe) in Recipes)
        {
            var tip = TipGenerator.Create(recipe, name);
            tip.Id = IdPrefix + id;
            tips.Add(tip);
        }
        return tips;
    }
}
