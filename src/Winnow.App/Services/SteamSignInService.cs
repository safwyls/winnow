using Winnow.Api.Contracts.Connections;
using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.Services;

/// <summary>The frontend owns the browser; the independent API owns the stored session.</summary>
public sealed class SteamSignInService(ISteamSignInSession session, IStoreConnectionApi api) : ISteamSignInService
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) => session.IsAvailableAsync(ct);
    public async ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default) => (await api.GetAsync(ct)).SteamHealth;
    public Task SignOutAsync(CancellationToken ct = default) => api.SignOutSteamAsync(ct);

    public async Task<SteamSignInReport> SignInAsync(SteamSignInRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConsentGranted)
            return new(SteamSignInOutcome.Cancelled, "Sign-in cancelled.", null, null, false, false, await GetHealthAsync(ct), null);
        var challenge = await api.BeginSteamAsync(request, ct);
        try
        {
            var result = await session.SignInAsync(challenge.Request, ct);
            if (result.HasSession) return await api.CompleteSteamAsync(challenge, result, ct);
            return new(result.Outcome, result.Detail, null, null, false, false, await GetHealthAsync(ct), result.Pages);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await api.CancelAsync(challenge.AttemptId, cleanup.Token); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException) { }
        }
    }
}
