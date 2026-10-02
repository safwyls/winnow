using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Resolve;

namespace Winnow.App.Api;

internal sealed class ApiAccountVisibility(WinnowApiClient api) : IAccountVisibility
{
    public Task<AccountVisibilityState> GetAsync(CancellationToken ct = default)
        => api.GetAsync<AccountVisibilityState>("connections/account-visibility", ct);
    public Task SetOwnAccountOnlyAsync(bool ownAccountOnly, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Put, "connections/account-visibility", new { ownAccountOnly }, ct);
}

internal sealed class ApiGameRefetch(WinnowApiClient api) : IGameRefetch
{
    public Task<GameRefetchResult> RefetchAsync(long workId, CancellationToken ct = default)
        => api.SendAsync<object?, GameRefetchResult>(HttpMethod.Post, $"games/{workId}/refetch", null, ct: ct);
}

internal sealed class ApiMergeSuggestionRefresh(WinnowApiClient api) : IMergeSuggestionRefresh
{
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    public async Task<SoftMatchSweepReport> RefreshAsync(CancellationToken ct = default)
    {
        var report = await api.SendAsync<object?, SoftMatchSweepReport>(HttpMethod.Post, "identity/review/refresh", null, ct: ct);
        Interlocked.Increment(ref _revision);
        return report;
    }
}
