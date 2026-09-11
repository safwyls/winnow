using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenSteamGridDbSettingsPage : FullscreenPage
{
    private readonly SteamGridDbSettingsViewModel _model;
    public override string Title => "SteamGridDB artwork";

    public FullscreenSteamGridDbSettingsPage(FullscreenContext context) : base(context)
    {
        _model = context.Shared.EnrichmentSettings.SteamGridDb;
        DetachedFromVisualTree += (_, _) => _model.ApiKey = string.Empty;
        var key = new TextBox { FontSize = 28, MinHeight = 72, PasswordChar = '●',
            Watermark = "Enter a key to save or replace it" };
        AutomationProperties.SetName(key, "SteamGridDB API key");
        key.Bind(TextBox.TextProperty, new Binding(nameof(_model.ApiKey)) { Source = _model, Mode = BindingMode.TwoWay });
        key.Bind(IsEnabledProperty, new Binding("!" + nameof(_model.IsBusy)) { Source = _model });
        var setup = FullscreenUi.Button("Get SteamGridDB API key", () => { });
        setup.Command = _model.OpenSetupCommand;
        var save = FullscreenUi.Button("Save API key", () => { });
        save.Command = _model.SaveCommand;
        var remove = FullscreenUi.Button("Remove saved API key", () => { });
        remove.Command = _model.RemoveCommand;
        var back = FullscreenUi.Button("Back", context.Back);
        var status = FullscreenUi.Text("", 28, "TextDim");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(_model.Status)) { Source = _model });
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(
            FullscreenUi.Text(Title, 64), FullscreenUi.Text(SteamGridDbSettingsViewModel.Intro, 28, "TextDim"),
            setup, FullscreenUi.Text("API key", 28), key,
            FullscreenUi.Text(SteamGridDbSettingsViewModel.StorageNote, 28, "TextDim"), save, remove, status, back));
        SetFocusRows([setup], [key], [save], [remove], [back]);
    }

    public override void Dispose()
    {
        _model.ApiKey = string.Empty;
        base.Dispose();
    }
}
