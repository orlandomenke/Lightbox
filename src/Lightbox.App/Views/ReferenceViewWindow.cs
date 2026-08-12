using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// A character-sheet view in a window of its own, beside the art — the
/// reference held up next to the easel rather than opened in its place.
/// </summary>
/// <remarks>
/// <para>
/// <b>A viewer, not a canvas</b> (Q69): it renders the view and follows its
/// edits live, and drawing on it still means opening the view's tab. The
/// content pane is a single <see cref="Image"/> on purpose, so the day an
/// editable canvas earns its way in (see ROADMAP → the drawing floor) it
/// replaces one control rather than a window's worth of assumptions.
/// </para>
/// <para>
/// Live through the same definition of "the view as a picture" as the AI
/// payloads, the MCP surface and the taped-on-canvas copy:
/// <see cref="MainViewModel.RenderReferenceViewPng(ReferenceView)"/>. The
/// window re-renders on the edit funnel's <see cref="MainViewModel.DocumentEdited"/>
/// and skips the UI churn when the bytes did not change — the same
/// compare-and-skip the taped strip does, for the same reason.
/// </para>
/// </remarks>
public sealed class ReferenceViewWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ReferenceView _view;
    private readonly Image _image;
    private string _shown = "";

    public string ViewId => _view.Id;

    public ReferenceViewWindow(MainViewModel vm, ReferenceView view, string sheetName)
    {
        _vm = vm;
        _view = view;
        Title = $"{sheetName} · {view.Name}";
        ShowInTaskbar = false;

        // Sized to the view's aspect at a companion-window scale: big enough
        // to read a drawing, small enough to sit beside one.
        var scale = Math.Min(1.0, 480.0 / Math.Max(1, Math.Max(view.Width, view.Height)));
        Width = Math.Max(200, view.Width * scale);
        Height = Math.Max(150, view.Height * scale);

        _image = new Image { Stretch = Avalonia.Media.Stretch.Uniform };
        Content = _image;

        Render();
        _vm.DocumentEdited += Render;
        Closed += (_, _) => _vm.DocumentEdited -= Render;
    }

    /// <summary>Re-render the view; a no-op when its picture has not changed.</summary>
    private void Render()
    {
        // A deleted view degrades to its last picture, exactly like the taped
        // copy on the canvas: losing the source is no reason to blank the
        // window the artist deliberately opened.
        string png;
        try
        {
            png = _vm.RenderReferenceViewPng(_view);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (png == _shown) return;
        _shown = png;

        using var stream = new System.IO.MemoryStream(Convert.FromBase64String(png));
        _image.Source = new Bitmap(stream);
    }
}
