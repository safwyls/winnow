using Winnow.Enrich.SteamWeb;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.App.Services;

/// <summary>
/// Bridges the Stores panel's questions to the ingest modules that can answer
/// them. Every dependency is optional; a missing one degrades into copy, not a crash.
/// </summary>
public sealed class StoreConnections : IStoreConnections
{
    private readonly EpicSignInService? _epic;

    /// <summary>
    /// Read for the display name only, and read directly rather than through a
    /// refresh, because <c>IEpicTokenProvider.GetAsync</c> would go to Epic to
    /// renew a spent access token — a network call on the path that draws a
    /// status line. The store is the local half of the same fact.
    /// </summary>
    private readonly IEpicTokenStore? _epicSessions;

    private readonly ISteamWebApiClient? _steam;

    public StoreConnections(
        EpicSignInService? epic = null,
        IEpicTokenStore? epicSessions = null,
        ISteamWebApiClient? steam = null)
    {
        _epic = epic;
        _epicSessions = epicSessions;
        _steam = steam;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default)
        => _steam is not null && await _steam.IsConfiguredAsync(ct);

    /// <inheritdoc/>
    public async ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default)
    {
        if (_epic is null)
        {
            return null;
        }

        // Asked first and independently of the stored blob: this is the
        // authoritative "is the refresh token still worth spending", and it is
        // the half that goes false on its own while the app is closed.
        var live = await _epic.IsSignedInAsync(ct);

        // The stored session may still be readable after it stopped being
        // usable, and that is precisely the state worth naming — "your Epic
        // session expired" reads as an event that happened TO the user, where a
        // bare "not connected" reads as Winnow having forgotten them.
        var stored = _epicSessions is null ? null : await _epicSessions.LoadAsync(ct);

        if (!live && stored is null)
        {
            return null;
        }

        return new StoreSession(live, Clean(stored?.DisplayName));
    }

    /// <inheritdoc/>
    public async Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default)
    {
        if (_epic is null)
        {
            return new StoreSignInOutcome(
                false, null, false, StoreSignInProblem.NotConfigured,
                "Epic sign-in is not available in this build.");
        }

        EpicSignInResult result;
        try
        {
            result = await _epic.SignInAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // EpicSignInService rethrows cancellation deliberately — it will not
            // dress a shutdown as a sign-in failure. Here, one layer up, we know
            // what the cancellation MEANT: the panel's Cancel button, or the
            // window closing. Both are the user backing out, which is an
            // ordinary outcome with a name, so it becomes one rather than
            // travelling on to a command handler as an exception.
            return new StoreSignInOutcome(
                false, null, false, StoreSignInProblem.Cancelled, StoreSignInMessages.Cancelled);
        }

        if (result.Succeeded)
        {
            return new StoreSignInOutcome(
                true, Clean(result.DisplayName), result.Persisted, StoreSignInProblem.None,
                EpicSignInService.Explain(EpicSignInFailure.None));
        }

        return new StoreSignInOutcome(
            false, null, false, Translate(result.Failure),
            // The sentences are EpicSignInService's own and are not restated
            // here. Each one already says what to DO, and two copies of a
            // remedy is how they drift apart — one of them gets updated when
            // Epic changes something, and it is never the one on screen.
            EpicSignInService.Explain(result.Failure));
    }

    /// <inheritdoc/>
    public Task SignOutOfEpicAsync(CancellationToken ct = default)
        => _epic?.SignOutAsync(ct) ?? Task.CompletedTask;

    /// <summary>
    /// Ingest's reason, in the panel's vocabulary. Exhaustive on purpose: a new
    /// <c>EpicSignInFailure</c> arriving would otherwise fall silently into
    /// "unexpected" and lose whatever remedy it was added to carry, so the
    /// default is the one case that genuinely means "we do not know".
    /// </summary>
    private static StoreSignInProblem Translate(EpicSignInFailure failure) => failure switch
    {
        EpicSignInFailure.None => StoreSignInProblem.None,
        EpicSignInFailure.Cancelled => StoreSignInProblem.Cancelled,
        EpicSignInFailure.NoInteractivePrompt => StoreSignInProblem.NoPromptAvailable,
        EpicSignInFailure.NoCodeCaptured => StoreSignInProblem.NoCodeCaptured,
        EpicSignInFailure.InvalidAuthorizationCode => StoreSignInProblem.CodeRejected,
        EpicSignInFailure.InvalidClientCredentials => StoreSignInProblem.ClientRejected,
        EpicSignInFailure.Unreachable => StoreSignInProblem.Unreachable,
        EpicSignInFailure.NotConfigured => StoreSignInProblem.NotConfigured,
        _ => StoreSignInProblem.Unexpected,
    };

    /// <summary>
    /// An all-whitespace display name is the same absence as a null one, and
    /// the panel's "Connected as ___" would otherwise render an empty gap that
    /// looks like a rendering fault.
    /// </summary>
    private static string? Clean(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
}
