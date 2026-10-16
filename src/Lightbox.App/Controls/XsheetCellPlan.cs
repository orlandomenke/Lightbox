using Lightbox.App.ViewModels;

namespace Lightbox.App.Controls;

/// <summary>How the X-sheet shows a cel's drawing (Q282).</summary>
/// <remarks>
/// A preference, not document data: it is how the sheet is read, and changes no
/// pixel of the art. <see cref="Numbers"/> is first so it is the default — the
/// paper sheet's convention, and the one in which a cycle reads as the same
/// numbers again.
/// </remarks>
public enum XsheetCellStyle
{
    /// <summary>The drawing's number where it lands, and a line along its hold.</summary>
    Numbers,

    /// <summary>A mark where the drawing lands — filled key, ring breakdown, small dot inbetween — on a bar along its hold.</summary>
    Dots,

    /// <summary>A picture of the drawing in the cel. The costly one: every drawing is rendered small.</summary>
    Pictures,
}

/// <summary>What one cel says on the drawn sheet.</summary>
/// <param name="Number">
/// Which drawing of the row is exposed here, counting from 1; 0 before the
/// row's first drawing and past the end of the scene.
/// </param>
/// <param name="Starts">Whether the drawing lands on this cel, rather than being held into it.</param>
/// <param name="HoldEnd">The last cel this drawing is exposed on.</param>
/// <param name="Blank">Whether the drawing exposed here has nothing on it.</param>
internal readonly record struct CelGlyph(int Number, bool Starts, int HoldEnd, bool Blank);

/// <summary>
/// Reads a row of cels as the paper sheet does: drawings numbered along the
/// row, each one held until the next lands or the scene ends.
/// </summary>
/// <remarks>
/// The number is a count along the row, which is exactly a drawing's identity
/// while every exposure is its own drawing. When one drawing can be exposed in
/// several cels (Q282) the number becomes the drawing's own, and this is the one
/// place that changes.
/// </remarks>
internal static class XsheetCellPlan
{
    public static CelGlyph[] Of(IReadOnlyList<FrameCell> cells)
    {
        var plan = new CelGlyph[cells.Count];
        var number = 0;
        var blank = false;
        var start = -1;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.IsVirtual)
            {
                Close(i - 1);
                continue;
            }
            if (cell.IsKeyed)
            {
                Close(i - 1);
                number++;
                blank = cell.IsBlank;
                start = i;
            }
            plan[i] = new CelGlyph(number, cell.IsKeyed, i, number > 0 && blank);
        }
        Close(cells.Count - 1);
        return plan;

        // The drawing that started at `start` is held to `last`: every cel of the
        // run gets the same end, so a hold can be drawn from whichever is on screen.
        void Close(int last)
        {
            if (start < 0) return;
            for (var j = start; j <= last; j++) plan[j] = plan[j] with { HoldEnd = last };
            start = -1;
        }
    }
}
