using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamPlaytimeReconciliationTests
{
    private static readonly DateTime Origin = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unknowns_and_first_source_readings_are_baselines_not_activity()
    {
        var observations = new[]
        {
            O(1, 0, null), O(2, 10, 100), O(3, 20, 1000, "steam_web_api"),
            O(4, 30, 1000), O(5, 40, 1010, "steam_web_api"), O(6, 50, 1010),
        };
        var activity = Assert.Single(Read(observations));
        Assert.Equal(5, activity.Id);
        Assert.Equal(10, activity.SteamDeltaMinutes);
        Assert.Equal(10, activity.UnexplainedMinutes);
    }

    [Fact]
    public void Settling_waits_thirty_minutes_from_the_increase()
    {
        var observations = new[] { O(1, 0, 100), O(2, 10, 110) };
        Assert.Empty(SteamPlaytimeReconciler.Reconcile(observations, [], Origin.AddMinutes(40).AddTicks(-1)));
        Assert.Single(SteamPlaytimeReconciler.Reconcile(observations, [], Origin.AddMinutes(40)));
    }

    [Fact]
    public void Decreases_and_rebounds_cannot_recount_prior_play()
    {
        var observations = new[] { O(1, 0, 100), O(2, 10, 80), O(3, 20, 100), O(4, 30, 105) };
        Assert.Equal(5, Assert.Single(Read(observations)).SteamDeltaMinutes);
        Assert.Equal(Read(observations), Read(observations.Reverse().ToArray()));
    }

    [Fact]
    public void Session_credit_is_available_for_delayed_increments_but_spent_once()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 120), O(3, 120, 140) };
        var rows = Read(observations, [S(10, 30)]);
        Assert.Equal(20, rows.Single(r => r.Id == 2).MatchedRecordedMinutes);
        Assert.Equal(0, rows.Single(r => r.Id == 2).UnexplainedMinutes);
        Assert.Equal(0, rows.Single(r => r.Id == 3).MatchedRecordedMinutes);
        Assert.Equal(20, rows.Single(r => r.Id == 3).UnexplainedMinutes);
    }

    [Fact]
    public void Late_session_writes_recompute_evidence_without_changing_identity_or_observations()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 120) };
        var before = Assert.Single(Read(observations));
        var after = Assert.Single(Read(observations, [S(10, 30)]));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(20, before.UnexplainedMinutes);
        Assert.Equal(0, after.UnexplainedMinutes);
        Assert.Equal(120, observations[1].PlaytimeMinutes);
    }

    [Fact]
    public void Open_monitored_session_retains_uncertain_evidence_until_it_closes()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 120) };
        var open = S(10, 30) with { EndedAt = null, DurationSeconds = null, MonitorKey = "open" };
        var uncertain = Assert.Single(Read(observations, [open]));
        Assert.True(uncertain.ComparisonUnavailable);
        Assert.Null(uncertain.UnexplainedMinutes);
        Assert.Equal(0, Assert.Single(Read(observations, [S(10, 30)])).UnexplainedMinutes);
    }

    [Fact]
    public void Higher_source_baseline_cannot_spend_older_sessions_again()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 130, "steam_web_api"), O(3, 120, 140) };
        Assert.Equal(10, Assert.Single(Read(observations, [S(10, 40)])).UnexplainedMinutes);
    }

    [Fact]
    public void Future_ended_sessions_do_not_change_an_earlier_read_and_minute_rounding_is_tolerated()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 131) };
        var earlier = Assert.Single(SteamPlaytimeReconciler.Reconcile(observations, [S(10, 200)], Origin.AddMinutes(90)));
        Assert.Equal(31, earlier.UnexplainedMinutes);
        Assert.Equal(0, Assert.Single(Read(observations, [S(10, 40)])).UnexplainedMinutes);
    }

    [Fact]
    public async Task Known_account_membership_without_totals_still_prevents_guessing_session_account()
    {
        using var db = new TempDatabase();
        var ownershipId = await Seed(db);
        var repository = new SteamPlaytimeObservationRepository(db.Factory);
        await repository.ObserveAsync(O(0, 0, 100) with { OwnershipId = ownershipId });
        await repository.ObserveAsync(O(0, 60, 120) with { OwnershipId = ownershipId });
        using (var lease = db.Factory.Lease())
        {
            await lease.Connection.ExecuteAsync("""
                INSERT INTO ownership_accounts (ownership_id, account_ref, source, first_seen_at, last_seen_at)
                VALUES (@ownershipId, '2', 'steam_local', @at, @at);
                """, new { ownershipId, at = Origin });
        }
        var row = Assert.Single(await repository.GetActivityAsync([ownershipId], Origin.AddDays(1), "1"));
        Assert.True(row.ComparisonUnavailable);
        Assert.Null(row.UnexplainedMinutes);
    }

    [Fact]
    public void Account_filter_never_hides_comparison_ambiguity_or_reuses_sessions()
    {
        var observations = new[]
        {
            O(1, 0, 100), O(2, 60, 120),
            O(3, 0, 500) with { AccountRef = "2" }, O(4, 60, 520) with { AccountRef = "2" },
        };
        var rows = Read(observations, [S(10, 30)]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.True(r.ComparisonUnavailable);
            Assert.Null(r.MatchedRecordedMinutes);
            Assert.Null(r.UnexplainedMinutes);
        });
        var filtered = Assert.Single(SteamPlaytimeReconciler.Reconcile(observations, [S(10, 30)], Origin.AddDays(1), "1"));
        Assert.True(filtered.ComparisonUnavailable);
    }

    [Fact]
    public void Sessions_before_baseline_and_after_observation_do_not_cover_an_increase()
    {
        var observations = new[] { O(1, 0, 100), O(2, 60, 120) };
        var row = Assert.Single(Read(observations, [S(-40, -20), S(70, 90)]));
        Assert.Equal(20, row.UnexplainedMinutes);
    }

    [Fact]
    public async Task Repository_keeps_change_points_raw_decreases_and_account_scope_without_session_writes()
    {
        using var db = new TempDatabase();
        var ownershipId = await Seed(db);
        var repository = new SteamPlaytimeObservationRepository(db.Factory);
        var baseline = O(0, 0, 100) with { OwnershipId = ownershipId, AccountRef = "0001" };
        await repository.ObserveAsync(baseline);
        await repository.ObserveAsync(baseline);
        await repository.ObserveAsync(baseline with { ObservedAt = Origin.AddMinutes(10) });
        var increase = baseline with { PlaytimeMinutes = 120, ObservedAt = Origin.AddMinutes(60), LastPlayedAt = Origin.AddMinutes(50) };
        await repository.ObserveAsync(increase);
        await repository.ObserveAsync(increase with { ObservedAt = Origin.AddMinutes(70) });
        await repository.ObserveAsync(increase with { PlaytimeMinutes = 90, ObservedAt = Origin.AddMinutes(80) });
        var raw = await repository.GetByOwnershipAsync(ownershipId);
        Assert.Equal(3, raw.Count);
        Assert.All(raw, row => Assert.Equal("1", row.AccountRef));
        Assert.Equal(90, raw[2].PlaytimeMinutes);
        Assert.Equal(Origin.AddMinutes(50), raw[1].LastPlayedAt);
        var row = Assert.Single(await repository.GetActivityAsync([ownershipId], Origin.AddDays(1), "0001"));
        Assert.Equal(20, row.UnexplainedMinutes);
        Assert.Equal(raw[1].Id, row.Id);
        Assert.Empty(await repository.GetActivityAsync([ownershipId], Origin.AddDays(1), "2"));
        Assert.Empty(await new SessionRepository(db.Factory).GetByOwnershipAsync(ownershipId));
        Assert.Empty(await new PlayRecordRepository(db.Factory).GetByOwnershipAsync(ownershipId));
    }

    [Fact]
    public async Task Repository_preserves_out_of_order_and_unknown_observations_and_reconciles_late_sessions()
    {
        using var db = new TempDatabase();
        var ownershipId = await Seed(db);
        var repository = new SteamPlaytimeObservationRepository(db.Factory);
        foreach (var observation in new[] { O(0, 60, 120), O(0, 0, null), O(0, 10, 100) })
            await repository.ObserveAsync(observation with { OwnershipId = ownershipId });
        var before = Assert.Single(await repository.GetActivityAsync([ownershipId], Origin.AddDays(1)));
        Assert.Equal(20, before.UnexplainedMinutes);
        await new SessionRepository(db.Factory).InsertAsync(S(20, 40) with { OwnershipId = ownershipId });
        var after = Assert.Single(await repository.GetActivityAsync([ownershipId], Origin.AddDays(1)));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(0, after.UnexplainedMinutes);
        Assert.Equal(3, (await repository.GetByOwnershipAsync(ownershipId)).Count);
    }

    [Fact]
    public async Task Observation_participates_in_the_callers_transaction()
    {
        using var db = new TempDatabase();
        var ownershipId = await Seed(db);
        using (var transaction = db.Factory.Begin())
        {
            await new SteamPlaytimeObservationRepository(db.Factory).ObserveAsync(O(0, 0, 100) with { OwnershipId = ownershipId });
        }
        Assert.Empty(await new SteamPlaytimeObservationRepository(db.Factory).GetByOwnershipAsync(ownershipId));
    }

    [Theory]
    [InlineData("steam_historical", "1", 100)]
    [InlineData("steam_local", "0", 100)]
    [InlineData("steam_local", "unknown", 100)]
    [InlineData("steam_local", "1", -1)]
    public async Task Invalid_observations_cannot_enter_the_live_history(string source, string account, long total)
    {
        using var db = new TempDatabase();
        var ownershipId = await Seed(db);
        var repository = new SteamPlaytimeObservationRepository(db.Factory);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.ObserveAsync(O(0, 0, total, source)
            with { OwnershipId = ownershipId, AccountRef = account }));
        Assert.Empty(await repository.GetByOwnershipAsync(ownershipId));
    }

    private static IReadOnlyList<SteamReportedActivity> Read(SteamPlaytimeObservation[] observations, Session[]? sessions = null)
        => SteamPlaytimeReconciler.Reconcile(observations, sessions ?? [], Origin.AddDays(1));

    private static SteamPlaytimeObservation O(long id, int minute, long? total, string source = "steam_local")
        => new() { Id = id, OwnershipId = 1, AccountRef = "1", Source = source,
            PlaytimeMinutes = total, ObservedAt = Origin.AddMinutes(minute) };

    private static Session S(int start, int end)
        => new() { OwnershipId = 1, StartedAt = Origin.AddMinutes(start), EndedAt = Origin.AddMinutes(end),
            DurationSeconds = (end - start) * 60, DetectionMethod = DetectionMethods.ProcessWatch };

    private static async Task<long> Seed(TempDatabase db)
    {
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Game" });
        var release = await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = work, Name = "Game" });
        return await new OwnershipRepository(db.Factory).InsertAsync(new Ownership { ReleaseId = release, Store = "steam", Installed = true });
    }
}
