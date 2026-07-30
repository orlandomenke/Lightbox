using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

public sealed partial class FrameCell(int index) : ObservableObject
{
    public int Index { get; } = index;

    public string Display => (Index + 1).ToString();

    [ObservableProperty]
    private bool _isKeyed;

    [ObservableProperty]
    private bool _isCurrent;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly FrameBitmapCache _cache = new();
    private readonly StrokeBuilder _strokeBuilder = new();
    private readonly PlaybackClock _clock = new();

    private DocumentEditor _editor;
    private SKBitmap? _liveComposite;

    /// <summary>Fired with a fresh snapshot whenever the canvas must repaint.</summary>
    public event Action<RenderSnapshot>? SnapshotChanged;

    public MainViewModel()
    {
        _editor = new DocumentEditor(DocumentFactory.CreateDoc());
        _editor.Changed += OnDocumentChanged;
        _clock.Tick += OnPlaybackTick;
        RebuildFrameCells();
    }

    public Doc Doc => _editor.Doc;

    private Scene Scene => _editor.Doc.Scene;

    private Layer ActiveLayer => Scene.Layers[0];

    // ---- observable state ---------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameLabel))]
    private int _currentFrameIndex;

    [ObservableProperty]
    private double _brushSize = 6;

    [ObservableProperty]
    private double _brushHardness = 0.8;

    [ObservableProperty]
    private string _colorHex = "#1a1a1a";

    [ObservableProperty]
    private bool _isEraser;

    [ObservableProperty]
    private bool _onionSkin = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _tweenCount = 3;

    [ObservableProperty]
    private Easing _tweenEasing = Easing.EaseInOut;

    public IReadOnlyList<Easing> EasingChoices { get; } =
        [Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];

    public ObservableCollection<FrameCell> FrameCells { get; } = [];

    public string FrameLabel => $"{CurrentFrameIndex + 1} / {Scene.FrameCount}";

    partial void OnCurrentFrameIndexChanged(int value)
    {
        RefreshCellHighlights();
        PublishSnapshot();
    }

    partial void OnOnionSkinChanged(bool value) => PublishSnapshot();

    // ---- painting -----------------------------------------------------------

    /// <summary>The keyed frame paint lands on (exposure-sheet: the key at or before the playhead).</summary>
    private PaintedFrame? PaintTarget()
    {
        var i = ExposureSheet.KeyIndexAtOrBefore(ActiveLayer, CurrentFrameIndex);
        return i < 0 ? null : ActiveLayer.Cels[i].Frame as PaintedFrame;
    }

    public void BeginStroke(double x, double y, double pressure)
    {
        if (IsPlaying || PaintTarget() is null) return;
        _strokeBuilder.Begin(
            IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            ColorHex,
            new BrushSettings { Size = BrushSize, Hardness = BrushHardness, Opacity = 1, Spacing = 0.15 },
            x, y, pressure);
        PublishSnapshot();
    }

    public void MoveStroke(double x, double y, double pressure)
    {
        if (!_strokeBuilder.IsActive) return;
        _strokeBuilder.Add(x, y, pressure);
        PublishSnapshot();
    }

    public void EndStroke()
    {
        var stroke = _strokeBuilder.End();
        if (stroke is null) return;
        var target = PaintTarget();
        if (target is null) return;

        _editor.Perform(_ => target.Strokes.Add(stroke));
        _cache.Invalidate(target.Id);
        PublishSnapshot();
    }

