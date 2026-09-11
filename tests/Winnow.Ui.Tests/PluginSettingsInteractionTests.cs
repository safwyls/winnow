using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PluginSettingsInteractionTests
{
    [AvaloniaFact]
    public async Task Artwork_order_accepts_any_number_of_named_plugin_sources()
    {
        var preferences = new ArtworkPreferences(new Store());
        preferences.ConfigureSources([new("plugin:first", "First artwork"), new("plugin:second", "Second artwork"), new("plugin:third", "Third artwork")]);
        await preferences.LoadAsync();
        var order = new ArtworkOrderViewModel(preferences);
        var row = order.Sources.Single(source => source.SourceId == "plugin:third");
        Assert.Equal("Third artwork", row.Label);
        Assert.True(order.MoveDownCommand.CanExecute(row.SourceId));
        await order.MoveDownCommand.ExecuteAsync(row.SourceId);
        Assert.Equal("plugin:third", order.Sources[^1].SourceId);
        Assert.False(order.MoveDownCommand.CanExecute(row.SourceId));
        Assert.True(order.MoveUpCommand.CanExecute(row.SourceId));
        Assert.False(order.MoveUpCommand.CanExecute(order.Sources[0].SourceId));
    }

    [AvaloniaFact]
    public async Task Desktop_generated_fields_mask_secrets_save_and_clear_drafts_on_navigation()
    {
        var shell = await ShellAsync();
        var model = shell.EnrichmentSettings;
        var plugin = Assert.Single(model.Plugins.Plugins);
        var view = new EnrichmentSettingsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 688, Content = view };
        window.Show();
        try
        {
            shell.ShowEnrichmentSettingsCommand.Execute(null);
            Assert.True(shell.IsSettingsVisible);
            Assert.False(shell.IsLibraryVisible);
            var key = Named<TextBox>(view, "Community artwork API key");
            var text = Named<TextBox>(view, "Community artwork Language");
            Assert.Equal('●', key.PasswordChar);
            Assert.Equal('\0', text.PasswordChar);
            key.Text = "test-key";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("test-key", plugin.Fields[0].Value);
            var save = Named<Button>(view, "Save Community artwork settings");
            Assert.Same(plugin.SaveCommand, save.Command);
            await plugin.SaveCommand.ExecuteAsync(null);
            Assert.Empty(plugin.Fields[0].Value);
            plugin.Fields[0].Value = "unsaved-key";
            model.Igdb.ClientSecret = "unsaved-secret";
            shell.ShowLibraryCommand.Execute(null);
            Assert.Empty(plugin.Fields[0].Value);
            Assert.Empty(model.Igdb.ClientSecret);
            await shell.ShowSettingsCommand.ExecuteAsync(null);
            Assert.True(shell.IsEnrichmentSettingsVisible);
            plugin.Fields[0].Value = "detach-key";
            window.Content = null;
            Assert.Empty(plugin.Fields[0].Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_generated_editor_uses_controller_text_entry_and_preserves_focus_after_save()
    {
        var shell = await ShellAsync();
        var plugin = Assert.Single(shell.EnrichmentSettings.Plugins.Plugins);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenPluginSettingsPage(context, plugin);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        TextBox? requested = null;
        context.TextRequested += field => requested = field;
        window.Show();
        try
        {
            var key = Named<TextBox>(page, "Community artwork API key");
            key.Focus(); page.Handle(GamepadButtons.Accept);
            Assert.Same(key, requested);
            Assert.Equal('●', key.PasswordChar);
            key.Text = "test-key";
            Dispatcher.UIThread.RunJobs();
            var save = Named<Button>(page, "Save Community artwork settings");
            save.Focus();
            await plugin.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsFocused);
            Assert.Empty(plugin.Fields[0].Value);
            key.Text = "detach-key";
            window.Content = null;
            Assert.Empty(plugin.Fields[0].Value);

            using var settings = new FullscreenSettingsPage(context, "Metadata & artwork");
            window.Content = settings;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Named<Button>(settings, "Community artwork"));
            Assert.NotNull(Named<Button>(settings, "Open plugins folder"));
            await settings.PendingPluginRefresh;
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_and_fullscreen_share_restart_state_and_generated_field_removal()
    {
        var shell = await ShellAsync();
        var plugin = Assert.Single(shell.EnrichmentSettings.Plugins.Plugins);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenPluginSettingsPage(context, plugin);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            await plugin.ToggleEnabledCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(plugin.Enabled);
            Assert.True(plugin.RestartRequired);
            Assert.NotNull(Named<Button>(page, "Enable plugin: Community artwork"));
            Assert.False(Named<Button>(page, "Refresh Community artwork").IsEffectivelyEnabled);
            await plugin.Fields[0].RemoveSecretCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Named<Button>(page, "Remove saved Community artwork API key").IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants()
        .OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static async Task<MainWindowViewModel> ShellAsync()
    {
        var preview = PreviewData.Shell;
        var plugins = new PluginSettingsViewModel(new Backend());
        await plugins.LoadAsync();
        return new(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings,
            enrichmentSettings: new EnrichmentSettingsViewModel(new(), plugins));
    }

    private sealed class Backend : IPluginSettingsBackend
    {
        public string UserPluginDirectory => "plugins";
        private bool _enabled = true;
        private bool _stored = true;
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([new("community-art", "Community artwork", "Community game backgrounds.",
                "1.0.0", "Artwork", _enabled, true, !_enabled, "Loaded.", [
                    new("api-key", "API key", null, true, true, null, _stored),
                    new("language", "Language", null, false, false, "en", false)])]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
        { _stored = true; return Task.CompletedTask; }
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default)
        { _stored = false; return Task.CompletedTask; }
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        { _enabled = enabled; return Task.CompletedTask; }
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Store : ISettingsStore
    {
        private string? _value;
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_value);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) { _value = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default) { _value = null; return Task.CompletedTask; }
    }
}
