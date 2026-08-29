using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Resolve;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// M5 end to end below the wire: the real repositories over a real migrated
/// database, the real reconstruction, the real completion markers. Only the two
/// HTTP calls are substituted.
///
/// <para>What these pin is the contract the backfill was written against:
/// historical points go in through <c>TryAppendAsync</c> and are judged on their
/// own identity, never through <see cref="ExternalIdResolver"/>, which compares
/// against the newest row and would rewrite four years of history up to
/// today.</para>
/// </summary>
public sealed class SteamPlaytimeBackfillTests : IDisposable
{
    private const string Enshrouded = "1203620";
    private const string Enderal = "933480";

    /// <summary>
    /// An appid Steam reports months for and this library has no row for:
    /// a delisted app, or a title played on a shared account.
    /// </summary>
    private const string Unowned = "555550000";

    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly SettingsRepository _settings;
    private readonly ExternalIdResolver _resolver;
    private readonly FakeTimeProvider _clock = new(Now);

    public SteamPlaytimeBackfillTests()
    {
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);
        _snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        _settings = new SettingsRepository(_db.Factory);
        _resolver = new ExternalIdResolver(
            new WorkRepository(_db.Factory),
            _releases,
            _ownerships,
            _playRecords,
            _snapshots,
            _db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The worked case, all the way through. Two years of Year in Review against
    /// the anchor from <c>ClientGetLastPlayedTimes</c>, landing as a cumulative
    /// series that ends exactly on the figure the ordinary sync already wrote.
    /// </summary>
    [Fact]
    public async Task Two_years_of_year_in_review_reconstruct_a_cumulative_series()
    {
        await SeedLibraryAsync();
        var service = Backfill();

        var report = await service.BackfillAsync();

        Assert.Equal(1, report.Accounts);
        Assert.Equal(2, report.GamesReconstructed);
        Assert.Equal(0, report.SkippedNoAnchor);

        // The appid Steam reported that this library has no ownership for is
        // counted, not resolved: this job never creates works or releases.
        Assert.Equal(1, report.SkippedNoOwnership);

        var series = (await _snapshots.GetByOwnershipAsync(await OwnershipAsync(Enshrouded)))
            .Select(s => (s.ObservedAt, s.PlaytimeMinutes))
            .ToList();

        Assert.Equal(
            [
                (new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc), 317L),
                (new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc), 417L),
                (new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Utc), 617L),
                (new DateTime(2024, 5, 31, 23, 59, 59, DateTimeKind.Utc), 667L),
                (new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), 817L),

