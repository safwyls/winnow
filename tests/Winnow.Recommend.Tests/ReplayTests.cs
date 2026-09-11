using System.Globalization;
using System.Text.Json;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Replay;
using Xunit;

namespace Winnow.Recommend.Tests;

public sealed class ReplayTests
{
    private static readonly DateTime AsOf = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NamedTuning[] Tunings =
    [new("default", RecommendationTuning.Default), new("installed", RecommendationTuning.Default with { WeightInstalled = 2 })];

    [Fact]
    public async Task Capture_is_transactionally_consistent_and_later_source_writes_do_not_enter_replay()
    {
        using var fixture = new ReplayFixture();
        var game = await fixture.Seed("Captured game", installed: true);
        var clock = new FixedClock(AsOf, () =>
        {
            using var writer = fixture.Database.Factory.Open();
            writer.Execute("UPDATE ownerships SET installed=0; UPDATE works SET name='Later title';");
        });
        var manifest = SnapshotBundle.Capture(fixture.Database.DatabasePath, fixture.CapturePath, clock);
        using var snapshot = SnapshotBundle.Open(fixture.CapturePath, manifest.AsOfUtc);
        var library = await new LibraryQueryRepository(snapshot.Factory).GetSnapshotAsync(BucketThresholds.Default, AsOf);
        Assert.True(Assert.Single(library.Ownerships).Installed);
        Assert.Equal("Captured game", Assert.Single(library.Works).Name);
        using var source = fixture.Database.Factory.Open();
        Assert.Equal(0, source.ExecuteScalar<int>("SELECT installed FROM ownerships WHERE id=@Id;", new { Id = game.OwnershipId }));
    }

