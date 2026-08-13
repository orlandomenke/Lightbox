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
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>The camera — the one transform that is authored rather than view-only (invariant 5).</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q75, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- camera ---------------------------------------------------------------

    /// <summary>
    /// Whether this document has a camera at all. Everything camera-related in
    /// the UI hangs off this: a document without one shows no overlay, no
    /// controls and no ruler keys. Optional means absent, not disabled.
    /// </summary>
    public bool HasCamera => Scene.Camera is not null;

    /// <summary>
    /// The camera frame's corners in document coordinates, or null. The canvas
    /// draws this as view-only chrome — it never reaches a pixel.
    /// </summary>
    public SKPoint[]? CameraFrameCorners { get; private set; }

    /// <summary>Fired when the camera appears, disappears, or reframes.</summary>
    public event Action? CameraChanged;

    /// <summary>The framing at the playhead — what the overlay and the fields show.</summary>
    private CameraFraming FramingNow() =>
        CameraOps.At(Scene.Camera, CurrentFrameIndex, Scene.Width, Scene.Height);

    /// <summary>
    /// Give the scene a camera, framed on the whole canvas at 1:1 so the first
    /// thing the artist sees is what they already had. Output defaults to the
    /// canvas size for the same reason — a camera should start by changing
    /// nothing.
    /// </summary>
    [RelayCommand]
    private void AddCamera()
    {
        if (Scene.Camera is not null) return;
        Scene.Camera = new Camera { OutputWidth = Scene.Width, OutputHeight = Scene.Height };
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Take the camera away entirely, keys and all, returning the document to
    /// the state it saves in when it never had one.
    /// </summary>
    [RelayCommand]
    private void RemoveCamera()
    {
        if (Scene.Camera is null) return;
        Scene.Camera = null;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Key the current framing at the playhead.</summary>
    [RelayCommand]
    private void SetCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, FramingNow());
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Retime a camera key — the track timeline's dot drag. Refuses an
    /// occupied destination rather than clobbering a framing the artist
    /// authored; the status line says why nothing moved.
    /// </summary>
    public void MoveCameraKey(int fromFrame, int toFrame)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, fromFrame) is not { } key) return;
        if (CameraOps.KeyAt(camera, toFrame) is not null)
        {
            AiStatus = "There is already a camera key on that frame.";
            return;
        }
        key.Frame = toFrame;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Author a key at the given frame with the framing already interpolated
    /// there — the graph's double-click. Keying what is already true changes
    /// nothing visually, which is exactly what makes it safe to then drag.
    /// </summary>
    public void AddCameraKeyAt(int frame)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, frame) is not null) return;
        CameraOps.SetKey(camera, frame, CameraOps.At(camera, frame, Scene.Width, Scene.Height));
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Remove the key at the given frame — the graph's key menu.</summary>
    public void RemoveCameraKeyAt(int frame)
    {
        if (Scene.Camera is not { } camera) return;
        if (!CameraOps.ClearKey(camera, frame)) return;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>The easing a key runs into its successor with, for the menu's check mark.</summary>
    public Easing? CameraKeyEaseAt(int frame) => CameraOps.KeyAt(Scene.Camera, frame)?.Ease;

    /// <summary>Set how the key at the given frame eases into the next one.</summary>
    public void SetCameraKeyEase(int frame, Easing ease)
    {
        if (CameraOps.KeyAt(Scene.Camera, frame) is not { } key || key.Ease == ease) return;
        key.Ease = ease;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Remove the key at the playhead, if there is one.</summary>
    [RelayCommand]
    private void ClearCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        if (!CameraOps.ClearKey(camera, CurrentFrameIndex)) return;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>True when the playhead sits on an authored camera key.</summary>
    public bool IsOnCameraKey => CameraOps.KeyAt(Scene.Camera, CurrentFrameIndex) is not null;

    /// <summary>Frames carrying a camera key, for the timeline ruler.</summary>
    public IReadOnlyList<int> CameraKeyFrames =>
        CameraOps.Ordered(Scene.Camera).Select(k => k.Frame).ToList();

    public int CameraOutputWidth
    {
        get => Scene.Camera?.OutputWidth ?? Scene.Width;
        set => SetCameraOutput(Math.Clamp(value, 1, 16384), CameraOutputHeight);
    }

    public int CameraOutputHeight
    {
        get => Scene.Camera?.OutputHeight ?? Scene.Height;
        set => SetCameraOutput(CameraOutputWidth, Math.Clamp(value, 1, 16384));
    }

    private void SetCameraOutput(int width, int height)
    {
        if (Scene.Camera is not { } camera) return;
        if (camera.OutputWidth == width && camera.OutputHeight == height) return;
        camera.OutputWidth = width;
        camera.OutputHeight = height;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// The framing at the playhead, editable. Writing any of these moves the
    /// live framing; it only becomes part of the shot once keyed, which is the
    /// same bargain as a transform gizmo before it is committed.
    /// </summary>
    public double CameraX
    {
        get => FramingNow().X;
        set => SetFraming(FramingNow() with { X = value });
    }

    public double CameraY
    {
        get => FramingNow().Y;
        set => SetFraming(FramingNow() with { Y = value });
    }

    public double CameraZoom
    {
        get => FramingNow().Zoom;
        set => SetFraming(FramingNow() with { Zoom = Math.Clamp(value, 0.05, 64) });
    }

    public double CameraRotationDeg
    {
        get => FramingNow().RotationDeg;
        set => SetFraming(FramingNow() with { RotationDeg = value });
    }

    /// <summary>
    /// The canvas gizmo's three drags. Each goes through
    /// <see cref="SetFraming"/>, so a drag keys the framing at the playhead
    /// exactly as typing into the fields does — adjusting IS keying.
    /// </summary>
    public void NudgeCamera(double dx, double dy)
    {
        var now = FramingNow();
        SetFraming(now with { X = now.X + dx, Y = now.Y + dy });
    }

    public void ZoomCameraBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var now = FramingNow();
        SetFraming(now with { Zoom = Math.Clamp(now.Zoom * factor, 0.05, 64) });
    }

    public void RotateCameraBy(double deltaDeg)
    {
        if (!double.IsFinite(deltaDeg)) return;
        var now = FramingNow();
        SetFraming(now with { RotationDeg = now.RotationDeg + deltaDeg });
    }

    /// <summary>
    /// Editing a framing field writes it straight to the key at the playhead,
    /// creating one if there is none. A framing you cannot see keyed is a
    /// framing you will lose by scrubbing away from it.
    /// </summary>
    private void SetFraming(CameraFraming framing)
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, framing);
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Show the canvas through the camera rather than the world. Off by
    /// default: the artist draws in the world, and this is for checking the
    /// shot.
    /// </summary>
    [ObservableProperty]
    private bool _viewThroughCamera;

    partial void OnViewThroughCameraChanged(bool value)
    {
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        RefreshCamera();
        PublishSnapshot();
    }

    /// <summary>The matrix a publish composites through, or null for the world.</summary>
    private SKMatrix? CameraViewTransform(double renderScale) =>
        ViewThroughCamera && Scene.Camera is { } camera
            ? CameraTransform.Matrix(
                FramingNow(), camera.OutputWidth, camera.OutputHeight, renderScale)
            : null;

    private void RefreshCamera()
    {
        // No camera, or looking through it — in which case the frame IS the
        // viewport and an overlay would just outline the window.
        CameraFrameCorners = Scene.Camera is { } camera && !ViewThroughCamera
            ? CameraTransform.FrameCorners(FramingNow(), camera.OutputWidth, camera.OutputHeight)
            : null;
        CameraChanged?.Invoke();
    }

    private void NotifyCameraSurface()
    {
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(GraphSeriesList));
        OnPropertyChanged(nameof(HasCamera));
        OnPropertyChanged(nameof(IsOnCameraKey));
        OnPropertyChanged(nameof(CameraKeyFrames));
        OnPropertyChanged(nameof(CameraOutputWidth));
        OnPropertyChanged(nameof(CameraOutputHeight));
        OnPropertyChanged(nameof(CameraX));
        OnPropertyChanged(nameof(CameraY));
        OnPropertyChanged(nameof(CameraZoom));
        OnPropertyChanged(nameof(CameraRotationDeg));
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        PublishSnapshot();
    }
}
