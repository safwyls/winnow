using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.Services;

public interface ISteamSignInService
{
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);
    ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default);
    Task<SteamSignInReport> SignInAsync(SteamSignInRequest request, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
}
