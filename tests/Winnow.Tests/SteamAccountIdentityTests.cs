using System.Globalization;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Tests.SteamWeb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-55 S4: one account confirmation, two credentials that can earn it.
///
/// <para>The key path observes which account it belongs to by spending a Year in
/// Review disclosure call. The sign-in path reads the same fact out of the
/// minted token's <c>sub</c> claim, for free, the moment the window closes —
/// which is acceptance criterion 4. Both write through the SAME
/// <see cref="ISteamAccountConfirmation"/>, because two writers of
/// <c>steam.owned_account_ref</c> is how the account filter starts hiding the
/// wrong library.</para>
///
/// <para>What these tests do NOT cover is the key path's own behaviour: that is
/// <see cref="OwnedAccountConfirmationTests"/> and
/// <see cref="OwnedAccountDisclosureRefetchTests"/>, both of which pass
/// unmodified, which is the real proof that consolidating the writer changed
/// nothing for an install that has only ever had a key.</para>
/// </summary>
public sealed class SteamAccountIdentityTests : IDisposable
{
    /// <summary>The user's own account, as the local scan names it.</summary>
    private const string Mine = "11111";

    /// <summary>Somebody else's, for the two-credential disagreement.</summary>
    private const string Theirs = "22222";

    private const string AppId = "400";

    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase _db = new();
    private readonly SettingsRepository _settings;
    private readonly FakeTimeProvider _clock = new(Now);

    public SteamAccountIdentityTests() => _settings = new SettingsRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    // ══ AC#4 — a sign-in confirms the account with no request at all ═════════

    [Fact]
    public async Task A_sign_in_confirms_the_account_and_the_toggle_goes_live_with_no_http_call()
    {
        // ACCEPTANCE CRITERION 4. The key path needs a Year in Review disclosure
        // to learn this; the token states it. The history client here throws on
        // any call, so "no request" is enforced rather than counted.
        await SeedOwnershipAsync(Mine);

        var (service, _) = SignIn(Mine);

        var report = await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.True(report.SignedIn);
        Assert.True(report.AccountConfirmed);
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // And the toggle the user actually sees is enabled, read through the
        // same seam the settings panel binds to.
        var visibility = new AccountVisibilityService(
            _settings, new LibraryQueryRepository(_db.Factory));

        Assert.True((await visibility.GetAsync()).AccountConfirmed);
    }

    [Fact]
    public async Task The_confirmation_a_sign_in_writes_is_stamped_with_the_session_not_the_key()
    {
        await SeedOwnershipAsync(Mine);

        var (service, _) = SignIn(Mine, keys: new FakeSteamApiKeyProvider("a-key-as-well"));
        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        var stored = await _settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey);

