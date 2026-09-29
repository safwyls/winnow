using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class SetupParityTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    public async Task Only_new_normal_install_opens_automatically(bool existed, bool sample, int? expected)
    {
        await using var host = await Host.Start(existed, sample);
        Assert.Equal(expected, (await host.Api.GetAsync<SetupProgress>("setup")).Step);
        Assert.Equal(expected is null ? "done" : "0", await host.Settings.GetAsync(FirstRunSetupService.ProgressKey));
    }

    [Fact]
    public async Task Existing_database_on_second_launch_resumes_cursor_and_does_not_reset_it()
    {
        await using var host = await Host.Start();
        await host.Api.SendAsync(HttpMethod.Put, "setup", new SetSetupProgress(4));
        await host.Restart();
        Assert.Equal(4, (await host.Api.GetAsync<SetupProgress>("setup")).Step);
        await host.Api.SendAsync(HttpMethod.Put, "setup", new SetSetupProgress(3));
        Assert.Equal("3", await host.Settings.GetAsync(FirstRunSetupService.ProgressKey));
    }

    [Fact]
    public async Task Completion_and_replay_persist_across_restart_without_resetting_saved_preferences()
    {
        await using var host = await Host.Start();
        await host.Settings.SetAsync("application.close_to_tray", "true");
        await host.Settings.SetAsync("appearance.theme", "fixture-theme");
        await host.Api.SendAsync(HttpMethod.Put, "setup", new SetSetupProgress(null));
        await host.Restart();
        Assert.Null((await host.Api.GetAsync<SetupProgress>("setup")).Step);
        await host.Api.SendAsync(HttpMethod.Put, "setup", new SetSetupProgress(0));
        await host.Restart();
        Assert.Equal(0, (await host.Api.GetAsync<SetupProgress>("setup")).Step);
        Assert.Equal("true", await host.Settings.GetAsync("application.close_to_tray"));
        Assert.Equal("fixture-theme", await host.Settings.GetAsync("appearance.theme"));
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-setup-tests", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public ISettingsRepository Settings => _app.Services.GetRequiredService<ISettingsRepository>();
        public static async Task<Host> Start(bool existed = false, bool sample = false)
        {
            var host = new Host();
            Directory.CreateDirectory(host._directory);
            try
            {
                if (existed) await File.WriteAllBytesAsync(Path.Combine(host._directory, "winnow.db"), []);
                await host.Open(sample);
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        private async Task Open(bool sample = false)
        {
            _app = BackendApplication.Build(["--data-dir", _directory, "--no-sync", .. sample ? new[] { "--seed-sample" } : Array.Empty<string>()]);
            await _app.StartAsync();
            Api = WinnowApiClient.Attach(_directory);
        }
        public async Task Restart()
        {
            Api.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await Open();
        }
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
