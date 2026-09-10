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

public sealed class IgdbSettingsInteractionTests
{
    [AvaloniaFact]
    public async Task Desktop_binds_masked_input_and_reports_missing_fields_without_exposing_secret()
    {
        var model = new ApplicationSettingsViewModel(igdb: new IgdbSettingsViewModel(new SettingsService()));
        var view = new ApplicationSettingsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 688, Content = view };
        window.Show();
        try
        {
            var secret = view.FindControl<TextBox>("IgdbClientSecret")!;
            Assert.Equal('●', secret.PasswordChar);
            Assert.Equal("IGDB client secret", AutomationProperties.GetName(secret));
            secret.Text = "test-secret";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("test-secret", model.Igdb.ClientSecret);
            var save = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save credentials"));
            Assert.Same(model.Igdb.SaveCommand, save.Command);
            await model.Igdb.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.Igdb.Status);
            Assert.Contains("Enter both", model.Igdb.Status);
            view.IsVisible = false;
            Assert.Empty(model.Igdb.ClientSecret);
            view.IsVisible = true;
            Assert.Empty(secret.Text!);
            secret.Text = "another-test-secret";
            model.Igdb.ClientId = "test-client";
            await model.Igdb.SaveCommand.ExecuteAsync(null);
            Assert.Contains("Metadata refresh queued", model.Igdb.Status);
            Assert.Empty(model.Igdb.ClientSecret);
        }
        finally { window.Close(); }
        Assert.Empty(model.Igdb.ClientSecret);
    }

    [AvaloniaFact]
    public async Task Fullscreen_controller_opens_masked_editor_and_keeps_shared_values()
    {
        var preview = PreviewData.Shell;
        var app = new ApplicationSettingsViewModel(igdb: new IgdbSettingsViewModel(new SettingsService()));
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: app);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenIgdbSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        TextBox? requested = null;
        context.TextRequested += field => requested = field;
        window.Show();
        try
        {
            var fields = page.GetVisualDescendants().OfType<TextBox>().ToArray();
            var secret = fields.Single(f => AutomationProperties.GetName(f) == "IGDB client secret");
            secret.Focus();
            page.Handle(GamepadButtons.Accept);
            Assert.Same(secret, requested);
            Assert.Equal('●', requested!.PasswordChar);
            secret.Text = "test-secret";
            fields.Single(f => AutomationProperties.GetName(f) == "IGDB client ID").Text = "test-client";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("test-secret", app.Igdb.ClientSecret);
            Assert.Equal("test-client", app.Igdb.ClientId);
            var save = page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save credentials"));
            Assert.Same(app.Igdb.SaveCommand, save.Command);
            await app.Igdb.SaveCommand.ExecuteAsync(null);
            Assert.Contains("Metadata refresh queued", app.Igdb.Status);
            Assert.Empty(app.Igdb.ClientSecret);
            await app.Igdb.RemoveCommand.ExecuteAsync(null);
            Assert.Contains("active now", app.Igdb.Status);
            window.Content = null;
            Assert.Empty(app.Igdb.ClientSecret);
            window.Content = page;
            secret.Text = "another-test-secret";
            page.Dispose();
            Assert.Empty(app.Igdb.ClientSecret);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Presentation_switches_clear_unsaved_secret()
    {
        var shell = PreviewData.Shell;
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            shell.ApplicationSettings.Igdb.ClientSecret = "desktop-draft";
            window.ToggleFullscreen();
            Assert.Empty(shell.ApplicationSettings.Igdb.ClientSecret);
            shell.ApplicationSettings.Igdb.ClientSecret = "fullscreen-draft";
            window.ToggleFullscreen();
            Assert.Empty(shell.ApplicationSettings.Igdb.ClientSecret);
        }
        finally { shell.ApplicationSettings.Igdb.ClientSecret = ""; window.ExitFromTray(); }
    }

    private sealed class SettingsService : IIgdbSettingsService
    {
        public Task<IgdbSettingsSnapshot> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(new IgdbSettingsSnapshot("", false, false, false));
        public Task<IgdbSettingsSaveResult> SaveAsync(string clientId, string clientSecret)
            => Task.FromResult(string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
                ? IgdbSettingsSaveResult.MissingFields : IgdbSettingsSaveResult.Saved);
        public Task<bool> RemoveAsync() => Task.FromResult(false);
    }
}
