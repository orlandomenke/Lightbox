using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lightbox.App.Controls;

namespace Lightbox.Gallery.Stories;

/// <summary>How a cel's drawing is shown on the sheet.</summary>
public enum CellMode
{
    /// <summary>Today: a picture of the drawing in every keyed cel.</summary>
    Pictures,

    /// <summary>A mark per drawing (filled key, ring breakdown, small dot inbetween) on its hold bar.</summary>
    Dots,

    /// <summary>The paper sheet: the drawing's number, and a line down the hold.</summary>
    Numbers,
}

/// <summary>What a drawing is, for its mark.</summary>
public enum Mark { Key, Breakdown, Inbetween, Blank, AiPending, AiDraft }

/// <summary>How far through the pipeline a drawing is (rough → clean → colour).</summary>
public enum Stage { None, Rough, Clean, Colour }

/// <summary>One drawing on a row: where it lands, how long it holds, its number.</summary>
public sealed record SketchDrawing(int Frame, int HoldEnd, int Number, Mark Mark, Stage Stage = Stage.None, bool Reused = false);

/// <summary>One row of the sketch sheet.</summary>
public sealed record SketchRow(string Name, Color Colour, IReadOnlyList<SketchDrawing> Drawings, bool Folder = false, (int Start, int End, int Times)? Cycle = null);

/// <summary>Which parts of the sheet a state draws.</summary>
public sealed record SheetOptions(
    CellMode Mode,
    double RowHeight = 16,
    double FrameWidth = 14,
    bool Vertical = false,
    bool Onion = false,
    bool Stages = false,
    bool Ai = false,
    bool Cycles = false,
    bool Dialogue = false,
    (int Row, int Frame)? Hover = null);

/// <summary>
/// A sketch of the X-sheet as one drawn control rather than a Button per cel —
/// sample data only, no view model. The same scene under every
/// <see cref="CellMode"/>, so the states compare like for like.
/// </summary>
/// <remarks>
/// Drawn, as <see cref="TrackView"/> is, because the shape it proposes for the
/// app is that one: the cost is the cels on screen, not one control (and one
/// context menu) per cel in the scene.
/// </remarks>
public sealed class XsheetSketch : Control
{
    public const int SceneLength = 36;
    private const int DrawnTo = 44;
    private const double Gutter = 132;
    private const double RulerHeight = 22;
    private const double DialogueHeight = 22;
    private const double ColumnWidth = 46;
    private const double FrameColumn = 30;

    private static readonly Color AiColour = Color.Parse("#B49CFF");
    private static readonly Color Playhead = Color.Parse("#B49CFF");

    private readonly SheetOptions _o;
    private int _current = 12;
    private (int Row, int Frame)? _hover;

    public XsheetSketch(SheetOptions options)
    {
        _o = options;
        _hover = options.Hover;
        ClipToBounds = true;
        ThemeColour.RepaintOnThemeChange(this);
    }

    // ── sample scene ────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<SketchRow> Rows = BuildRows();

