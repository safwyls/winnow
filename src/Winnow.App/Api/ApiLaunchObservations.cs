using Microsoft.Extensions.Hosting;
using Winnow.Api.Client;
using Winnow.Monitor;

namespace Winnow.App.Api;

public sealed class ApiLaunchObservations(WinnowApiClient api) : BackgroundService, ILaunchObservations
{
    public TimeSpan Window => TimeSpan.FromSeconds(90);
    public event EventHandler<LaunchObserved>? Observed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var change in api.WatchEventsAsync(ct: stoppingToken))
                if (change.Kind == "launch.observed" && long.TryParse(change.Resource, out var ownership))
                    Observed?.Invoke(this, new(ownership, change.OccurredAt.UtcDateTime));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
