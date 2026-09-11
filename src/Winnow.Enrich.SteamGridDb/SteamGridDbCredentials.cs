using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Winnow.Data;

namespace Winnow.Enrich.SteamGridDb;

public interface ISteamGridDbKeyProvider
{
    ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default);
}

public sealed record SteamGridDbCredentialStatus(bool HasStoredKey, bool IsConfigured, bool CanSave, string? Source);

public interface ISteamGridDbSettingsStore
{
    Task<SteamGridDbCredentialStatus> GetStatusAsync(CancellationToken ct = default);
    Task<bool> SaveAsync(string apiKey, CancellationToken ct = default);
    Task RemoveAsync(CancellationToken ct = default);
}

public interface ISteamGridDbSecretProtector
{
    bool IsAvailable { get; }
    string? Protect(string plaintext);
    string? Unprotect(string ciphertext);
}

/// <summary>DPAPI current-user protection; unsupported hosts refuse persisted secrets.</summary>
public sealed class SteamGridDbSecretProtector : ISteamGridDbSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Winnow.SteamGridDb.ApiKey.v1");
    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string? Unprotect(string ciphertext)
    {
        if (!OperatingSystem.IsWindows()) return null;
        byte[]? bytes = null;
        try
        {
            bytes = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
}

public sealed class SteamGridDbSettingsStore(
    ISqliteConnectionFactory factory,
    ISteamGridDbSecretProtector protector,
    IConfiguration configuration) : ISteamGridDbSettingsStore, ISteamGridDbKeyProvider
{
    public const string SettingsKey = "steamgriddb.api_key.protected";

    public async ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        var stored = await ReadStoredAsync(ct);
        return Normalize(stored is null ? null : protector.Unprotect(stored)) ?? Fallback();
    }

    public async Task<SteamGridDbCredentialStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var stored = await ReadStoredAsync(ct);
        var readable = stored is not null && Normalize(protector.Unprotect(stored)) is not null;
        var fallback = Fallback();
        return new(stored is not null, readable || fallback is not null, protector.IsAvailable,
            readable ? "saved" : fallback is not null ? "environment or configuration" : null);
    }

    public async Task<bool> SaveAsync(string apiKey, CancellationToken ct = default)
    {
        var normalized = Normalize(apiKey);
        if (normalized is null || !protector.IsAvailable) return false;
        var encrypted = protector.Protect(normalized);
        if (string.IsNullOrEmpty(encrypted)) return false;
        using var lease = factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO settings(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
            new { key = SettingsKey, value = encrypted }, lease.Transaction, cancellationToken: ct));
        return true;
    }

    public async Task RemoveAsync(CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM settings WHERE key=@key;",
            new { key = SettingsKey }, lease.Transaction, cancellationToken: ct));
    }

    private async Task<string?> ReadStoredAsync(CancellationToken ct)
    {
        using var lease = factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT value FROM settings WHERE key=@key;", new { key = SettingsKey }, lease.Transaction, cancellationToken: ct));
    }

    private string? Fallback() => Normalize(configuration["SteamGridDb:ApiKey"])
        ?? Normalize(Environment.GetEnvironmentVariable("SteamGridDb__ApiKey"));

    private static string? Normalize(string? key)
        => string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Any(char.IsControl) || key.Trim().Any(char.IsWhiteSpace)
            ? null : key.Trim();
}
