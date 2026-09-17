using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PsnPluginPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_mask_session_token_save_history_options_and_remove_the_saved_secret(bool fullscreen)
    {
        var backend = new Backend();
        var plugins = new PluginSettingsViewModel(backend);
        await plugins.LoadAsync();
        var plugin = Assert.Single(plugins.Plugins);
        var shell = PreviewData.Shell;
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = fullscreen ? new FullscreenPluginSettingsPage(context, plugin) : null;
        Control view = page is null ? new PluginSettingsView { DataContext = plugins } : page;
        var window = new Window { Width = fullscreen ? 1920 : 1200, Height = 1080, Content = view };
        window.Show();
        try
        {
            var secret = Named<TextBox>(view, "PlayStation Sony session token (NPSSO)");
            var field = plugin.Fields.Single(value => value.Key == "npsso");
            Assert.Equal('●', secret.PasswordChar);
            Assert.True(string.IsNullOrEmpty(secret.Text));
            Assert.True(field.HasStoredSecret);
            Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
            Assert.True(Named<Button>(view, "Get PlayStation Sony session token (NPSSO)").IsEffectivelyVisible);
            Assert.Equal("https://ca.account.sony.com/api/v1/ssocookie", field.SetupUrl);
            Assert.False(plugin.HasAccount);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible
                && AutomationProperties.GetName(button) is "Sign in to PlayStation" or "Sign out of PlayStation");

            if (page is not null)
            {
                TextBox? requested = null;
                context.TextRequested += value => requested = value;
                Assert.True(secret.Focus());
                page.Handle(GamepadButtons.Accept);
                Assert.Same(secret, requested);
            }
            secret.Text = "fixture-session-token";
            Activate(Named<ToggleSwitch>(view, "PlayStation Include played games"), toggle: true);
            Activate(Named<ToggleSwitch>(view, "PlayStation Include PS3 and PS Vita trophy history"), toggle: true);
            var save = Named<Button>(view, "Save PlayStation settings");
            Activate(save);
            await plugin.SaveCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("fixture-session-token", backend.Saved!["npsso"]);
            Assert.Equal("true", backend.Saved["import-history"]);
            Assert.Equal("true", backend.Saved["include-legacy"]);
            Assert.Equal(3, backend.Saved.Count);
            Assert.Empty(field.Value);
            Assert.True(string.IsNullOrEmpty(secret.Text));
            Assert.Contains("Refresh queued.", plugin.Status);
            if (fullscreen) Assert.True(save.IsFocused);

            Activate(save);
            await plugin.SaveCommand.ExecutionTask!;
            Assert.DoesNotContain("npsso", backend.Saved.Keys);
            Assert.True(field.HasStoredSecret);
            var remove = Named<Button>(view, "Remove saved PlayStation Sony session token (NPSSO)");
            Activate(remove);
            await field.RemoveSecretCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("npsso", backend.RemovedSecret);
            Assert.False(field.HasStoredSecret);
            Assert.False(remove.IsEffectivelyEnabled);
            secret.Text = "unsaved-session-token";
            Dispatcher.UIThread.RunJobs();
            window.Content = null;
            Assert.Empty(field.Value);
        }
        finally { window.Close(); }

        void Activate(Control control, bool toggle = false)
        {
            Assert.True(control.Focus());
            if (page is not null) page.Handle(GamepadButtons.Accept);
            else
            {
                var key = toggle ? Key.Space : Key.Enter;
                var physical = toggle ? PhysicalKey.Space : PhysicalKey.Enter;
                window.KeyPress(key, Avalonia.Input.RawInputModifiers.None, physical, null);
                window.KeyRelease(key, Avalonia.Input.RawInputModifiers.None, physical, null);
            }
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Imported_history_shows_its_PlayStation_source_without_a_launch_or_install_action(bool fullscreen)
    {
        var now = DateTime.UtcNow;
        var entry = TileEntry.For(1, 1, 1, "plugin:psn", 60, now.AddDays(-1)) with
        { PluginActions = new("Played history — not proof of ownership.", null, null) };
        var tile = TileFixture.Tile(now, [entry], 1, LibraryBuckets.Active);
        using var details = new GameDetailsViewModel(tile, "Active", [], now);
        var shell = PreviewData.Shell;
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = fullscreen ? new FullscreenDetailsPage(context, details) : null;
        Control view = page is null ? new GameDetailsView { DataContext = details } : page;
        var window = new Window { Width = fullscreen ? 1920 : 1200, Height = 1080, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("PlayStation", entry.StoreName);
            Assert.Equal("PLAYSTATION", entry.StoreBadge);
            Assert.Null(entry.Installed);
            Assert.Null(entry.PrimaryAction);
            Assert.False(details.HasPrimaryAction);
            Assert.False(details.HasInstallState);
            var source = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                block => AutomationProperties.GetAutomationId(block) == "LibrarySourceSummary");
            Assert.True(source.IsEffectivelyVisible);
            Assert.Equal("PlayStation: Played history — not proof of ownership.", source.Text);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible
                && (button.Name == "LaunchButton" || AutomationProperties.GetAutomationId(button) == "details-primary-action"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayStation_filter_selects_imported_titles_on_both_surfaces(bool fullscreen)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open())
            connection.Execute("UPDATE ownerships SET store='plugin:psn' WHERE id=2;");
        using var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory));
        await library.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenBrowseFiltersPage(context);
        FullscreenPage? groupPage = null;
        var window = new Window { Width = 1920, Height = 1080,
            Content = fullscreen ? page : new FilterPanelView { DataContext = library.Filters } };
        context.PageRequested += value => { groupPage = value; window.Content = value; };
        context.BackRequested += () => window.Content = page;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (fullscreen)
            {
                Assert.True(Named<Button>(page, "PLATFORM · Any").Focus());
                page.Handle(GamepadButtons.Accept);
                Dispatcher.UIThread.RunJobs();
                Assert.True(Named<Button>(groupPage!, "PlayStation, 1 matching title").Focus());
                groupPage!.Handle(GamepadButtons.Accept);
                groupPage.Handle(GamepadButtons.Keyboard);
            }
            else
            {
                var choice = Named<CheckBox>(window, "PlayStation, 1 matching title");
                Assert.True(choice.Focus());
                window.KeyPress(Key.Space, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Space, null);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "plugin:psn" }, library.Filters.ToFilter().Stores);
            Assert.Equal("Game 2", Assert.Single(library.VisibleTiles).Title);
            var stores = library.Filters.Groups.Single(group => group.Key == FilterPanelViewModel.StoreKey);
            Assert.Equal(1, stores.AllOptions.Single(option => option.Label == "PlayStation").Count);
        }
        finally { groupPage?.Dispose(); window.Close(); }
    }

    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants()
        .OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private sealed class Backend : IPluginSettingsBackend
    {
        private bool _stored = true;
        public string UserPluginDirectory => "plugins";
        public IReadOnlyDictionary<string, string>? Saved { get; private set; }
        public string? RemovedSecret { get; private set; }
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([new("psn", "PlayStation", "Import PlayStation games and optional history.",
                "1.0.0", "Library sources, Metadata, Artwork", true, true, false, "", [
                    new("npsso", "Sony session token (NPSSO)", null, true, false, null, _stored,
                        SetupUrl: "https://ca.account.sony.com/api/v1/ssocookie"),
                    new("import-history", "Include played games", null, false, false,
                        Saved?.GetValueOrDefault("import-history") ?? "false", false, IsBoolean: true),
                    new("include-legacy", "Include PS3 and PS Vita trophy history", null, false, false,
                        Saved?.GetValueOrDefault("include-legacy") ?? "false", false, IsBoolean: true)])]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
        { Assert.Equal("psn", pluginId); Saved = values; _stored |= values.ContainsKey("npsso"); return Task.CompletedTask; }
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default)
        { Assert.Equal("psn", pluginId); RemovedSecret = key; _stored = false; return Task.CompletedTask; }
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
