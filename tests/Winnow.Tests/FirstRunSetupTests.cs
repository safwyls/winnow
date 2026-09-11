using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class FirstRunSetupTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private ISettingsRepository Settings => new SettingsRepository(_db.Factory);
    public void Dispose() => _db.Dispose();
    private static FirstRunSetupViewModel Model(FirstRunSetupService progress,
        ApplicationSettingsViewModel? app = null, LibrarySettingsViewModel? library = null)
        => new(DetachedStores.Create(), DetachedAppearance.Create(), app ?? new ApplicationSettingsViewModel(),
            library ?? new LibrarySettingsViewModel(), progress);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Only_new_normal_install_opens_automatically(bool existed, bool suppressed, bool opens)
    {
        var progress = new FirstRunSetupService(Settings);
        await progress.InitializeAsync(existed, suppressed);
        var model = Model(progress);
        await model.LoadAsync();
        Assert.Equal(opens, model.IsOpen);
        Assert.Equal(opens ? "0" : "done", await Settings.GetAsync(FirstRunSetupService.ProgressKey));
    }

    [Fact]
    public async Task Sample_run_does_not_show_or_destroy_an_existing_unfinished_cursor()
    {
        await Settings.SetAsync(FirstRunSetupService.ProgressKey, "2");
        var sample = new FirstRunSetupService(Settings);
        await sample.InitializeAsync(true, true);
        Assert.Null(await sample.LoadAsync());
        Assert.Equal(2, await new FirstRunSetupService(Settings).LoadAsync());
    }

    [Fact]
    public async Task Existing_database_on_second_launch_resumes_cursor_and_does_not_reset_it()
    {
        var initial = new FirstRunSetupService(Settings);
        await initial.InitializeAsync(false, false);
        await initial.SaveAsync((int)FirstRunStep.Gog);
        var second = new FirstRunSetupService(Settings);
        await second.InitializeAsync(true, false);
        var model = Model(second);
        await model.LoadAsync();
        Assert.True(model.IsOpen);
        Assert.Equal(FirstRunStep.Gog, model.Step);
        Assert.Equal(StorePlatform.Gog, model.Stores.SelectedPlatform);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Epic, model.Step);
        Assert.Equal("3", await Settings.GetAsync(FirstRunSetupService.ProgressKey));
    }

    [Fact]
    public async Task Every_optional_step_can_be_skipped_without_credentials_and_completion_persists()
    {
        var progress = new FirstRunSetupService(Settings);
        await progress.InitializeAsync(false, false);
        var model = Model(progress);
        await model.LoadAsync();
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Welcome, model.Step);
        await model.NextCommand.ExecuteAsync(null);
        model.Application.Igdb.ClientSecret = "unsaved-test-secret";
        model.Stores.SteamApiKeyInput = "unsaved-test-key";
        foreach (var step in Enum.GetValues<FirstRunStep>().Where(s => s is > FirstRunStep.Welcome and < FirstRunStep.Ready))
        {
            Assert.Equal(step, model.Step);
            Assert.True(model.CanSkipStep);
            await model.SkipStepCommand.ExecuteAsync(null);
        }
        Assert.Empty(model.Application.Igdb.ClientSecret);
        Assert.Empty(model.Stores.SteamApiKeyInput);
        Assert.Null(await Settings.GetAsync("igdb.client_id"));
        Assert.Equal(FirstRunStep.Ready, model.Step);
        await model.NextCommand.ExecuteAsync(null);
        Assert.False(model.IsOpen);
        Assert.Null(await new FirstRunSetupService(Settings).LoadAsync());
    }

    [Fact]
    public async Task Skip_all_preserves_saved_preferences_and_application_entry_reopens()
    {
        var app = new ApplicationSettingsViewModel(Settings);
        var model = Model(new FirstRunSetupService(Settings), app);
        app.OpenSetupCommand.Execute(null);
        await model.ReopenCommand.ExecutionTask!;
        app.CloseToTray = true;
        await app.PendingSave;
        await model.SkipAllCommand.ExecuteAsync(null);
        Assert.False(model.IsOpen);
        Assert.Equal("true", await Settings.GetAsync("application.close_to_tray"));
        app.OpenSetupCommand.Execute(null);
        await model.ReopenCommand.ExecutionTask!;
        Assert.True(model.IsOpen);
        Assert.Equal(FirstRunStep.Welcome, model.Step);
        Assert.True(app.CloseToTray);
    }

    [Fact]
    public async Task Progress_failure_keeps_wizard_open_and_retry_can_complete()
    {
        var settings = new SwitchableSettings(Settings);
        var model = Model(new FirstRunSetupService(settings));
        await model.ReopenCommand.ExecuteAsync(null);
        settings.FailWrites = true;
        await model.SkipAllCommand.ExecuteAsync(null);
        Assert.True(model.IsOpen);
        Assert.Contains("Could not save setup progress", model.Problem);
        Assert.Equal("0", await Settings.GetAsync(FirstRunSetupService.ProgressKey));
        settings.FailWrites = false;
        await model.SkipAllCommand.ExecuteAsync(null);
        Assert.False(model.IsOpen);
        Assert.Null(model.Problem);
    }

    [Fact]
    public async Task Failed_preference_does_not_trap_skip_or_back_navigation()
    {
        var settings = new SwitchableSettings(Settings) { FailWrites = true };
        var library = new LibrarySettingsViewModel(settings: settings);
        var model = Model(new FirstRunSetupService(Settings), library: library);
        await model.ReopenCommand.ExecuteAsync(null);
        await model.NextCommand.ExecuteAsync(null);
        library.ShowExplicitContent = true;
        await Assert.ThrowsAsync<IOException>(() => library.PendingSave);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Igdb, model.Step);
        Assert.Contains("A preference could not be saved", model.Problem);
        await model.SkipStepCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Steam, model.Step);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(FirstRunStep.Igdb, model.Step);
        await model.SkipAllCommand.ExecuteAsync(null);
        Assert.False(model.IsOpen);
    }

    [Fact]
    public async Task Reopen_write_failure_still_displays_recovery_and_retains_previous_completion()
    {
        await Settings.SetAsync(FirstRunSetupService.ProgressKey, "done");
        var model = Model(new FirstRunSetupService(new SwitchableSettings(Settings) { FailWrites = true }));
        await model.ReopenCommand.ExecuteAsync(null);
        Assert.True(model.IsOpen);
        Assert.Equal(FirstRunStep.Welcome, model.Step);
        Assert.NotNull(model.Problem);
        Assert.Equal("done", await Settings.GetAsync(FirstRunSetupService.ProgressKey));
    }

    [Fact]
    public async Task Corrupt_cursor_restarts_setup_without_touching_other_settings()
    {
        await Settings.SetAsync(FirstRunSetupService.ProgressKey, "1000");
        await Settings.SetAsync("appearance.theme", "test-theme");
        var progress = new FirstRunSetupService(Settings);
        await progress.InitializeAsync(true, false);
        Assert.Equal(0, await progress.LoadAsync());
        Assert.Equal("test-theme", await Settings.GetAsync("appearance.theme"));
    }

    private sealed class SwitchableSettings(ISettingsRepository inner) : ISettingsRepository
    {
        public bool FailWrites { get; set; }
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public Task SetAsync(string key, string value, CancellationToken ct = default)
            => FailWrites ? Task.FromException(new IOException("Test write refusal")) : inner.SetAsync(key, value, ct);
    }
}