        // Not the key's digest, because the key did not earn this. And not the
        // token either: a session fingerprints over its ACCOUNT, so tomorrow's
        // renewed token still answers "the same credential".
        Assert.Equal(SteamCredentialFingerprint.OfSession(SteamIdFor(Mine)), stored);
        Assert.NotEqual(FakeSteamApiKeyProvider.HashOf("a-key-as-well"), stored);
    }

    // ══ TASK-54's refetch is kept, and is a no-op for a signed-in user ═══════

    [Fact]
    public async Task The_disclosure_refetch_is_a_no_op_once_a_sign_in_has_set_the_ref()
    {
        // Verified rather than asserted. The refetch is NOT switched off for
        // signed-in users — it is left exactly as TASK-54 shipped it, and its
        // own first condition ("is the ref already known?") answers yes for one
        // settings read. A key-only user still gets the repair.
        await SeedOwnershipAsync(Mine);
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync(Mine);

        var confirmation = Confirmation(new FakeSteamApiKeyProvider(), out var sessions);

        var (service, _) = SignIn(Mine, confirmation: confirmation, sessions: sessions);
        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        var history = new YearStub { PopulatedYears = { 2025 } };
        await Backfill(history, confirmation).BackfillAsync();

        // Only the current year, which the ordinary loop always fetches. No
        // repair read, because there was nothing to repair.
        Assert.Equal([2026], history.Asked.Select(a => a.Year));

        // And the sign-in's answer is still standing afterwards.
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    [Fact]
    public async Task A_key_only_user_still_gets_the_disclosure_refetch()
    {
        // The other half of the same claim: keeping the refetch means keeping it
        // working. Nobody signed in here, so the repair is the only route to the
        // fact and it must still fire.
        await SeedOwnershipAsync(Mine);
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync(Mine);

        var confirmation = Confirmation(new FakeSteamApiKeyProvider(), out _);
        var history = new YearStub { PopulatedYears = { 2025 } };

        await Backfill(history, confirmation).BackfillAsync();

        Assert.Contains(2025, history.Asked.Select(a => a.Year));
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    // ══ Sign-out clears what the session earned, and only that ══════════════

    [Fact]
    public async Task Signing_out_clears_a_session_earned_confirmation()
    {
        await SeedOwnershipAsync(Mine);

        var (service, _) = SignIn(Mine);
        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        await service.SignOutAsync();

        // Nothing proves the stored account is the user's any more, so the toggle
        // goes back to disabled rather than filtering on a fact whose evidence
        // has been discarded.
        Assert.Null(SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.RefSettingKey)));
    }

    [Fact]
    public async Task Signing_out_leaves_a_key_earned_confirmation_alone()
    {
        // The credential that earned this one is still here. A sign-out is not a
        // reason to make the user re-earn a confirmation the API key proved and
        // still proves.
        await SeedOwnershipAsync(Mine);

        var keys = new FakeSteamApiKeyProvider("the-one-key");
        var confirmation = Confirmation(keys, out var sessions);

        await Backfill(new YearStub { PopulatedYears = { 2026 } }, confirmation).BackfillAsync();
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
        Assert.Equal(
            FakeSteamApiKeyProvider.HashOf("the-one-key"),
            await _settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey));

        // Somebody signs in and out afterwards. The key never moved.
        var (service, _) = SignIn(Theirs, keys: keys, confirmation: confirmation, sessions: sessions);
        await service.SignOutAsync();

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    // ══ A key and a session naming different accounts ═══════════════════════

    [Fact]
    public async Task A_session_earned_confirmation_survives_a_key_belonging_to_someone_else()
    {
        // The mismatch case. The session says the user is Mine; the key belongs
        // to Theirs. Reconciliation compares the stored fingerprint against every
        // credential in force, so the session — which earned it and is still here
        // — keeps it. The key does not get to overwrite an identity it never
        // proved.
        await SeedOwnershipAsync(Mine);

        var keys = new FakeSteamApiKeyProvider("a-second-persons-key");
        var confirmation = Confirmation(keys, out var sessions);

        var (service, _) = SignIn(Mine, keys: keys, confirmation: confirmation, sessions: sessions);
        await service.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // A backfill pass now runs with that foreign key. Year in Review answers
        // for the OTHER account, and the AccountMismatch guard abandons it —
        // without writing the stranger's identity over the session's.
        var history = new YearStub
        {
            PopulatedYears = { 2026 },
            AnswersForAccountId = uint.Parse(Theirs, CultureInfo.InvariantCulture),
        };

        await Backfill(history, confirmation).BackfillAsync();

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    [Fact]
    public async Task Signing_in_as_a_different_account_replaces_the_confirmation()
    {
        // The fourth reconciliation case. A session fingerprint is derived from
        // the account, so a second sign-in as somebody else produces a different
        // digest — and the confirmation follows the account that is actually
        // signed in.
        await SeedOwnershipAsync(Mine);

        var confirmation = Confirmation(new FakeSteamApiKeyProvider(null), out var sessions);

        var (first, _) = SignIn(Mine, confirmation: confirmation, sessions: sessions);
        await first.SignInAsync(new SteamSignInRequest { ConsentGranted = true });
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        var (second, _) = SignIn(Theirs, confirmation: confirmation, sessions: sessions);
        await second.SignInAsync(new SteamSignInRequest { ConsentGranted = true });

        Assert.Equal(Theirs, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
        Assert.Equal(
            SteamCredentialFingerprint.OfSession(SteamIdFor(Theirs)),
            await _settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey));
    }

    // ══ The generalisation does not churn existing installs ═════════════════

    [Fact]
    public void The_api_key_digest_is_byte_identical_to_what_shipped()
    {
        // Frozen. If this changes, every existing install's stored fingerprint
        // stops matching its own unchanged key, the confirmation is cleared, and
        // the user pays a TASK-54 disclosure refetch to earn back something that
        // was never in doubt.
        Assert.Equal(
            FakeSteamApiKeyProvider.HashOf("SUPERSECRETKEYVALUE"),
            SteamCredentialFingerprint.OfApiKey("SUPERSECRETKEYVALUE"));

        // SHA-256 as lower-case hex: 64 characters, no upper case, no key in it.
        var digest = SteamCredentialFingerprint.OfApiKey("SUPERSECRETKEYVALUE");
        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToLowerInvariant(), digest);
        Assert.DoesNotContain("SUPERSECRET", digest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_install_confirmed_by_a_key_before_this_stage_is_left_untouched()
    {
        // The state an upgrading install is in: ref and key digest written by the
        // shipped code. Reconciliation must recognise it, not clear it.
        await SeedOwnershipAsync(Mine);
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await _settings.SetAsync(
            SteamOwnedAccount.KeyFingerprintSettingKey, FakeSteamApiKeyProvider.HashOf("the-one-key"));

        var confirmation = Confirmation(new FakeSteamApiKeyProvider("the-one-key"), out _);

        Assert.False(await confirmation.ReconcileAsync());
        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    [Fact]
    public void A_session_fingerprint_cannot_collide_with_a_key_digest()
    {
        // Domain separation, so a stored value can never be satisfied by the
        // wrong KIND of credential — an API key whose text happened to be a
        // SteamID64 included.
        var steamId = SteamIdFor(Mine);

        Assert.NotEqual(
            SteamCredentialFingerprint.OfApiKey(steamId.ToString()),
            SteamCredentialFingerprint.OfSession(steamId));
    }

    [Fact]
    public void A_session_fingerprint_survives_a_token_rotation()
    {
        // Derived from the account, not from either token. S6 rotates both on
        // every renewal; a digest over a token would report "the credential
        // changed" daily and clear a confirmation nothing was wrong with.
        var early = SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now.AddHours(24), SubjectFor(Mine)),
            SteamSessionFixtures.RefreshToken(Now.AddDays(207), SubjectFor(Mine)),
            Now)!;

        var renewed = early.WithRenewedAccess(
            SteamSessionFixtures.AccessToken(Now.AddHours(48), SubjectFor(Mine)),
            Now.AddHours(48),
            Now.AddHours(23),
            refreshToken: "a-rotated-refresh-token");

        Assert.Equal(
            SteamCredentialFingerprint.OfSession(early.SteamId),
            SteamCredentialFingerprint.OfSession(renewed.SteamId));
    }

    [Fact]
    public void An_unidentified_session_earns_no_fingerprint()
        => Assert.Null(SteamCredentialFingerprint.OfSession(null));

    // ══ One credential serves a whole pass ══════════════════════════════════

    [Fact]
    public async Task Both_backfill_endpoints_send_the_same_credential_in_one_pass()
    {
        // ClientGetLastPlayedTimes carries no steamid and cannot be checked
        // against the account it answered for, so the AccountMismatch guard on
        // Year in Review is only worth anything if the anchor call was made with
        // the SAME credential. The selector is pure and both call sites default
        // to the same purpose, so it is — and this is the pin that keeps it so.
        using var host = new SteamWebTestHost(
            (request, _) => FakeSteamWebHandler.Json(System.Net.HttpStatusCode.OK, "{\"response\":{}}"),
            apiKey: "the-one-key",
            now: Now);

        await host.Resolve<ISteamSessionProvider>().SaveAsync(
            SteamSession.TryCreate(
                SteamSessionFixtures.AccessToken(Now.AddHours(24), SubjectFor(Theirs)),
                SteamSessionFixtures.RefreshToken(Now.AddDays(207), SubjectFor(Theirs)),
                Now)!);

        // Both credentials present and naming different accounts — the exact
        // shape that would let a pass mix them.
        await host.History.GetYearInReviewAsync(SteamIdFor(Mine), 2025);
        await host.History.GetLastPlayedTimesAsync();

        var sent = host.Handler.Requests
            .Select(r => r.HasParameter(SteamCredential.ApiKeyParameter) ? "key" : "access_token")
            .Distinct()
            .ToArray();

        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(["key"], sent);
    }

    // ══ Fixtures ════════════════════════════════════════════════════════════

    private static SteamId SteamIdFor(string accountRef)
    {
        Assert.True(SteamId.TryParse(accountRef, out var steamId));
        return steamId;
    }

    /// <summary>The SteamID64 a token's <c>sub</c> claim would carry for an account.</summary>
    private static string SubjectFor(string accountRef) => SteamIdFor(accountRef).ToString();

    private ISteamAccountConfirmation Confirmation(
        ISteamApiKeyProvider keys, out ISteamSessionProvider sessions)
    {
        sessions = new SteamSessionProvider(
            new InMemorySteamSessionStore(), new SteamWebOptions(), _clock);

        return new SteamAccountConfirmation(_settings, keys, sessions);
    }

    /// <summary>
    /// A sign-in service that mints a token for <paramref name="accountRef"/> and
    /// writes through the shared confirmation writer. No HTTP client of any kind
    /// is involved, which is the point of the acceptance criterion.
    /// </summary>
    private (SteamSignInService Service, ISteamSessionProvider Sessions) SignIn(
        string accountRef,
        ISteamApiKeyProvider? keys = null,
        ISteamAccountConfirmation? confirmation = null,
        ISteamSessionProvider? sessions = null)
    {
        confirmation ??= Confirmation(keys ?? new FakeSteamApiKeyProvider(null), out sessions);
        sessions ??= new SteamSessionProvider(
            new InMemorySteamSessionStore(), new SteamWebOptions(), _clock);

        var minted = SteamSignInResult.SignedIn(
            SteamSessionFixtures.AccessToken(Now.AddHours(24), SubjectFor(accountRef)),
            Now.AddHours(24),
            SubjectFor(accountRef),
            ["web:store"],
            "steam",
            SteamSessionFixtures.RefreshToken(Now.AddDays(207), SubjectFor(accountRef)),
            pages: null);

        return (
            new SteamSignInService(
                new ScriptedSignInSession(minted),
                sessions,
                _clock,
                NullLogger<SteamSignInService>.Instance,
                confirmation),
            sessions);
    }

    private SteamPlaytimeBackfillService Backfill(
        YearStub history, ISteamAccountConfirmation confirmation, FakeSteamApiKeyProvider? keys = null)
        => new(
            history,
            new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory),
            new OwnershipAccountRepository(_db.Factory),
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _settings,
            _db.Factory,
            new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions { FirstYear = 2022 },
            _clock,
            keys ?? new FakeSteamApiKeyProvider(),
            NullLogger<SteamPlaytimeBackfillService>.Instance,
            confirmation);

    private async Task SeedOwnershipAsync(string accountRef)
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Portal" });
        var releaseId = await releases.InsertAsync(new Release { WorkId = workId, Name = "Portal" });
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = AppId,
        });

        await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
            AccountRef = accountRef,
        });
    }

    private Task MarkYearAsync(string accountRef, int year, int games)
        => _settings.SetAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SteamPlaytimeBackfillService.YearMarkerPrefix}{SteamIdFor(accountRef).Value}.{year}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"2026-01-01T00:00:00.0000000Z;games={games};written=0"));

    private async Task MarkFinishedAsync(int? populated)
    {
        for (var year = 2022; year <= Now.Year - 1; year++)
        {
            await MarkYearAsync(Mine, year, games: year == populated ? 7 : 0);
        }
    }

    private Task MarkConfirmedAsync(string accountRef)
        => _settings.SetAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SteamPlaytimeBackfillService.ConfirmedPrefix}{SteamIdFor(accountRef).Value}.confirmed"),
            "2026-01-01T00:00:00.0000000Z");

    /// <summary>A sign-in that hands back a prepared result and opens no window.</summary>
    private sealed class ScriptedSignInSession(SteamSignInResult result) : ISteamSignInSession
    {
        public string Name => "scripted";

        public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamSignInResult> SignInAsync(
            SteamSignInRequest request, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    /// <summary>
    /// A Year in Review that discloses only for the years named, and records
    /// every year it was asked about. Deliberately the same stub shape
    /// <see cref="OwnedAccountDisclosureRefetchTests"/> uses, so a difference in
    /// outcome here is a difference in the code and not in the fixture.
    /// </summary>
    private sealed class YearStub : ISteamHistoryClient
    {
        public HashSet<int> PopulatedYears { get; } = [];

        /// <summary>Set to answer for a DIFFERENT account, as a stranger's key would.</summary>
        public uint? AnswersForAccountId { get; init; }

        public List<(int Year, TimeSpan? CacheTtl)> Asked { get; } = [];

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamYearInReview> GetYearInReviewAsync(
            SteamId steamId,
            int year,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
        {
            Asked.Add((year, cacheTtl));

            return Task.FromResult(new SteamYearInReview(
                steamId,
                year,
                Answered: true,
                AccountId: PopulatedYears.Contains(year)
                    ? AnswersForAccountId ?? steamId.AccountId
                    : null,
                Games: [],
                ObservedAt: Now.UtcDateTime,
                FromCache: false));
        }

        public Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
            => Task.FromResult(new SteamLastPlayedTimes(
                Answered: true, Games: [], ObservedAt: Now.UtcDateTime, FromCache: false));
    }
}
