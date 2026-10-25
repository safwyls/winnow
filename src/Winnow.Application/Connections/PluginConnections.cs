using Winnow.App.Services;
using Winnow.PluginSdk;

namespace Winnow.Application.Connections;

/// <summary>Frontends can poll or cancel only challenges they initiated.</summary>
public sealed class PluginConnections(IPluginSettingsBackend backend, TimeProvider clock)
{
    private sealed record Attempt(string ClientId, string PluginId, string ProviderAttemptId, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Attempt> _attempts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PluginSignInChallenge?> BeginAsync(string pluginId, string clientId, CancellationToken ct)
    {
        ValidateClient(clientId);
        await _gate.WaitAsync(ct);
        try
        {
            await ExpireAsync(ct);
            if (_attempts.Count >= 256 || _attempts.Values.Count(a => a.ClientId == clientId) >= 8)
                throw new ApplicationConflictException("Too many pending sign-in attempts. Cancel an existing attempt first.");
            var challenge = await backend.BeginSignInAsync(pluginId, ct);
            if (challenge is null) return null;
            var id = Guid.NewGuid().ToString("N");
            _attempts.Add(id, new(clientId, pluginId, challenge.AttemptId, challenge.ExpiresAt));
            return challenge with { AttemptId = id };
        }
        finally { _gate.Release(); }
    }

    public async Task<PluginSignInResult> PollAsync(string pluginId, string clientId, string attemptId, CancellationToken ct)
    {
        ValidateClient(clientId);
        await _gate.WaitAsync(ct);
        try
        {
            await ExpireAsync(ct);
            var attempt = Find(pluginId, clientId, attemptId);
            var result = await backend.PollSignInAsync(pluginId, attempt.ProviderAttemptId, ct);
            if (result.State is PluginSignInState.Connected or PluginSignInState.Failed)
                _attempts.Remove(attemptId);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync(string pluginId, string clientId, string attemptId, CancellationToken ct)
    {
        ValidateClient(clientId);
        await _gate.WaitAsync(ct);
        try
        {
            if (!_attempts.ContainsKey(attemptId)) return;
            var attempt = Find(pluginId, clientId, attemptId);
            await backend.CancelSignInAsync(pluginId, attempt.ProviderAttemptId, ct);
            _attempts.Remove(attemptId);
        }
        finally { _gate.Release(); }
    }

    private Attempt Find(string pluginId, string clientId, string attemptId)
    {
        if (!_attempts.TryGetValue(attemptId, out var attempt) || attempt.PluginId != pluginId || attempt.ClientId != clientId)
            throw new ApplicationNotFoundException("The sign-in attempt is unavailable or expired.");
        return attempt;
    }

    private async Task ExpireAsync(CancellationToken ct)
    {
        foreach (var (id, attempt) in _attempts.Where(item => item.Value.ExpiresAt <= clock.GetUtcNow()).ToArray())
        {
            _attempts.Remove(id);
            await backend.CancelSignInAsync(attempt.PluginId, attempt.ProviderAttemptId, ct);
        }
    }

    private static void ValidateClient(string clientId)
    {
        if (!Guid.TryParseExact(clientId, "N", out _)) throw new ArgumentException("A valid frontend instance ID is required.");
    }
}
