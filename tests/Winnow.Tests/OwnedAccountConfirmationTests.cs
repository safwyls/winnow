using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Winnow.Tests.SteamWeb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The two things that have to be true before the account filter can be trusted:
/// every account is <b>asked about</b>, and the account the user's key belongs
/// to is <b>recorded</b>.
///
/// <para>Both close the same gap from opposite ends.
/// <c>ownerships.account_ref</c> holds the play tuple's winner, so an account
/// that never out-played another is invisible to any query over that column —
/// and if it is the user's own account, the filter would have nothing to filter
/// to.</para>
/// </summary>
public sealed class OwnedAccountConfirmationTests : IDisposable
{
    private const string Mine = "11111";
    private const string Theirs = "22222";

    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    // ══ Every account is asked about ════════════════════════════════════════

    [Fact]
    public async Task The_remote_sync_asks_about_an_account_that_never_won_a_candidate()
    {
        // The local scan saw two accounts on one appid. Only one of them won the
        // candidate's AccountRef; reading that column alone — which is what the
        // sync used to do — would never ask Steam about the other, and the other
        // is exactly who the user might be.
        var steam = new RecordingSteamWebClient();
        var scan = new LocalLibraryScan(
            [
                new CandidateOwnership(
                    Provider: ExternalIdProviders.Steam,
                    ProviderId: "400",
                    Title: "Portal",
                    AccountRef: Theirs,
                    InstallPath: null,
                    Installed: null,
                    PlaytimeMinutes: 900,
                    LastPlayedAt: null,
                    AcquiredAt: null,
                    Source: SteamLibrarySource.SourceName,
                    ObservedAt: new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc))
                {
                    Accounts =
                    [
                        new CandidateAccount(Mine, 40, null),
                        new CandidateAccount(Theirs, 900, null),
                    ],
                },
            ],
            [],
            []);

        await SyncService(steam).SyncAsync(scan);

