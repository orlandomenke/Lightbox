using Avalonia.Threading;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Services;

/// <summary>
/// Periodically writes the document to a per-user recovery path so a crash
/// never costs more than one interval of work. Recover by opening the autosave
/// file from the Open dialog.
/// </summary>
/// <remarks>
/// The interval comes from <see cref="AppSettings"/>, and zero turns it off —
/// which is a real answer for someone working on a network drive, not a
/// mistake to guard against.
///
/// It writes to the recovery copy, not to the document's own file, unless
/// <see cref="AppSettings.AutosaveInPlace"/> says otherwise. Silently
/// rewriting the file someone opened takes away the ability to close without
/// saving, and that is an editing move, not an accident.
/// </remarks>
public sealed class AutosaveService
{
    public static string AutosavePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lightbox",
            "autosave.lightbox.json");

    private readonly DispatcherTimer _timer;
    private readonly Func<Doc> _docProvider;
    private readonly Func<string?>? _inPlacePath;
    private bool _dirty;

    /// <param name="inPlacePath">
    /// Where the document itself lives, or null when it has never been saved.
    /// Consulted only when the setting asks for in-place autosave.
    /// </param>
    public AutosaveService(
        Func<Doc> docProvider,
        TimeSpan? interval = null,
        Func<string?>? inPlacePath = null)
    {
        _docProvider = docProvider;
        _inPlacePath = inPlacePath;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Flush();
        if (interval != TimeSpan.Zero) _timer.Start();
    }

    /// <summary>
    /// Change the cadence without rebuilding the service — what the settings
    /// screen calls. Zero or null stops it.
    /// </summary>
    public void Reschedule(TimeSpan? interval)
    {
        _timer.Stop();
        if (interval is not { TotalSeconds: > 0 } every) return;
        _timer.Interval = every;
        _timer.Start();
    }

    /// <summary>Whether the recovery copy is also written over the real file.</summary>
    public bool InPlace { get; set; }

    /// <summary>Call whenever the document changes; the next tick persists it.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>Write now if there are unsaved changes. Failures are swallowed — autosave must never crash the app.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            var doc = _docProvider();
            Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
            DocJson.Save(doc, AutosavePath);
            if (InPlace && _inPlacePath?.Invoke() is { Length: > 0 } path) DocJson.Save(doc, path);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
