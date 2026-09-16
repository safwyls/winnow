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
        controls.Children.Add(FullscreenInformation.Metadata(model.Version));
        controls.Children.Add(FullscreenInformation.Text(model.Description));
        controls.Children.Add(FullscreenInformation.Metadata(model.Capabilities));
        if (model.CanConfigure) FullscreenInformation.AddSection(controls, "Activation");
        var focus = new List<Control[]>();
        Button Action(string label, System.Windows.Input.ICommand command, string? accessibleName = null)
        {
            var button = FullscreenUi.Button(label, () => { });
            button.FontSize = 24;
            button.Command = command;
            AutomationProperties.SetName(button, accessibleName ?? label);
            controls.Children.Add(button); focus.Add([button]);
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
        foreach (var field in model.Fields)
        {
            controls.Children.Add(FullscreenInformation.Rule());
            controls.Children.Add(FullscreenInformation.Title(field.Label));
            if (field.HasDescription) controls.Children.Add(FullscreenInformation.Metadata(field.Description!));
            var editor = new TextBox { FontSize = 24, MinHeight = 72, PasswordChar = field.PasswordChar, Watermark = field.Watermark };
            editor.Bind(TextBox.TextProperty, new Binding(nameof(field.Value)) { Source = field, Mode = BindingMode.TwoWay });
            editor.Bind(IsEnabledProperty, new Binding(nameof(field.IsEnabled)) { Source = field });
            AutomationProperties.SetName(editor, field.AccessibleName);
            controls.Children.Add(editor); focus.Add([editor]);
            if (field.HasSetup) Action(field.SetupLabel + "     Browser ↗", field.OpenSetupCommand, field.SetupAccessibleName);
            if (field.IsSecret) Action("Remove saved secret", field.RemoveSecretCommand, field.RemoveAccessibleName);
        }
        if (model.HasSecrets) controls.Children.Add(FullscreenInformation.Metadata(PluginSettingsViewModel.SecretNote));
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
        DetachedFromVisualTree += (_, _) => model.ClearSecrets();
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginCardViewModel.IsBusy) || _model.IsBusy) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !this.IsAttachedToVisualTree()) return;
            if (_lastFocused is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target) FocusControl(target);
            else FocusInitial();
        }, DispatcherPriority.Loaded);
    }

    public override void Dispose()
    {
        _disposed = true;
        _model.PropertyChanged -= ModelChanged;
        _model.ClearSecrets();
        base.Dispose();
    }
}