    private static IReadOnlyList<SketchRow> BuildRows()
    {
        // Ink: on 2s, keys every 8, breakdowns halfway. Clean to 16, rough after.
        var ink = new List<SketchDrawing>();
        for (int f = 0, n = 1; f < SceneLength; f += 2, n++)
        {
            var mark = f % 8 == 0 ? Mark.Key : f % 4 == 0 ? Mark.Breakdown : Mark.Inbetween;
            ink.Add(new(f, f + 1, n, mark, f < 16 ? Stage.Clean : Stage.Rough));
        }

        // Colour follows the ink to 16, then a blank keyframe held out.
        var colour = ink.Where(d => d.Frame < 16).Select(d => d with { Stage = Stage.Colour }).ToList();
        colour.Add(new(16, SceneLength - 1, 9, Mark.Blank));

        // Rough keys, with the inbetweens asked of the AI between them: pending
        // between the first pair, a draft waiting for review between the second.
        var rough = new List<SketchDrawing>
        {
            new(0, 1, 1, Mark.Key, Stage.Rough),
            new(2, 3, 0, Mark.AiPending), new(4, 5, 0, Mark.AiPending), new(6, 7, 0, Mark.AiPending),
            new(8, 9, 2, Mark.Key, Stage.Rough),
            new(10, 11, 5, Mark.AiDraft), new(12, 13, 6, Mark.AiDraft), new(14, 15, 7, Mark.AiDraft),
            new(16, 23, 3, Mark.Key, Stage.Rough),
            new(24, 31, 4, Mark.Breakdown, Stage.Rough),
            new(32, SceneLength - 1, 8, Mark.Key, Stage.Rough),
        };

        // A walk cycle: four drawings on 2s, exposed three times. Today every
        // repeat is a copy; on paper it is the same number written again.
        var walk = new List<SketchDrawing>();
        for (int f = 0; f < 24; f += 2)
        {
            var n = f / 2 % 4 + 1;
            walk.Add(new(f, f + 1, n, n == 1 ? Mark.Key : Mark.Inbetween, Stage.Clean, Reused: f >= 8));
        }
        walk.Add(new(24, SceneLength - 1, 5, Mark.Key, Stage.Clean));

        var bg = new List<SketchDrawing> { new(0, SceneLength - 1, 1, Mark.Key, Stage.Colour) };

        // A folder row: a bar wherever anything inside it is drawn.
        var folder = ink.Select(d => d with { Mark = Mark.Inbetween, Stage = Stage.None }).ToList();

        return
        [
            new("▾ Character", Color.Parse("#9AA0AE"), folder, Folder: true),
            new("Ink", Color.Parse("#7B61FF"), ink),
            new("Colour", Color.Parse("#2FD1B9"), colour),
            new("Rough", Color.Parse("#E8C55F"), rough),
            new("Walk", Color.Parse("#7BD156"), walk, Cycle: (0, 7, 3)),
            new("BG", Color.Parse("#4FA3FF"), bg),
        ];
    }

    private static readonly (int Frame, int End, string Sound)[] Phonemes =
    [
        (2, 4, "M"), (5, 8, "A"), (9, 10, "E"), (11, 14, "O"), (15, 16, "U"), (17, 19, "rest"),
        (22, 23, "F"), (24, 27, "A"), (28, 30, "L"), (31, 34, "O"),
    ];

    private static readonly (int Frame, string Label)[] Markers = [(8, "contact"), (12, "passing")];

    // ── geometry ────────────────────────────────────────────────────────────

    private int RowCount => Rows.Count;

    private double SheetTop => _o.Vertical ? RulerHeight : RulerHeight + (_o.Dialogue ? DialogueHeight : 0);

    private double SheetLeft => _o.Vertical ? FrameColumn + ColumnWidth * (_o.Dialogue ? 2 : 1) : Gutter;

    private Rect Cell(int row, int frame) => _o.Vertical
        ? new Rect(SheetLeft + row * ColumnWidth, SheetTop + frame * _o.RowHeight, ColumnWidth, _o.RowHeight)
        : new Rect(Gutter + frame * _o.FrameWidth, SheetTop + row * _o.RowHeight, _o.FrameWidth, _o.RowHeight);

    private Rect Span(int row, int from, int to) => Cell(row, from).Union(Cell(row, to));

    /// <summary>Shrink across the row only — a bar keeps its length along time.</summary>
    private Rect Across(Rect r, double fraction)
    {
        if (_o.Vertical)
        {
            var w = r.Width * fraction;
            return new Rect(r.Center.X - w / 2, r.Y, w, r.Height);
        }
        var h = r.Height * fraction;
        return new Rect(r.X, r.Center.Y - h / 2, r.Width, h);
    }

    protected override Size MeasureOverride(Size available) => _o.Vertical
        ? new Size(SheetLeft + RowCount * ColumnWidth + 8, SheetTop + DrawnTo * _o.RowHeight)
        : new Size(Math.Min(available.Width, Gutter + DrawnTo * _o.FrameWidth), SheetTop + RowCount * _o.RowHeight + 4);

