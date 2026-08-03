namespace Lightbox.Core.Export;

/// <summary>
/// The GameMaker naming contract, which is the whole of the GameMaker integration.
/// </summary>
/// <remarks>
/// <para>
/// <b>GameMaker has no import-time scripting.</b> Unity takes a C# editor script, Godot
/// a GDScript, Unreal a Python script — GameMaker has nothing equivalent, so the pattern
/// the other three share is unavailable. That left one question, and the roadmap had it
/// filed as blocking: write <c>.yy</c> project files, which are JSON but version-coupled
/// and carry GUIDs the IDE expects to own, or find something stable.
/// </para>
/// <para>
/// <b>The answer is the <c>_stripN</c> filename convention, and it makes writing
/// <c>.yy</c> unnecessary.</b> GameMaker's documented behaviour is that a strip image
/// whose filename ends in <c>_strip8</c> is sliced into eight frames on import, with the
/// frame width taken as the image's width divided by that number — honoured when the file
/// is dragged into the IDE, imported through the sprite editor, and by
/// <c>sprite_add()</c> at runtime. It is a naming rule rather than a file format, so
/// there is no schema to guess and no version to be wrong about. This is the same
/// principle the Godot decision settled on, arrived at from the other end: <b>we do not
/// write formats we cannot verify.</b>
/// </para>
/// <para>
/// What the convention costs is layout freedom, and the cost is absolute rather than
/// negotiable: because the frame width is <em>derived by division</em>, the strip must be
/// <b>a single row of uniform cells with no padding</b>. A packed atlas, a per-frame trim
/// or one pixel of gutter all make the division produce the wrong width, and the result
/// is not an error — it is a sprite whose frames are visibly sliced through the middle.
/// The exporter therefore forces the layout rather than offering it.
/// </para>
/// </remarks>
public static class GameMakerConvert
{
    /// <summary>
    /// The filename a strip must have for GameMaker to slice it.
    /// </summary>
    /// <param name="stem">The sprite's name, before sanitising.</param>
    /// <param name="frames">How many frames are in the strip. Below 1 is treated as 1.</param>
    /// <remarks>
    /// The number is the contract: GameMaker divides the image's width by it. Get it
    /// wrong by one and every frame is offset by a fraction of a cell, which reads as a
    /// drawing that wobbles rather than as a bad import.
    /// </remarks>
    public static string StripFileName(string? stem, int frames) =>
        $"{AssetName(stem)}_strip{Math.Max(1, frames)}.png";

    /// <summary>
    /// A name GameMaker will accept as an asset name.
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="UnrealConvert.AssetName"/> deliberately: both engines
    /// reject the same things — spaces, punctuation, anything that is not a letter, digit
    /// or underscore — and one tested implementation is better than two that agree today.
    /// The fallback differs because a GameMaker sprite conventionally reads as one.
    /// </remarks>
    public static string AssetName(string? stem) => UnrealConvert.AssetName(stem, "sprite");

    /// <summary>
    /// Read a strip filename back: the asset name GameMaker will use, and the count.
    /// </summary>
    /// <returns>
    /// The stem without the <c>_stripN</c> suffix and the frame count, or the whole name
    /// and 1 when there is no suffix.
    /// </returns>
    /// <remarks>
    /// Here so the naming contract has an inverse that can be tested against it, rather
    /// than being asserted in one direction only — a round trip catches an off-by-one in
    /// the writer that a forward test agrees with.
    /// </remarks>
    public static (string Name, int Frames) ReadStripName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var at = stem.LastIndexOf("_strip", StringComparison.Ordinal);
        if (at < 0) return (stem, 1);

        var digits = stem[(at + "_strip".Length)..];
        return digits.Length > 0 && int.TryParse(digits, out var frames) && frames > 0
            ? (stem[..at], frames)
            : (stem, 1);
    }

    /// <summary>
    /// A pivot as a GameMaker sprite origin: pixels from the cell's top-left, Y down.
    /// </summary>
    /// <remarks>
    /// The same convention as Unreal's <c>custom_pivot_point</c>, so it delegates rather
    /// than restating the arithmetic — and <c>GameMakerConvertTests</c> asserts the two
    /// agree, which is worth more than a comment claiming they do.
    /// </remarks>
    public static (double X, double Y) SpriteOrigin(
        double pivotX, double pivotY, int cellLeft, int cellTop) =>
        UnrealConvert.PivotPoint(pivotX, pivotY, cellLeft, cellTop);
}