        Assert.Equal(
            [Mine, Theirs],
            steam.Asked.Select(id => id.AccountRef).Order());
    }

    // ══ The account is recorded, and un-recorded ════════════════════════════

    [Fact]
    public async Task A_confirmed_account_is_written_where_the_filter_can_read_it()
    {
        var settings = new SettingsRepository(_db.Factory);
        await SeedSteamOwnershipAsync(Mine);

        await Backfill(settings, new FakeSteamApiKeyProvider("key-one")).BackfillAsync();

        Assert.Equal(Mine, await settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    [Fact]
    public async Task Changing_the_api_key_clears_the_confirmed_account()
    {
        var settings = new SettingsRepository(_db.Factory);
        await SeedSteamOwnershipAsync(Mine);

        await Backfill(settings, new FakeSteamApiKeyProvider("key-one")).BackfillAsync();
        Assert.Equal(Mine, await settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // A second person's key. Keeping the first person's account id would
        // point the filter at a stranger's library — a wrong answer given
        // confidently, which is worse than the unfiltered view it replaced.
        await Backfill(settings, new FakeSteamApiKeyProvider("key-two")).BackfillAsync();

        // Re-confirmed for the same account in this fixture, but the stored
        // fingerprint must have moved with the key: that is what makes the NEXT
        // change detectable.
        Assert.Equal(
            FakeSteamApiKeyProvider.HashOf("key-two"),
            await settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey));
    }

    [Fact]
    public async Task A_changed_key_that_discloses_nothing_leaves_the_account_cleared()
    {
        // The case the fingerprint exists for. The new key never proves whose it
        // is — Steam's envelope is the same for "no Replay this year" and "not
        // your account" — so the confirmation cannot be re-earned, and the
        // filter must be left with nothing rather than with the previous
        // person's account id.
        var settings = new SettingsRepository(_db.Factory);
        await SeedSteamOwnershipAsync(Mine);

        await Backfill(settings, new FakeSteamApiKeyProvider("key-one")).BackfillAsync();
        Assert.Equal(Mine, await settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        await Backfill(
                settings,
                new FakeSteamApiKeyProvider("key-two"),
                discloses: false)
            .BackfillAsync();

        Assert.Null(SteamOwnedAccount.Clean(
            await settings.GetAsync(SteamOwnedAccount.RefSettingKey)));
    }

    [Fact]
    public async Task Removing_the_api_key_clears_the_confirmed_account()
    {
        var settings = new SettingsRepository(_db.Factory);
        await SeedSteamOwnershipAsync(Mine);

        await Backfill(settings, new FakeSteamApiKeyProvider("key-one")).BackfillAsync();
        Assert.Equal(Mine, await settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // Nothing currently proves the stored account is the user's, so the
        // toggle has to go back to disabled rather than filter on a stale fact.
        await Backfill(settings, new FakeSteamApiKeyProvider(null)).BackfillAsync();

        Assert.Null(SteamOwnedAccount.Clean(
            await settings.GetAsync(SteamOwnedAccount.RefSettingKey)));
    }

    [Fact]
    public async Task The_stored_key_fingerprint_is_never_the_key()
    {
        var settings = new SettingsRepository(_db.Factory);
        await SeedSteamOwnershipAsync(Mine);

        await Backfill(settings, new FakeSteamApiKeyProvider("SUPERSECRETKEYVALUE")).BackfillAsync();

        var stored = await settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey);

        Assert.NotNull(stored);
        Assert.DoesNotContain("SUPERSECRETKEYVALUE", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FakeSteamApiKeyProvider.HashOf("SUPERSECRETKEYVALUE"), stored);
    }

    // ══ Fixtures ════════════════════════════════════════════════════════════

    private RemoteOwnershipSyncService SyncService(ISteamWebApiClient steam)
    {
        var resolver = new ExternalIdResolver(
            new WorkRepository(_db.Factory),
            new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory),
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        var gate = new LibrarySyncGate();

        return new RemoteOwnershipSyncService(
            new LocalLibrarySyncService(
                new SteamLibrarySource(steamRoot: Path.Combine(Path.GetTempPath(), "no-steam-here")),
                // Guaranteed to find nothing. A default GogLibrarySource reads
                // the machine's real registry, so on a developer box with GOG
                // installed this test would gain candidates it never asked for.
                SilentStores.Epic(),
                SilentStores.Gog(),
                resolver,
                gate,
                NullLogger<LocalLibrarySyncService>.Instance),
            resolver,
            gate,
            NullLogger<RemoteOwnershipSyncService>.Instance,
            steam);
    }

    /// <summary>One Steam ownership attributed to an account, so the backfill has somebody to ask about.</summary>
    private async Task SeedSteamOwnershipAsync(string accountRef)
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Portal" });
        var releaseId = await releases.InsertAsync(
            new Release { WorkId = workId, Name = "Portal" });
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = "400",
        });
        await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
            AccountRef = accountRef,
        });
    }

    private SteamPlaytimeBackfillService Backfill(
        SettingsRepository settings, FakeSteamApiKeyProvider keys, bool discloses = true)
        => new(
            // Configured exactly when a key is present, as the real client is:
            // its IsConfiguredAsync asks the same provider.
            new ConfirmingHistoryClient { Configured = keys.HasKey, Discloses = discloses },
            new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory),
            new OwnershipAccountRepository(_db.Factory),
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            settings,
            _db.Factory,
            new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions { FirstYear = 2026 },
            TimeProvider.System,
            keys,
            NullLogger<SteamPlaytimeBackfillService>.Instance);

    /// <summary>Records which accounts the sync asked Steam about, and answers nothing.</summary>
    private sealed class RecordingSteamWebClient : ISteamWebApiClient
    {
        public List<SteamId> Asked { get; } = [];

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(
            SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
        {
            Asked.Add(steamId);
            return Task.FromResult(SteamOwnedLibrary.Unanswered(steamId, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
            SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
        {
            Asked.Add(steamId);
            return Task.FromResult<IReadOnlyList<CandidateOwnership>>([]);
        }
    }

    /// <summary>
    /// A history client that discloses an account id — the disclosure being the
    /// whole of what "confirmed" means — and has no history to import.
    /// </summary>
    private sealed class ConfirmingHistoryClient : ISteamHistoryClient
    {
        public bool Configured { get; init; } = true;

        /// <summary>Whether Steam names the account it answered for. False is "not proved".</summary>
        public bool Discloses { get; init; } = true;

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Configured);

        public Task<SteamYearInReview> GetYearInReviewAsync(
            SteamId steamId,
            int year,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
            => Task.FromResult(new SteamYearInReview(
                steamId, year, Answered: true,
                AccountId: Discloses ? steamId.AccountId : null, Games: [],
                ObservedAt: DateTime.UtcNow, FromCache: false));

        public Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
            => Task.FromResult(new SteamLastPlayedTimes(
                Answered: true, Games: [], ObservedAt: DateTime.UtcNow, FromCache: false));
    }

}
