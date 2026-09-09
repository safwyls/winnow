using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ApplicationSettingsViewModelTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Tray_preferences_are_off_when_absent_and_round_trip()
    {
        var repository = new SettingsRepository(_db.Factory);
        var model = new ApplicationSettingsViewModel(repository, new FakeStartupRegistration());
        await model.LoadAsync();

        Assert.False(model.MinimizeToTray);
        Assert.False(model.CloseToTray);
        Assert.False(model.TrayIconWanted);

        model.MinimizeToTray = true;
        await model.PendingSave;
        model.CloseToTray = true;
        await model.PendingSave;

        var reloaded = new ApplicationSettingsViewModel(repository, new FakeStartupRegistration());
        await reloaded.LoadAsync();

        Assert.True(reloaded.MinimizeToTray);
        Assert.True(reloaded.CloseToTray);
        Assert.True(reloaded.TrayIconWanted);
    }

    [Fact]
    public async Task Fullscreen_startup_defaults_off_and_round_trips_both_values()
    {
        var repository = new SettingsRepository(_db.Factory);
        var model = new ApplicationSettingsViewModel(repository);
        await model.LoadAsync();
        Assert.False(model.StartInFullscreen);
        model.StartInFullscreen = true;
        await model.PendingSave;
        var reloaded = new ApplicationSettingsViewModel(repository);
        await reloaded.LoadAsync();
        Assert.True(reloaded.StartInFullscreen);
        Assert.False(reloaded.TrayIconWanted);
        reloaded.StartInFullscreen = false;
        await reloaded.PendingSave;
        await model.LoadAsync();
        Assert.False(model.StartInFullscreen);
    }

    [Fact]
    public async Task Startup_toggle_updates_the_operating_system_registration()
    {
        var startup = new FakeStartupRegistration();
        var model = new ApplicationSettingsViewModel(startup: startup);
        await model.LoadAsync();

        model.StartWithWindows = true;
        await model.PendingSave;
        Assert.True(startup.Enabled);

        model.StartWithWindows = false;
        await model.PendingSave;
        Assert.False(startup.Enabled);
    }

    [Fact]
    public async Task Startup_load_reflects_the_operating_system_registration()
    {
        var startup = new FakeStartupRegistration { Enabled = true };
        var model = new ApplicationSettingsViewModel(startup: startup);

        await model.LoadAsync();

        Assert.True(model.StartWithWindows);
    }

    [Fact]
    public async Task Failed_startup_change_reverts_the_toggle_and_reports_the_problem()
    {
        var startup = new FakeStartupRegistration { ThrowOnWrite = true };
        var model = new ApplicationSettingsViewModel(startup: startup);
        await model.LoadAsync();

        model.StartWithWindows = true;
        await model.PendingSave;

        Assert.False(model.StartWithWindows);
        Assert.Equal("Windows startup could not be changed.", model.Problem);
    }

    [Fact]
    public void Windows_registration_quotes_the_executable_and_starts_in_background()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registration = new WindowsStartupRegistration(@"C:\Program Files\Winnow\Winnow.exe");

        Assert.Equal(
            "\"C:\\Program Files\\Winnow\\Winnow.exe\" --background",
            registration.Command);
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsSupported { get; init; } = true;
        public bool Enabled { get; set; }
        public bool ThrowOnWrite { get; init; }

        public bool IsEnabled() => Enabled;

        public void SetEnabled(bool enabled)
        {
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("blocked");
            }

            Enabled = enabled;
        }
    }
}
