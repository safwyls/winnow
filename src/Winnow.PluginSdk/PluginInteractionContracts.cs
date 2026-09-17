namespace Winnow.PluginSdk;

/// <summary>Each call is bounded; the host owns the user wait and polls separate short operations.</summary>
public interface IPluginAccount : IPlugin
{
    Task<PluginAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default);
    Task<PluginSignInChallenge?> BeginSignInAsync(CancellationToken cancellationToken = default);
    Task<PluginSignInResult> PollSignInAsync(string attemptId, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task CancelSignInAsync(string attemptId, CancellationToken cancellationToken = default);
}

public sealed record PluginAccountStatus(bool Connected, string Message);

/// <summary>AttemptId is opaque; it must never contain a device code or token. Only UserCode is displayed.</summary>
public sealed record PluginSignInChallenge(string AttemptId, string VerificationUrl, string UserCode,
    DateTimeOffset ExpiresAt, int PollIntervalSeconds);

public enum PluginSignInState { Pending, SlowDown, Connected, Failed }
public sealed record PluginSignInResult(PluginSignInState State, string Message);

public enum PluginGameActionKind { Play, OpenStore }

/// <summary>The provider resolves a known SourceId to a current target; the host never executes provider command text.</summary>
public interface IPluginGameActions : IPlugin
{
    Task<PluginGameActionResult> ExecuteGameActionAsync(string sourceId, PluginGameActionKind action,
        CancellationToken cancellationToken = default);
}

public sealed record PluginGameActionResult(bool HandedOff);
