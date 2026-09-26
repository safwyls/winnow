using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Enrich.Igdb.Storage;

namespace Winnow.App.Api;

/// <summary>The artwork editor can reach only its public source-order preference.</summary>
internal sealed class ArtworkPreferenceStore(ApiPresentationSettings settings) : ISettingsStore
{
    private static void Validate(string key)
    {
        if (key != ArtworkPreferences.SettingKey) throw new ArgumentException("Only artwork source order is available to this frontend adapter.", nameof(key));
    }
    public Task<string?> GetAsync(string key, CancellationToken ct = default) { Validate(key); return settings.GetAsync(key, ct); }
    public Task SetAsync(string key, string? value, CancellationToken ct = default) { Validate(key); return settings.SetAsync(key, value ?? "", ct); }
    public Task RemoveAsync(string key, CancellationToken ct = default) { Validate(key); return settings.SetAsync(key, "", ct); }
}
