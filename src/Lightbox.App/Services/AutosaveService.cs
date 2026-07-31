using Avalonia.Threading;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Services;

/// <summary>
/// Periodically writes the document to a fixed per-user autosave path so a
/// crash never costs more than a minute of work. Recover by opening the
/// autosave file from the Open dialog.
/// </summary>
public sealed class AutosaveService
{
    public static string AutosavePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lightbox",
            "autosave.lightbox.json");

    private readonly DispatcherTimer _timer;
    private readonly Func<Doc> _docProvider;
    private bool _dirty;

    public AutosaveService(Func<Doc> docProvider, TimeSpan? interval = null)
    {
        _docProvider = docProvider;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    /// <summary>Call whenever the document changes; the next tick persists it.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>Write now if there are unsaved changes. Failures are swallowed — autosave must never crash the app.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
            DocJson.Save(_docProvider(), AutosavePath);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
