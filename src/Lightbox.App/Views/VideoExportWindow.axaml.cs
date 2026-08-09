using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Export video…</c> — the settings, the destination, the progress
/// and the answer, in one window (B146).
/// </summary>
/// <remarks>
/// <para>
/// One window rather than the save-picker-and-hope it replaced. That version
/// asked for a filename, rendered the whole timeline at the document's size
/// with no way to say otherwise, and reported everything it had to say —
/// success, failure, and "FFmpeg was not found" — into the AI bar, which is
/// hidden whenever assistance is switched off. An export therefore looked
/// like it did nothing at all.
/// </para>
/// <para>
/// So the render runs while this window is open and reports into it: the
/// progress bar moves per frame, the sentence at the end names the file and
/// its size, and a failure stays on screen until it is read. The encoder's
/// absence is said first, before any setting, because it is the one thing
/// that makes the rest moot.
/// </para>
/// </remarks>
public partial class VideoExportWindow : Window
{
    private readonly Doc? _doc;
    private readonly Func<string?>? _resolveAudio;
    private readonly string? _ffmpeg;
    private CancellationTokenSource? _cancel;
    private bool _running;

    /// <summary>The designer's constructor; the application always uses the other one.</summary>
    public VideoExportWindow()
    {
        InitializeComponent();
    }

    public VideoExportWindow(Doc doc, Func<string?> resolveAudio, string suggestedName)
    {
        InitializeComponent();
        _doc = doc;
        _resolveAudio = resolveAudio;
        _ffmpeg = VideoExporter.FindFfmpeg();

        FormatBox.ItemsSource = new[] { "MP4 — H.264", "ProRes 422 — MOV" };
        FormatBox.SelectedIndex = 0;
        ScaleBox.ItemsSource = VideoExportReport.Scales.Select(s => s.Label).ToArray();
        ScaleBox.SelectedIndex = 2;   // 100%
        FillQualities();

        var scene = doc.Scene;
        FromBox.Text = "1";
        ToBox.Text = Math.Max(1, scene.FrameCount).ToString();
        FpsBox.Text = Math.Max(1, scene.Fps).ToString();
        FromBox.IsEnabled = ToBox.IsEnabled = false;

        // Whether there is sound is not the same question as whether the sound
        // can be found: a referenced WAV that has moved, or embedded bytes that
        // will not decode, resolve to nothing. Asking the resolver here rather
        // than trusting the track means the checkbox cannot promise a sound
        // the encode then silently drops — which would be B146 again, one
        // feature along.
        var hasTrack = scene.Audio is { Muted: false };
        var resolvable = hasTrack && resolveAudio() is not null;
        AudioBox.IsChecked = resolvable;
        AudioBox.IsEnabled = resolvable;
        if (hasTrack && !resolvable)
        {
            AudioBox.Content = "The scratch track's file cannot be found";
        }

        PathBox.Text = Path.Combine(DefaultFolder(), suggestedName + ".mp4");

        if (VideoExportReport.EncoderNotice(_ffmpeg) is { } notice)
        {
            EncoderText.Text = notice;
            EncoderBanner.IsVisible = true;
            ExportButton.IsEnabled = false;
        }
        Refresh();
    }

