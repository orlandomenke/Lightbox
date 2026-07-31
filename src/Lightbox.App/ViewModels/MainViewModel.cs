using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
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

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumb;

    /// <summary>Frame id the current thumb was rendered from (staleness check).</summary>
    public string? ThumbFrameId { get; set; }
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

    public MainViewModel() : this(ResolveArtist())
    {
    }

    /// <summary>Test seam: inject a fake artist (or null for "no API key").</summary>
    public MainViewModel(IAiArtist? artist)
    {
        _artist = artist;
        _editor = new DocumentEditor(DocumentFactory.CreateDoc());
        _editor.Changed += OnDocumentChanged;
        _clock.Tick += OnPlaybackTick;
        _autosave = new AutosaveService(() => Doc);
        SyncLayerChoices();
        RebuildFrameCells();
    }

    private static IAiArtist? ResolveArtist()
    {
        var key = ApiKeyProvider.GetApiKey();
        if (key is not null) return new AnthropicArtist(key);
        if (ApiKeyProvider.GetOllamaConfig() is { } ollama)
            return new OllamaArtist(ollama.Url, ollama.Model);
        return null;
    }

    private readonly AutosaveService _autosave;

    public Doc Doc => _editor.Doc;

    private Scene Scene => _editor.Doc.Scene;

    private Layer ActiveLayer => Scene.Layers[Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1)];

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
    private int _onionDepth = 1;

    [ObservableProperty]
    private bool _smoothStrokes = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _activeLayerIndex;

    public ObservableCollection<Layer> LayerChoices { get; } = [];

    public int Fps
    {
        get => Scene.Fps;
        set
        {
            var fps = Math.Clamp(value, 1, 60);
            if (Scene.Fps == fps) return;
            Scene.Fps = fps;
            OnPropertyChanged();
            _autosave.MarkDirty();
            if (IsPlaying)
            {
                _clock.Stop();
                _clock.Start(fps);
            }
        }
    }

    partial void OnOnionDepthChanged(int value) => PublishSnapshot();

    partial void OnActiveLayerIndexChanged(int value) => PublishSnapshot();

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
    private Frame? PaintTarget()
    {
        var i = ExposureSheet.KeyIndexAtOrBefore(ActiveLayer, CurrentFrameIndex);
        return i < 0 ? null : ActiveLayer.Cels[i].Frame;
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

        if (SmoothStrokes && stroke.Points.Count >= 3)
        {
            stroke.Points = GeometryOps.Smooth(stroke.Points, 1);
        }

        _editor.Perform(_ => StrokesOf(target).Add(stroke));
        _cache.Invalidate(target.Id);
        _dirtyThumbIds.Add(target.Id);
        PublishSnapshot();
        RefreshThumbnails();
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
        _allThumbsDirty = true;
        ClampCurrentFrame();
        RefreshThumbnails();
    }

    [RelayCommand]
    private void Redo()
    {
        _editor.Redo();
        _cache.Clear();
        _allThumbsDirty = true;
        ClampCurrentFrame();
        RefreshThumbnails();
    }

    [RelayCommand]
    private void AddPaintedLayer() => AddLayer(LayerKind.Painted);

    [RelayCommand]
    private void AddVectorLayer() => AddLayer(LayerKind.Vector);

    private void AddLayer(LayerKind kind)
    {
        _editor.Perform(doc =>
        {
            var layer = new Layer
            {
                Name = $"{(kind == LayerKind.Vector ? "Vector" : "Paint")} {doc.Scene.Layers.Count + 1}",
                Kind = kind,
                Cels = [new Cel { Frame = kind == LayerKind.Vector ? new VectorFrame() : new PaintedFrame() }],
            };
            while (layer.Cels.Count < doc.Scene.FrameCount) layer.Cels.Add(new Cel());
            doc.Scene.Layers.Add(layer);
        });
        ActiveLayerIndex = Scene.Layers.Count - 1;
    }

    [RelayCommand]
    private void ToggleActiveLayerVisible()
    {
        _editor.Perform(_ => ActiveLayer.Visible = !ActiveLayer.Visible);
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
        var frames = series.Select(strokes => NewFrameFor(layer, strokes)).ToList();

        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
    }

    private static List<Stroke> StrokesOf(Frame frame) => frame switch
    {
        PaintedFrame p => p.Strokes,
        VectorFrame v => v.Strokes,
        _ => [],
    };

    /// <summary>A new frame of the layer's own kind carrying the given strokes.</summary>
    private static Frame NewFrameFor(Layer layer, List<Stroke> strokes) => layer.Kind switch
    {
        LayerKind.Vector => new VectorFrame { Strokes = strokes },
        _ => new PaintedFrame { Strokes = strokes },
    };

    // ---- AI -----------------------------------------------------------------

    private readonly IAiArtist? _artist;
    private CancellationTokenSource? _aiCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAi))]
    private bool _aiBusy;

    [ObservableProperty]
    private string _aiStatus = "";

    [ObservableProperty]
    private string _aiPrompt = "";

    public bool IsAiAvailable => _artist is not null;

    public bool CanUseAi => IsAiAvailable && !AiBusy;

    public string AiUnavailableHint => IsAiAvailable
        ? ""
        : "Enable AI features by setting ANTHROPIC_API_KEY (Claude API), or LIGHTBOX_OLLAMA_MODEL for a local model via Ollama, or the equivalent settings-file keys. No key at all? Use Claude Desktop with the Lightbox MCP server — see the README.";

    [RelayCommand]
    private void CancelAi() => _aiCts?.Cancel();

    /// <summary>
    /// Claude draws the inbetweens between the key at/before the playhead and
    /// the next key. Same insertion path as the deterministic engine — only
    /// the frame producer differs.
    /// </summary>
    [RelayCommand]
    private async Task AiInbetweenAsync()
    {
        if (_artist is null || AiBusy) return;
        var layer = ActiveLayer;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0)
        {
            AiStatus = "Needs a second keyframe after the current one.";
            return;
        }

        var ts = Enumerable.Range(1, TweenCount)
            .Select(k => (double)k / (TweenCount + 1))
            .ToList();
        var request = new InbetweenRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            StrokesOf(layer.Cels[aIndex].Frame!),
            StrokesOf(layer.Cels[bIndex].Frame!),
            ts,
            TweenEasing);

        var result = await RunAiAsync(
            $"Claude is drawing {TweenCount} inbetween(s)…",
            ct => _artist.GenerateInbetweensAsync(request, ct));
        if (result is null) return;

        var frames = result
            .OrderBy(f => f.T)
            .Select(f => NewFrameFor(layer, f.Strokes))
            .ToList();
        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        AiStatus = $"Inserted {frames.Count} AI inbetween(s).";
    }

    /// <summary>Claude paints strokes from a text prompt onto the current frame.</summary>
    [RelayCommand]
    private async Task AiDrawAsync()
    {
        if (_artist is null || AiBusy || string.IsNullOrWhiteSpace(AiPrompt)) return;
        var target = PaintTarget();
        if (target is null) return;

        var request = new DrawRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            AiPrompt.Trim(),
            StrokesOf(target));

        var strokes = await RunAiAsync(
            "Claude is drawing…",
            ct => _artist.DrawAsync(request, ct));
        if (strokes is null) return;

        _editor.Perform(_ => StrokesOf(target).AddRange(strokes));
        _cache.Invalidate(target.Id);
        _dirtyThumbIds.Add(target.Id);
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Drew {strokes.Count} stroke(s).";
    }

    /// <summary>Shared busy/cancel/error plumbing for AI calls; null on failure.</summary>
    private async Task<T?> RunAiAsync<T>(string busyMessage, Func<CancellationToken, Task<AiResult<T>>> call)
        where T : class
    {
        _aiCts = new CancellationTokenSource();
        AiBusy = true;
        AiStatus = busyMessage;
        try
        {
            var result = await call(_aiCts.Token);
            if (result.Outcome == AiOutcome.Success) return result.Value;
            AiStatus = result.Message ?? "AI request failed.";
            return null;
        }
        finally
        {
            AiBusy = false;
            _aiCts.Dispose();
            _aiCts = null;
        }
    }

    // ---- document I/O -------------------------------------------------------

    // ---- external producers (IPC/MCP) ---------------------------------------

    /// <summary>The layer external tools target when they don't name one.</summary>
    public Layer ActiveLayerForIpc => ActiveLayer;

    /// <summary>Composite one timeline frame to PNG (no onion skin, no live stroke).</summary>
    public string RenderFramePng(int frameIndex)
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>
    /// Insert externally produced inbetween frames (already validated, sorted
    /// by t) after key <paramref name="aIndex"/>. One undo step. Returns the
    /// number of frames inserted.
    /// </summary>
    public int InsertExternalInbetweens(string layerId, int aIndex, List<List<Stroke>> strokeFrames)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        var frames = strokeFrames.Select(s => NewFrameFor(layer, s)).ToList();
        _editor.InsertInbetweens(layerId, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        return frames.Count;
    }

    /// <summary>
    /// Append externally produced strokes to the key exposed at
    /// <paramref name="frameIndex"/>. One undo step. Returns strokes added
    /// (0 when the layer has no key there).
    /// </summary>
    public int AppendExternalStrokes(string layerId, int frameIndex, List<Stroke> strokes)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, frameIndex);
        if (keyIndex < 0) return 0;
        var frame = layer.Cels[keyIndex].Frame!;
        _editor.Perform(_ => StrokesOf(frame).AddRange(strokes));
        _cache.Invalidate(frame.Id);
        _dirtyThumbIds.Add(frame.Id);
        PublishSnapshot();
        RefreshThumbnails();
        return strokes.Count;
    }

    public void ReplaceDocument(Doc doc)
    {
        _clock.Stop();
        IsPlaying = false;
        _editor.Changed -= OnDocumentChanged;
        _editor = new DocumentEditor(doc);
        _editor.Changed += OnDocumentChanged;
        _cache.Clear();
        _allThumbsDirty = true;
        ActiveLayerIndex = 0;
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
        _autosave.MarkDirty();
        SyncLayerChoices();
        ClampCurrentFrame();
        RebuildFrameCells();
        OnPropertyChanged(nameof(FrameLabel));
        OnPropertyChanged(nameof(Fps));
        PublishSnapshot();
        RefreshThumbnails();
    }

    private void SyncLayerChoices()
    {
        if (LayerChoices.SequenceEqual(Scene.Layers)) return;
        LayerChoices.Clear();
        foreach (var layer in Scene.Layers) LayerChoices.Add(layer);
        if (ActiveLayerIndex >= LayerChoices.Count) ActiveLayerIndex = LayerChoices.Count - 1;
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

    // ---- thumbnails ----------------------------------------------------------

    private readonly HashSet<string> _dirtyThumbIds = [];
    private bool _allThumbsDirty;

    /// <summary>
    /// Update timeline thumbnails lazily: only cells whose keyed frame is new,
    /// changed, or explicitly marked dirty are re-rendered.
    /// </summary>
    private void RefreshThumbnails()
    {
        foreach (var cell in FrameCells)
        {
            var frame = ExposureSheet.FrameAtExactIndex(ActiveLayer, cell.Index);
            if (frame is null)
            {
                cell.Thumb = null;
                cell.ThumbFrameId = null;
                continue;
            }
            var stale = _allThumbsDirty
                        || cell.ThumbFrameId != frame.Id
                        || _dirtyThumbIds.Contains(frame.Id);
            if (!stale && cell.Thumb is not null) continue;

            var bmp = _cache.Get(frame, Scene.Width, Scene.Height);
            cell.Thumb = ThumbnailRenderer.Render(bmp);
            cell.ThumbFrameId = frame.Id;
        }
        _dirtyThumbIds.Clear();
        _allThumbsDirty = false;
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
                for (var d = Math.Max(1, OnionDepth); d >= 1; d--)
                {
                    var prev = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex - d);
                    if (prev is not null)
                        passes.Add(new RenderPass(_cache.Get(prev, scene.Width, scene.Height), SceneRenderer.OnionPrevTint, 0.25 / d));
                    var next = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex + d);
                    if (next is not null)
                        passes.Add(new RenderPass(_cache.Get(next, scene.Width, scene.Height), SceneRenderer.OnionNextTint, 0.25 / d));
                }
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
