using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Geometry;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The perspective checker (Q135): on demand, the active layer's drawing is
/// measured against the document's vanishing points — the artist's own first,
/// inferred ones only where none are declared — and the lines that nearly
/// converge and miss are handed to the existing line selection, so they are
/// highlighted with chrome the artist already reads and can be acted on with
/// the tools that already act on selections. Read-only: the one thing it
/// changes is what is selected.
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    public void CheckPerspective()
    {
        if (ActiveLayer is not { } layer
            || ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame)
        {
            AiStatus = "No drawing under the playhead to check.";
            return;
        }

        var report = PerspectiveChecker.Check(Doc.Scene, frame);
        if (report.StraightLines == 0)
        {
            AiStatus = "Nothing here reads as a straight line long enough to judge — the checker measures construction lines, not curves.";
            return;
        }
        if (report.VanishingPoints == 0)
        {
            AiStatus = $"{Plural(report.StraightLines, "straight line")}, but no vanishing point to judge against — " +
                       "place one (View → Guides → Add vanishing point), or draw more converging lines for the checker to read one from.";
            return;
        }

        // The hedge is not politeness (Q135): an inferred horizon is a guess
        // judging the artist, so its findings must read as suggestions.
        var hedge = report.Inferred
            ? " Judged against vanishing points read off the ink itself — place a VP guide to be judged against your own."
            : "";

        if (report.Findings.Count == 0)
        {
            AiStatus = $"{Plural(report.StraightLines, "straight line")} against " +
                       $"{Plural(report.VanishingPoints, "vanishing point")} — nothing off.{hedge}";
            return;
        }

        Selection.SelectStrokes(report.Findings.Select(f => f.StrokeId));
        OnStrokeSelectionChanged();
        var worst = report.Findings[0];
        AiStatus = $"{Plural(report.Findings.Count, "line")} nearly converge{(report.Findings.Count == 1 ? "s" : "")} and " +
                   $"miss{(report.Findings.Count == 1 ? "es" : "")} — worst is off by {worst.OffByDeg:0.#}° " +
                   $"from {worst.VpName}. They are selected.{hedge}";
    }
}