    [Fact]
    public async Task Achievement_evidence_keeps_baseline_ranking_accounts_platforms_and_retirement_separate()
    {
        using var fixture = new ReplayFixture();
        var zero = await fixture.Seed("Known zero unlocks", minutes: 300);
        var partial = await fixture.Seed("Partial achievements", minutes: 300);
        var complete = await fixture.Seed("All achievements but below retirement", minutes: 300);
        var unavailable = await fixture.Seed("Private achievements", minutes: 300);
        var noSchema = await fixture.Seed("Confirmed no schema");
        var unknown = await fixture.Seed("Not fetched");
        var retired = await fixture.Seed("Retired by playtime", minutes: 6000);
        var epic = await new ReleaseRepository(fixture.Database.Factory).InsertAsync(new Release
            { WorkId = partial.WorkId, Name = "Epic copy" });
        await new OwnershipRepository(fixture.Database.Factory).InsertAsync(new Ownership { ReleaseId = epic, Store = "epic" });
        await new SettingsRepository(fixture.Database.Factory).SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
        var without = Path.Combine(fixture.Root, "without-achievements");
        SnapshotBundle.Capture(fixture.Database.DatabasePath, without, new FixedClock(AsOf));

        var repository = new AchievementRepository(fixture.Database.Factory);
        var evidence = new AchievementFetch
        {
            AttemptedAt = AsOf.AddHours(-1), Schema = [new("A", "First", null, false), new("B", "Second", null, false)],
            Unlocks = new Dictionary<string, DateTime?>(), GlobalPercentages = new Dictionary<string, double> { ["A"] = 99, ["B"] = 1 },
        };
        await repository.SaveAsync(zero.ReleaseId, "12345", evidence);
        await repository.SaveAsync(partial.ReleaseId, "12345", evidence with { Unlocks = new Dictionary<string, DateTime?> { ["A"] = null } });
        var all = evidence with { Unlocks = new Dictionary<string, DateTime?> { ["A"] = null, ["B"] = null } };
        await repository.SaveAsync(complete.ReleaseId, "12345", all);
        await repository.SaveAsync(retired.ReleaseId, "12345", all);
        await repository.SaveAsync(partial.ReleaseId, "67890", all);
        await repository.SaveAsync(noSchema.ReleaseId, "12345", new AchievementFetch { AttemptedAt = evidence.AttemptedAt, Schema = [] });
        await repository.SaveAsync(unavailable.ReleaseId, "12345", new AchievementFetch { AttemptedAt = evidence.AttemptedAt });
        fixture.Capture();
        using (var captured = SnapshotBundle.Open(fixture.CapturePath))
        {
            var ids = new[] { zero.ReleaseId, partial.ReleaseId, complete.ReleaseId, retired.ReleaseId,
                noSchema.ReleaseId, unavailable.ReleaseId, unknown.ReleaseId, epic };
            var scoped = await new AchievementRepository(captured.Factory).GetForAccountAsync(ids, "12345", AsOf);
            Assert.Equal(8, scoped.Count);
            Assert.Equal(4, scoped.Count(row => row.HasKnownProgress));
            Assert.Equal(2, scoped.Count(row => row.Availability == AchievementAvailability.Unknown));
            Assert.Single(scoped, row => row.Availability == AchievementAvailability.Unavailable);
            Assert.Single(scoped, row => row.Availability == AchievementAvailability.NoSchema);
            Assert.Equal(0, scoped.Single(row => row.ReleaseId == zero.ReleaseId).PercentComplete);
            Assert.Null(scoped.Single(row => row.ReleaseId == unknown.ReleaseId).PercentComplete);
            Assert.Equal(50, scoped.Single(row => row.ReleaseId == partial.ReleaseId).PercentComplete);
            Assert.Null(scoped.Single(row => row.ReleaseId == epic).PercentComplete);
            var other = Assert.Single(await new AchievementRepository(captured.Factory).GetForAccountAsync([partial.ReleaseId], "67890", AsOf));
            Assert.Equal(100, other.PercentComplete);
        }
        await fixture.Surface(partial, 1);
        await fixture.Launch(partial, 2);
        await fixture.Surface(zero, 1);
        await fixture.Verdict(zero, 2);
        await fixture.Surface(noSchema, 1);
        fixture.CaptureOutcomes();
        NamedTuning[] policy = [new("baseline", RecommendationTuning.Default), new("no-achievement-contribution", RecommendationTuning.Default)];
        var before = await ReplayEvaluation.CompareAsync(without, fixture.OutcomePath, policy, new ReplayOptions { K = 1 });
        var after = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, policy, new ReplayOptions { K = 1 });
        Assert.NotEqual(before.SnapshotSha256, after.SnapshotSha256);
        Assert.Equal(before.Tunings[0].Ranking.ToArray(), after.Tunings[0].Ranking.ToArray());
        Assert.Equal(after.Tunings[0].Ranking.ToArray(), after.Tunings[1].Ranking.ToArray());
        Assert.Equal(6, after.Tunings[0].Ranking.Count);
        Assert.Equal(2, after.Tunings[0].JudgedCount);
        Assert.Equal(1d / 3, after.Tunings[0].JudgedCoverage);
        Assert.Single(after.Tunings[0].Ranking, item => item.WorkId == partial.WorkId);
        Assert.Contains(after.Tunings[0].Ranking, item => item.WorkId == complete.WorkId);
        Assert.DoesNotContain(after.Tunings[0].Ranking, item => item.WorkId == retired.WorkId);
    }

    [Fact]
    public async Task Acquisition_captures_preserve_account_provenance_and_baseline_without_comparing_prices()
    {
        using var fixture = new ReplayFixture();
        var paid = await fixture.Seed("Attributed amount", minutes: 300);
        var free = await fixture.Seed("Known free", minutes: 300);
        var unknown = await fixture.Seed("Unknown amount");
        var conflict = await fixture.Seed("Conflicting amounts", minutes: 300);
        var bundle = await fixture.Seed("Unallocated bundle", minutes: 300);
        var currency = await fixture.Seed("Uncomparable currency", minutes: 300);
        var sibling = await fixture.Seed("Linked Epic copy", minutes: 300);
        using (var connection = fixture.Database.Factory.Open())
            connection.Execute("UPDATE ownerships SET store='epic' WHERE id=@Id;", new { Id = sibling.OwnershipId });
        await new IdentityLinkRepository(fixture.Database.Factory).LinkAsync(new()
            { ParentWorkId = paid.WorkId, ChildWorkIds = [sibling.WorkId] });
        var games = new[] { paid, free, unknown, conflict, bundle, currency };
        var memberships = new OwnershipAccountRepository(fixture.Database.Factory);
        foreach (var game in games)
            foreach (var account in new[] { "12345", "67890" })
                await memberships.UpsertAsync(new(game.OwnershipId, account, 300, AsOf.AddYears(-2), "fixture", AsOf.AddDays(-1)));
        var settings = new SettingsRepository(fixture.Database.Factory);
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
        var baseline = Path.Combine(fixture.Root, "without-acquisitions");
        SnapshotBundle.Capture(fixture.Database.DatabasePath, baseline, new FixedClock(AsOf));

        var observations = new AccountAcquisitionRepository(fixture.Database.Factory);
        var observation = new OwnershipAcquisitionObservation
        {
            OwnershipId = paid.OwnershipId, AccountRef = "12345", PricePaidCents = 500,
            AcquiredAt = AsOf.AddYears(-3), PriceSource = "steam_account_history", Source = "steam", CapturedAt = AsOf.AddHours(-1),
        };
        await observations.TryAppendAsync(observation);
        await observations.TryAppendAsync(observation with { AccountRef = "67890", PricePaidCents = 9000 });
        await observations.TryAppendAsync(observation with { AccountRef = null, PricePaidCents = 99999 });
        await observations.TryAppendAsync(observation with { OwnershipId = free.OwnershipId, PricePaidCents = 0 });
        await observations.TryAppendAsync(observation with { OwnershipId = conflict.OwnershipId, PricePaidCents = 500 });
        await observations.TryAppendAsync(observation with { OwnershipId = conflict.OwnershipId, PricePaidCents = 700 });
        using (var connection = fixture.Database.Factory.Open())
        {
            connection.Execute("UPDATE ownerships SET price_paid_cents=@Price, price_source='fixture', acquired_at=@At WHERE id=@Id;",
                new[] { new { Id = paid.OwnershipId, Price = 500, At = AsOf.AddYears(-3) },
                    new { Id = free.OwnershipId, Price = 0, At = AsOf.AddYears(-3) },
                    new { Id = currency.OwnershipId, Price = 99999, At = AsOf.AddYears(-3) },
                    new { Id = sibling.OwnershipId, Price = 500, At = AsOf.AddYears(-3) } });
        }
        var transaction = new AccountTransactionFact
        {
            Source = "steam", AccountRef = "12345", Kind = "purchase", TransactionTypeRaw = "Purchase",
            ItemNames = ["Unallocated bundle", "Uncomparable currency"], TotalCents = 1000, ListPriceCents = 10000,
            DiscountPercent = 90, CurrencySymbol = "$", CapturedAt = AsOf.AddHours(-1), OccurredAt = AsOf.AddYears(-3),
        };
        var transactions = new AccountFactRepository(fixture.Database.Factory);
        await transactions.TryAppendAsync(transaction);
        await transactions.TryAppendAsync(transaction with { CurrencySymbol = "€", TotalCents = 3000 });
        fixture.Capture();
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "67890");
        var otherAccount = Path.Combine(fixture.Root, "other-account");
        SnapshotBundle.Capture(fixture.Database.DatabasePath, otherAccount, new FixedClock(AsOf));
        using (var captured = SnapshotBundle.Open(fixture.CapturePath))
        {
            var rows = await new AccountAcquisitionRepository(captured.Factory).GetAsync(games.Select(game => game.OwnershipId).ToArray());
            Assert.Equal(6, rows.Count);
            var selected = rows.Where(row => row.AccountRef == "12345").ToArray();
            Assert.Equal(4, selected.Length);
            Assert.Equal(3, selected.Select(row => row.OwnershipId).Distinct().Count());
            Assert.Equal(0, Assert.Single(selected, row => row.OwnershipId == free.OwnershipId).PricePaidCents);
            Assert.DoesNotContain(rows, row => row.OwnershipId == unknown.OwnershipId);
            Assert.Equal(2, selected.Count(row => row.OwnershipId == conflict.OwnershipId));
            Assert.Equal(9000, Assert.Single(rows, row => row.AccountRef == "67890").PricePaidCents);
            Assert.Equal(99999, Assert.Single(rows, row => row.AccountRef is null).PricePaidCents);
            var receipts = await new AccountFactRepository(captured.Factory).GetTransactionsAsync("steam");
            Assert.Equal(2, receipts.Count);
            Assert.Equal(2, receipts.Select(row => row.CurrencySymbol).Distinct().Count());
            Assert.All(receipts, row => { Assert.Equal(2, row.ItemNames.Count); Assert.Null(row.AppId); });
            Assert.Null((await new OwnershipRepository(captured.Factory).GetAsync(bundle.OwnershipId))!.PricePaidCents);
        }
        await fixture.Surface(paid, 1);
        await fixture.Launch(paid, 2);
        await fixture.Surface(free, 1);
        await fixture.Verdict(free, 2);
        await fixture.Surface(bundle, 1);
        fixture.CaptureOutcomes();
        NamedTuning[] policy = [new("baseline", RecommendationTuning.Default), new("no-acquisition-contribution", RecommendationTuning.Default)];
        var before = await ReplayEvaluation.CompareAsync(baseline, fixture.OutcomePath, policy, new ReplayOptions { K = 1 });
        foreach (var path in new[] { fixture.CapturePath, otherAccount })
        {
            var after = await ReplayEvaluation.CompareAsync(path, fixture.OutcomePath, policy, new ReplayOptions { K = 1 });
            Assert.NotEqual(before.SnapshotSha256, after.SnapshotSha256);
            Assert.Equal(before.Tunings[0].Ranking.ToArray(), after.Tunings[0].Ranking.ToArray());
            Assert.Equal(after.Tunings[0].Ranking.ToArray(), after.Tunings[1].Ranking.ToArray());
            Assert.Equal(6, after.Tunings[0].Ranking.Count);
            Assert.Equal(2, after.Tunings[0].JudgedCount);
            Assert.Equal(1d / 3, after.Tunings[0].JudgedCoverage);
            Assert.Single(after.Tunings[0].Ranking, item => item.WorkId == paid.WorkId);
            Assert.DoesNotContain(after.Tunings[0].Ranking, item => item.WorkId == sibling.WorkId);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_captured_database_cannot_be_backdated_or_advanced(int days)
    {
        using var fixture = new ReplayFixture();
        fixture.Capture();
        Assert.Throws<InvalidDataException>(() => SnapshotBundle.Open(fixture.CapturePath, AsOf.AddDays(days)));
    }

    [Fact]
    public void Modified_snapshot_and_future_manifest_versions_are_refused()
    {
        using var fixture = new ReplayFixture();
        fixture.Capture();
        var database = Path.Combine(fixture.CapturePath, SnapshotBundle.DatabaseName);
        using (var stream = new FileStream(database, FileMode.Open, FileAccess.Write))
        { stream.Position = stream.Length - 1; stream.WriteByte(123); }
        Assert.Throws<InvalidDataException>(() => SnapshotBundle.Open(fixture.CapturePath));
        File.WriteAllText(Path.Combine(fixture.CapturePath, SnapshotBundle.ManifestName),
            JsonSerializer.Serialize(new SnapshotManifest(99, AsOf, "invalid")));
        Assert.Throws<InvalidDataException>(() => SnapshotBundle.Open(fixture.CapturePath));
    }

    [Fact]
    public void Capture_refuses_to_overwrite_an_existing_bundle()
    {
        using var fixture = new ReplayFixture();
        fixture.Capture();
        var manifest = File.ReadAllText(Path.Combine(fixture.CapturePath, SnapshotBundle.ManifestName));
        Assert.Throws<IOException>(() => fixture.Capture());
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(fixture.CapturePath, SnapshotBundle.ManifestName)));
    }

    [Fact]
    public void Incomplete_schema_is_refused_without_upgrading_the_capture()
    {
        using var fixture = new ReplayFixture();
        using (var writer = fixture.Database.Factory.Open())
            writer.Execute("DELETE FROM SchemaVersions WHERE ScriptName=(SELECT MAX(ScriptName) FROM SchemaVersions);");
        fixture.Capture();
        var failure = Assert.Throws<InvalidDataException>(() => SnapshotBundle.Open(fixture.CapturePath));
        Assert.Contains("never migrated", failure.Message);
    }

    [Fact]
    public async Task Cancellation_is_reported_without_creating_a_capture()
    {
        using var fixture = new ReplayFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(130, await ReplayCommand.RunAsync(["capture", fixture.Database.DatabasePath, fixture.CapturePath],
            new StringWriter(), new StringWriter(), cancellation.Token));
        Assert.False(Directory.Exists(fixture.CapturePath));
    }

    [Fact]
    public async Task Two_tunings_share_frozen_facts_and_report_deterministic_judged_metrics()
    {
        using var fixture = new ReplayFixture();
        var positive = await fixture.Seed("Installed positive", installed: true);
        var negative = await fixture.Seed("Bounced negative", minutes: 300);
        var ignored = await fixture.Seed("Unanswered impression");
        var unobserved = await fixture.Seed("Never shown");
        fixture.Capture();
        foreach (var game in new[] { positive, negative, ignored }) await fixture.Surface(game, 1);
        await fixture.Launch(positive, 2);
        await fixture.Verdict(negative, 2);
        fixture.CaptureOutcomes();

        var report = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings, new ReplayOptions { K = 1 });
        Assert.Equal(OutcomeKind.Positive, report.Outcomes.Single(item => item.WorkId == positive.WorkId).Kind);
        Assert.Equal(OutcomeKind.Negative, report.Outcomes.Single(item => item.WorkId == negative.WorkId).Kind);
        Assert.Equal(OutcomeKind.WeakNegative, report.Outcomes.Single(item => item.WorkId == ignored.WorkId).Kind);
        Assert.Equal(OutcomeKind.Unobserved, report.Outcomes.Single(item => item.WorkId == unobserved.WorkId).Kind);
        Assert.Equal(0, report.Tunings[0].PrecisionAtK);
        Assert.Equal(0.5, report.Tunings[0].MeanReciprocalRank);
        Assert.Equal(1, report.Tunings[1].PrecisionAtK);
        Assert.Equal(1, report.Tunings[1].MeanReciprocalRank);
        Assert.All(report.Tunings, result => Assert.Equal(0.5, result.JudgedCoverage));
        Assert.Equal(positive.WorkId, report.Tunings[1].Ranking[0].WorkId);
        using (var writer = fixture.Database.Factory.Open())
            writer.Execute("UPDATE ownerships SET installed=0; UPDATE works SET name='Present-day mutation'; DELETE FROM work_facets;");
        var repeated = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings, new ReplayOptions { K = 1 });
        Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(repeated));
    }

    [Fact]
    public async Task Later_outcomes_change_labels_without_changing_either_tunings_ranking()
    {
        using var fixture = new ReplayFixture();
        var game = await fixture.Seed("Unplayed at capture", installed: true);
        fixture.Capture();
        fixture.CaptureOutcomes();
        var first = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings);
        await fixture.Surface(game, 1);
        await fixture.Launch(game, 2);
        using (var writer = fixture.Database.Factory.Open())
        {
            writer.Execute("INSERT INTO play_records (ownership_id,playtime_minutes,source,observed_at) VALUES (@Id,999999,'later',@At);",
                new { Id = game.OwnershipId, At = AsOf.AddDays(3) });
            writer.Execute("UPDATE ownerships SET installed=0;");
        }
        var later = Path.Combine(fixture.Root, "later");
        SnapshotBundle.Capture(fixture.Database.DatabasePath, later, new FixedClock(AsOf.AddDays(11)));
        var second = await ReplayEvaluation.CompareAsync(fixture.CapturePath, later, Tunings);
        Assert.Equal(OutcomeKind.Positive, Assert.Single(second.Outcomes).Kind);
        for (var i = 0; i < Tunings.Length; i++) Assert.Equal(first.Tunings[i].Ranking, second.Tunings[i].Ranking);
    }

    [Theory]
    [InlineData("play_records", "observed_at")]
    [InlineData("play_records", "last_played_at")]
    [InlineData("playtime_snapshots", "observed_at")]
    public async Task Future_dated_scoring_evidence_refuses_replay(string table, string column)
    {
        using var fixture = new ReplayFixture();
        await fixture.Seed("Future-poisoned game");
        using (var writer = fixture.Database.Factory.Open())
            writer.Execute($"UPDATE {table} SET {column}=@future;", new { future = AsOf.AddDays(1) });
        fixture.Capture();
        fixture.CaptureOutcomes();
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings));
        Assert.Contains(table + "." + column, failure.Message);
    }

    [Fact]
    public async Task Same_day_inferred_revoked_conflicting_and_unanchored_outcomes_do_not_create_judged_labels()
    {
        using var fixture = new ReplayFixture();
        var sameDay = await fixture.Seed("Same day");
        var inferred = await fixture.Seed("Inferred session");
        var revoked = await fixture.Seed("Undone verdict");
        var conflict = await fixture.Seed("Conflicting actions");
        var unanchored = await fixture.Seed("No external identity", anchored: false);
        fixture.Capture();
        foreach (var game in new[] { sameDay, inferred, revoked, conflict, unanchored }) await fixture.Surface(game, 1);
        await fixture.Launch(sameDay, 1);
        await fixture.Launch(inferred, 2, "inferred");
        await fixture.Verdict(revoked, 2, revoked: AsOf.AddDays(3));
        await fixture.Launch(conflict, 2);
        await fixture.Verdict(conflict, 2);
        await fixture.Launch(unanchored, 2);
        fixture.CaptureOutcomes();
        var report = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings);
        Assert.DoesNotContain(report.Outcomes, item => item.Kind is OutcomeKind.Positive or OutcomeKind.Negative);
        Assert.All(report.Tunings, result => { Assert.Null(result.PrecisionAtK); Assert.Null(result.MeanReciprocalRank); });
    }

    [Fact]
    public async Task Outcome_upper_boundary_excludes_later_launch_and_future_revocation_does_not_undo_a_label_early()
    {
        using var fixture = new ReplayFixture();
        var game = await fixture.Seed("Later outcome");
        fixture.Capture();
        await fixture.Surface(game, 1);
        await fixture.Launch(game, 3);
        await fixture.Verdict(game, 2, revoked: AsOf.AddDays(4));
        fixture.CaptureOutcomes();
        var report = await ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath, Tunings,
            new ReplayOptions { K = 1, OutcomesThroughUtc = AsOf.AddDays(2).AddHours(1) });
        Assert.Equal(OutcomeKind.Negative, Assert.Single(report.Outcomes).Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => ReplayEvaluation.CompareAsync(fixture.CapturePath, fixture.OutcomePath,
            Tunings, new ReplayOptions { OutcomesThroughUtc = AsOf.AddYears(1) }));
    }

    [Fact]
    public async Task Command_capture_and_compare_emit_json_and_reject_backdated_requests()
    {
        using var fixture = new ReplayFixture();
        await fixture.Seed("Command fixture");
        var first = Path.Combine(fixture.Root, "command");
        var captureOutput = new StringWriter();
        Assert.Equal(0, await ReplayCommand.RunAsync(["capture", fixture.Database.DatabasePath, first], captureOutput, new StringWriter()));
        // The command does not accept a caller-supplied capture date. Fixtures below use the
        // deterministic library entry point so their outcome interval does not depend on wall time.
        fixture.Capture();
        fixture.CaptureOutcomes();
        var tuningPaths = Tunings.Select((tuning, index) =>
        {
            var path = Path.Combine(fixture.Root, $"tuning-{index}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(tuning));
            return path;
        }).ToArray();
        var output = new StringWriter();
        Assert.Equal(0, await ReplayCommand.RunAsync(["compare", fixture.CapturePath, fixture.OutcomePath, .. tuningPaths, "--k", "1"], output, new StringWriter()));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(2, json.RootElement.GetProperty("Tunings").GetArrayLength());
        var error = new StringWriter();
        Assert.Equal(2, await ReplayCommand.RunAsync(["compare", fixture.CapturePath, fixture.OutcomePath, .. tuningPaths,
            "--as-of", AsOf.AddDays(-1).ToString("O", CultureInfo.InvariantCulture)], new StringWriter(), error));
        Assert.Contains("capture instant", error.ToString());
    }

    private sealed class ReplayFixture : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-replay-tests-" + Guid.NewGuid().ToString("N"));
        public string CapturePath => Path.Combine(Root, "capture");
        public string OutcomePath => Path.Combine(Root, "outcomes");
        public ReplayFixture() => Directory.CreateDirectory(Root);
        public void Capture() => SnapshotBundle.Capture(Database.DatabasePath, CapturePath, new FixedClock(AsOf));
        public void CaptureOutcomes() => SnapshotBundle.Capture(Database.DatabasePath, OutcomePath, new FixedClock(AsOf.AddDays(10)));
        public async Task<SeededGame> Seed(string name, bool installed = false, long minutes = 0, bool anchored = true)
        {
            var work = await new WorkRepository(Database.Factory).InsertAsync(new Work { Name = name });
            var release = await new ReleaseRepository(Database.Factory).InsertAsync(new Release { WorkId = work, Name = name });
            var owner = await new OwnershipRepository(Database.Factory).InsertAsync(new Ownership { ReleaseId = release, Store = "steam", Installed = installed });
            await new PlayRecordRepository(Database.Factory).InsertAsync(new PlayRecord
            {
                OwnershipId = owner, PlaytimeMinutes = minutes, Source = "fixture", ObservedAt = AsOf.AddDays(-1),
                LastPlayedAt = minutes > 0 ? AsOf.AddYears(-2) : null,
            });
            await new PlaytimeSnapshotRepository(Database.Factory).InsertAsync(new PlaytimeSnapshot
                { OwnershipId = owner, PlaytimeMinutes = minutes, ObservedAt = AsOf.AddDays(-1) });
            if (anchored)
            {
                using var connection = Database.Factory.Open();
                connection.Execute("INSERT INTO external_ids (release_id,provider,provider_id) VALUES (@release,'steam',@providerId);",
                    new { release, providerId = (90000 + release).ToString(CultureInfo.InvariantCulture) });
            }
            return new SeededGame(work, release, owner);
        }
        public Task Surface(SeededGame game, int day) => new FeedFeedbackRepository(Database.Factory).RecordSurfacedAsync(
            [new FeedSurfacing { ReleaseId = game.ReleaseId, SurfacedOn = DateOnly.FromDateTime(AsOf.AddDays(day)), ShelfId = "fixture" }]);
        public async Task Launch(SeededGame game, int day, string attribution = "launch")
            => await new SessionRepository(Database.Factory).InsertAsync(new Session
            {
                OwnershipId = game.OwnershipId, StartedAt = AsOf.AddDays(day), EndedAt = AsOf.AddDays(day).AddMinutes(30),
                DurationSeconds = 1800, DetectionMethod = "manual", AttributedBy = attribution,
            });
        public async Task Verdict(SeededGame game, int day, DateTime? revoked = null)
            => await new FeedFeedbackRepository(Database.Factory).RecordVerdictAsync(new FeedVerdict
            { ReleaseId = game.ReleaseId, Kind = FeedVerdictKinds.NotInterested, CreatedAt = AsOf.AddDays(day), RevokedAt = revoked });
        public void Dispose() { Database.Dispose(); Directory.Delete(Root, recursive: true); }
    }

    private sealed class FixedClock(DateTime instant, Action? onRead = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { onRead?.Invoke(); return new DateTimeOffset(instant); }
    }
}
