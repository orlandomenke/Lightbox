using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.ViewModels;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.SnapshotChanged += snapshot => Canvas.UpdateSnapshot(snapshot);
        Canvas.PaintStarted += _vm.BeginStroke;
        Canvas.PaintMoved += _vm.MoveStroke;
        Canvas.PaintEnded += _vm.EndStroke;

        KeyDown += OnKeyDown;
        Loaded += (_, _) => _vm.PublishSnapshot();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e)
        {
            case { Key: Key.Space }:
                _vm.TogglePlaybackCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Z, KeyModifiers: KeyModifiers.Control }:
                _vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Y, KeyModifiers: KeyModifiers.Control }:
                _vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Left }:
                _vm.CurrentFrameIndex = Math.Max(0, _vm.CurrentFrameIndex - 1);
                e.Handled = true;
                break;
            case { Key: Key.Right }:
                _vm.CurrentFrameIndex = Math.Min(_vm.Doc.Scene.FrameCount - 1, _vm.CurrentFrameIndex + 1);
                e.Handled = true;
                break;
        }
    }

    private static readonly FilePickerFileType LightboxFileType = new("Lightbox document")
    {
        Patterns = ["*.lightbox.json"],
    };

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save animation",
            SuggestedFileName = "untitled.lightbox.json",
            FileTypeChoices = [LightboxFileType],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_vm.SerializeDocument());
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open animation",
            AllowMultiple = false,
            FileTypeFilter = [LightboxFileType],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        _vm.ReplaceDocument(DocJson.Deserialize(json));
    }
}
