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
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SteamGridDbSettingsInteractionTests
{
    [AvaloniaFact]
    public async Task Desktop_metadata_tab_masks_key_saves_and_clears_drafts_on_navigation()
    {
        var shell = Shell();
        var model = shell.EnrichmentSettings;
        var view = new EnrichmentSettingsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 688, Content = view };
        window.Show();
        try
        {
            shell.ShowEnrichmentSettingsCommand.Execute(null);
            Assert.True(shell.IsSettingsVisible);
            Assert.False(shell.IsLibraryVisible);
            Assert.False(shell.IsApplicationSettingsVisible);
            var key = view.FindControl<TextBox>("SteamGridDbApiKey")!;
            Assert.Equal('●', key.PasswordChar);
            Assert.Equal("SteamGridDB API key", AutomationProperties.GetName(key));
            key.Text = "test-key";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("test-key", model.SteamGridDb.ApiKey);
            var save = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save API key"));
            Assert.Same(model.SteamGridDb.SaveCommand, save.Command);
            await model.SteamGridDb.SaveCommand.ExecuteAsync(null);
            Assert.Empty(model.SteamGridDb.ApiKey);
            Assert.Contains("Artwork refresh queued", model.SteamGridDb.Status);
            model.SteamGridDb.ApiKey = "unsaved-key";
            model.Igdb.ClientSecret = "unsaved-secret";
            shell.ShowLibraryCommand.Execute(null);
            Assert.Empty(model.SteamGridDb.ApiKey);
            Assert.Empty(model.Igdb.ClientSecret);
            await shell.ShowSettingsCommand.ExecuteAsync(null);
            Assert.True(shell.IsEnrichmentSettingsVisible);
            model.SteamGridDb.ApiKey = "detach-key";
            window.Content = null;
            Assert.Empty(model.SteamGridDb.ApiKey);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_metadata_section_opens_masked_editor_and_shared_order_controls()
    {
        var shell = Shell();
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSteamGridDbSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        TextBox? requested = null;
        context.TextRequested += field => requested = field;
        window.Show();
        try
        {
            var key = Assert.Single(page.GetVisualDescendants().OfType<TextBox>());
            key.Focus();
            page.Handle(GamepadButtons.Accept);
            Assert.Same(key, requested);
            Assert.Equal('●', key.PasswordChar);
            key.Text = "test-key";
            Dispatcher.UIThread.RunJobs();
            await shell.EnrichmentSettings.SteamGridDb.SaveCommand.ExecuteAsync(null);
            Assert.Empty(shell.EnrichmentSettings.SteamGridDb.ApiKey);
            key.Text = "detach-key";
            window.Content = null;
            Assert.Empty(shell.EnrichmentSettings.SteamGridDb.ApiKey);

            using var settings = new FullscreenSettingsPage(context, "Metadata & artwork");
            window.Content = settings;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(settings.GetVisualDescendants().OfType<Button>(), button => (AutomationProperties.GetName(button) ?? string.Empty).Contains("SteamGridDB artwork"));
            using var order = new FullscreenArtworkOrderPage(context);
            window.Content = order;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(order.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Move SteamGridDB up");
            await shell.EnrichmentSettings.ArtworkOrder.MoveUpCommand.ExecuteAsync(ArtworkPreferences.SteamGridDb);
            Assert.Equal(ArtworkPreferences.SteamGridDb, shell.EnrichmentSettings.ArtworkOrder.Sources[0].SourceId);
        }
        finally { window.Close(); }
    }

    private static MainWindowViewModel Shell()
    {
        var preview = PreviewData.Shell;
        var app = new ApplicationSettingsViewModel(steamGridDb: new SteamGridDbSettingsViewModel(new Settings()));
        return new(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings, applicationSettings: app);
    }

    private sealed class Settings : ISteamGridDbSettingsService
    {
        public Task<SteamGridDbSettingsSnapshot> LoadAsync(CancellationToken ct = default) => Task.FromResult(new SteamGridDbSettingsSnapshot(false, false, true, null));
        public Task<SteamGridDbSettingsSaveResult> SaveAsync(string apiKey) => Task.FromResult(string.IsNullOrWhiteSpace(apiKey)
            ? SteamGridDbSettingsSaveResult.MissingKey : SteamGridDbSettingsSaveResult.Saved);
        public Task<bool> RemoveAsync() => Task.FromResult(false);
    }
}
