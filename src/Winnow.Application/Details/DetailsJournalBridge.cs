using Microsoft.Extensions.Hosting;
using Winnow.App.Services;

namespace Winnow.Application.Details;

public sealed class DetailsJournalBridge(SessionJournalService journal, IApplicationChangePublisher changes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await journal.LoadAsync(ct);
        journal.SessionEnded += OnSessionEnded;
    }
    public Task StopAsync(CancellationToken ct)
    {
        journal.SessionEnded -= OnSessionEnded;
        return Task.CompletedTask;
    }
    private void OnSessionEnded(object? sender, EndedSession session)
        => changes.Publish("session.ended", $"sessions/{session.SessionId}");
}
