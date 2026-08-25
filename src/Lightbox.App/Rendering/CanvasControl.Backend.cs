using Avalonia.Skia;

namespace Lightbox.App.Rendering;

/// <summary>
/// What kind of machine this is, asked once on the first frame: which graphics
/// backend Avalonia handed us, what its texture limit is, and — since the
/// compositor stopped being told and started measuring — whether blending is
/// actually faster on the card here.
/// </summary>
/// <remarks>
/// <para>
/// A partial rather than more of <c>CanvasControl.cs</c>, because that file is
/// on the monolith ratchet. The rule the ratchet encodes is not "put new code
/// anywhere else"; it is that a concern with a name of its own should have a
/// file of its own, and "what is this machine" is one — every member here is
/// a static about the hardware rather than about the control.
/// </para>
/// <para>
/// It also pays for itself: <see cref="GpuComposeProbe.RunOnce"/> is called from
/// here, so the automatic compositor decision added no lines to the file that
/// could not afford them.
/// </para>
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// Whether Avalonia handed the canvas a GPU-backed Skia context, and so
    /// whether the frame the artist sees is presented by the GPU at all.
    ///
    /// Every "should this be on the GPU" question starts here and cannot be
    /// answered from a headless container: on Windows the default backend is
    /// ANGLE/D3D11 and this is expected to read "GPU", but a machine that fell
    /// back to software rendering has a completely different cost profile and
    /// no amount of GPU work would help it. Reported in the info strip so it
    /// is a fact rather than an assumption.
    /// </summary>
    public static string GraphicsBackend { get; private set; } = "unknown";

    /// <summary>
    /// True once a frame has been presented without a GPU context, null while
    /// nothing has been drawn yet.
    /// </summary>
    /// <remarks>
    /// Worth a separate flag from the label because something has to act on
    /// it. A machine on the software rasteriser is not a machine with a
    /// slightly slower canvas — presenting the frame becomes the dominant
    /// cost, and the setting that decides how many pixels get presented is the
    /// only lever that helps.
    /// </remarks>
    public static bool? SoftwareRendering { get; private set; }

    /// <summary>Raised the first time the backend is known.</summary>
    public static event Action? BackendDetected;

    /// <summary>
    /// The context's texture limit, or null when there is no context. Reported
    /// rather than merely used: at 4K with display scaling the presentation
    /// surface approaches this, and exceeding it is what makes a GPU surface fail
    /// to allocate and fall back to CPU without saying so.
    /// </summary>
    public static int? MaxTextureSize { get; private set; }

    private static void RecordBackend(ISkiaSharpApiLease lease)
    {
        if (GraphicsBackend != "unknown") return;
        var software = lease.GrContext is null;
        GraphicsBackend = software ? "CPU (software)" : "GPU";
        SoftwareRendering = software;
        MaxTextureSize = lease.GrContext?.MaxTextureSize;
        // The same event, asked one question further on: not only which backend
        // is present but which one is actually faster at blending here. Once,
        // on the first frame, and only when the mode is Automatic.
        GpuComposeProbe.RunOnce(lease.GrContext);
        BackendDetected?.Invoke();
    }

    /// <summary>Test seam: pretend the backend came back as software, or as a GPU.</summary>
    internal static void ForceBackendForTests(bool? software)
    {
        SoftwareRendering = software;
        GraphicsBackend = software switch
        {
            true => "CPU (software)",
            false => "GPU",
            null => "unknown",
        };
        if (software is not null) BackendDetected?.Invoke();
    }
}
