namespace Winnow.Api.Contracts.Connections;

public sealed record SaveIgdbCredentials(string ClientId, string ClientSecret)
{
    public override string ToString() => "SaveIgdbCredentials(credentials redacted)";
}
public sealed record PluginSettingsValues(IReadOnlyDictionary<string, string> Values);
public sealed record SetPluginEnabled(bool Enabled);
public sealed record PluginSignInRequest(string ClientId);
public sealed record PluginSignInAttempt(string ClientId, string AttemptId);
public sealed record PluginDirectoryResponse(string Directory);
public sealed record PluginChallengeResponse(Winnow.PluginSdk.PluginSignInChallenge? Challenge);
public sealed record SetAccountVisibility(bool OwnAccountOnly);
