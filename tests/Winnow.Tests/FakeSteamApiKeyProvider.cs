using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.Tests;

/// <summary>
/// A Steam Web API key provider holding one fixed value, or none.
///
/// <para>Shared because three test classes now need one:
/// <see cref="SteamPlaytimeBackfillService"/> takes the provider as a required
/// dependency, since the key fingerprint is the only thing standing between a
/// freshly pasted key and the previous owner's account identity.</para>
/// </summary>
internal sealed class FakeSteamApiKeyProvider : ISteamApiKeyProvider
{
    /// <summary>The key most tests do not care about the value of.</summary>
    internal const string DefaultKey = "the-one-key";

    private readonly SteamApiKey? _key;

    internal FakeSteamApiKeyProvider(string? value = DefaultKey)
        => _key = SteamApiKey.TryCreate(value, "test");

    /// <summary>Whether a key is present at all. Null models an unconfigured install.</summary>
    internal bool HasKey => _key is not null;

    /// <summary>
    /// The digest the service stores beside the confirmed account, so a test can
    /// seed the "key never changed" state without reaching into the service.
    /// Never the key itself — that is the whole point of storing a digest.
    /// </summary>
    internal static string HashOf(string key)
        => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key)));

    public ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default)
        => ValueTask.FromResult(_key);

    public void Invalidate()
    {
    }
}
