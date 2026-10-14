namespace Lightbox.App.Views;

/// <summary>
/// Q261: the window answers the view model's request for a direction with
/// <see cref="DirectionDialog"/>. The bar's own buttons live in <see cref="AiBar"/>.
/// </summary>
public partial class MainWindow
{
    private void WireAiDirections() =>
        _vm.AskDirection = ask => DirectionDialog.AskAsync(this, ask);
}
