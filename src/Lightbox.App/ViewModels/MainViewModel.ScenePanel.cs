using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// The Scene panel (Q84): a schematic of the layer stack with its depths, and
/// the camera's path across the canvas — the surface multiplane parallax is
/// authored in. Everything here is a projection of the document; the one write
/// (<see cref="SetLayerDepth"/>) goes through the editor like any other edit.
/// </remarks>
public partial class MainViewModel
{
    [RelayCommand]
    private void ToggleSceneDocker() =>
        Workspace.SceneDockerVisible = !Workspace.SceneDockerVisible;

    /// <summary>
    /// One row per layer, topmost first — the same order the Layers docker
    /// reads in, so the two lists of the same layers agree.
    /// </summary>
    public IReadOnlyList<LayerDepthRow> DepthRows
    {
        get
        {
            var rows = new List<LayerDepthRow>(Scene.Layers.Count);
            for (var i = Scene.Layers.Count - 1; i >= 0; i--)
            {
                rows.Add(new LayerDepthRow(this, Scene.Layers[i]));
            }
            return rows;
        }
    }

    /// <summary>The canvas the plot letterboxes, in document units.</summary>
    public Avalonia.Size ScenePlotSize => new(Scene.Width, Scene.Height);

    /// <summary>
    /// The camera's centre on every frame — the eased path, not the keys'
    /// straight lines, because the easing is the part a straight line hides.
    /// </summary>
    public IReadOnlyList<Avalonia.Point> CameraPathPoints
    {
        get
        {
            if (Scene.Camera is not { } camera || camera.Keys.Count == 0) return [];
            var points = new List<Avalonia.Point>(Scene.FrameCount);
            for (var frame = 0; frame < Scene.FrameCount; frame++)
            {
                var f = CameraOps.At(camera, frame, Scene.Width, Scene.Height);
                points.Add(new Avalonia.Point(f.X, f.Y));
            }
            return points;
        }
    }

    /// <summary>The authored keys, a dot each, in timeline order.</summary>
    public IReadOnlyList<Avalonia.Point> CameraKeyPoints =>
        CameraOps.Ordered(Scene.Camera)
            .Select(k => new Avalonia.Point(k.X, k.Y))
            .ToList();

    /// <summary>
    /// Author a layer's depth, as one undo step. Zero writes null — zero and
    /// "no depth" are the same plane, and the distinction must never reach a
    /// file (the record's optional-means-absent rule).
    /// </summary>
    internal void SetLayerDepth(Layer layer, double depth)
    {
        double? value = Math.Abs(depth) < 0.0001 ? null : depth;
        if (layer.Depth == value) return;
        _editor.Perform(_ => layer.Depth = value, label: "Set layer depth",
            frameContentUnchanged: true);
        NotifyScenePanel();
    }

    /// <summary>
    /// Re-read the panel's projections. Called from the document-changed
    /// funnel, so an undo, a layer edit or a camera key lands here without
    /// the panel holding any state of its own to go stale.
    /// </summary>
    internal void NotifyScenePanel()
    {
        OnPropertyChanged(nameof(DepthRows));
        OnPropertyChanged(nameof(ScenePlotSize));
        OnPropertyChanged(nameof(CameraPathPoints));
        OnPropertyChanged(nameof(CameraKeyPoints));
    }
}

/// <summary>
/// One layer in the Scene panel: its name, its authored depth, and what that
/// depth means as a fraction of a camera move.
/// </summary>
public sealed class LayerDepthRow(MainViewModel vm, Layer layer)
{
    public string Name => layer.Name;

    public bool IsBackground => layer.IsBackground;

    /// <summary>The authored depth, with 0 standing in for "none".</summary>
    public double Depth
    {
        get => layer.Depth ?? 0;
        set => vm.SetLayerDepth(layer, value);
    }

    /// <summary>What the depth does, said in the artist's terms.</summary>
    public string FactorLabel
    {
        get
        {
            var k = LayerDepth.Factor(layer.Depth);
            if (k == 1) return "on the picture plane";
            return k > 1
                ? $"in front — {k:0.##}× a camera move"
                : $"shows {k:P0} of a camera move";
        }
    }

    /// <summary>
    /// The plane's marker position on the schematic's 50 px depth track:
    /// 0 on the picture plane, sliding right as the factor falls toward the
    /// horizon. A layer floating in front pins to the left edge.
    /// </summary>
    public double MarkerX => 50 * (1 - Math.Clamp(LayerDepth.Factor(layer.Depth), 0, 1));
}
