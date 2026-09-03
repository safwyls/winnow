using System.Globalization;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb.Credentials;
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

    /// <summary>
    /// The credential inventory — both Steam credentials at once, and the only
    /// thing this class asks about Steam. Deliberately not
    /// <c>ISteamWebApiClient</c>: an HTTP client is the wrong object to ask a
    /// status question of, and its <c>IsConfiguredAsync</c> is a pass-through to
    /// exactly this inventory anyway.
    /// </summary>
    private readonly ISteamCredentialProvider? _steamCredentials;

    /// <summary>Where an in-app Web API key is written. Null in a host with no settings table.</summary>
    private readonly ISettingsRepository? _settings;

    /// <summary>
    /// The shared account-confirmation writer, so a key change reconciles the
    /// recorded account with the credentials actually in force. Optional: absent
    /// means the confirmation is left alone, which is the pre-S4 behaviour and
    /// not a failure.
    /// </summary>
    private readonly ISteamAccountConfirmation? _confirmation;

    public StoreConnections(
        EpicSignInService? epic = null,
        IEpicTokenStore? epicSessions = null,
        ISteamCredentialProvider? steamCredentials = null,
        ISettingsRepository? settings = null,
        ISteamAccountConfirmation? confirmation = null)
    {
        _epic = epic;
        _epicSessions = epicSessions;
        _steamCredentials = steamCredentials;
        _settings = settings;
        _confirmation = confirmation;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default)
        => (await GetSteamConnectionAsync(ct)).HasUsableCredential;

    /// <inheritdoc/>
    public async ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default)
    {
        if (_steamCredentials is null)
        {
            return SteamConnection.None;
        }

        var inventory = await _steamCredentials.GetInventoryAsync(ct);

        return new SteamConnection(
            inventory.HasApiKey,
            ApiKeyIsAppManaged: string.Equals(
                inventory.ApiKeySource, SettingsTableApiKeySource.SourceName, StringComparison.Ordinal),
            inventory.HasSession,
            inventory.SessionUsable,
            inventory.SessionExpiresAt,
            inventory.SessionAccount?.Value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public Task SaveSteamApiKeyAsync(string? key, CancellationToken ct = default)
        => WriteSteamApiKeyAsync(key?.Trim() ?? string.Empty, ct);

    /// <inheritdoc/>
    public Task ClearSteamApiKeyAsync(CancellationToken ct = default)
        => WriteSteamApiKeyAsync(string.Empty, ct);

    /// <summary>
    /// The one write path for the settings-table key, so saving and clearing
    /// cannot drift apart on what has to happen afterwards.
    ///
    /// <para>The empty string is the cleared state rather than a deleted row:
    /// <see cref="ISettingsRepository"/> has no delete, and
    /// <c>SteamApiKey.TryCreate</c> already treats blank as unset, so an empty
    /// value and an absent row mean the same thing to every reader.</para>
    ///
    /// <para><b>Invalidate, then reconcile, in that order.</b> The key chain
    /// memoises — it is read on every enrichment call — so nothing sees the new
    /// key until the cache is dropped, and reconciliation asks which credentials
    /// are in force, which is a question with the wrong answer until it is.</para>
    /// </summary>
    private async Task WriteSteamApiKeyAsync(string value, CancellationToken ct)
    {
        if (_settings is null)
        {
            return;
        }

        await _settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, value, ct);

        _steamCredentials?.Invalidate();

        if (_confirmation is not null)
        {
            // A confirmation earned by the key that was just replaced or removed
            // no longer names a credential in force, and an account filter still
            // trusting it would hide the wrong library. One earned by a sign-in,
            // or by an identical key, survives — reconciliation compares against
            // every credential present, not against a preferred one.
            await _confirmation.ReconcileAsync(ct);
        }
    }

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
