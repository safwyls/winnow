using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
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
    public async Task Desktop_cards_fill_the_pane_and_activation_switch_handles_keyboard_and_save_failure()
    {
        var backend = new Backend();
        var shell = await ShellAsync(backend);
        var view = new PluginSettingsView { DataContext = shell.PluginSettings };
        var window = new Window { Width = 1000, Height = 900, Content = view };
        window.Show();
        try
        {
            var card = Named<Border>(view, "Community artwork");
            Assert.True(card.Bounds.Width > 900);
            var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Community artwork");
            var version = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "1.0.0");
            Assert.True(version.TranslatePoint(default, card)!.Value.Y >= title.TranslatePoint(default, card)!.Value.Y + title.Bounds.Height);
            var toggle = Named<ToggleSwitch>(view, "Disable plugin: Community artwork");
            Assert.True(toggle.IsChecked);
            toggle.Focus();
            window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null);
            window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            Assert.False(shell.PluginSettings.Plugins[0].Enabled);
            Assert.True(shell.PluginSettings.Plugins[0].RestartRequired);
            backend.FailActivation = true;
            toggle.Focus();
            window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null);
            window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            Assert.StartsWith("Could not change this plugin", shell.PluginSettings.Plugins[0].Status);
            Capture(window, "desktop-plugin-layout");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_activation_switch_handles_controller_accept()
    {
        var shell = await ShellAsync();
        var plugin = shell.PluginSettings.Plugins[0];
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenPluginSettingsPage(context, plugin);
        var window = new Window { Content = page, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            var toggle = Named<ToggleSwitch>(page, "Disable plugin: Community artwork");
            toggle.Focus();
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            Assert.False(plugin.Enabled);
            Assert.True(plugin.RestartRequired);
            Capture(window, "fullscreen-plugin-toggle");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_lazy_tab_loads_plugins_on_first_open_and_refreshes_when_reopened()
    {
        var backend = new RuntimeStateBackend();
        var shell = await ShellAsync(backend, preload: false);
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            Assert.Empty(shell.PluginSettings.Plugins);
            shell.ShowPluginSettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(window.GetVisualDescendants().OfType<PluginSettingsView>());
            Assert.Equal(3, shell.PluginSettings.Plugins.Count);
            Assert.Equal("Active artwork · 2.0", Named<TextBlock>(view, "Loaded plugins").Text);
            shell.ShowEnrichmentSettingsCommand.Execute(null);
            backend.Loaded = false;
            shell.ShowPluginSettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("No plugins are loaded in this session.", Named<TextBlock>(view, "Loaded plugins").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_loads_plugins_without_desktop_preloading()
    {
        var shell = await ShellAsync(new RuntimeStateBackend(), preload: false);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Plugins");
        var window = new Window { Content = page, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            await page.PendingPluginRefresh;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Active artwork · 2.0", Named<TextBlock>(page, "Loaded plugins").Text);
            Assert.NotNull(Named<Button>(page, "Active artwork"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Loaded_plugins_show_runtime_names_and_versions_on_both_surfaces()
    {
        var backend = new RuntimeStateBackend();
        var shell = await ShellAsync(backend);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        var desktop = new PluginSettingsView { DataContext = shell.PluginSettings };
        var window = new Window { Width = 1200, Height = 800, Content = desktop };
        window.Show();
        try
        {
            Assert.Equal("Active artwork · 2.0", Named<TextBlock>(desktop, "Loaded plugins").Text);
            Assert.Equal(3, shell.PluginSettings.Plugins.Count);
            Capture(window, "desktop-loaded-plugins");
            using var fullscreen = new FullscreenSettingsPage(context, "Plugins");
            window.Content = fullscreen;
            await fullscreen.PendingPluginRefresh;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Active artwork · 2.0", Named<TextBlock>(fullscreen, "Loaded plugins").Text);
            Assert.NotNull(Named<Button>(fullscreen, "Waiting for restart"));
            Capture(window, "fullscreen-loaded-plugins");

            backend.Loaded = false;
            await shell.PluginSettings.LoadAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("No plugins are loaded in this session.", Named<TextBlock>(fullscreen, "Loaded plugins").Text);
            window.Content = desktop;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("No plugins are loaded in this session.", Named<TextBlock>(desktop, "Loaded plugins").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Zip_installation_help_and_archive_errors_are_visible_on_both_surfaces()
    {
        var shell = await ShellAsync(new ArchiveIssueBackend());
        var plugin = Assert.Single(shell.PluginSettings.Plugins);
        var desktop = new PluginSettingsView { DataContext = shell.PluginSettings };
        var window = new Window { Width = 1200, Height = 800, Content = desktop };
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        window.Show();
        try
        {
            Assert.Contains(desktop.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == PluginSettingsViewModel.InstallationNote);
            Assert.Contains(desktop.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == ArchiveIssueBackend.Error);
            Assert.False(plugin.CanConfigure);
            using var settings = new FullscreenSettingsPage(context, "Plugins");
            window.Content = settings;
            await settings.PendingPluginRefresh;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == PluginSettingsViewModel.InstallationNote);
            var archiveButton = Named<Button>(settings, "broken.zip");
            FullscreenPage? opened = null;
            context.PageRequested += page => opened = page;
            archiveButton.Focus();
            settings.Handle(GamepadButtons.Accept);
            using var errorPage = Assert.IsType<FullscreenPluginSettingsPage>(opened);
            window.Content = errorPage;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(errorPage.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == ArchiveIssueBackend.Error);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_tabs_separate_plugin_controls_from_metadata_and_fit_the_minimum_window()
    {
        var shell = await ShellAsync();
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 688 };
        window.Show();
        try
        {
            shell.ShowEnrichmentSettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var metadata = window.GetVisualDescendants().OfType<EnrichmentSettingsView>().Single();
            Assert.NotNull(Named<TextBox>(metadata, "IGDB client ID"));
            Assert.DoesNotContain(metadata.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Open plugins folder"));
            var tab = Named<Button>(window, "PLUGINS");
            tab.Focus();
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsPluginSettingsVisible);
            Assert.False(shell.IsEnrichmentSettingsVisible);
            var plugins = window.GetVisualDescendants().OfType<PluginSettingsView>().Single();
            Assert.True(plugins.IsEffectivelyVisible);
            Assert.NotNull(Named<TextBox>(plugins, "Community artwork API key"));
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tab") && button.IsEffectivelyVisible))
            {
                var origin = button.TranslatePoint(default, window)!.Value;
                Assert.InRange(origin.X + button.Bounds.Width, 0, window.Bounds.Width);
            }
            Capture(window, "desktop-plugins-shell");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_triggers_navigate_between_metadata_plugins_and_application()
    {
        var shell = await ShellAsync();
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Metadata & artwork");
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Open plugins folder");
            page.Handle(GamepadButtons.PageNext);
            await page.PendingPluginRefresh;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Named<Button>(page, "Open plugins folder"));
            Assert.NotNull(Named<Button>(page, "Community artwork"));
            page.Handle(GamepadButtons.PageNext);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Open plugins folder");
            page.Handle(GamepadButtons.PagePrevious);
            await page.PendingPluginRefresh;
            Assert.NotNull(Named<Button>(page, "Community artwork"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_overflowing_tabs_scroll_the_focused_section_into_view()
    {
        var shell = await ShellAsync();
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Plugins");
        var window = new Window { Width = 1000, Height = 800, Content = page };
        window.Show();
        try
        {
            await page.PendingPluginRefresh;
            Dispatcher.UIThread.RunJobs();
            var application = page.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Application"));
            application.Focus();
            Dispatcher.UIThread.RunJobs();
            var strip = application.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(strip.Offset.X > 0);
            var origin = application.TranslatePoint(default, window)!.Value;
            Assert.InRange(origin.X, 0, window.Bounds.Width);
            Assert.InRange(origin.X + application.Bounds.Width, 0, window.Bounds.Width);
        }
        finally { window.Close(); }
    }

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
        var view = new PluginSettingsView { DataContext = shell.PluginSettings };
        var window = new Window { Width = 1200, Height = 688, Content = view };
        window.Show();
        try
        {
            shell.ShowPluginSettingsCommand.Execute(null);
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
            shell.ShowEnrichmentSettingsCommand.Execute(null);
            Assert.Empty(plugin.Fields[0].Value);
            model.Igdb.ClientSecret = "unsaved-secret";
            shell.ShowPluginSettingsCommand.Execute(null);
            Assert.Empty(model.Igdb.ClientSecret);
            plugin.Fields[0].Value = "unsaved-key";
            shell.ShowLibraryCommand.Execute(null);
            Assert.Empty(plugin.Fields[0].Value);
            Assert.Empty(model.Igdb.ClientSecret);
            await shell.ShowSettingsCommand.ExecuteAsync(null);
            Assert.True(shell.IsPluginSettingsVisible);
            Capture(window, "desktop-plugins");
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

            using var settings = new FullscreenSettingsPage(context, "Plugins");
            window.Content = settings;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Named<Button>(settings, "Community artwork"));
            Assert.NotNull(Named<Button>(settings, "Open plugins folder"));
            await settings.PendingPluginRefresh;
            Capture(window, "fullscreen-plugins");
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

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }

    private static async Task<MainWindowViewModel> ShellAsync(IPluginSettingsBackend? backend = null, bool preload = true)
    {
        var preview = PreviewData.Shell;
        var plugins = new PluginSettingsViewModel(backend ?? new Backend());
        if (preload) await plugins.LoadAsync();
        return new(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings,
            enrichmentSettings: new EnrichmentSettingsViewModel(new(), plugins));
    }

    private sealed class ArchiveIssueBackend : IPluginSettingsBackend
    {
        public const string Error = "Could not unpack the plugin ZIP. Download the package again, then restart Winnow.";
        public string UserPluginDirectory => "plugins";
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([new("invalid:broken.zip", "broken.zip", "This plugin could not be loaded.",
                "", "", false, false, false, Error, [], CanConfigure: false)]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RuntimeStateBackend : IPluginSettingsBackend
    {
        public bool Loaded { get; set; } = true;
        public string UserPluginDirectory => "plugins";
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([
                new("active", "Active artwork", "", "2.0", "Artwork", false, Loaded, Loaded, "", []),
                new("pending", "Waiting for restart", "", "1.0", "Metadata", true, false, true, "", []),
                new("failed", "Failed plugin", "", "3.0", "Metadata", true, false, true, "Could not start.", [])]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Backend : IPluginSettingsBackend
    {
        public bool FailActivation { get; set; }
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
        { if (FailActivation) throw new IOException(); _enabled = enabled; return Task.CompletedTask; }
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
