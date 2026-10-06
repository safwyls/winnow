using Winnow.Api.Contracts.Companion;

namespace Winnow.Api.Client;

public sealed class ApiCompanionSettings(WinnowApiClient api) : ICompanionSettingsService
{
    public Task<CompanionStatus> StatusAsync(CancellationToken ct = default) => api.GetAsync<CompanionStatus>("companion", ct);
    public Task<CompanionStatus> SetEnabledAsync(bool enabled, CancellationToken ct = default) =>
        api.SendAsync<SetCompanionEnabled, CompanionStatus>(HttpMethod.Put, "companion/enabled", new(enabled), ct: ct);
    public Task<CompanionStatus> OpenPairingAsync(CancellationToken ct = default) =>
        api.SendAsync<object?, CompanionStatus>(HttpMethod.Post, "companion/pairing", null, ct: ct);
    public Task<CompanionStatus> ClosePairingAsync(CancellationToken ct = default) =>
        api.SendAsync<object?, CompanionStatus>(HttpMethod.Delete, "companion/pairing", null, ct: ct);
    public Task RemoveDeviceAsync(string id, CancellationToken ct = default) =>
        api.SendAsync<object?>(HttpMethod.Delete, "companion/devices/" + Uri.EscapeDataString(id), null, ct);
}