    /// <summary>
    /// Where a render lands unless the artist says otherwise. Videos, then
    /// home, then wherever the application is running — and it must be a
    /// folder that exists, because the empty string these return on a machine
    /// with no such folders composes into a bare filename, which writes
    /// somewhere nobody chose.
    /// </summary>
    private static string DefaultFolder()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                     Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                 })
        {
            if (folder is { Length: > 0 } && Directory.Exists(folder)) return folder;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>The settings the controls currently describe.</summary>
    internal VideoExportSettings CurrentSettings()
    {
        var format = FormatBox.SelectedIndex == 1 ? VideoFormat.ProRes : VideoFormat.Mp4;
        var qualities = VideoExportReport.QualitiesFor(format);
        var quality = qualities[Math.Clamp(QualityBox.SelectedIndex, 0, qualities.Length - 1)].Value;
        var scale = VideoExportReport.Scales[Math.Clamp(ScaleBox.SelectedIndex, 0, VideoExportReport.Scales.Length - 1)].Scale;
        var last = Math.Max(0, (_doc?.Scene.FrameCount ?? 1) - 1);

        // The artist counts from 1, the record counts from 0.
        var from = WholeTimelineBox.IsChecked == true ? 0 : ParseFrame(FromBox.Text, 0);
        var to = WholeTimelineBox.IsChecked == true ? last : ParseFrame(ToBox.Text, last);

        return new VideoExportSettings(
            format, scale, from, to,
            ParseInt(FpsBox.Text, Math.Max(1, _doc?.Scene.Fps ?? 12)),
            AudioBox.IsChecked == true,
            quality);

        static int ParseFrame(string? text, int fallback) =>
            int.TryParse(text, out var v) ? Math.Max(0, v - 1) : fallback;

        static int ParseInt(string? text, int fallback) =>
            int.TryParse(text, out var v) && v >= 1 ? v : fallback;
    }

    private void FillQualities()
    {
        var format = FormatBox.SelectedIndex == 1 ? VideoFormat.ProRes : VideoFormat.Mp4;
        var qualities = VideoExportReport.QualitiesFor(format);
        QualityBox.ItemsSource = qualities.Select(q => q.Label).ToArray();
        QualityBox.SelectedIndex = Array.FindIndex(
            qualities, q => q.Value == VideoExportSettings.DefaultQuality(format)) is var i && i >= 0 ? i : 0;
    }

    private void Refresh()
    {
        if (_doc is null || _running) return;
        var settings = CurrentSettings();
        SizeText.Text = VideoExportReport.Summarise(settings, _doc.Scene);
        DurationText.Text = settings.Fps == _doc.Scene.Fps
            ? "the scene's rate"
            : $"the scene is {_doc.Scene.Fps} fps — this plays back faster or slower";
        if (!EncoderBanner.IsVisible)
        {
            ExportButton.IsEnabled = true;
        }
    }

    // Avalonia matches a handler by its exact argument type, so the two kinds
    // of "something changed" cannot share one signature.
    private void OnSettingChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    private void OnSettingChanged(object? sender, TextChangedEventArgs e) => Refresh();

    private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_doc is null) return;
        FillQualities();
        // Keep the extension honest with the format: a ProRes stream in a
        // .mp4 is a file nothing will open.
        var format = FormatBox.SelectedIndex == 1 ? VideoFormat.ProRes : VideoFormat.Mp4;
        if (PathBox.Text is { Length: > 0 } path)
        {
            PathBox.Text = Path.ChangeExtension(path, VideoExporter.ExtensionOf(format));
        }
        Refresh();
    }

    private void OnWholeTimelineChanged(object? sender, RoutedEventArgs e)
    {
        var whole = WholeTimelineBox.IsChecked == true;
        FromBox.IsEnabled = ToBox.IsEnabled = !whole;
        Refresh();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var format = FormatBox.SelectedIndex == 1 ? VideoFormat.ProRes : VideoFormat.Mp4;
        var extension = VideoExporter.ExtensionOf(format);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export video",
            SuggestedFileName = Path.GetFileNameWithoutExtension(PathBox.Text ?? "animation"),
            DefaultExtension = extension,
            FileTypeChoices =
            [
                format == VideoFormat.ProRes
                    ? new FilePickerFileType("ProRes 422 (MOV)") { Patterns = ["*.mov"] }
                    : new FilePickerFileType("MP4 video (H.264)") { Patterns = ["*.mp4"] },
            ],
        });
        if (file?.TryGetLocalPath() is { } picked)
        {
            PathBox.Text = Path.ChangeExtension(picked, extension);
            Refresh();
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_doc is null) return;
        if (_running)
        {
            _cancel?.Cancel();
            return;
        }

        var settings = CurrentSettings();
        var path = PathBox.Text?.Trim();
        if (VideoExportReport.Validate(settings, _doc.Scene, path) is { } problem)
        {
            StatusText.Text = problem;
            return;
        }

        var audio = settings.IncludeAudio ? _resolveAudio?.Invoke() : null;
        // The file can go missing between opening this window and pressing the
        // button. A silent video reported as a success is the failure this
        // whole window exists to end, so the sentence says so.
        var soundWasAskedForAndLost = settings.IncludeAudio && audio is null && _doc.Scene.Audio is { Muted: false };
        _cancel = new CancellationTokenSource();
        _running = true;
        ExportButton.Content = "Stop";
        CloseButton.IsEnabled = false;
        Progress.IsVisible = true;
        Progress.Value = 0;
        StatusText.Text = "Rendering…";

        var token = _cancel.Token;
        var doc = _doc;
        var target = path!;
        string? error;
        try
        {
            error = await Task.Run(() => VideoExporter.Export(
                doc, settings, target, audio,
                (done, total) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Progress.Value = total <= 0 ? 0 : done / (double)total;
                    StatusText.Text = $"Rendering frame {done} of {total}…";
                }),
                token));
        }
        catch (Exception ex)
        {
            // An exception out of an async void handler is an unhandled crash,
            // and a crash is the silence this whole window exists to end. Any
            // way the render can fail becomes a sentence.
            error = $"Video export failed: {ex.Message}";
        }

        _running = false;
        _cancel.Dispose();
        _cancel = null;
        ExportButton.Content = "Export";
        CloseButton.IsEnabled = true;
        Progress.IsVisible = false;
        StatusText.Text = error
            ?? VideoExportReport.Done(target, settings, doc.Scene)
               + (soundWasAskedForAndLost
                   ? " No sound: the scratch track's file could not be read."
                   : "");
        Reported = StatusText.Text;
    }

    /// <summary>
    /// The last sentence this window said, so the host can echo it into the
    /// status strip — and so a test can read what an export reported.
    /// </summary>
    public string? Reported { get; private set; }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        Close();
    }
}
