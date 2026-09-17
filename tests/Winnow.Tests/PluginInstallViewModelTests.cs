using Winnow.App.Services;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginInstallViewModelTests
{
    [Fact]
    public async Task Browser_request_waits_for_manual_retry_instead_of_being_dropped()
    {
        var installer = new Installer();
        var opened = new List<string>();
        var model = new PluginInstallViewModel(installer, null, id => { opened.Add(id); return Task.CompletedTask; });
        await model.InstallAsync(new("psn", "v0.2.0"));
        Assert.True(model.CanRetry);
        var retry = model.RetryCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        var browser = model.InstallAsync(new("xbox", "v0.2.0"));
        Assert.False(browser.IsCompleted);
        Assert.Equal(["psn", "psn"], installer.Requests);
        installer.Retry.TrySetResult(new(PluginInstallOutcome.Installed, "psn", "Installed."));
        await Task.WhenAll(retry, browser);
        Assert.Equal(["psn", "psn", "xbox"], installer.Requests);
        Assert.Equal(["psn", "xbox"], opened);
        Assert.Equal("xbox", model.PluginId);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Cancellation_clears_busy_state_and_offers_retry()
    {
        using var stop = new CancellationTokenSource();
        var model = new PluginInstallViewModel(new CancellingInstaller(), null, _ => Task.CompletedTask);
        var running = model.InstallAsync(new("psn", "v0.2.0"), stop.Token);
        stop.Cancel();
        await running;
        Assert.False(model.IsBusy);
        Assert.True(model.RetryCommand.CanExecute(null));
        Assert.Contains("cancelled", model.Status);
    }

    private sealed class Installer : IOfficialPluginInstaller
    {
        public List<string> Requests { get; } = [];
        public TaskCompletionSource<PluginInstallResult> Retry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PluginInstallResult> InstallAsync(PluginInstallRequest request, IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default)
        {
            Requests.Add(request.PluginId);
            return Requests.Count switch
            {
                1 => Task.FromResult(new PluginInstallResult(PluginInstallOutcome.Failed, request.PluginId, "Try again.")),
                2 => Retry.Task,
                _ => Task.FromResult(new PluginInstallResult(PluginInstallOutcome.Installed, request.PluginId, "Installed.")),
            };
        }
    }
    private sealed class CancellingInstaller : IOfficialPluginInstaller
    {
        public async Task<PluginInstallResult> InstallAsync(PluginInstallRequest request, IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }
}
