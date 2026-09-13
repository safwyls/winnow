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
        _model = context.Shared.EnrichmentSettings.Igdb;
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
        var status = FullscreenInformation.Metadata("");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(_model.Status)) { Source = _model });
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var content = FullscreenInformation.Column();
        content.Children.Add(FullscreenUi.Text(Title, 64));
        content.Children.Add(FullscreenInformation.Text("IGDB adds game details and artwork. Enter the client ID and secret from your Twitch developer application."));
        content.Children.Add(setup);
        FullscreenInformation.AddSection(content, "Credentials");
        content.Children.Add(FullscreenInformation.Title("Client ID"));
        content.Children.Add(id);
        content.Children.Add(FullscreenInformation.Title("Client secret"));
        content.Children.Add(secret);
        content.Children.Add(FullscreenInformation.Metadata("The secret is stored securely on this device. Changes take effect immediately."));
        content.Children.Add(FullscreenInformation.Rule());
        foreach (var button in new[] { setup, save, remove, back }) button.FontSize = 24;
        content.Children.Add(save);
        content.Children.Add(remove);
        content.Children.Add(status);
        content.Children.Add(back);
        Content = FullscreenUi.Scroll(content);
        SetFocusRows([setup], [id], [secret], [save], [remove], [back]);
    }

    private TextBox Field(string label, string property)
    {
        var field = new TextBox { FontSize = 24, MinHeight = 72 };
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
