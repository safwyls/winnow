using System.Globalization;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-54: the account visibility toggle stayed disabled forever on exactly the
/// accounts that had already finished their playtime backfill.
///
/// <para>Every year but the current one is asked about once per install, so a
/// finished account refetches only the current year — and an uncompiled
/// current-year Replay answers empty, carrying no account id. Nothing else in
/// Winnow discloses which account the API key belongs to, so the one write that
/// enables the toggle could never happen again. Found live 2026-08-30.</para>
///
/// <para>The repair re-reads one already-imported, known-populated year purely
/// for the account id in it. These tests pin that it fires when it should, that
/// it imports nothing while doing so, and that it cannot hand a freshly pasted
/// key the previous owner's identity.</para>
/// </summary>
public sealed class OwnedAccountDisclosureRefetchTests : IDisposable
{
    /// <summary>The account under test, as the local scan would name it.</summary>
    private const string Mine = "11111";

    private const string AppId = "400";

    /// <summary>Fixed so "the current year" and "a completed year" are not the calendar's business.</summary>
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly SettingsRepository _settings;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly FakeTimeProvider _clock = new(Now);

    private SteamId _steamId;
    private long _ownershipId;

    public OwnedAccountDisclosureRefetchTests()
    {
        _settings = new SettingsRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);
        _snapshots = new PlaytimeSnapshotRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ══ AC#1 — the ref is recovered, and nothing is imported doing it ═══════

    [Fact]
    public async Task A_finished_account_recovers_its_ref_from_a_completed_year()
    {
        await SeedAsync();

        // The live shape: 2022-2025 imported and marked, 2025 populated. The
        // current year is uncompiled and answers empty, which is what leaves the
        // ordinary disclosure with nothing to report.
        await MarkYearAsync(2022, games: 0);
        await MarkYearAsync(2023, games: 4);
        await MarkYearAsync(2024, games: 0);
        await MarkYearAsync(2025, games: 7);
        await MarkConfirmedAsync();

        var history = new YearStub { PopulatedYears = { 2023, 2025 } };

        await Backfill(history).BackfillAsync();

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // The NEWEST populated year, not merely the first one found.
        Assert.Contains(2025, history.Asked.Select(a => a.Year));
        Assert.DoesNotContain(2023, history.Asked.Select(a => a.Year));

        // Empty years are never asked about: an empty Replay is exactly what the
        // current year already answered.
        Assert.DoesNotContain(2024, history.Asked.Select(a => a.Year));
    }

    [Fact]
    public async Task The_disclosure_refetch_imports_nothing()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync();

        // 2025 carries real months and a first-played date, and the anchor
        // endpoint answers for the same appid — so an implementation that let
        // the disclosure re-run the import would leave rows behind here.
        var history = new YearStub
        {
            PopulatedYears = { 2025 },
            GamesInPopulatedYears = true,
            AnchorMinutes = 900,
        };

        await Backfill(history).BackfillAsync();

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // Zero observation rows, by construction rather than by the identity
        // indexes swallowing a re-import.
        Assert.Null(await _playRecords.GetLatestAsync(_ownershipId));
        Assert.Null(await _snapshots.GetLatestAsync(_ownershipId));

