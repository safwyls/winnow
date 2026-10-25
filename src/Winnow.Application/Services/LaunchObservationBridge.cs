using Microsoft.Extensions.Hosting;
using Winnow.Application;
using Winnow.Monitor;

namespace Winnow.App.Services;

public sealed class LaunchObservationBridge(LaunchIntents intents, IApplicationChangePublisher changes) : IHostedService
{
    private void Observed(object? sender, LaunchObserved observation)
        => changes.Publish("launch.observed", observation.OwnershipId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public Task StartAsync(CancellationToken ct) { intents.Observed += Observed; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { intents.Observed -= Observed; return Task.CompletedTask; }
}
