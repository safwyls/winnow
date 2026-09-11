using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.App.Services;

/// <summary>Plugin namespaces share the local database without sharing settings or credentials.</summary>
public sealed class PluginStorage(ISettingsStore settings, IMetadataCache cache, IConfiguration configuration) : IPluginStateStore
{
    public async ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken ct = default)
        => bool.TryParse(await settings.GetAsync(StorageKey(pluginId, "enabled", ""), ct), out var enabled) ? enabled : null;

    public async ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        => await settings.SetAsync(StorageKey(pluginId, "enabled", ""), enabled.ToString(), ct);

    public Task<string?> ReadSettingAsync(string id, string key, CancellationToken ct = default)
        => settings.GetAsync(StorageKey(id, "setting", key), ct);

    public Task WriteSettingAsync(string id, string key, string? value, CancellationToken ct = default)
        => settings.SetAsync(StorageKey(id, "setting", key), value, ct);

    public async Task<bool> HasStoredSecretAsync(string id, string key, CancellationToken ct = default)
        => await settings.GetAsync(StorageKey(id, "secret", key), ct) is not null;

    public async Task<string?> ReadSecretAsync(string id, string key, CancellationToken ct = default)
    {
        return await ReadStoredSecretAsync(id, key, ct)
            ?? configuration[$"Plugins:{id}:{key}"];
    }

    public async Task<string?> ReadStoredSecretAsync(string id, string key, CancellationToken ct = default)
        => Unprotect(await settings.GetAsync(StorageKey(id, "secret", key), ct), Entropy(id, key));

    public async Task WriteSecretAsync(string id, string key, string value, CancellationToken ct = default)
    {
        if (value.Length > 4096 || value.Any(char.IsControl)) throw new ArgumentException("Invalid credential.");
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Protected credential storage is unavailable.");
        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = Convert.ToBase64String(ProtectedData.Protect(plain, Entropy(id, key), DataProtectionScope.CurrentUser));
            await settings.SetAsync(StorageKey(id, "secret", key), encrypted, ct);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public Task RemoveSecretAsync(string id, string key, CancellationToken ct = default)
        => settings.RemoveAsync(StorageKey(id, "secret", key), ct);

    public async Task<PluginCacheEntry?> ReadCacheAsync(string id, string key, CancellationToken ct = default)
    {
        var stored = await cache.GetAsync("plugin:" + id, key, ct);
        if (stored?.PayloadJson is not { } json) return null;
        try
        {
            var result = JsonSerializer.Deserialize<PluginCacheEntry>(json);
            return result?.Payload is { Length: <= 2097152 } ? result : null;
        }
        catch (JsonException) { return null; }
    }

    public Task WriteCacheAsync(string id, string key, PluginCacheEntry entry, CancellationToken ct = default)
    {
        if (key.Length > 512 || entry.Payload.Length > 2 * 1024 * 1024) throw new ArgumentException("Plugin cache limit exceeded.");
        return cache.SetAsync("plugin:" + id, key, JsonSerializer.Serialize(entry), DateTime.UtcNow, ct);
    }

    internal static string StorageKey(string id, string kind, string key)
        => $"plugin.{id.Length}:{id}.{kind}.{key.Length}:{key}";
    internal static byte[] Entropy(string id, string key) => Encoding.UTF8.GetBytes(StorageKey(id, "secret.v1", key));
    internal static string? Unprotect(string? encrypted, byte[] entropy)
    {
        if (encrypted is null || !OperatingSystem.IsWindows()) return null;
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
}

public sealed class PluginContextFactory(PluginStorage storage, PluginHttpClient http) : IPluginContextFactory
{
    public IPluginContext Create(PluginManifest manifest) => new Context(manifest, storage, http.CreateScope(manifest));

    private sealed class Context(PluginManifest manifest, PluginStorage storage, IPluginHttp http)
        : IPluginContext, IPluginSettings, IPluginSecrets, IPluginCache
    {
        public string PluginId => manifest.Id;
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => http;

        private void Check(string key, bool secret)
        {
            if (!manifest.Settings.Any(s => s.Key == key && s.Secret == secret)) throw new ArgumentException("Undeclared setting.");
        }
        async ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken ct)
        { Check(key, false); return await storage.ReadSettingAsync(PluginId, key, ct); }
        async ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken ct)
        { Check(key, false); if (value?.Length > 4096) throw new ArgumentException("Setting too long."); await storage.WriteSettingAsync(PluginId, key, value, ct); }
        async ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken ct)
        { Check(key, true); return await storage.ReadSecretAsync(PluginId, key, ct); }
        async ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken ct)
            => await storage.ReadCacheAsync(PluginId, key, ct);
        async ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken ct)
            => await storage.WriteCacheAsync(PluginId, key, entry, ct);
    }
}