        // And the year it re-read is still marked exactly as it was: re-reading
        // a finished year does not make it more finished.
        Assert.Equal(
            MarkerFor(games: 7), await _settings.GetAsync(YearMarkerKey(2025)));
    }

    [Fact]
    public async Task Nothing_is_refetched_once_the_ref_is_known()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync();

        // The steady state a confirmed install is in: ref and fingerprint
        // written together, as ConfirmAccountAsync writes them. A ref with no
        // fingerprint is deliberately NOT this state — nothing there proves the
        // ref belongs to the key in force, so it is cleared and re-earned.
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await _settings.SetAsync(
            SteamOwnedAccount.KeyFingerprintSettingKey, FakeSteamApiKeyProvider.HashOf("the-one-key"));

        var history = new YearStub { PopulatedYears = { 2025 } };

        await Backfill(history).BackfillAsync();

        // The repair is a repair, not a per-launch cost. Only the current year.
        Assert.Equal([2026], history.Asked.Select(a => a.Year));
    }

    // ══ AC#2 — a new key never inherits the previous ref ════════════════════

    [Fact]
    public async Task The_fingerprint_clear_runs_before_any_disclosure_refetch()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync();

        // The state a previous owner's key left behind.
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, "99999");
        await _settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, "an-older-key");

        // A different key, and a Steam that answers for somebody else entirely.
        var history = new YearStub
        {
            PopulatedYears = { 2025 },
            AnswersForAccountId = 777_777,
        };

        await Backfill(history, new FakeSteamApiKeyProvider("a-second-persons-key")).BackfillAsync();

        // The clear fired first, so there was no ref to inherit; the mismatch
        // then abandoned the account without writing one back.
        Assert.Null(SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.RefSettingKey)));
    }

    [Fact]
    public async Task A_changed_key_reads_fresh_rather_than_from_the_previous_keys_cache()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync();
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await _settings.SetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, "an-older-key");

        var history = new YearStub { PopulatedYears = { 2025 } };

        await Backfill(history, new FakeSteamApiKeyProvider("a-second-persons-key")).BackfillAsync();

        // Nothing records which key the cached bodies were fetched with, so the
        // disclosure must not be allowed to answer from them: a cached response
        // fetched with the PREVIOUS key would disclose the previous account and
        // hand back the identity the clear had just removed.
        var disclosure = Assert.Single(history.Asked, a => a.Year == 2025);
        Assert.Equal(TimeSpan.Zero, disclosure.CacheTtl);
    }

    [Fact]
    public async Task An_unchanged_key_uses_the_ordinary_cache()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);
        await MarkConfirmedAsync();

        // The live case: the key never changed, only the ref went missing.
        var keys = new FakeSteamApiKeyProvider("the-one-key");
        await _settings.SetAsync(
            SteamOwnedAccount.KeyFingerprintSettingKey, FakeSteamApiKeyProvider.HashOf("the-one-key"));

        var history = new YearStub { PopulatedYears = { 2025 } };

        await Backfill(history, keys).BackfillAsync();

        Assert.Equal(Mine, await _settings.GetAsync(SteamOwnedAccount.RefSettingKey));

        // Null means "the client's own 6-hour TTL", which is what respecting the
        // existing caching looks like from here.
        var disclosure = Assert.Single(history.Asked, a => a.Year == 2025);
        Assert.Null(disclosure.CacheTtl);
    }

    // ══ AC#3 — nothing to disclose from leaves the toggle disabled ══════════

    [Fact]
    public async Task An_account_with_no_populated_years_leaves_the_toggle_disabled()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: null);
        await MarkConfirmedAsync();

        var history = new YearStub();

        await Backfill(history).BackfillAsync();

        Assert.Null(SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.RefSettingKey)));

        // Not a request spent to be told the same nothing the current year said.
        Assert.Equal([2026], history.Asked.Select(a => a.Year));

        // And the panel says why, in the copy it already had.
        var panel = new StoresViewModel(
            new FakeStoreConnections(),
            accountVisibility: new AccountVisibilityService(
                _settings, new LibraryQueryRepository(_db.Factory)));

        await panel.RefreshCommand.ExecuteAsync(null);

        Assert.False(panel.CanChooseAccountScope);
        Assert.True(panel.ShowAccountScopeBlocked);
    }

    [Fact]
    public async Task An_account_that_never_confirmed_is_not_refetched_at_all()
    {
        await SeedAsync();
        await MarkFinishedAsync(populated: 2025);

        // No confirmed marker: nothing has ever proved a Year in Review answers
        // for this account, so there is no reason to think a re-read would
        // disclose what the current year did not.
        var history = new YearStub { PopulatedYears = { 2025 } };

        await Backfill(history).BackfillAsync();

        Assert.Equal([2026], history.Asked.Select(a => a.Year));
    }

    // ══ The marker parse ════════════════════════════════════════════════════

    [Theory]
    [InlineData("2026-08-30T00:00:00.0000000Z;games=7;written=12", 7)]
    [InlineData("2026-08-30T00:00:00.0000000Z;games=0;written=0", 0)]
    [InlineData("2026-08-30T00:00:00.0000000Z", null)]
    [InlineData("", null)]
    [InlineData("2026-08-30T00:00:00.0000000Z;games=;written=0", null)]
    public void The_games_figure_is_read_out_of_a_completion_marker(string marker, int? expected)
        => Assert.Equal(expected, SteamPlaytimeBackfillService.GamesRecordedIn(marker));

    // ══ Fixtures ════════════════════════════════════════════════════════════

    private static string MarkerFor(int games)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"2026-01-01T00:00:00.0000000Z;games={games};written=0");

    private string YearMarkerKey(int year)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{SteamPlaytimeBackfillService.YearMarkerPrefix}{_steamId.Value}.{year}");

    private Task MarkYearAsync(int year, int games)
        => _settings.SetAsync(YearMarkerKey(year), MarkerFor(games));

    /// <summary>
    /// The state the bug needs: EVERY year before the current one already
    /// imported and marked, so the ordinary loop has nothing left to fetch but
    /// the uncompiled current year. One of them may be populated; the rest
    /// answered empty and are marked as such.
    /// </summary>
    private async Task MarkFinishedAsync(int? populated)
    {
        for (var year = 2022; year <= Now.Year - 1; year++)
        {
            await MarkYearAsync(year, games: year == populated ? 7 : 0);
        }
    }

    private Task MarkConfirmedAsync()
        => _settings.SetAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SteamPlaytimeBackfillService.ConfirmedPrefix}{_steamId.Value}.confirmed"),
            "2026-01-01T00:00:00.0000000Z");

    /// <summary>One Steam ownership attributed to the account, so the pass has somebody to ask about.</summary>
    private async Task SeedAsync()
    {
        Assert.True(SteamId.TryParse(Mine, out _steamId));

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

        _ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
            AccountRef = Mine,
        });
    }

    private SteamPlaytimeBackfillService Backfill(
        YearStub history, FakeSteamApiKeyProvider? keys = null)
        => new(
            history,
            new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory),
            new OwnershipAccountRepository(_db.Factory),
            _playRecords,
            _snapshots,
            _settings,
            _db.Factory,
            new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions { FirstYear = 2022 },
            _clock,
            keys ?? new FakeSteamApiKeyProvider(),
            NullLogger<SteamPlaytimeBackfillService>.Instance);

    /// <summary>
    /// A Year in Review that discloses only for the years named, and records
    /// every year it was asked about along with the cache policy it was asked
    /// under.
    /// </summary>
    private sealed class YearStub : ISteamHistoryClient
    {
        /// <summary>Years whose Replay was compiled. Everything else answers empty.</summary>
        public HashSet<int> PopulatedYears { get; } = [];

        /// <summary>Whether a populated year carries importable games, not just an account id.</summary>
        public bool GamesInPopulatedYears { get; init; }

        /// <summary>Cumulative minutes the anchor endpoint reports, or null for no anchor.</summary>
        public long? AnchorMinutes { get; init; }

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

            if (!PopulatedYears.Contains(year))
            {
                // The uncompiled current year: answered, for the right account,
                // with nothing in it and no account id to read.
                return Task.FromResult(new SteamYearInReview(
                    steamId, year, Answered: true, AccountId: null, Games: [],
                    ObservedAt: Now, FromCache: false));
            }

            var games = GamesInPopulatedYears
                ?
                [
                    new SteamYearInReviewGame(
                        AppId,
                        [new SteamMonthlyPlaytime(year, 6, 120 * 60, 4)],
                        TotalPlaytimeSeconds: 120 * 60,
                        TotalSessions: 4,
                        FirstPlayedUtc: new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
                ]
                : (IReadOnlyList<SteamYearInReviewGame>)[];

            return Task.FromResult(new SteamYearInReview(
                steamId, year, Answered: true,
                AccountId: AnswersForAccountId ?? steamId.AccountId,
                Games: games,
                ObservedAt: Now, FromCache: false));
        }

        public Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
            => Task.FromResult(new SteamLastPlayedTimes(
                Answered: true,
                Games: AnchorMinutes is { } minutes
                    ?
                    [
                        new SteamLastPlayedGame(
                            AppId,
                            PlaytimeForeverMinutes: minutes,
                            LastPlayedUtc: null,
                            FirstPlayedUtc: null,
                            PlaytimeTwoWeeksMinutes: 0),
                    ]
                    : [],
                ObservedAt: Now,
                FromCache: false));
    }

}
