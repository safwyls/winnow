using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PluginTabsTests
{
    [AvaloniaFact]
    public async Task Fullscreen_resizing_keeps_the_selected_tab_visible_and_removes_unused_arrows()
    {
        var plugins = new PluginSettingsViewModel(new Backend(2));
        await plugins.LoadAsync();
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings, enrichmentSettings: new EnrichmentSettingsViewModel(new(), plugins));
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Plugins");
        var window = new Window { Width = 900, Height = 1000, Content = page };
        window.Show();
        try
        {
            await page.PendingPluginRefresh; Flush();
            plugins.SelectPlugin(null); Flush();
            Button Next() => page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NextFullscreenPluginTabs");
            Assert.True(Next().IsEffectivelyVisible);
            window.Width = 3000; Flush();
            Assert.False(Next().IsEffectivelyVisible);
            window.Width = 900; Flush();
            var selected = page.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Manage plugins");
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "FullscreenPluginTabScroll");
            var at = selected.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(at.X, -1, scroll.Viewport.Width);
            Assert.InRange(at.X + selected.Bounds.Width, 0, scroll.Viewport.Width + 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1280, 720, 1)]
    [InlineData(1920, 1080, 1.4)]
    public async Task Fullscreen_plugin_tabs_preserve_drafts_and_controller_navigation(int width, int height, double textScale)
    {
        var plugins = new PluginSettingsViewModel(new Backend(12));
        await plugins.LoadAsync();
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings, enrichmentSettings: new EnrichmentSettingsViewModel(new(), plugins));
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell) { TextScale = textScale };
        using var page = new FullscreenSettingsPage(context, "Plugins");
        using var view = new FullscreenView(context);
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show(); context.Push(page);
        try
        {
            await page.PendingPluginRefresh; Flush();
            Button Named(string name) => page.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);
            var first = plugins.LoadedPlugins[0];
            first.Fields[0].Value = "Draft language";
            first.Fields[1].Value = "Private draft";
            Assert.True(Named("Scroll plugin tabs right").IsEffectivelyVisible);
            Named(plugins.LoadedPlugins[^1].Name).Focus();
            page.Handle(GamepadButtons.Accept); Flush();
            Assert.Same(plugins.LoadedPlugins[^1], plugins.SelectedPlugin);
            Assert.Empty(first.Fields[1].Value);
            Assert.True(Named(plugins.SelectedPlugin!.Name).IsKeyboardFocusWithin);
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "FullscreenPluginTabScroll");
            Assert.True(scroll.Offset.X > 0);
            Capture(window, $"plugin-tabs-fullscreen-{width}");
            Named(first.Name).Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.Equal("Draft language", first.Fields[0].Value);
            page.Handle(GamepadButtons.Down); Flush();
            Assert.Contains(page.GetVisualDescendants().OfType<ToggleSwitch>(), control => control.IsKeyboardFocusWithin);
            Named("Manage plugins").Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.True(plugins.IsManagingPlugins);
            Assert.True(Named("Disabled provider").IsEffectivelyVisible);
            Assert.True(Named("Broken package").IsEffectivelyVisible);
            Capture(window, $"plugin-tabs-fullscreen-manage-{width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(700, 700)]
    [InlineData(1280, 820)]
    public async Task Desktop_plugin_tabs_scroll_select_and_keep_management_reachable(int width, int height)
    {
        var model = new PluginSettingsViewModel(new Backend(12));
        await model.LoadAsync();
        var view = new PluginSettingsView { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        try
        {
            Flush();
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single(control => control.Name == "PluginTabs");
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "PluginTabScroll");
            var previous = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "PreviousPluginTabs");
            var next = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "NextPluginTabs");
            Assert.Equal(13, tabs.ItemCount);
            Assert.True(previous.IsEffectivelyVisible);
            Assert.True(next.IsEffectivelyVisible);
            Assert.False(previous.IsEffectivelyEnabled);
            Assert.True(next.IsEffectivelyEnabled);
            var selected = model.SelectedPlugin;
            next.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
            Assert.True(scroll.Offset.X > 0);
            Assert.Same(selected, model.SelectedPlugin);
            Capture(window, $"plugin-tabs-overflow-{width}");

            tabs.GetVisualDescendants().OfType<TabItem>().First().Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Flush();
            Assert.Same(model.LoadedPlugins[1], model.SelectedPlugin);
            Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && !box.PasswordChar.Equals('●'));
            window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None); Flush();
            Assert.True(model.IsManagingPlugins);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Disabled provider");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "The package could not be loaded.");
            Assert.False(next.IsEffectivelyEnabled);
            Capture(window, $"plugin-tabs-manage-{width}");
            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None); Flush();
            Assert.Same(model.LoadedPlugins[0], model.SelectedPlugin);
            Assert.InRange(scroll.Offset.X, 0, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_arrows_disappear_when_tabs_fit_after_resize()
    {
        var model = new PluginSettingsViewModel(new Backend(2));
        await model.LoadAsync();
        var view = new PluginSettingsView { DataContext = model };
        var window = new Window { Width = 460, Height = 800, Content = view };
        window.Show();
        try
        {
            Flush();
            var next = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "NextPluginTabs");
            Assert.True(next.IsEffectivelyVisible);
            model.SelectPlugin(model.LoadedPlugins[1]); Flush();
            window.Width = 1600; Flush();
            Assert.False(next.IsEffectivelyVisible);
            Assert.Same(model.LoadedPlugins[1], model.SelectedPlugin);
            Capture(window, "plugin-tabs-wide");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_catalog_removal_keeps_surviving_selection_and_falls_back_for_removed_plugin()
    {
        var backend = new Backend(3);
        var model = new PluginSettingsViewModel(backend);
        await model.LoadAsync();
        var view = new PluginSettingsView { DataContext = model };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        window.Show();
        try
        {
            Flush();
            var selected = model.LoadedPlugins[2];
            model.SelectPlugin(selected); Flush();
            backend.RemovedId = "plugin-0";
            await model.LoadAsync(); Flush();
            Assert.Same(selected, model.SelectedPlugin);
            backend.RemovedId = selected.Id;
            await model.LoadAsync(); Flush();
            Assert.True(model.IsManagingPlugins);
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single(control => control.Name == "PluginTabs");
            Assert.Same(model.SelectedTab, tabs.SelectedItem);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Selection_survives_refresh_and_tab_switches_preserve_only_nonsecret_drafts()
    {
        var backend = new Backend(3);
        var model = new PluginSettingsViewModel(backend);
        await model.LoadAsync();
        var selected = model.Plugins[1];
        model.SelectPlugin(selected);
        selected.Fields[0].Value = "Unfinished language choice";
        selected.Fields[1].Value = "Unsubmitted secret";
        model.SelectPlugin(model.Plugins[0]);
        Assert.Empty(selected.Fields[1].Value);
        model.SelectPlugin(selected);
        Assert.Equal("Unfinished language choice", selected.Fields[0].Value);
        await model.LoadAsync();
        Assert.Same(selected, model.SelectedPlugin);
        backend.RemovedId = selected.Id;
        await model.LoadAsync();
        Assert.DoesNotContain(selected, model.LoadedPlugins);
        Assert.NotSame(selected, model.SelectedPlugin);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        Flush();
        window.CaptureRenderedFrame()!.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class Backend(int count) : IPluginSettingsBackend
    {
        public string? RemovedId { get; set; }
        public string UserPluginDirectory => "plugins";
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>(Enumerable.Range(0, count)
                .Where(index => $"plugin-{index}" != RemovedId)
                .Select(index => new PluginSettingsSnapshot($"plugin-{index}", $"Community provider {index + 1:00}",
                    "Library imports, metadata and artwork from a community provider.", "1.2.3", "Metadata and artwork",
                    true, true, false, "", [new("language", "Language", "Preferred metadata language.", false, false, "en", false),
                        new("api-key", "API key", null, true, false, null, false)]))
                .Append(new("disabled", "Disabled provider", "Enable this provider and restart Winnow.", "1.0", "Metadata",
                    false, false, false, "Disabled", []))
                .Append(new("invalid:broken", "Broken package", "", "", "", false, false, false,
                    "The package could not be loaded.", [], CanConfigure: false)).ToArray());
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
