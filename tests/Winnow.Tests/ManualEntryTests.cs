using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Migration 0025's <c>manual_entries</c> table: a hand-added game is an ordinary
/// work + release + ownership whose <c>store</c> is <c>manual</c>, with a
/// <c>manual_entries</c> row as the sole origin marker.
///
/// <para>The guarantee that an ingest pass never deletes or overwrites a
/// hand-added entry rests on two facts rather than on new machinery:
/// <c>OwnershipRepository.UpsertAsync</c> conflicts on <c>(release_id, store)</c>
/// and no reader emits the store <c>manual</c>, so the upsert cannot reach the
/// row; and the work is created with <c>name_is_provisional = 0</c>, which makes
/// the title ineligible for the resolver's name promotion and for the fill-only
/// enrichment patch. No ingest path writes the <c>manual_entries</c> table.</para>
///
/// <para>Deleting a hand-added entry removes the ownership, then the release only
/// when no other ownership hangs off it, then the work only when it has no
/// releases left.</para>
/// </summary>
public sealed class ManualEntryTests : IDisposable
{
    private static readonly BucketThresholds Thresholds = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly ManualEntryRepository _manual;
    private readonly LibraryQueryRepository _library;

    public ManualEntryTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _manual = new ManualEntryRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Creating_an_entry_builds_the_whole_row_chain()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft
        {
            Title = "  Blasphemous  ",
            FirstReleaseYear = 2019,
            PlatformLabel = "GOG offline installer",
            ExecutablePath = @"D:\Games\Blasphemous\Blasphemous.exe",
        });

        Assert.Equal("Blasphemous", entry.Title);
        Assert.Equal(@"D:\Games\Blasphemous", entry.InstallPath);

        var ownership = await _ownerships.GetAsync(entry.OwnershipId);
        Assert.NotNull(ownership);
        Assert.Equal(OwnershipStores.Manual, ownership.Store);
        Assert.True(ownership.Installed);

        var work = await _works.GetAsync(entry.WorkId);
        Assert.NotNull(work);
        Assert.Equal("Blasphemous", work.Name);
        Assert.False(work.NameIsProvisional);
    }

    [Fact]
    public async Task An_entry_with_no_executable_is_not_installed()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft { Title = "Chrono Trigger" });

        Assert.Null(entry.InstallPath);
        Assert.Null(entry.ExecutablePath);

        var ownership = await _ownerships.GetAsync(entry.OwnershipId);
        Assert.NotNull(ownership);
        Assert.False(ownership.Installed);
    }

    [Fact]
    public async Task An_explicit_install_path_wins_over_the_executable_directory()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft
        {
            Title = "Chrono Trigger",
            ExecutablePath = @"D:\Emu\snes9x.exe",
            InstallPath = @"D:\Roms\Chrono Trigger",
        });

        Assert.Equal(@"D:\Roms\Chrono Trigger", entry.InstallPath);
        Assert.Equal(@"D:\Emu\snes9x.exe", entry.ExecutablePath);
    }

    [Fact]
    public async Task A_blank_title_is_refused()
        => await Assert.ThrowsAsync<ArgumentException>(
            () => _manual.CreateAsync(new ManualGameDraft { Title = "   " }));

    [Fact]
    public async Task A_hand_added_entry_appears_in_the_library_and_its_counts()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft { Title = "Blasphemous" });

        var row = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));

        Assert.Equal(entry.OwnershipId, row.OwnershipId);
        Assert.Equal(entry.WorkId, row.ResolvedWorkId);
        Assert.Equal(LibraryBuckets.NeverPlayed, row.Bucket);
    }

    [Fact]
    public async Task A_full_ingest_pass_leaves_a_hand_added_entry_untouched()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft
        {
            Title = "Team Fortress 2",
            ExecutablePath = @"D:\Games\TF2\tf2.exe",
        });

        var resolver = new ExternalIdResolver(
            _works, _releases, _ownerships,
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        var observed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", observed)], CancellationToken.None);
        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", observed.AddDays(1))], CancellationToken.None);

        var after = await _manual.GetAsync(entry.OwnershipId);

        Assert.Equal(entry, after);

        var ownership = await _ownerships.GetAsync(entry.OwnershipId);
        Assert.NotNull(ownership);
        Assert.Equal(OwnershipStores.Manual, ownership.Store);
        Assert.Equal(@"D:\Games\TF2", ownership.InstallPath);

        var work = await _works.GetAsync(entry.WorkId);
        Assert.NotNull(work);
        Assert.Equal("Team Fortress 2", work.Name);

        // The ingested Steam entry is its own game; the hand-added one is still there.
        Assert.Equal(2, (await _ownerships.GetAllAsync()).Count);
    }

    [Fact]
    public async Task An_entry_can_be_edited()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft { Title = "Blasphemus" });

        Assert.True(await _manual.UpdateAsync(entry.OwnershipId, new ManualGameDraft
        {
            Title = "Blasphemous",
            FirstReleaseYear = 2019,
            PlatformLabel = "itch.io",
            ExecutablePath = @"D:\Games\Blasphemous\Blasphemous.exe",
        }));

        var edited = await _manual.GetAsync(entry.OwnershipId);

        Assert.NotNull(edited);
        Assert.Equal("Blasphemous", edited.Title);
        Assert.Equal(2019, edited.FirstReleaseYear);
        Assert.Equal("itch.io", edited.PlatformLabel);
        Assert.Equal(@"D:\Games\Blasphemous", edited.InstallPath);
        Assert.Equal(entry.AddedAt, edited.AddedAt);
    }

    [Fact]
    public async Task Editing_an_entry_that_is_not_hand_added_reports_false()
        => Assert.False(await _manual.UpdateAsync(9999, new ManualGameDraft { Title = "Nothing" }));

    [Fact]
    public async Task Deleting_an_entry_removes_its_whole_chain()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft { Title = "Blasphemous" });

        Assert.True(await _manual.DeleteAsync(entry.OwnershipId));

        Assert.Empty(await _manual.GetAllAsync());
        Assert.Empty(await _ownerships.GetAllAsync());
        Assert.Equal(0, CountRows("releases"));
        Assert.Empty(await _works.GetAllAsync());
        Assert.Empty(await _library.GetOwnershipBucketsAsync(Thresholds));
    }

    [Fact]
    public async Task Deleting_an_entry_keeps_a_store_entry_that_attached_to_the_same_release()
    {
        var entry = await _manual.CreateAsync(new ManualGameDraft
        {
            Title = "Team Fortress 2",
            SteamAppId = "440",
        });

        var resolver = new ExternalIdResolver(
            _works, _releases, _ownerships,
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))],
            CancellationToken.None);

        Assert.True(await _manual.DeleteAsync(entry.OwnershipId));

        var survivor = Assert.Single(await _ownerships.GetAllAsync());
        Assert.Equal(ExternalIdProviders.Steam, survivor.Store);
        Assert.Equal(entry.ReleaseId, survivor.ReleaseId);
        Assert.Single(await _works.GetAllAsync());
    }

    [Fact]
    public async Task Deleting_an_entry_that_is_not_hand_added_reports_false()
        => Assert.False(await _manual.DeleteAsync(9999));

    [Fact]
    public async Task An_igdb_id_already_in_the_library_is_refused_by_name()
    {
        await _works.InsertAsync(new Work { Name = "Outer Wilds", IgdbId = 12345 });

        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(
            () => _manual.CreateAsync(new ManualGameDraft { Title = "Outer Wilds", IgdbId = 12345 }));

        Assert.Equal(nameof(ManualGameDraft.IgdbId), conflict.Field);
    }

    [Fact]
    public async Task A_steam_appid_already_in_the_library_is_refused_by_name()
    {
        var resolver = new ExternalIdResolver(
            _works, _releases, _ownerships,
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))],
            CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<ManualEntryConflictException>(
            () => _manual.CreateAsync(new ManualGameDraft { Title = "TF2", SteamAppId = "440" }));

        Assert.Equal(nameof(ManualGameDraft.SteamAppId), conflict.Field);
    }

    [Fact]
    public async Task A_refused_creation_leaves_nothing_behind()
    {
        await _works.InsertAsync(new Work { Name = "Outer Wilds", IgdbId = 12345 });

        await Assert.ThrowsAsync<ManualEntryConflictException>(
            () => _manual.CreateAsync(new ManualGameDraft { Title = "Outer Wilds", IgdbId = 12345 }));

        Assert.Single(await _works.GetAllAsync());
        Assert.Equal(0, CountRows("releases"));
        Assert.Empty(await _manual.GetAllAsync());
    }

    private long CountRows(string table)
    {
        using var lease = _db.Factory.Lease();
        using var command = lease.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    private static CandidateOwnership Candidate(string appId, string title, DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: @"C:\Steam\steamapps\common\Team Fortress 2",
            Installed: true,
            PlaytimeMinutes: 0,
            LastPlayedAt: null,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt);
}
