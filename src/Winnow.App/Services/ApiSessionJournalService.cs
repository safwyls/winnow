using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;

namespace Winnow.App.Services;

public sealed class ApiSessionJournalService(WinnowApiClient api) : ISessionJournalService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _watch;
    public event EventHandler<EndedSession>? SessionEnded;
    public bool PromptEnabled { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        PromptEnabled = (await api.GetAsync<JournalPreferences>("journal/preferences", ct)).PromptAfterPlay;
        _watch ??= WatchAsync(_lifetime.Token);
    }
    public async Task SetPromptEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await api.SendAsync(HttpMethod.Put, "journal/preferences", new JournalPreferences(enabled), ct);
        PromptEnabled = enabled;
    }
    public async Task SaveAsync(long sessionId, string? note, int? rating, CancellationToken ct = default)
    {
        // A post-session prompt begins with no saved note. Another frontend's answer must win over this stale prompt.
        await new DetailsClient(api).SaveJournalAsync(sessionId, new(
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(), rating is >= 1 and <= 5 ? rating : null,
            JournalRevision.For(sessionId, null, null)), ct);
    }
    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var change in api.WatchEventsAsync(ct: ct))
            {
                try
                {
                    if (change.Kind == "preferences.changed" || change.Kind == "resync-required")
                        PromptEnabled = (await api.GetAsync<JournalPreferences>("journal/preferences", ct)).PromptAfterPlay;
                    if (!PromptEnabled || change.Kind != "session.ended" || change.Resource is not { } resource
                        || !resource.StartsWith("sessions/", StringComparison.Ordinal)
                        || !long.TryParse(resource[9..], out var id)) continue;
                    var session = await api.GetAsync<SessionPromptResponse>($"sessions/{id}/prompt", ct);
                    SessionEnded?.Invoke(this, new(session.SessionId, session.OwnershipId, session.DurationSeconds));
                }
                catch (HttpRequestException) { /* A restart invalidates this transient prompt; the stream reconnects. */ }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
