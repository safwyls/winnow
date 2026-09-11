using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class ManualEntryCorrectionTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly ManualEntryRepository _manual;
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly WorkIgdbPinRepository _pins;

    public ManualEntryCorrectionTests()
    {
        _manual = new(_db.Factory);
        _works = new(_db.Factory);
        _releases = new(_db.Factory);
        _pins = new(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Correcting_a_pinned_manual_game_replaces_ids_pin_and_user_metadata_together()
    {
        var entry = await Seed();
        await _pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 333, Name = "Provider title", FirstReleaseYear = 2001, Summary = "Old game" });
        var before = (await _manual.GetAsync(entry.OwnershipId))!;

        await _manual.UpdateAsync(entry.OwnershipId, Draft(before) with
        {
            Title = "Correct title", FirstReleaseYear = 2020, IgdbId = 444, SteamAppId = "456",
        });

        var work = (await _works.GetAsync(entry.WorkId))!;
        Assert.Equal(444, work.IgdbId);
        Assert.Equal(444, (await _pins.GetAsync(entry.WorkId))!.IgdbId);
        Assert.Equal("Correct title", work.Name);
        Assert.Equal(2020, work.FirstReleaseYear);
        Assert.Null(work.Summary);
        Assert.Equal(before.IgdbMappingRevision + 1, work.IgdbMappingRevision);
        Assert.Equal(["igdb:444", "steam:456"], await Identifiers(entry.ReleaseId));
        Assert.Equal(FieldSources.User, Source(entry.WorkId, WorkFields.Name));
        Assert.Equal(FieldSources.User, Source(entry.WorkId, WorkFields.FirstReleaseYear));
        Assert.Null(Source(entry.WorkId, WorkFields.Summary));

        await ResolveSteam("123");
        Assert.Equal(2, (await _works.GetAllAsync()).Count);
        Assert.NotEqual(entry.ReleaseId, (await _releases.FindByExternalIdAsync("steam", "123"))!.Id);
    }

    [Fact]
    public async Task Clearing_tracked_ids_removes_their_join_assertions_and_live_pin()
    {
        var entry = await Seed();
        await _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { IgdbId = null, SteamAppId = null });
        var saved = (await _manual.GetAsync(entry.OwnershipId))!;
        Assert.Null(saved.IgdbId);
        Assert.Null(saved.SteamAppId);
        Assert.Null(await _pins.GetAsync(entry.WorkId));
        Assert.Empty(await Identifiers(entry.ReleaseId));
        using var db = _db.Factory.Open();
        Assert.Equal(2, db.ExecuteScalar<int>("SELECT COUNT(*) FROM manual_entry_identifiers WHERE retracted_at IS NULL AND provider_id IS NULL;"));
    }

    [Fact]
    public async Task A_full_pin_updates_the_manual_identifier_and_the_next_form_snapshot()
    {
        var entry = await Seed();
        Assert.Equal(WorkIgdbPinOutcome.Pinned, await _pins.PinAsync(new()
        {
            WorkId = entry.WorkId, IgdbId = 444, Name = "Chosen game", ExpectedIgdbMappingRevision = entry.IgdbMappingRevision,
        }));
        var saved = (await _manual.GetAsync(entry.OwnershipId))!;
        Assert.Equal(444, saved.IgdbId);
        Assert.Equal("Chosen game", saved.Title);
        Assert.Equal(["igdb:444", "steam:123"], await Identifiers(entry.ReleaseId));
        Assert.Equal(entry.IgdbMappingRevision + 1, saved.IgdbMappingRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_form_or_pin_cannot_overwrite_a_later_mapping_intent(bool pin)
    {
        var entry = await Seed();
        await _pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 444, Name = "Later choice" });
        var before = Snapshot();
        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(() => pin
            ? _pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 555, ExpectedIgdbMappingRevision = entry.IgdbMappingRevision })
            : _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { Title = "Stale form", IgdbId = 555 }));
        Assert.Equal(ManualEntryConflictReason.MappingChanged, conflict.Reason);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Pin_clear_advances_the_revision_without_changing_the_mapping_or_user_fields()
    {
        var entry = await Seed();
        Assert.True(await _pins.ClearAsync(entry.WorkId));
        var saved = (await _manual.GetAsync(entry.OwnershipId))!;
        Assert.Equal(entry.IgdbMappingRevision + 1, saved.IgdbMappingRevision);
        Assert.Equal(333, saved.IgdbId);
        Assert.Equal(FieldSources.User, Source(entry.WorkId, WorkFields.Name));
        Assert.False(await _pins.ClearAsync(entry.WorkId));
        Assert.Equal(saved.IgdbMappingRevision, (await _works.GetAsync(entry.WorkId))!.IgdbMappingRevision);
    }

    [Theory]
    [InlineData("456")]
    [InlineData(null)]
    public async Task A_storefront_attachment_prevents_id_retraction_but_allows_metadata_edits(string? replacement)
    {
        var entry = await Seed();
        await ResolveSteam("123");
        var before = Snapshot();
        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(() => _manual.UpdateAsync(
            entry.OwnershipId, Draft(entry) with { SteamAppId = replacement, Title = "Correct title" }));
        Assert.Equal(ManualEntryConflictReason.StorefrontObservation, conflict.Reason);
        Assert.Equal(nameof(ManualGameDraft.SteamAppId), conflict.Field);
        Assert.Equal(before, Snapshot());

        Assert.True(await _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { Title = "Correct title", FirstReleaseYear = 2020 }));
        Assert.Equal("Correct title", (await _works.GetAsync(entry.WorkId))!.Name);
        Assert.Equal("Original", (await _releases.GetAsync(entry.ReleaseId))!.Name);
        Assert.Equal(["igdb:333", "steam:123"], await Identifiers(entry.ReleaseId));
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("igdb")]
    public async Task Untracked_legacy_ids_are_never_invented_as_manual_history(string provider)
    {
        var entry = await Seed();
        using (var db = _db.Factory.Open()) db.Execute("DELETE FROM manual_entry_identifiers;");
        Assert.True(await _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { Title = "Edited legacy title" }));
        var before = Snapshot();
        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(() => _manual.UpdateAsync(entry.OwnershipId,
            Draft(entry) with { IgdbId = provider == "igdb" ? 444 : 333, SteamAppId = provider == "steam" ? "456" : "123" }));
        Assert.Equal(ManualEntryConflictReason.LegacyIdentifierHistory, conflict.Reason);
        Assert.Equal(before, Snapshot());
        using var check = _db.Factory.Open();
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM manual_entry_identifiers;"));
    }

    [Fact]
    public async Task Legacy_entries_without_ids_can_add_their_first_tracked_identifiers()
    {
        var entry = await _manual.CreateAsync(new() { Title = "Legacy without ids" });
        using (var db = _db.Factory.Open()) db.Execute("DELETE FROM manual_entry_identifiers;");
        await _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { IgdbId = 333, SteamAppId = "123" });
        Assert.Equal(["igdb:333", "steam:123"], await Identifiers(entry.ReleaseId));
    }

    [Fact]
    public async Task Unknown_year_on_creation_and_explicit_year_clear_have_different_ownership()
    {
        var entry = await _manual.CreateAsync(new() { Title = "Unknown year" });
        Assert.Null(Source(entry.WorkId, WorkFields.FirstReleaseYear));
        await _works.ApplyEnrichmentAsync(new(entry.WorkId, FirstReleaseYear: 2001));
        entry = (await _manual.GetAsync(entry.OwnershipId))!;
        Assert.Equal(2001, entry.FirstReleaseYear);
        await _manual.UpdateAsync(entry.OwnershipId, Draft(entry) with { FirstReleaseYear = null });
        Assert.Null((await _works.GetAsync(entry.WorkId))!.FirstReleaseYear);
        Assert.Equal(FieldSources.User, Source(entry.WorkId, WorkFields.FirstReleaseYear));
        await _works.ApplyEnrichmentAsync(new(entry.WorkId, FirstReleaseYear: 2002));
        Assert.Null((await _works.GetAsync(entry.WorkId))!.FirstReleaseYear);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("pin")]
    public async Task Every_igdb_authority_is_checked_before_creating_a_manual_game(string authority)
    {
        var workId = await _works.InsertAsync(new() { Name = "Claimant" });
        var releaseId = await _releases.InsertAsync(new() { WorkId = workId, Name = "Claimant" });
        using (var db = _db.Factory.Open())
        {
            if (authority == "external") db.Execute("INSERT INTO external_ids(release_id,provider,provider_id) VALUES(@releaseId,'igdb','333');", new { releaseId });
            else db.Execute("INSERT INTO work_igdb_pins(work_id,igdb_id,pinned_at) VALUES(@workId,333,'2026-09-10');", new { workId });
        }
        var before = Snapshot();
        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(() => Seed());
        Assert.Equal(ManualEntryConflictReason.ClaimedByAnotherGame, conflict.Reason);
        Assert.Equal(before, Snapshot());
    }

    public static TheoryData<string, bool> FailurePoints => new()
    {
        { "BEFORE INSERT ON manual_entry_identifiers WHEN NEW.provider = 'igdb' AND NEW.provider_id = '444'", false },
        { "BEFORE INSERT ON manual_entry_identifiers WHEN NEW.provider = 'igdb' AND NEW.provider_id = '444'", true },
        { "BEFORE INSERT ON work_field_sources WHEN NEW.field = 'first_release_year'", false },
        { "BEFORE INSERT ON work_field_sources WHEN NEW.field = 'first_release_year'", true },
        { "BEFORE UPDATE ON manual_entries", false },
        { "BEFORE UPDATE ON manual_entries", true },
    };

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task A_failure_after_identifier_retraction_rolls_back_the_entire_correction(string point, bool ambient)
    {
        var entry = await Seed();
        var before = Snapshot();
        using (var db = _db.Factory.Open()) db.Execute($"CREATE TRIGGER fail_correction {point} BEGIN SELECT RAISE(ABORT,'injected correction failure'); END;");
        using (var outer = ambient ? _db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => _manual.UpdateAsync(entry.OwnershipId,
                Draft(entry) with { IgdbId = 444, SteamAppId = "456", FirstReleaseYear = 2020 }));
            await new SettingsRepository(_db.Factory).SetAsync("other_operation", "committed");
            outer?.Commit();
        }
        Assert.Equal(before, Snapshot());
        Assert.Equal("committed", await new SettingsRepository(_db.Factory).GetAsync("other_operation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pin_clear_failure_restores_the_pin_and_revision(bool ambient)
    {
        var entry = await Seed();
        var before = Snapshot();
        using (var db = _db.Factory.Open()) db.Execute("CREATE TRIGGER fail_revision BEFORE UPDATE OF igdb_mapping_revision ON works BEGIN SELECT RAISE(ABORT,'injected revision failure'); END;");
        using (var outer = ambient ? _db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => _pins.ClearAsync(entry.WorkId));
            outer?.Commit();
        }
        Assert.Equal(before, Snapshot());
    }

    private Task<ManualEntry> Seed() => _manual.CreateAsync(new() { Title = "Original", FirstReleaseYear = 2000, IgdbId = 333, SteamAppId = "123" });
    private static ManualGameDraft Draft(ManualEntry entry) => new()
    {
        Title = entry.Title, FirstReleaseYear = entry.FirstReleaseYear, IgdbId = entry.IgdbId,
        SteamAppId = entry.SteamAppId, ExpectedIgdbMappingRevision = entry.IgdbMappingRevision,
    };

    private async Task<string[]> Identifiers(long releaseId) => (await _releases.GetExternalIdsAsync(releaseId))
        .Select(id => id.Provider + ":" + id.ProviderId).Order().ToArray();

    private string? Source(long workId, string field)
    {
        using var db = _db.Factory.Open();
        return db.QuerySingleOrDefault<string>("SELECT source FROM work_field_sources WHERE work_id = @workId AND field = @field;", new { workId, field });
    }

    private string Snapshot()
    {
        using var db = _db.Factory.Open();
        return string.Join("\n", new[] { "works", "releases", "ownerships", "manual_entries", "manual_entry_identifiers", "external_ids", "work_igdb_pins", "work_field_sources" }
            .Select(table => JsonSerializer.Serialize(db.Query($"SELECT * FROM {table} ORDER BY rowid;"))));
    }

    private Task ResolveSteam(string appId) => new ExternalIdResolver(_works, _releases, new OwnershipRepository(_db.Factory),
        new PlayRecordRepository(_db.Factory), new PlaytimeSnapshotRepository(_db.Factory), _db.Factory, new OwnershipAccountRepository(_db.Factory))
        .ResolveAsync([new CandidateOwnership("steam", appId, "Store title", "12345678", null, false, 0, null, null, "steam_local", DateTime.UtcNow)], CancellationToken.None);
}
