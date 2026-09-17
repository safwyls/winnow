using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenPluginInstallPage : FullscreenPage
{
    private readonly PluginSettingsViewModel _plugins;
    public override string Title => "Plugin installation";

    public FullscreenPluginInstallPage(FullscreenContext context) : base(context)
    {
        _plugins = context.Shared.PluginSettings;
        var rows = FullscreenInformation.Column();
        rows.Children.Add(FullscreenInformation.Title(Title));
        rows.Children.Add(Status(_plugins.Installation));
        var progress = new ProgressBar { IsIndeterminate = true, Height = 6 };
        progress[!ProgressBar.ForegroundProperty] = new DynamicResourceExtension("Volt");
        progress.Bind(IsVisibleProperty, new Binding(nameof(PluginInstallViewModel.IsBusy)) { Source = _plugins.Installation });
        AutomationProperties.SetName(progress, "Installing plugin");
        rows.Children.Add(progress);
        var retry = FullscreenUi.Button("Retry installation", () => { });
        retry.Command = _plugins.Installation.RetryCommand;
        retry.Bind(IsVisibleProperty, new Binding(nameof(PluginInstallViewModel.CanRetry)) { Source = _plugins.Installation });
        AutomationProperties.SetName(retry, "Retry plugin installation");
        rows.Children.Add(retry);
        var back = FullscreenUi.Button("Back", context.Back);
        rows.Children.Add(back);
        SetFocusRows([retry], [back]);
        Content = FullscreenUi.Scroll(rows);
        _plugins.PluginSettingsRequested += ShowSettings;
    }

    internal static TextBlock Status(PluginInstallViewModel model)
    {
        var status = FullscreenInformation.Text("");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(model.Status)) { Source = model });
        AutomationProperties.SetAutomationId(status, "PluginInstallStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        return status;
    }

    private void ShowSettings(PluginCardViewModel plugin)
    {
        if (this.IsAttachedToVisualTree()) Context.Push(new FullscreenPluginSettingsPage(Context, plugin));
    }

    public override void Dispose()
    {
        _plugins.PluginSettingsRequested -= ShowSettings;
        base.Dispose();
    }
}
