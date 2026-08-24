using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.Services;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Save as image…</c>, and opening a Photoshop file.
/// </summary>
/// <remarks>
/// A partial of its own rather than lines in <c>MainWindow.axaml.cs</c>, which is
/// at its line budget — and the two belong together anyway: they are the two ends
/// of "Lightbox talks to the formats everybody else uses".
/// </remarks>
public partial class MainWindow
{
    /// <summary>Every picture format Lightbox can write, as one filter.</summary>
    private static readonly FilePickerFileType ImageFileType = new("Image")
    {
        Patterns = [.. ImageSaveFormats.All.SelectMany(
            f => ImageSaveFormats.Extensions(f).Select(e => "*" + e))],
    };

    internal static readonly FilePickerFileType PhotoshopFileType = new("Photoshop document")
    {
        Patterns = ["*.psd", "*.psb"],
    };

    private async void OnSaveAsImageClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Doc is not { } doc) return;

        var dialog = new SaveImageDialog(doc.Scene);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        var choice = dialog.Choice;
        var suggested = SuggestedImageName(choice.Extension);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as image",
            SuggestedFileName = suggested,
            DefaultExtension = choice.Extension.TrimStart('.'),
            FileTypeChoices = [ImageFileType],
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        // The picker can hand back a name whose extension disagrees with the
        // format the dialog chose — an artist typing "cover.jpg" over a PNG
        // default means it. The extension wins, because it is the more
        // deliberate of the two.
        var options = choice.ToOptions();
        if (ImageSaveFormats.FromExtension(path) is { } typed && typed != options.Format)
        {
            options = options with { Format = typed };
        }

        try
        {
            var result = SaveAsImage.Write(doc, path, options, _vm.CurrentFrameIndex);
            _vm.AiStatus = result.Warning is { } warning
                ? $"Saved {Describe(result)}. {warning}"
                : $"Saved {Describe(result)}.";
        }
        catch (Exception ex)
        {
            _vm.AiStatus = $"Could not save the image: {ex.Message}";
        }
    }

    private static string Describe(ImageSaveResult result) =>
        result.Paths.Count == 1
            ? Path.GetFileName(result.Paths[0])
            : $"{result.Paths.Count} images";

    private string SuggestedImageName(string extension)
    {
        var stem = _vm.Doc?.Scene.Name;
        if (string.IsNullOrWhiteSpace(stem)) stem = "drawing";
        foreach (var bad in Path.GetInvalidFileNameChars()) stem = stem.Replace(bad, '-');
        return stem + extension;
    }

    /// <summary>
    /// Open a .psd or .psb as a new document, or say precisely why not.
    /// </summary>
    /// <remarks>
    /// The refusal goes in a dialog rather than the status line because it is a
    /// list: every feature Lightbox cannot represent, the layer carrying it, and
    /// the Photoshop step that fixes it. One trip back to Photoshop should fix
    /// everything, which a truncated message cannot deliver.
    /// </remarks>
    private async Task OpenPhotoshopFileAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        byte[] bytes;
        await using (var stream = await file.OpenReadAsync())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        var name = Path.GetFileNameWithoutExtension(path ?? file.Name);
        try
        {
            var imported = PsdDocumentImport.Open(bytes, name);
            // No path: this is a Photoshop file, not a Lightbox one, so Save
            // must not offer to write a .psd back over it. Save as is the route,
            // which is also what makes the import non-destructive.
            _vm.OpenDocumentTab(imported.Document, null);
            _vm.AiStatus = imported.Notes.Count == 0
                ? $"Imported {name}."
                : $"Imported {name}. {string.Join(" ", imported.Notes)}";
        }
        catch (Lightbox.Import.PsdUnsupportedException refused)
        {
            await new NoticeDialog().Show(
                "Cannot open this PSD",
                $"“{name}” uses {refused.Reasons.Count} feature"
                    + $"{(refused.Reasons.Count == 1 ? "" : "s")} Lightbox has no model for. "
                    + "Flattening these in Photoshop and saving again will let it open.",
                string.Join("\n\n", refused.Reasons.Select(r => "• " + r)))
                .ShowDialog(this);
        }
        catch (Exception ex)
        {
            await new NoticeDialog().Show(
                "Cannot open this PSD",
                $"“{name}” could not be read.",
                ex.Message).ShowDialog(this);
        }
    }
}
