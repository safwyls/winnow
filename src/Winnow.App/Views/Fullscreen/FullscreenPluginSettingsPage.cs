using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenPluginSettingsPage : FullscreenPage
{
    private readonly PluginCardViewModel _model;
    private Control? _lastFocused;
    private bool _disposed;
    public override string Title => _model.Name;

    public FullscreenPluginSettingsPage(FullscreenContext context, PluginCardViewModel model) : base(context)
    {
        _model = model;
        var controls = FullscreenInformation.Column();
        controls.Children.Add(FullscreenUi.Text(model.Name, 64));
        var installation = context.Shared.PluginSettings.Installation;
        if (installation.HasRequest && installation.PluginId == model.Id)
            controls.Children.Add(FullscreenPluginInstallPage.Status(installation));
        controls.Children.Add(FullscreenInformation.Metadata(model.Version));
        controls.Children.Add(FullscreenInformation.Text(model.Description));
        controls.Children.Add(FullscreenInformation.Metadata(model.Capabilities));
        if (model.CanConfigure) FullscreenInformation.AddSection(controls, "Activation");
        var focus = new List<Control[]>();
        Button Action(string label, System.Windows.Input.ICommand command, string? accessibleName = null, Panel? parent = null)
        {
            var button = FullscreenUi.Button(label, () => { });
            button.FontSize = 24;
            button.Command = command;
            AutomationProperties.SetName(button, accessibleName ?? label);
            (parent ?? controls).Children.Add(button); focus.Add([button]);
            return button;
        }
        var enabled = new ToggleSwitch { FontSize = 24, MinHeight = 72,
            OnContent = "Enabled", OffContent = "Disabled", Command = model.ToggleEnabledCommand };
        controls.Children.Add(enabled); focus.Add([enabled]);
        enabled.IsVisible = model.CanConfigure;
        enabled.Bind(ToggleSwitch.IsCheckedProperty, new Binding(nameof(model.ActivationSelected)) { Source = model, Mode = BindingMode.TwoWay });
        enabled.Bind(AutomationProperties.NameProperty, new Binding(nameof(model.ToggleAccessibleName)) { Source = model });
        enabled.Bind(AutomationProperties.ItemStatusProperty, new Binding(nameof(model.EnabledStatus)) { Source = model });
        var restart = FullscreenInformation.Text("Restart Winnow to apply the enable or disable change.", 24, "AmberForeground");
        restart.Bind(IsVisibleProperty, new Binding(nameof(model.RestartRequired)) { Source = model });
        controls.Children.Add(restart);
        if (model.HasWebsite) Action("Provider website     Browser ↗", model.OpenWebsiteCommand, model.WebsiteAccessibleName);
        if (model.HasAccount)
        {
            FullscreenInformation.AddSection(controls, "Account");
            var accountStatus = FullscreenInformation.Text("");
            accountStatus.Bind(TextBlock.TextProperty, new Binding(nameof(model.AccountStatus)) { Source = model });
            AutomationProperties.SetLiveSetting(accountStatus, AutomationLiveSetting.Polite);
            controls.Children.Add(accountStatus);
            var challenge = new StackPanel { Spacing = 12 };
            challenge.Bind(IsVisibleProperty, new Binding(nameof(model.HasSignInChallenge)) { Source = model });
            challenge.Children.Add(FullscreenInformation.Metadata("Sign-in code"));
            var code = FullscreenInformation.Text("", 36);
            code.Bind(TextBlock.TextProperty, new Binding(nameof(model.UserCode)) { Source = model });
            AutomationProperties.SetAutomationId(code, "PluginSignInCode");
            challenge.Children.Add(code);
            var address = FullscreenInformation.Text("", 24);
            address.Bind(TextBlock.TextProperty, new Binding(nameof(model.VerificationUrl)) { Source = model });
            AutomationProperties.SetAutomationId(address, "PluginSignInAddress");
            challenge.Children.Add(address);
            controls.Children.Add(challenge);
            var open = Action("Open sign-in page     Browser ↗", model.OpenSignInPageCommand, model.SignInPageAccessibleName);
            open.Bind(IsVisibleProperty, new Binding(nameof(model.HasSignInChallenge)) { Source = model });
            var connect = Action("Sign in", model.ConnectCommand, model.ConnectAccessibleName);
            connect.Bind(IsVisibleProperty, new Binding("!" + nameof(model.AccountConnected)) { Source = model });
            var disconnect = Action("Sign out", model.DisconnectCommand, model.DisconnectAccessibleName);
            disconnect.Bind(IsVisibleProperty, new Binding(nameof(model.AccountConnected)) { Source = model });
            var cancel = Action("Cancel sign-in", model.CancelSignInCommand, model.CancelSignInAccessibleName);
            cancel.Bind(IsVisibleProperty, new Binding(nameof(model.IsConnecting)) { Source = model });
        }
        void AddField(PluginSettingFieldViewModel field, Panel parent)
        {
            parent.Children.Add(FullscreenInformation.Rule());
            parent.Children.Add(FullscreenInformation.Title(field.Label));
            if (field.HasDescription) parent.Children.Add(FullscreenInformation.Metadata(field.Description!));
            Control editor;
            if (field.IsBoolean)
            {
                var toggle = new ToggleSwitch { FontSize = 24, MinHeight = 72, OnContent = "On", OffContent = "Off" };
                toggle.Bind(ToggleSwitch.IsCheckedProperty, new Binding(nameof(field.BooleanValue)) { Source = field, Mode = BindingMode.TwoWay });
                editor = toggle;
            }
            else
            {
                var text = new TextBox { FontSize = 24, MinHeight = 72, PasswordChar = field.PasswordChar, Watermark = field.Watermark };
                text.Bind(TextBox.TextProperty, new Binding(nameof(field.Value)) { Source = field, Mode = BindingMode.TwoWay });
                editor = text;
            }
            editor.Bind(IsEnabledProperty, new Binding(nameof(field.IsEnabled)) { Source = field });
            AutomationProperties.SetName(editor, field.AccessibleName);
            parent.Children.Add(editor); focus.Add([editor]);
            if (field.HasSetup) Action(field.SetupLabel + "     Browser ↗", field.OpenSetupCommand, field.SetupAccessibleName, parent);
            if (field.IsSecret) Action("Remove saved secret", field.RemoveSecretCommand, field.RemoveAccessibleName, parent);
        }
        foreach (var field in model.StandardFields) AddField(field, controls);
        if (model.HasAdvancedSettings)
        {
            var disclosure = Action(model.AdvancedSettingsLabel, model.ToggleAdvancedSettingsCommand);
            disclosure.Bind(Button.ContentProperty, new Binding(nameof(model.AdvancedSettingsLabel)) { Source = model });
            disclosure.Bind(AutomationProperties.NameProperty, new Binding(nameof(model.AdvancedSettingsAccessibleName)) { Source = model });
            disclosure.Bind(AutomationProperties.ItemStatusProperty, new Binding(nameof(model.AdvancedSettingsStatus)) { Source = model });
            var advanced = new StackPanel { Spacing = 16 };
            advanced.Bind(IsVisibleProperty, new Binding(nameof(model.AdvancedSettingsExpanded)) { Source = model });
            foreach (var field in model.AdvancedFields) AddField(field, advanced);
            controls.Children.Add(advanced);
        }
        if (model.HasSecrets)
        {
            var secretNote = FullscreenInformation.Metadata(PluginSettingsViewModel.SecretNote);
            secretNote.Bind(IsVisibleProperty, new Binding(nameof(model.HasVisibleSecrets)) { Source = model });
            controls.Children.Add(secretNote);
        }
        if (model.HasSettings || model.CanConfigure) FullscreenInformation.AddSection(controls, "Apply changes");
        if (model.HasSettings) Action("Save settings", model.SaveCommand, model.SaveAccessibleName);
        if (model.CanConfigure) Action("Refresh now     Run", model.RefreshCommand, model.RefreshAccessibleName);
        var status = FullscreenInformation.Metadata("");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)) { Source = model });
        status.Bind(IsVisibleProperty, new Binding(nameof(model.Status)) { Source = model,
            Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        controls.Children.Add(status);
        var back = FullscreenUi.Button("Back", context.Back);
        controls.Children.Add(back); focus.Add([back]);
        Content = FullscreenUi.Scroll(controls);
        SetFocusRows(focus.ToArray());
        foreach (var control in focus.SelectMany(row => row)) control.GotFocus += (_, _) => _lastFocused = control;
        model.PropertyChanged += ModelChanged;
        DetachedFromVisualTree += (_, _) => model.Deactivate();
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginCardViewModel.HasSignInChallenge) && _model.HasSignInChallenge)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !this.IsAttachedToVisualTree() || !_model.HasSignInChallenge) return;
                var open = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Command == _model.OpenSignInPageCommand);
                if (open is not null) FocusControl(open);
            }, DispatcherPriority.Loaded);
        }
        if (e.PropertyName != nameof(PluginCardViewModel.IsBusy) || _model.IsBusy) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !this.IsAttachedToVisualTree()) return;
            if (_lastFocused is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target) FocusControl(target);
            else FocusInitial();
        }, DispatcherPriority.Loaded);
    }

    public override bool Handle(Winnow.App.Services.GamepadButtons buttons)
    {
        // Generated value switches have no command; controller Accept must toggle their bound value.
        if (buttons.HasFlag(Winnow.App.Services.GamepadButtons.Accept)
            && _lastFocused is ToggleSwitch { Command: null, IsEffectivelyEnabled: true } toggle)
        {
            toggle.IsChecked = toggle.IsChecked != true;
            return true;
        }
        return base.Handle(buttons);
    }

    public override void Dispose()
    {
        _disposed = true;
        _model.PropertyChanged -= ModelChanged;
        _model.Deactivate();
        base.Dispose();
    }
}
