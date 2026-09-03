using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.App.Services;

/// <summary>
/// Which kind of credential earned a confirmation. Passed in rather than
/// guessed, because the two paths learn the same fact by different means and the
/// fingerprint stamped alongside the account has to name the one that actually
/// proved it.
/// </summary>
public enum SteamAccountConfirmationSource
{
    /// <summary>
    /// A Web API call made with the configured key answered <em>for</em> an
    /// account. The only disclosure the key path has.
    /// </summary>
    WebApiKey,

    /// <summary>
    /// A WebView sign-in minted a token whose <c>sub</c> claim names the account.
    /// Free, immediate, and no request spent to learn it.
    /// </summary>
    Session,
}

/// <summary>
/// The single writer of <see cref="SteamOwnedAccount.RefSettingKey"/> and
/// <see cref="SteamOwnedAccount.KeyFingerprintSettingKey"/>.
///
/// <para><b>Why a seam at all.</b> Two paths now learn which Steam account is
/// the user's: the key path, which observes it in a Year in Review disclosure,
/// and the sign-in path, which reads it out of the token's subject claim. Two
/// writers of the same two settings rows is how the account filter starts hiding
/// the wrong library — one path clearing what the other had just written, or
/// stamping a fingerprint for a credential it does not own. There is one writer,
/// and both paths call it.</para>
///
/// <para><b>What it will not do.</b> It does not decide <em>whether</em> an
/// account is confirmed — that judgement stays with the path that made the
/// observation. It writes the answer, reconciles it against the credentials
/// actually present, and reports what it holds.</para>
/// </summary>
public interface ISteamAccountConfirmation
{
    /// <summary>
    /// Records <paramref name="steamId"/> as the user's own account, stamped
    /// with a fingerprint of the credential that earned it.
    /// </summary>
    /// <returns>Whether a fingerprint could be taken and stored alongside the account.</returns>
    Task<bool> ConfirmAsync(
        SteamId steamId, SteamAccountConfirmationSource source, CancellationToken ct = default);

    /// <summary>
    /// Clears the recorded account when the credential that earned it is no
    /// longer present, or is no longer the same credential.
    /// </summary>
    /// <returns>Whether anything was cleared.</returns>
    Task<bool> ReconcileAsync(CancellationToken ct = default);

    /// <summary>The recorded account reference, or null when nothing is confirmed.</summary>
    Task<string?> GetConfirmedAccountRefAsync(CancellationToken ct = default);

    /// <summary>
    /// The stored fingerprint, or null when none was recorded. Null is a real
    /// state and not merely an absence: it means nothing says which credential
    /// earned the confirmation, which is what makes a cached disclosure unsafe
    /// to answer from.
    /// </summary>
    Task<string?> GetRecordedFingerprintAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="fingerprint"/> names a credential present right
    /// now. The one question reconciliation asks, exposed so a caller can ask it
    /// without re-deriving the digest.
    /// </summary>
    Task<bool> IsInForceAsync(string fingerprint, CancellationToken ct = default);
}

/// <summary>
/// Implements <see cref="ISteamAccountConfirmation"/> over the settings table
/// and the two credential sources.
///
/// <para><b>The fingerprint generalises; the key digest does not change.</b> An
/// API key still fingerprints as <c>SHA256(key)</c> in lower-case hex, byte for
/// byte what shipped, so every existing install's stored value still matches its
/// own unchanged key and nobody pays a TASK-54 disclosure refetch for a
/// confirmation that was never in doubt. A session fingerprints over its own
/// account instead of over either token, because the tokens are replaced on a
/// schedule and the account is not.</para>
///
/// <para><b>The four cases it has to answer, and how.</b> Reconciliation asks
/// whether the credential that earned the confirmation is still present and
/// unchanged, by comparing the stored fingerprint against the fingerprints of
/// every credential in force. A changed key produces a different digest; a
/// removed key produces none; a sign-out removes the session and with it the
/// only digest that matched; and signing in as a different account produces a
/// digest for the new account rather than the old one. All four clear.</para>
///
/// <para><b>A key-earned confirmation survives a sign-out</b>, and a
/// session-earned one survives a key being pasted, because the comparison is
/// against the whole set of credentials present rather than against a single
/// preferred one. Whoever earned it, if they are still here, the confirmation
/// stands.</para>
///
/// <para><b>A present-but-expired session still counts as present.</b> The
/// provider deliberately keeps a lapsed session on the books so the UI can say
/// "signed in and expired" rather than "never connected"; treating expiry as
/// absence here would switch the account filter off every time a token aged out
/// and back on after the next sign-in, for a fact that never stopped being
/// true.</para>
/// </summary>
public sealed class SteamAccountConfirmation : ISteamAccountConfirmation
{
    private readonly ISettingsRepository _settings;
    private readonly ISteamApiKeyProvider? _keys;
    private readonly ISteamSessionProvider? _sessions;
    private readonly ILogger<SteamAccountConfirmation> _log;