    // ---- commands -----------------------------------------------------------

    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying)
        {
            _clock.Stop();
            IsPlaying = false;
        }
        else
        {
            _strokeBuilder.Cancel();
            IsPlaying = true;
            _clock.Start(Scene.Fps);
        }
        PublishSnapshot();
    }

    [RelayCommand]
    private void AddFrame()
    {
        _editor.AddFrameAfter(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        _editor.DuplicateFrame(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        if (Scene.FrameCount <= 1) return;
        _editor.DeleteFrame(CurrentFrameIndex);
        CurrentFrameIndex = Math.Min(CurrentFrameIndex, Scene.FrameCount - 1);
    }

    [RelayCommand]
    private void Undo()
    {
        _editor.Undo();
        _cache.Clear();
        ClampCurrentFrame();
    }

    [RelayCommand]
    private void Redo()
    {
        _editor.Redo();
        _cache.Clear();
        ClampCurrentFrame();
    }

    [RelayCommand]
    private void SelectFrame(FrameCell cell) => CurrentFrameIndex = cell.Index;

    /// <summary>
    /// Deterministic inbetweens between the key at/before the playhead and
    /// the next key. Strokes are interpolated, then re-rendered by the same
    /// brush pipeline as hand-painted frames when the cels are displayed.
    /// </summary>
    [RelayCommand]
    private void InsertInbetweens()
    {
        var layer = ActiveLayer;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0) return;

        var a = StrokesOf(layer.Cels[aIndex].Frame!);
        var b = StrokesOf(layer.Cels[bIndex].Frame!);
        var series = Inbetweener.InbetweenSeries(a, b, TweenCount, TweenEasing);
        var frames = series
            .Select(strokes => (Frame)new PaintedFrame { Strokes = strokes })
            .ToList();

        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
    }

    private static List<Stroke> StrokesOf(Frame frame) => frame switch
    {
        PaintedFrame p => p.Strokes,
        VectorFrame v => v.Strokes,
        _ => [],
    };

    // ---- document I/O -------------------------------------------------------

    public void ReplaceDocument(Doc doc)
    {
        _clock.Stop();
        IsPlaying = false;
        _editor.Changed -= OnDocumentChanged;
        _editor = new DocumentEditor(doc);
        _editor.Changed += OnDocumentChanged;
        _cache.Clear();
        CurrentFrameIndex = 0;
        OnDocumentChanged();
    }

    public string SerializeDocument() => DocJson.Serialize(Doc);

    // ---- internals ----------------------------------------------------------

    private void OnPlaybackTick()
    {
        CurrentFrameIndex = (CurrentFrameIndex + 1) % Math.Max(1, Scene.FrameCount);
    }

    private void OnDocumentChanged()
    {
        ClampCurrentFrame();
        RebuildFrameCells();
        OnPropertyChanged(nameof(FrameLabel));
        PublishSnapshot();
    }

    private void ClampCurrentFrame()
    {
        var max = Math.Max(0, Scene.FrameCount - 1);
        if (CurrentFrameIndex > max) CurrentFrameIndex = max;
        else PublishSnapshot();
    }

    private void RebuildFrameCells()
    {
        while (FrameCells.Count > Scene.FrameCount) FrameCells.RemoveAt(FrameCells.Count - 1);
        while (FrameCells.Count < Scene.FrameCount) FrameCells.Add(new FrameCell(FrameCells.Count));
        foreach (var cell in FrameCells)
        {
            cell.IsKeyed = ExposureSheet.FrameAtExactIndex(ActiveLayer, cell.Index) is not null;
            cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    private void RefreshCellHighlights()
    {
        foreach (var cell in FrameCells) cell.IsCurrent = cell.Index == CurrentFrameIndex;
    }

    /// <summary>Composite the scene for the current playhead and hand it to the view.</summary>
    public void PublishSnapshot()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();

        if (OnionSkin && !IsPlaying)
        {
            foreach (var layer in scene.Layers)
            {
                if (!layer.Visible) continue;
                var prev = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex - 1);
                if (prev is not null)
                    passes.Add(new RenderPass(_cache.Get(prev, scene.Width, scene.Height), SceneRenderer.OnionPrevTint, 0.25));
                var next = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex + 1);
                if (next is not null)
                    passes.Add(new RenderPass(_cache.Get(next, scene.Width, scene.Height), SceneRenderer.OnionNextTint, 0.25));
            }
        }

        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;

            var bmp = _cache.Get(frame, scene.Width, scene.Height);

            // Live stroke preview: overlay the in-progress stroke on the
            // active layer without touching the cached bitmap.
            if (_strokeBuilder.IsActive && layer.Id == ActiveLayer.Id && _strokeBuilder.Current is { } live)
            {
                _liveComposite?.Dispose();
                _liveComposite = bmp.Copy();
                FrameRasterizer.Append(_liveComposite, live);
                bmp = _liveComposite;
            }

            passes.Add(new RenderPass(bmp, null, layer.Opacity));
        }

        var image = SceneRenderer.Compose(scene.Width, scene.Height, passes);
        SnapshotChanged?.Invoke(new RenderSnapshot(image, scene.Width, scene.Height));
    }
}
