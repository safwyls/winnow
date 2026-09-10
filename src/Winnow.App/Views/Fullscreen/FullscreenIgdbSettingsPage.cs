using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenIgdbSettingsPage : FullscreenPage
{
    private readonly IgdbSettingsViewModel _model;
    public override string Title => "IGDB metadata";

    public FullscreenIgdbSettingsPage(FullscreenContext context) : base(context)
    {
        _model = context.Shared.ApplicationSettings.Igdb;
        DetachedFromVisualTree += (_, _) => _model.ClientSecret = "";
        var id = Field("IGDB client ID", nameof(_model.ClientId));
        var secret = Field("IGDB client secret", nameof(_model.ClientSecret));
        secret.PasswordChar = '●';
        secret.Watermark = "Enter a secret to save or replace credentials";
        var setup = FullscreenUi.Button("Get IGDB credentials", () => { });
        setup.Command = _model.OpenSetupCommand;
        var save = FullscreenUi.Button("Save credentials", () => { });
        save.Command = _model.SaveCommand;
        var remove = FullscreenUi.Button("Remove saved credentials", () => { });
        remove.Command = _model.RemoveCommand;
        var back = FullscreenUi.Button("Back", context.Back);
        var status = FullscreenUi.Text("", 28, "TextDim");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(_model.Status)) { Source = _model });
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(
            FullscreenUi.Text(Title, 64),
            FullscreenUi.Text("IGDB adds game details and artwork. Enter the client ID and secret from your Twitch developer application.", 28, "TextDim"),
            setup, FullscreenUi.Text("Client ID", 28), id,
            FullscreenUi.Text("Client secret", 28), secret,
            FullscreenUi.Text("The secret is stored securely on this device. Changes take effect immediately.", 28, "TextDim"),
            save, remove, status, back));
        SetFocusRows([setup], [id], [secret], [save], [remove], [back]);
    }

    private TextBox Field(string label, string property)
    {
        var field = new TextBox { FontSize = 28, MinHeight = 72 };
        AutomationProperties.SetName(field, label);
        field.Bind(TextBox.TextProperty, new Binding(property) { Source = _model, Mode = BindingMode.TwoWay });
        field.Bind(IsEnabledProperty, new Binding("!" + nameof(_model.IsBusy)) { Source = _model });
        return field;
    }

    public override void Dispose()
    {
        _model.ClientSecret = "";
        base.Dispose();
    }
}
