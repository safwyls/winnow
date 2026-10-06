using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Winnow.Api.Contracts.Companion;
using Winnow.Core.Repositories;

namespace Winnow.Application.Companion;

/// <summary>
/// Paired phones, kept in settings. Only a SHA-256 of each device token is stored: the
/// token itself leaves Winnow once, in the pairing response, and is never logged.
/// </summary>
public sealed class CompanionDevices(ISettingsRepository settings, TimeProvider clock)
{
    public const string DevicesKey = "companion.devices.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record Stored(string Id, string Name, string TokenHash, DateTime PairedAt, DateTime? LastSyncAt);

    public async Task<IReadOnlyList<CompanionDevice>> ListAsync(CancellationToken ct = default)
        => [.. (await ReadAsync(ct)).Select(x => new CompanionDevice(x.Id, x.Name, x.PairedAt, x.LastSyncAt))];

    /// <summary>Registers a phone and returns its device ID and the token to give it.</summary>
    public async Task<(string Id, string Token)> AddAsync(string name, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        await MutateAsync(list => [.. list, new Stored(id, Clean(name), Hash(token), clock.GetUtcNow().UtcDateTime, null)], ct);
        return (id, token);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken ct = default)
        => MutateAsync(list => [.. list.Where(x => x.Id != id)], ct);

    /// <summary>The device the token belongs to, compared in constant time; null when none.
    /// A match records the sync time.</summary>
    public async Task<string?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(token));
        string? match = null;
        foreach (var device in await ReadAsync(ct))
            if (CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(device.TokenHash))) match = device.Id;
        if (match is not null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await MutateAsync(list => [.. list.Select(x => x.Id == match ? x with { LastSyncAt = now } : x)], ct);
        }
        return match;
    }

    private async Task<bool> MutateAsync(Func<IReadOnlyList<Stored>, IReadOnlyList<Stored>> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var before = await ReadAsync(ct);
            var after = change(before);
            await settings.SetAsync(DevicesKey, JsonSerializer.Serialize(after, Json), ct);
            return after.Count != before.Count || !after.SequenceEqual(before);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<Stored>> ReadAsync(CancellationToken ct)
    {
        var json = await settings.GetAsync(DevicesKey, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Stored[]>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A display name only: trimmed, single-line, at most 60 characters.</summary>
    private static string Clean(string name)
    {
        var line = new string([.. (name ?? "").Where(c => !char.IsControl(c))]).Trim();
        return line.Length == 0 ? "Phone" : line[..Math.Min(line.Length, 60)];
    }
}
