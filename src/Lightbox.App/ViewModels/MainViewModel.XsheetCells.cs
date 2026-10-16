using Lightbox.App.Controls;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// What the X-sheet shows in a cel (Q282): the drawing's number by default, a
/// dot, or a picture.
/// </summary>
/// <remarks>
/// A preference rather than document data — it is how the sheet is read, and no
/// pixel of the art depends on it. Pictures are the one style that costs a
/// render per drawing, so they are made only while that style is chosen
/// (<see cref="RefreshThumbnails"/>) and let go when it is not.
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The three styles, in the order the selectors list them.</summary>
    public IReadOnlyList<XsheetCellStyle> XsheetCellStyles { get; } = Enum.GetValues<XsheetCellStyle>();

    public XsheetCellStyle XsheetCellStyle
    {
        get => Enum.IsDefined(Settings.XsheetCellStyle) ? Settings.XsheetCellStyle : XsheetCellStyle.Numbers;
        set
        {
            if (XsheetCellStyle == value) return;
            Settings.XsheetCellStyle = value;
            Settings.Save();
            OnPropertyChanged();
            // Into pictures: every cel goes through the thumbnail cache, which
            // renders only what it does not already hold. Out of them: the cels
            // let go of theirs.
            _allThumbsDirty = true;
            RefreshThumbnails();
        }
    }

    private bool CelPicturesShown => XsheetCellStyle == XsheetCellStyle.Pictures;

    /// <summary>
    /// A drawing with nothing on it: no strokes, no pixels, nothing placed. The
    /// sheet marks it apart from a drawing with ink.
    /// </summary>
    internal static bool IsBlankDrawing(Frame frame) =>
        frame.Strokes.Count == 0 && frame.PngBase64 is null && frame.Placements is not { Count: > 0 };
}
