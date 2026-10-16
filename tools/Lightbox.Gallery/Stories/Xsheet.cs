using Avalonia.Controls;
using Avalonia.Layout;
using static Lightbox.Gallery.Stories.Kit;

namespace Lightbox.Gallery.Stories;

/// <summary>
/// The X-sheet's cels, sketched before any of it is built: today's picture per
/// cel against marks and numbers, and the columns the paper sheet had that
/// digital sheets dropped. Sample data on <see cref="XsheetSketch"/>; the
/// app's sheet is untouched.
/// </summary>
public static partial class Catalog
{
    private static Story XsheetCells() => new(
        "X-sheet · cels (sketch)",
        "One scene, six layers, 36 frames, drawn three ways. As built: a picture per keyed cel, 28 px rows — the row height is set by the "
        + "picture, and only ~28 frames fit. Dots: filled key, ring breakdown, small dot inbetween, white square blank, on the hold bar — "
        + "16 px rows, the whole scene fits. Numbers: the paper sheet — the drawing's number where it lands and a line along the hold, so a "
        + "cycle reads as 1 2 3 4 1 2 3 4. Live: point at a cel, click to move the playhead.",
        [
            new("As built · a picture per cel, 28 px rows", () => Sheet(new(CellMode.Pictures, RowHeight: 28, FrameWidth: 28))),
            new("Dots · 16 px rows", () => Sheet(new(CellMode.Dots))),
            new("Numbers · the paper sheet, 16 px rows", () => Sheet(new(CellMode.Numbers, FrameWidth: 18))),
        ],
        Columns: 1);

    private static Story XsheetAdditions() => new(
        "X-sheet · additions (sketch)",
        "What the field mostly lacks, on the 16 px sheet. One picture, rendered when pointed at, instead of one per cel. The onion range "
        + "tinted on the sheet (red behind, green ahead). The pipeline stage under each hold: grey rough, blue clean, green colour. Cycle "
        + "seams and a ×3 count. Inbetweens asked of the AI (dashed, purple) and drafts waiting for review. A dialogue lane: the scratch "
        + "track's waveform with its sounds broken down.",
        [
            new("Dots · one picture, on hover", () => Sheet(new(CellMode.Dots, Hover: (1, 9)))),
            new("Numbers · every addition, hovering an AI draft", () => Sheet(new(CellMode.Numbers, FrameWidth: 18, Onion: true, Stages: true, Ai: true, Cycles: true, Dialogue: true, Hover: (3, 12)))),
            new("Dots · every addition", () => Sheet(new(CellMode.Dots, Onion: true, Stages: true, Ai: true, Cycles: true, Dialogue: true))),
        ],
        Columns: 1);

    private static Story XsheetVertical() => new(
        "X-sheet · vertical (sketch)",
        "The same scene the way paper lays it: frames down, a column per layer, the Action column (markers, notes) and the Dialogue column "
        + "first, a heavier rule every 8 frames — half a foot of 35 mm. Q58 left this as an orientation toggle over the same grid, not a "
        + "second sheet; drawn this way it is the same painter with the axes swapped.",
        [
            new("Numbers · vertical", () => Sheet(new(CellMode.Numbers, Vertical: true, Dialogue: true, Stages: true, Cycles: true, Ai: true))),
            new("Dots · vertical", () => Sheet(new(CellMode.Dots, Vertical: true, Dialogue: true, Onion: true))),
        ],
        Columns: 2);

    private static Control Sheet(SheetOptions options) => new Border
    {
        Background = Brush("SurfacePanelBrush"),
        Padding = new Avalonia.Thickness(0, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new XsheetSketch(options) { HorizontalAlignment = HorizontalAlignment.Left },
    };
}
