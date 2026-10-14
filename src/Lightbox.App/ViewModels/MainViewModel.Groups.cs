using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Split group (Q268): a merge taken back out onto a layer of its own.
/// </summary>
/// <remarks>
/// Here rather than beside merge in <c>MainViewModel.Layers.cs</c> for the
/// ratchet's reason — the files a layer command would otherwise land in are
/// already the largest in the view model.
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Split the merge under the selection, or the topmost one, on the active layer.</summary>
    [RelayCommand]
    private void SplitGroup() => SplitGroupOn(null);

    /// <summary>
    /// Put the merge a group came from back on a layer of its own, directly
    /// above <paramref name="layer"/> (the active one when null). One undo step.
    /// </summary>
    /// <remarks>
    /// Which merge: the group that is selected, when exactly one is; otherwise
    /// the topmost group in the drawing at the playhead. Every cel's group from
    /// that merge comes out together (<see cref="Lightbox.Raster.GroupSplit"/>).
    /// </remarks>
    public bool SplitGroupOn(Layer? layer)
    {
        layer ??= ActiveLayer;
        if (!CanEdit(layer, "split a group out of it")) return false;
        var key = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        var frame = key < 0 ? null : layer.Cels[key].Frame;
        var selected = frame?.Strokes.Where(s => s.Group is not null && Selection.IsStrokeSelected(s.Id)).ToList() ?? [];
        var target = selected.Count == 1 ? selected[0] : frame?.Strokes.LastOrDefault(s => s.Group is not null);
        if (frame is null || target is null)
        {
            AiStatus = $"Nothing on “{layer.Name}” here was kept as a group by a merge.";
            return false;
        }

        Lightbox.Raster.GroupSplit.Result? result = null;
        var (layerId, frameId, strokeId) = (layer.Id, frame.Id, target.Id);
        _editor.Perform(doc =>
        {
            var inDoc = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
            var stroke = inDoc?.Cels.Select(c => c.Frame).OfType<Frame>().FirstOrDefault(f => f.Id == frameId)?
                .Strokes.FirstOrDefault(s => s.Id == strokeId);
            if (inDoc is null || stroke is null) return;
            result = Lightbox.Raster.GroupSplit.Split(doc.Scene, inDoc, stroke);
        });
        if (result is null)
        {
            AiStatus = "Those drawings were merged from layers with different settings, so no one layer can take them back.";
            return false;
        }

        ClearStrokeSelection();
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == result.Layer.Id);
        var cels = result.Cels == 1 ? "1 drawing" : $"{result.Cels} drawings";
        AiStatus = $"Split “{result.Layer.Name}” back onto a layer of its own ({cels})."
            + result.MarksNowUnder switch
            {
                0 => "",
                1 => " One mark drawn after the merge overlapped it and now sits under it.",
                var n => $" {n} marks drawn after the merge overlapped it and now sit under it.",
            };
        return true;
    }
}