    public SteamAccountConfirmation(
        ISettingsRepository settings,
        ISteamApiKeyProvider? keys = null,
        ISteamSessionProvider? sessions = null,
        ILogger<SteamAccountConfirmation>? log = null)
    {
        _settings = settings;
        _keys = keys;
        _sessions = sessions;
        _log = log ?? NullLogger<SteamAccountConfirmation>.Instance;
    }

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(
        SteamId steamId, SteamAccountConfirmationSource source, CancellationToken ct = default)
    {
        // The account reference in the same shape the local scan writes
        // (SteamId.AccountRef — the steam3 account id, the userdata folder
        // name), so the filter's comparison is a string equality against rows
        // both sources produced and not a conversion nobody would notice failing.
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, steamId.AccountRef, ct);

        var fingerprint = source switch
        {
            SteamAccountConfirmationSource.Session => SteamCredentialFingerprint.OfSession(steamId),
            _ => await KeyFingerprintAsync(ct),
        };

        if (fingerprint is null)
        {
            // No credential to stamp it with. The account is still recorded — the
            // observation happened — but nothing says which credential earned it,
            // and reconciliation reads that absence as "cannot be vouched for"
            // and clears on the next pass. Deliberately left as the shipped
            // behaviour rather than tightened here: the key path cannot reach
            // this state (it is gated on a configured key) and changing it would
            // be a behaviour change in a stage that promised none.
            return false;
        }

        await _settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, fingerprint, ct);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ReconcileAsync(CancellationToken ct = default)
    {
        var stored = SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct));

        if (stored is null)
        {
            // Nothing to invalidate. The next confirmation writes both halves.
            return false;
        }

        var recorded = SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, ct));

        if (recorded is not null && await IsInForceAsync(recorded, ct))
        {
            return false;
        }

        // Cleared by writing a blank rather than deleting, because
        // ISettingsRepository has two methods and no remove — the same
        // convention the Epic token store already follows. Every reader treats
        // blank as never-written.
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, string.Empty, ct);
        await _settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, string.Empty, ct);

        // Neither the account nor the fingerprint is named: one identifies a real
        // person and the other is derived from a credential.
        _log.LogInformation(
            "The Steam credential that confirmed which account is yours is gone or has changed, so the "
            + "recorded account has been cleared. The account visibility filter is off until a sign-in or "
            + "an import re-confirms it.");

        return true;
    }

    /// <inheritdoc/>
    public async Task<string?> GetConfirmedAccountRefAsync(CancellationToken ct = default)
        => SteamOwnedAccount.Clean(await _settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct));

    /// <inheritdoc/>
    public async Task<string?> GetRecordedFingerprintAsync(CancellationToken ct = default)
        => SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, ct));

    /// <inheritdoc/>
    public async Task<bool> IsInForceAsync(string fingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        if (string.Equals(fingerprint, await KeyFingerprintAsync(ct), StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(fingerprint, await SessionFingerprintAsync(ct), StringComparison.Ordinal);
    }

    /// <summary>
    /// The digest of the configured API key, or null when there is none.
    /// <b>Never the key.</b>
    /// </summary>
    private async Task<string?> KeyFingerprintAsync(CancellationToken ct)
    {
        if (_keys is null || await _keys.GetAsync(ct) is not { } key)
        {
            // No key configured at all. Distinct from "a key whose fingerprint
            // could not be taken", which cannot happen: nothing here fails.
            return null;
        }

        return SteamCredentialFingerprint.OfApiKey(key.Value);
    }

    /// <summary>
    /// The digest of the stored session's account, or null when nobody is signed
    /// in. Neither token is read.
    /// </summary>
    private async Task<string?> SessionFingerprintAsync(CancellationToken ct)
    {
        if (_sessions is null || await _sessions.GetAsync(ct) is not { } session)
        {
            return null;
        }

        return SteamCredentialFingerprint.OfSession(session.SteamId);
    }
}