    private (int Row, int Frame)? HitCell(Point p)
    {
        for (var r = 0; r < RowCount; r++)
            for (var f = 0; f < DrawnTo; f++)
                if (Cell(r, f).Contains(p)) return (r, f);
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitCell(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = _o.Hover;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitCell(e.GetPosition(this)) is { } hit) { _current = hit.Frame; InvalidateVisual(); }
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    private static readonly Typeface Face = new("Inter");

    private static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Face.FontFamily, FontStyle.Normal, weight), size, brush);

    private static void Centre(DrawingContext dc, FormattedText t, Point at) =>
        dc.DrawText(t, new Point(at.X - t.Width / 2, at.Y - t.Height / 2));

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public override void Render(DrawingContext dc)
    {
        var text = new SolidColorBrush(ThemeColour.Of(this, "TextSecondaryBrush", Color.Parse("#A6ABB8")));
        var strong = new SolidColorBrush(ThemeColour.Of(this, "TextPrimaryBrush", Colors.White));
        var faint = new Pen(new SolidColorBrush(ThemeColour.Hairline(this, 0x14)), 1);
        var second = new Pen(new SolidColorBrush(ThemeColour.Hairline(this, 0x40)), 1);

        dc.FillRectangle(new SolidColorBrush(ThemeColour.Of(this, "SurfacePanelBrush", Color.Parse("#23252B"))), new Rect(Bounds.Size));

        DrawGrid(dc, faint, second);
        DrawPastTheEnd(dc);
        if (_o.Onion) DrawOnionRange(dc);
        DrawCurrentColumn(dc);
        DrawRuler(dc, text, strong);
        DrawHeaders(dc, text, strong);
        if (_o.Dialogue) DrawDialogue(dc, text, strong);

        for (var r = 0; r < RowCount; r++)
        {
            var row = Rows[r];
            if (_o.Cycles && row.Cycle is { } cycle) DrawCycle(dc, r, cycle, row.Colour, text);
            foreach (var d in row.Drawings)
                DrawDrawing(dc, r, row, d, strong, text);
        }

        if (_hover is { } h && _o.Mode != CellMode.Pictures) DrawHoverCard(dc, h, strong, text);
    }

    private void DrawGrid(DrawingContext dc, Pen faint, Pen second)
    {
        for (var f = 0; f <= DrawnTo; f++)
        {
            var pen = (_o.Vertical ? f % 8 : f % 24) == 0 ? second : faint;
            var c = Cell(0, Math.Min(f, DrawnTo - 1));
            if (_o.Vertical)
            {
                var y = f == DrawnTo ? c.Bottom : c.Top;
                dc.DrawLine(pen, new Point(0, y), new Point(SheetLeft + RowCount * ColumnWidth, y));
            }
            else
            {
                var x = f == DrawnTo ? c.Right : c.Left;
                dc.DrawLine(pen, new Point(x, SheetTop), new Point(x, SheetTop + RowCount * _o.RowHeight));
            }
        }
        for (var r = 0; r <= RowCount; r++)
        {
            if (_o.Vertical)
            {
                var x = SheetLeft + r * ColumnWidth;
                dc.DrawLine(faint, new Point(x, SheetTop), new Point(x, SheetTop + DrawnTo * _o.RowHeight));
            }
            else
            {
                var y = SheetTop + r * _o.RowHeight;
                dc.DrawLine(faint, new Point(0, y), new Point(Gutter + DrawnTo * _o.FrameWidth, y));
            }
        }
    }

    private void DrawPastTheEnd(DrawingContext dc)
    {
        var area = Span(0, SceneLength, DrawnTo - 1).Union(Span(RowCount - 1, SceneLength, DrawnTo - 1));
        var hatch = new Pen(new SolidColorBrush(ThemeColour.Hairline(this, 0x18)), 1);
        using (dc.PushClip(area))
            for (var x = area.Left - area.Height; x < area.Right + area.Height; x += 6)
                dc.DrawLine(hatch, new Point(x, area.Bottom), new Point(x + area.Height, area.Top));
    }

    private void DrawCurrentColumn(DrawingContext dc)
    {
        var col = Span(0, _current, _current).Union(Span(RowCount - 1, _current, _current));
        dc.FillRectangle(new SolidColorBrush(Alpha(Playhead, 0x26)), col);
        var pen = new Pen(new SolidColorBrush(Playhead), 1.5);
        if (_o.Vertical) dc.DrawLine(pen, new Point(0, col.Top), new Point(col.Right, col.Top));
        else dc.DrawLine(pen, new Point(col.Center.X, 0), new Point(col.Center.X, col.Bottom));
    }

    /// <summary>
    /// The onion range on the sheet itself: which frames are ghosting right now,
    /// tinted the onion colours (red behind, green ahead), so "why is that ghost
    /// there" is answered without opening the onion settings.
    /// </summary>
    private void DrawOnionRange(DrawingContext dc)
    {
        const int before = 3, after = 2;
        var back = Color.Parse("#FF6B6B");
        var ahead = Color.Parse("#5FD38A");
        for (var k = 1; k <= Math.Max(before, after); k++)
        {
            var fade = (byte)(0x30 - k * 6);
            if (k <= before && _current - k * 2 >= 0) Band(_current - k * 2, back, fade);
            if (k <= after) Band(_current + k * 2, ahead, fade);
        }

        void Band(int frame, Color c, byte a)
        {
            var r = Span(0, frame, frame + 1).Union(Span(RowCount - 1, frame, frame + 1));
            dc.FillRectangle(new SolidColorBrush(Alpha(c, a)), r);
            var ruler = _o.Vertical
                ? new Rect(0, r.Top, 4, r.Height)
                : new Rect(r.Left, RulerHeight - 4, r.Width, 3);
            dc.FillRectangle(new SolidColorBrush(Alpha(c, 0xC0)), ruler);
        }
    }

    private void DrawRuler(DrawingContext dc, IBrush text, IBrush strong)
    {
        for (var f = 0; f < DrawnTo; f++)
        {
            var n = f + 1;
            var labelled = _o.Vertical || n == 1 || n % 6 == 0 || f == _current;
            if (!labelled) continue;
            var c = Cell(0, f);
            var t = Text(n.ToString(), 9.5, f == _current ? strong : text, f == _current ? FontWeight.SemiBold : FontWeight.Normal);
            if (_o.Vertical) dc.DrawText(t, new Point(FrameColumn - 6 - t.Width, c.Center.Y - t.Height / 2));
            else Centre(dc, t, new Point(c.Center.X, RulerHeight / 2 - 1));
        }

        foreach (var (frame, label) in Markers)
        {
            var c = Cell(0, frame);
            var chip = Text(label, 8.5, Brushes.Black);
            var at = _o.Vertical
                ? new Rect(FrameColumn + 2, c.Top + 1, ColumnWidth - 4, c.Height - 2)
                : new Rect(c.Left, 1, chip.Width + 8, 11);
            if (!_o.Vertical) continue; // on the horizontal sheet markers stay on the ruler chips the app has
            dc.DrawRectangle(new SolidColorBrush(Color.Parse("#E8C55F")), null, at, 3, 3);
            dc.DrawText(chip, new Point(at.X + 3, at.Center.Y - chip.Height / 2));
        }
    }

    private void DrawHeaders(DrawingContext dc, IBrush text, IBrush strong)
    {
        for (var r = 0; r < RowCount; r++)
        {
            var row = Rows[r];
            if (_o.Vertical)
            {
                var x = SheetLeft + r * ColumnWidth;
                dc.FillRectangle(new SolidColorBrush(row.Colour), new Rect(x + 2, 2, ColumnWidth - 4, 3));
                var name = Text(row.Name.TrimStart('▾', ' '), 9.5, r == 1 ? strong : text);
                Centre(dc, name, new Point(x + ColumnWidth / 2, RulerHeight / 2 + 2));
            }
            else
            {
                var y = SheetTop + r * _o.RowHeight;
                dc.FillRectangle(new SolidColorBrush(row.Colour), new Rect(4, y + 3, 3, _o.RowHeight - 6));
                var name = Text(row.Name, _o.RowHeight < 18 ? 10 : 11, r == 1 ? strong : text, r == 1 ? FontWeight.SemiBold : FontWeight.Normal);
                dc.DrawText(name, new Point(row.Folder ? 12 : 22, y + (_o.RowHeight - name.Height) / 2));
            }
        }

        if (_o.Vertical)
        {
            Centre(dc, Text("Action", 9.5, text), new Point(FrameColumn + ColumnWidth / 2, RulerHeight / 2 + 2));
            if (_o.Dialogue) Centre(dc, Text("Dial.", 9.5, text), new Point(FrameColumn + ColumnWidth * 1.5, RulerHeight / 2 + 2));
        }
    }

    /// <summary>
    /// The dialogue lane: the scratch track's waveform with the sound broken
    /// down under it, the column a paper sheet had and a digital one dropped.
    /// </summary>
    private void DrawDialogue(DrawingContext dc, IBrush text, IBrush strong)
    {
        var wave = new SolidColorBrush(Color.Parse("#5563C7A6"));
        if (_o.Vertical)
        {
            var x0 = FrameColumn + ColumnWidth;
            foreach (var (frame, end, sound) in Phonemes)
            {
                var r = new Rect(x0 + 2, SheetTop + frame * _o.RowHeight + 1, ColumnWidth - 4, (end - frame + 1) * _o.RowHeight - 2);
                dc.DrawRectangle(wave, null, r, 3, 3);
                dc.DrawText(Text(sound, 9.5, strong), new Point(r.X + 4, r.Y + 1));
            }
            return;
        }

        var top = RulerHeight;
        for (var f = 0; f < SceneLength; f++)
        {
            var amp = Math.Abs(Math.Sin(f * 0.9) * Math.Sin(f * 0.23 + 1)) * (DialogueHeight - 6);
            var c = Cell(0, f);
            dc.FillRectangle(wave, new Rect(c.Left + 1, top + (DialogueHeight - amp) / 2, Math.Max(1, c.Width - 2), amp));
        }
        foreach (var (frame, end, sound) in Phonemes)
        {
            var span = Cell(0, frame).Union(Cell(0, end));
            var t = Text(sound, 9.5, strong, FontWeight.SemiBold);
            if (t.Width < span.Width + 4) dc.DrawText(t, new Point(span.Left + 2, top + (DialogueHeight - t.Height) / 2));
        }
        dc.DrawText(Text("Dialogue", 10, text), new Point(22, top + 4));
    }

    /// <summary>The paper cycle bracket: "these drawings, this many times".</summary>
    private void DrawCycle(DrawingContext dc, int r, (int Start, int End, int Times) cycle, Color colour, IBrush text)
    {
        var span = Span(r, cycle.Start, cycle.End * cycle.Times + cycle.Times - 1);
        var pen = new Pen(new SolidColorBrush(Alpha(colour, 0xB0)), 1);
        var len = cycle.End - cycle.Start + 1;
        for (var k = 1; k < cycle.Times; k++)
        {
            var seam = Cell(r, cycle.Start + k * len);
            if (_o.Vertical) dc.DrawLine(pen, new Point(seam.Left, seam.Top), new Point(seam.Right, seam.Top));
            else dc.DrawLine(new Pen(pen.Brush, 1.5, new DashStyle([2, 2], 0)), new Point(seam.Left, seam.Top), new Point(seam.Left, seam.Bottom));
        }
        var label = Text($"cycle ×{cycle.Times}", 8.5, text);
        if (!_o.Vertical) dc.DrawText(label, new Point(span.Right - label.Width - 2, span.Top - 1));
    }

    private void DrawDrawing(DrawingContext dc, int r, SketchRow row, SketchDrawing d, IBrush strong, IBrush text)
    {
        if (!_o.Ai && d.Mark is Mark.AiPending) return;
        var mark = !_o.Ai && d.Mark is Mark.AiDraft ? Mark.Inbetween : d.Mark;
        var cell = Cell(r, d.Frame);
        var hold = Span(r, d.Frame, d.HoldEnd);
        var c = row.Colour;

        if (row.Folder)
        {
            dc.FillRectangle(new SolidColorBrush(Alpha(c, 0x55)), Across(hold, 0.25));
            return;
        }

        switch (_o.Mode)
        {
            case CellMode.Pictures:
                DrawPicture(dc, cell, d, c);
                break;

            case CellMode.Dots:
            {
                if (mark is not Mark.AiPending)
                    dc.DrawRectangle(new SolidColorBrush(Alpha(c, mark is Mark.Blank ? (byte)0x22 : (byte)0x55)), null,
                        Across(hold.Deflate(new Thickness(1, 0)), 0.5), 3, 3);
                DrawMark(dc, cell.Center, mark, c, Math.Min(cell.Width, cell.Height));
                break;
            }

            case CellMode.Numbers:
            {
                // The paper convention: the number where the drawing lands, a
                // line down (here along) the hold. A reused drawing is the same
                // number again, which is what a cycle IS on paper.
                var holdLine = new Pen(new SolidColorBrush(Alpha(c, 0x90)), 1.5);
                if (d.HoldEnd > d.Frame)
                {
                    var from = Cell(r, d.Frame + 1);
                    var to = Cell(r, d.HoldEnd);
                    if (_o.Vertical) dc.DrawLine(holdLine, new Point(from.Center.X, from.Top + 2), new Point(to.Center.X, to.Bottom - 2));
                    else dc.DrawLine(holdLine, new Point(from.Left + 2, from.Center.Y), new Point(to.Right - 2, to.Center.Y));
                }
                var label = mark switch
                {
                    Mark.Blank => "×",
                    Mark.AiPending => "·",
                    _ => d.Number.ToString(),
                };
                var brush = mark is Mark.AiPending or Mark.AiDraft ? new SolidColorBrush(AiColour) : d.Reused ? text : strong;
                var weight = mark is Mark.Key ? FontWeight.Bold : FontWeight.Normal;
                var t = Text(label, mark is Mark.Inbetween ? 9.5 : 10.5, brush, weight);
                Centre(dc, t, cell.Center);
                if (mark is Mark.Breakdown)
                    dc.DrawEllipse(null, new Pen(strong, 1), cell.Center, Math.Max(t.Width, t.Height) / 2 + 1.5, Math.Max(t.Width, t.Height) / 2 + 1.5);
                if (mark is Mark.AiDraft)
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(AiColour), 1, new DashStyle([2, 2], 0)), cell.Deflate(2), 2, 2);
                break;
            }
        }

        if (_o.Stages && d.Stage is not Stage.None) DrawStage(dc, hold, d.Stage);
    }

    private void DrawMark(DrawingContext dc, Point at, Mark mark, Color c, double size)
    {
        var rad = Math.Max(3, size * 0.27);
        var fill = new SolidColorBrush(c);
        switch (mark)
        {
            case Mark.Key:
                dc.DrawEllipse(fill, null, at, rad, rad);
                break;
            case Mark.Breakdown:
                dc.DrawEllipse(null, new Pen(fill, 1.75), at, rad - 0.5, rad - 0.5);
                break;
            case Mark.Inbetween:
                dc.DrawEllipse(fill, null, at, rad * 0.55, rad * 0.55);
                break;
            case Mark.Blank:
                // A drawing with nothing on it: the white cel, as the sheet already says it.
                dc.DrawRectangle(Brushes.White, new Pen(fill, 1), new Rect(at.X - rad + 0.5, at.Y - rad + 0.5, rad * 2 - 1, rad * 2 - 1), 1.5, 1.5);
                break;
            case Mark.AiPending:
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(AiColour), 1.25, new DashStyle([1.5, 1.5], 0)), at, rad * 0.8, rad * 0.8);
                break;
            case Mark.AiDraft:
                dc.DrawEllipse(new SolidColorBrush(Alpha(AiColour, 0x50)), new Pen(new SolidColorBrush(AiColour), 1.25), at, rad * 0.7, rad * 0.7);
                break;
        }
    }

    /// <summary>
    /// Where the drawing is in the pipeline, as a hairline under its hold —
    /// read across a row, "everything up to 16 is clean" is one glance.
    /// </summary>
    private void DrawStage(DrawingContext dc, Rect hold, Stage stage)
    {
        var colour = stage switch
        {
            Stage.Rough => Color.Parse("#9AA0AE"),
            Stage.Clean => Color.Parse("#4FA3FF"),
            _ => Color.Parse("#5FD38A"),
        };
        var line = _o.Vertical
            ? new Rect(hold.Right - 3, hold.Top + 1, 2, hold.Height - 2)
            : new Rect(hold.Left + 1, hold.Bottom - 2.5, hold.Width - 2, 2);
        dc.FillRectangle(new SolidColorBrush(colour), line);
    }

    /// <summary>
    /// Today's look, schematically: a picture in every keyed cel. The scribble
    /// is a stand-in seeded from the drawing's number — the point is the space
    /// it takes and that each one is a render, not what it shows.
    /// </summary>
    private static void DrawPicture(DrawingContext dc, Rect cell, SketchDrawing d, Color c)
    {
        var tile = cell.Deflate(new Thickness(2, 3));
        dc.DrawRectangle(new SolidColorBrush(d.Mark switch
        {
            Mark.Breakdown => Color.Parse("#3A3F4E"),
            Mark.Inbetween => Color.Parse("#30343F"),
            _ => Color.Parse("#444A5C"),
        }), null, cell.Deflate(new Thickness(0.5, 0)));
        if (d.Mark is Mark.AiPending) return;
        dc.DrawRectangle(Brushes.White, null, tile, 1, 1);
        if (d.Mark is Mark.Blank) return;
        Scribble(dc, tile, d.Number, Color.Parse("#2A2D36"));
    }

    private static void Scribble(DrawingContext dc, Rect r, int seed, Color ink)
    {
        var pen = new Pen(new SolidColorBrush(ink), Math.Max(0.8, r.Height / 14));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double H(int k) => (Math.Sin(seed * 12.9898 + k * 78.233) * 43758.5453) % 1 is var v && v < 0 ? v + 1 : v;
            ctx.BeginFigure(new Point(r.X + r.Width * (0.3 + 0.1 * H(1)), r.Y + r.Height * 0.25), false);
            ctx.CubicBezierTo(
                new Point(r.X + r.Width * H(2), r.Y + r.Height * 0.1),
                new Point(r.X + r.Width * H(3), r.Y + r.Height * 0.9),
                new Point(r.X + r.Width * (0.5 + 0.3 * H(4)), r.Y + r.Height * 0.8));
            ctx.EndFigure(false);
            ctx.BeginFigure(new Point(r.X + r.Width * 0.2, r.Y + r.Height * (0.5 + 0.2 * H(5))), false);
            ctx.LineTo(new Point(r.X + r.Width * 0.8, r.Y + r.Height * (0.4 + 0.3 * H(6))));
            ctx.EndFigure(false);
        }
        using (dc.PushClip(r)) dc.DrawGeometry(null, pen, g);
    }

    /// <summary>
    /// The one picture the sheet renders: the drawing under the pointer, large
    /// enough to read. One render at a time, on demand, instead of a render per
    /// keyed cel kept up to date whether or not anyone looks.
    /// </summary>
    private void DrawHoverCard(DrawingContext dc, (int Row, int Frame) h, IBrush strong, IBrush text)
    {
        var row = Rows[h.Row];
        var d = row.Drawings.LastOrDefault(x => x.Frame <= h.Frame);
        if (d is null || row.Folder) return;

        var cell = Cell(h.Row, h.Frame);
        const double w = 120, ph = 68;
        // Beside the cel, never over its row: in the app this is a popup and
        // may leave the sheet; here it has to fit inside it.
        var x = cell.Right + 8 + w <= Bounds.Width ? cell.Right + 8 : cell.Left - w - 8;
        var card = new Rect(x, Math.Clamp(cell.Center.Y - (ph + 30) / 2, 2, Math.Max(2, Bounds.Height - ph - 32)), w, ph + 30);

        dc.DrawRectangle(new SolidColorBrush(ThemeColour.Of(this, "SurfaceRaisedBrush", Color.Parse("#2E3138"))),
            new Pen(new SolidColorBrush(ThemeColour.Hairline(this, 0x40)), 1), card, 6, 6);
        var pic = new Rect(card.X + 6, card.Y + 6, w - 12, ph - 6);
        dc.DrawRectangle(Brushes.White, null, pic, 3, 3);
        if (d.Mark is not Mark.Blank and not Mark.AiPending) Scribble(dc, pic.Deflate(6), d.Number, Color.Parse("#2A2D36"));

        var held = d.HoldEnd > d.Frame ? $"{d.Frame + 1}–{d.HoldEnd + 1}" : $"{d.Frame + 1}";
        var kind = d.Mark switch
        {
            Mark.Key => "key",
            Mark.Breakdown => "breakdown",
            Mark.Inbetween => "inbetween",
            Mark.Blank => "blank",
            Mark.AiPending => "asked of the AI",
            _ => "AI draft · review",
        };
        dc.DrawText(Text($"{row.Name} · {(d.Number > 0 ? "#" + d.Number : "—")} · {kind}", 9.5, strong, FontWeight.SemiBold), new Point(card.X + 7, card.Y + ph + 2));
        dc.DrawText(Text($"frames {held}" + (d.Stage is Stage.None ? "" : $" · {d.Stage.ToString().ToLowerInvariant()}"), 9, text), new Point(card.X + 7, card.Y + ph + 15));
    }
}