                // The point the ordinary sync wrote before the backfill ran. The
                // history slots in UNDERNEATH it, in order, and does not touch
                // it.
                (Now, 817L),
            ],
            series);
    }

    /// <summary>
    /// The guarantee the whole design turns on: a historical import must not be
    /// read as a change to the present. If these points had gone through the
    /// resolver, <c>PlaytimeView.LowerBound</c> would have clamped every one of
    /// them up to 817 and four years of history would have become four years of
    /// today.
    /// </summary>
    [Fact]
    public async Task The_backfill_does_not_rewrite_history_up_to_the_present()
    {
        await SeedLibraryAsync();
        await Backfill().BackfillAsync();

        var ownershipId = await OwnershipAsync(Enshrouded);
        var minutes = (await _snapshots.GetByOwnershipAsync(ownershipId))
            .Select(s => s.PlaytimeMinutes)
            .ToList();

        Assert.NotEqual(1, minutes.Distinct().Count());
        Assert.Equal(minutes, minutes.Order());

        // The present is still the present: the newest play record is the
        // sync's, not one of the backfill's historical rows.
        var latest = await _playRecords.GetLatestAsync(ownershipId);
        Assert.NotNull(latest);
        Assert.Equal(Now, latest.ObservedAt);
        Assert.Equal(817, latest.PlaytimeMinutes);
    }

    /// <summary>
    /// Re-running the import writes nothing. Every point is idempotent on its
    /// own identity, so a user who launches Winnow four times a day does not
    /// grow the table four times a day.
    /// </summary>
    [Fact]
    public async Task A_re_run_is_a_no_op()
    {
        await SeedLibraryAsync();
        var service = Backfill();

        var first = await service.BackfillAsync();
        Assert.True(first.WroteAnything);

        var before = (await _snapshots.GetByOwnershipAsync(await OwnershipAsync(Enshrouded))).Count;

        var second = await service.BackfillAsync();
        var third = await service.BackfillAsync();

        Assert.False(second.WroteAnything);
        Assert.False(third.WroteAnything);
        Assert.Equal(0, second.SnapshotsWritten);
        Assert.Equal(0, second.PlayRecordsWritten);
        Assert.Equal(before, (await _snapshots.GetByOwnershipAsync(await OwnershipAsync(Enshrouded))).Count);
    }

    /// <summary>
    /// Completed years are asked about once. The current year is refetched every
    /// pass because it is still accruing, so a marker written in August would
    /// freeze the series at August.
    /// </summary>
    [Fact]
    public async Task Completed_years_are_never_refetched_and_the_current_year_always_is()
    {
        await SeedLibraryAsync();
        var client = new HistoryStub();
        var service = Backfill(client);

        await service.BackfillAsync();

        // 2022 through 2026 inclusive, on a clock standing in 2026.
        Assert.Equal([2022, 2023, 2024, 2025, 2026], client.YearsAsked);

        client.YearsAsked.Clear();
        await service.BackfillAsync();

        Assert.Equal([2026], client.YearsAsked);
    }

    /// <summary>
    /// A year that did not answer is retried; only an answered year is marked
    /// done. Confusing the two either loses a year of history for the life of
    /// the install or refetches it forever.
    /// </summary>
    [Fact]
    public async Task A_failed_year_is_retried_while_an_empty_one_is_recorded_as_complete()
    {
        await SeedLibraryAsync();
        var client = new HistoryStub { FailingYears = { 2023 } };
        var service = Backfill(client);

        var report = await service.BackfillAsync();
        Assert.Equal(1, report.YearsFailed);

        // 2022 answered empty and is done. 2023 did not answer and comes back.
        Assert.Null(await Marker(2023));
        Assert.NotNull(await Marker(2022));

        client.YearsAsked.Clear();
        client.FailingYears.Clear();
        await service.BackfillAsync();

        Assert.Equal([2023, 2026], client.YearsAsked);
        Assert.NotNull(await Marker(2023));
    }

    /// <summary>
    /// The safety check. The API key identifies whose history Steam returns, and
    /// a machine with two Steam accounts has exactly one key: importing a
    /// mismatched response would write one person's play onto another's
    /// ownerships. Nothing is written and no year is marked, so a corrected key
    /// retries cleanly.
    /// </summary>
    [Fact]
    public async Task A_response_for_another_account_imports_nothing_and_marks_nothing()
    {
        await SeedLibraryAsync();
        var service = Backfill(new HistoryStub { AccountIdOverride = 99999999 });

        var report = await service.BackfillAsync();

        Assert.False(report.WroteAnything);
        Assert.Equal(0, report.YearsCompleted);
        Assert.Null(await Marker(2024));
        Assert.Single(await _snapshots.GetByOwnershipAsync(await OwnershipAsync(Enshrouded)));
    }

    /// <summary>
    /// <c>first_playtime</c> is 0 for many entries and 0 means "not tracked".
    /// Three of the five fixture entries carry it, and none of them may become a
    /// 1970 first-played record.
    /// </summary>
    [Fact]
    public async Task A_zero_first_playtime_writes_no_record()
    {
        await SeedLibraryAsync();
        await Backfill().BackfillAsync();

        // Enshrouded's first_playtime is 2024-01-01; appid 10's is 0.
        var enshrouded = await _playRecords.GetByOwnershipAsync(await OwnershipAsync(Enshrouded));
        Assert.Contains(
            enshrouded,
            r => r.Source == SteamHistorySources.FirstPlayed
                && r.LastPlayedAt == new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var untracked = await _playRecords.GetByOwnershipAsync(await OwnershipAsync("10"));
        Assert.DoesNotContain(untracked, r => r.Source == SteamHistorySources.FirstPlayed);
        Assert.DoesNotContain(untracked, r => r.Source == SteamHistorySources.YearInReview);

        // The failure mode this guards: 0 read as a timestamp rather than as
        // "not tracked" would date every such game's first play to 1970.
        Assert.All(untracked, r => Assert.True(r.LastPlayedAt is null or { Year: > 1980 }));
    }

    /// <summary>
    /// The two first-played facts come from different endpoints and are labelled
    /// separately, so a row can always be traced back to the call that produced
    /// it. Both are historical, so neither may displace the present.
    /// </summary>
    [Fact]
    public async Task First_played_records_carry_their_own_source_and_their_own_observed_at()
    {
        await SeedLibraryAsync();
        await Backfill().BackfillAsync();

        var records = await _playRecords.GetByOwnershipAsync(await OwnershipAsync(Enshrouded));
        var firstPlayed = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var fromYearInReview = Assert.Single(records, r => r.Source == SteamHistorySources.YearInReview);
        var fromLastPlayed = Assert.Single(records, r => r.Source == SteamHistorySources.FirstPlayed);

        // Each point's OWN historical observed_at, not the import time.
        Assert.Equal(firstPlayed, fromYearInReview.ObservedAt);
        Assert.Equal(firstPlayed, fromLastPlayed.ObservedAt);

        // Zero minutes, because at the instant of a first launch the cumulative
        // counter was, to within one session, zero, and no source reported
        // anything else.
        Assert.Equal(0, fromYearInReview.PlaytimeMinutes);
        Assert.Equal(0, fromLastPlayed.PlaytimeMinutes);

        var latest = await _playRecords.GetLatestAsync(await OwnershipAsync(Enshrouded));
        Assert.Equal(Now, latest!.ObservedAt);
    }

    /// <summary>
    /// The bucket hazard, refused at the source. <c>latest_play</c> reads the
    /// displayed playtime and the dormancy signal off whichever row sorts
    /// newest, so a zero-minute 2024 record landing on an ownership with no
    /// newer row would make an 817-minute game read as never played.
    /// </summary>
    [Fact]
    public async Task A_first_played_record_is_refused_when_it_would_become_the_newest_row()
    {
        // An ownership with no play record at all: the state between the
        // resolver creating a row and any source reporting playtime for it.
        var workId = await new WorkRepository(_db.Factory).InsertAsync(
            new Work { Name = "Enshrouded", NameIsProvisional = false });
        var releaseId = await _releases.InsertAsync(
            new Release { WorkId = workId, Name = "Enshrouded", Platform = "pc" });
        await _releases.AddExternalIdAsync(
            new ExternalId { ReleaseId = releaseId, Provider = "steam", ProviderId = Enshrouded });
        var ownershipId = await _ownerships.UpsertAsync(new OwnershipUpsert(
            ReleaseId: releaseId,
            Store: ExternalIdProviders.Steam,
            AccountRef: SteamWebFixtures.FixtureAccountId.ToString(),
            AcquiredAt: null,
            InstallPath: null,
            Installed: null));

        await Backfill().BackfillAsync();

        // Nothing was written that could be read as "played once in 2024, zero
        // minutes, and that is the latest we know".
        Assert.Empty(await _playRecords.GetByOwnershipAsync(ownershipId));

        // The series itself is unaffected: snapshots carry no last-played date
        // and cannot move a bucket, so the history still lands.
        Assert.NotEmpty(await _snapshots.GetByOwnershipAsync(ownershipId));
    }

    /// <summary>
    /// An unconfigured install pays nothing. "Registered and idle" is the common
    /// state and the backfill must not walk the ownership table to discover it.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_backfill_makes_no_request_and_writes_nothing()
    {
        await SeedLibraryAsync();
        var client = new HistoryStub { Configured = false };

        var report = await Backfill(client).BackfillAsync();

        Assert.Equal(0, report.Accounts);
        Assert.Empty(client.YearsAsked);
        Assert.False(client.AnchorsAsked);
        Assert.False(report.WroteAnything);
    }

    /// <summary>
    /// A library with no Steam ownership has no account to ask about, so the
    /// pass ends before any request.
    /// </summary>
    [Fact]
    public async Task An_empty_library_asks_about_no_account()
    {
        var client = new HistoryStub();

        var report = await Backfill(client).BackfillAsync();

        Assert.Equal(0, report.Accounts);
        Assert.Empty(client.YearsAsked);
    }

    /// <summary>
    /// Months without a cumulative total to anchor them are counted and left
    /// alone. Reconstructing from an assumed baseline is the forward-walk
    /// mistake the design rejects.
    /// </summary>
    [Fact]
    public async Task Months_with_no_anchor_are_skipped_rather_than_guessed_at()
    {
        await SeedLibraryAsync();
        var report = await Backfill(new HistoryStub { WithholdAnchorFor = Enderal }).BackfillAsync();

        Assert.Equal(1, report.SkippedNoAnchor);
        Assert.Single(await _snapshots.GetByOwnershipAsync(await OwnershipAsync(Enderal)));
    }

    /// <summary>
    /// A year fetched but never imported must not be marked complete, or the
    /// months it carried are lost for the life of the install.
    /// </summary>
    [Fact]
    public async Task An_anchor_failure_leaves_every_year_pending()
    {
        await SeedLibraryAsync();
        var client = new HistoryStub { AnchorsAnswer = false };

        var report = await Backfill(client).BackfillAsync();

        Assert.False(report.WroteAnything);
        Assert.Equal(0, report.YearsCompleted);
        Assert.Null(await Marker(2024));
    }

    private async Task<string?> Marker(int year)
        => await _settings.GetAsync(
            $"steam.backfill.yir.{SteamId.FromAccountId(SteamWebFixtures.FixtureAccountId)!.Value.Value}.{year}");

    private SteamPlaytimeBackfillService Backfill(HistoryStub? client = null)
        => new(
            client ?? new HistoryStub(),
            _releases,
            _ownerships,
            _playRecords,
            _snapshots,
            _settings,
            _db.Factory,
            new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions(),
            _clock,
            NullLogger<SteamPlaytimeBackfillService>.Instance);

    private async Task<long> OwnershipAsync(string appId)
    {
        var release = await _releases.FindByExternalIdAsync(ExternalIdProviders.Steam, appId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id)).Id;
    }

    /// <summary>
    /// The library as the sync jobs leave it: ownerships with a present-day play
    /// record apiece, which is the state the backfill is designed to attach
    /// history underneath.
    /// </summary>
    private async Task SeedLibraryAsync()
    {
        await _resolver.ResolveAsync(
            [
                Candidate(Enshrouded, "Enshrouded", 817),
                Candidate(Enderal, "Enderal", 100),
                Candidate("10", "Counter-Strike", 358),
            ]);
    }

    private static CandidateOwnership Candidate(string appId, string title, long minutes)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: SteamWebFixtures.FixtureAccountId.ToString(),
            InstallPath: null,
            Installed: null,
            PlaytimeMinutes: minutes,
            LastPlayedAt: Now,
            AcquiredAt: null,
            Source: SteamWebApiClient.SourceName,
            ObservedAt: Now);

    /// <summary>
    /// Stands in for the two HTTP calls and nothing else: the bodies are the
    /// canned fixtures, parsed by the real parser.
    /// </summary>
    private sealed class HistoryStub : ISteamHistoryClient
    {
        public bool Configured { get; init; } = true;

        public bool AnchorsAnswer { get; init; } = true;

        /// <summary>An appid to strip from the anchor response, to exercise the no-anchor path.</summary>
        public string? WithholdAnchorFor { get; init; }

        /// <summary>Forces every answered year to report a different account.</summary>
        public uint? AccountIdOverride { get; init; }

        public HashSet<int> FailingYears { get; } = [];

        public List<int> YearsAsked { get; } = [];

        public bool AnchorsAsked { get; private set; }

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Configured);

        public Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            AnchorsAsked = true;
            if (!AnchorsAnswer)
            {
                return Task.FromResult(SteamLastPlayedTimes.Unanswered(Now));
            }

            var games = SteamHistoryJson.TryReadLastPlayedTimes(SteamWebFixtures.LastPlayedTimes())!
                .Where(g => g.AppId != WithholdAnchorFor)
                .ToArray();

            return Task.FromResult(new SteamLastPlayedTimes(true, games, Now, FromCache: false));
        }

        public Task<SteamYearInReview> GetYearInReviewAsync(
            SteamId steamId, int year, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            YearsAsked.Add(year);

            if (FailingYears.Contains(year))
            {
                return Task.FromResult(SteamYearInReview.Unanswered(steamId, year, Now));
            }

            var body = year switch
            {
                2024 => SteamWebFixtures.YearInReview2024(),
                2025 => SteamWebFixtures.YearInReview2025(),
                _ => null,
            };

            if (body is null)
            {
                // An answered-but-empty year: the bare envelope, which is what a
                // year with no Steam Replay looks like.
                return Task.FromResult(new SteamYearInReview(
                    steamId, year, Answered: true, AccountId: null, Games: [], Now, FromCache: false));
            }

            var payload = SteamHistoryJson.TryReadYearInReview(body)!.Value;
            return Task.FromResult(new SteamYearInReview(
                steamId,
                year,
                Answered: true,
                AccountId: AccountIdOverride ?? payload.AccountId,
                Games: payload.Games,
                Now,
                FromCache: false));
        }
    }
}
