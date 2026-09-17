using Winnow.PluginSdk;

namespace Winnow.App.Services;

/// <summary>Presentation snapshots never contain stored secret values.</summary>
public sealed record PluginSettingSnapshot(
    string Key, string Label, string? Description, bool IsSecret, bool IsRequired,
    string? Value, bool HasStoredSecret, string? SetupUrl = null, bool IsBoolean = false);

public sealed record PluginSettingsSnapshot(
    string Id, string Name, string Description, string Version, string Capabilities,
    bool Enabled, bool IsLoaded, bool RestartRequired, string Status,
    IReadOnlyList<PluginSettingSnapshot> Settings, string? WebsiteUrl = null, bool CanConfigure = true,
    bool HasAccount = false, bool AccountConnected = false, IReadOnlyList<string>? AccountHosts = null);

public interface IPluginSettingsBackend
{
    string UserPluginDirectory { get; }
    Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
    Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default);
    Task RefreshAsync(string pluginId, CancellationToken ct = default);
    Task<PluginSignInChallenge?> BeginSignInAsync(string pluginId, CancellationToken ct = default)
        => Task.FromResult<PluginSignInChallenge?>(null);
    Task<PluginSignInResult> PollSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
        => Task.FromResult(new PluginSignInResult(PluginSignInState.Failed, string.Empty));
    Task SignOutAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    Task CancelSignInAsync(string pluginId, string attemptId, CancellationToken ct = default) => Task.CompletedTask;
}

internal static class PluginSignInValidation
{
    public static bool IsValid(PluginSignInChallenge challenge, IReadOnlyList<string> hosts, DateTimeOffset now)
        => challenge.AttemptId is { Length: > 0 and <= 256 } && !challenge.AttemptId.Any(char.IsControl)
            && challenge.UserCode is { Length: > 0 and <= 64 }
            && challenge.UserCode.Any(char.IsAsciiLetterOrDigit)
            && challenge.UserCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or ' ')
            && challenge.PollIntervalSeconds is >= 1 and <= 60
            && challenge.ExpiresAt > now && challenge.ExpiresAt <= now.AddHours(1)
            && VerificationUri(challenge.VerificationUrl, hosts) is not null;

    public static Uri? VerificationUri(string? url, IReadOnlyList<string> hosts)
        => url is { Length: > 0 and <= 2048 } && !url.Any(char.IsControl)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
            && hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase) ? uri : null;
}
