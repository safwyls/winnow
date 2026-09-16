using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamPlaytimeCaptureTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly SteamPlaytimeObservationRepository _observations;
    private readonly ExternalIdResolver _resolver;
    private static readonly DateTime Start = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    public SteamPlaytimeCaptureTests()
    {
        _releases = new(_db.Factory);
        _ownerships = new(_db.Factory);
        _observations = new(_db.Factory);
        _resolver = new(new WorkRepository(_db.Factory), _releases, _ownerships,
            new PlayRecordRepository(_db.Factory), new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory, new OwnershipAccountRepository(_db.Factory), steamObservations: _observations);
    }

    public void Dispose() => _db.Dispose();

    private static CandidateOwnership Candidate(long? minutes = 100, string source = "steam_local") =>
        new("steam", "1234", "Test game", "12345678", "C:\\Games\\Test", true,
            minutes, Start.AddMinutes(-5), null, source, Start);

    private async Task<long> OwnershipIdAsync()
    {
        var release = (await _releases.FindByExternalIdAsync("steam", "1234"))!;
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id)).Id;
    }

    [Fact]
    public async Task Raw_accounts_and_sources_survive_household_coalescing()
    {
        var local = Candidate() with
        {
            Accounts = [new("12345678", 100, Start.AddMinutes(-5)), new("87654321", 900, Start.AddDays(-2))],
        };
        var remote = Candidate(105, "steam_web_api") with
        {
            Installed = null, ObservedAt = Start.AddMinutes(1),
            Accounts = [new("12345678", 105, Start)],
        };
        await _resolver.ResolveAsync([local, remote]);
        var rows = await _observations.GetByOwnershipAsync(await OwnershipIdAsync());
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.AccountRef == "12345678" && row.Source == "steam_local"
            && row.PlaytimeMinutes == 100 && row.LastPlayedAt == Start.AddMinutes(-5));
        Assert.Contains(rows, row => row.AccountRef == "87654321" && row.Source == "steam_local"
            && row.PlaytimeMinutes == 900 && row.LastPlayedAt == Start.AddDays(-2));
        Assert.Contains(rows, row => row.AccountRef == "12345678" && row.Source == "steam_web_api"
            && row.PlaytimeMinutes == 105 && row.ObservedAt == Start.AddMinutes(1));
    }

    [Fact]
    public async Task Lower_bound_clamping_does_not_rewrite_raw_observations()
    {
        await _resolver.ResolveAsync([Candidate(100)]);
        await _resolver.ResolveAsync([Candidate(70) with { ObservedAt = Start.AddMinutes(15) }],
            playtime: PlaytimeView.LowerBound);
        var id = await OwnershipIdAsync();
        Assert.Contains(await _observations.GetByOwnershipAsync(id), row => row.PlaytimeMinutes == 70);
        Assert.Equal(100, (await new PlaytimeSnapshotRepository(_db.Factory).GetLatestAsync(id))!.PlaytimeMinutes);
    }

    [Theory]
    [InlineData("steam_yir")]
    [InlineData("steam_first_played")]
    [InlineData("steam_local+carried")]
    public async Task Historical_or_synthetic_sources_do_not_enter_live_reconciliation(string source)
    {
        await _resolver.ResolveAsync([Candidate(100, source)]);
        Assert.Empty(await _observations.GetByOwnershipAsync(await OwnershipIdAsync()));
    }

    [Fact]
    public async Task Remote_read_requires_known_installation_and_preserves_measurement_time()
    {
        var remote = Candidate(100, "steam_web_api") with { Installed = null, InstallPath = null };
        await _resolver.ResolveAsync([remote]);
        var id = await OwnershipIdAsync();
        Assert.Empty(await _observations.GetByOwnershipAsync(id));
        await _resolver.ResolveAsync([Candidate()]);
        await _resolver.ResolveAsync([remote with
        {
            ObservedAt = Start.AddHours(1), PlaytimeObservedAt = Start.AddMinutes(1), PlaytimeMinutes = 110,
        }]);
        var row = Assert.Single(await _observations.GetByOwnershipAsync(id), r => r.Source == "steam_web_api");
        Assert.Equal(Start.AddMinutes(1), row.ObservedAt);
        await _resolver.ResolveAsync([Candidate(120) with { Installed = false, ObservedAt = Start.AddHours(2) }]);
        Assert.DoesNotContain(await _observations.GetByOwnershipAsync(id), r => r.PlaytimeMinutes == 120);
    }

    [Fact]
    public async Task Unknown_minutes_and_unknown_accounts_are_not_zero_baselines()
    {
        await _resolver.ResolveAsync([Candidate(null)]);
        await _resolver.ResolveAsync([Candidate() with { AccountRef = null }]);
        Assert.Empty(await _observations.GetByOwnershipAsync(await OwnershipIdAsync()));
    }

    [Fact]
    public async Task Observation_failure_rolls_back_the_entire_import()
    {
        var invalid = Candidate() with
        {
            ProviderId = "9999", PlaytimeObservedAt = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _resolver.ResolveAsync([Candidate(), invalid]));
        Assert.Null(await _releases.FindByExternalIdAsync("steam", "1234"));
        Assert.Null(await _releases.FindByExternalIdAsync("steam", "9999"));
        Assert.Empty(await _ownerships.GetAllAsync());
    }

    [Fact]
    public async Task Account_number_spelling_does_not_split_the_history()
    {
        await _resolver.ResolveAsync([Candidate() with { AccountRef = "0012345678" }]);
        await _resolver.ResolveAsync([Candidate(120) with { ObservedAt = Start.AddHours(1) }]);
        var rows = await _observations.GetByOwnershipAsync(await OwnershipIdAsync());
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("12345678", row.AccountRef));
    }

    [Fact]
    public async Task Steam_rise_without_a_recorded_session_becomes_approximate_activity_after_settling()
    {
        await _resolver.ResolveAsync([Candidate(2042)]);
        var rise = Candidate(2073) with { ObservedAt = Start.AddHours(1), LastPlayedAt = Start.AddMinutes(59) };
        await _resolver.ResolveAsync([rise]);
        var id = await OwnershipIdAsync();
        Assert.Empty(await _observations.GetActivityAsync([id], Start.AddHours(1).AddMinutes(10)));
        var activity = Assert.Single(await _observations.GetActivityAsync([id], Start.AddHours(2)));
        Assert.Equal(31, activity.SteamDeltaMinutes);
        Assert.Equal(31d, activity.UnexplainedMinutes);
        Assert.Empty(await new SessionRepository(_db.Factory).GetByOwnershipAsync(id));
        await _resolver.ResolveAsync([rise]);
        Assert.Single(await _observations.GetActivityAsync([id], Start.AddHours(2)));
    }
}
