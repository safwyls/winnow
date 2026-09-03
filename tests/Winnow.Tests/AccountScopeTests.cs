using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The account-visibility filter (TASK-53, migration 0015) as the derived-bucket
/// query applies it.
///
/// <para>Everything here is asserted through
/// <see cref="LibraryQueryRepository.GetOwnershipBucketsAsync"/> and nothing
/// else, because that is the feature's whole reach: the preference is read
/// inside that one query, so the grid, the rail counts, the filter chips, the
/// recommender and the feed inherit it without knowing it exists. A test that
/// reached past it into <c>ownership_accounts</c> would be asserting about a
/// table rather than about what the user sees.</para>
/// </summary>
public class AccountScopeTests : IDisposable
{
    private const string Mine = "11111";
    private const string Theirs = "22222";

    /// <summary>Retired floor at 3000 so a "household retired, mine barely touched" case fits.</summary>
    private static readonly BucketThresholds Thresholds = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3,
        UpdateCorrelationWindowDays: 7);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;
    private readonly OwnershipAccountRepository _accounts;
    private readonly SettingsRepository _settings;
    private readonly LibraryQueryRepository _library;

    public AccountScopeTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _accounts = new OwnershipAccountRepository(_db.Factory);
        _settings = new SettingsRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ══ 1. The default, and the promise that nothing changed ════════════════

    [Fact]
    public async Task Default_is_every_account_and_returns_everything()
    {
        var mine = await SeedAsync("Mine", accounts: [(Mine, 10, null)]);
        var theirs = await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        // No settings written at all — a fresh install, and every install that
        // predates the toggle.
        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([mine, theirs], rows.Select(r => r.OwnershipId).Order());
    }

    [Fact]
    public async Task All_mode_is_identical_with_and_without_membership_rows()
    {
        var withRows = await SeedAsync(
            "Shared", minutes: 400, accounts: [(Mine, 100, null), (Theirs, 400, null)]);
        var withoutRows = await SeedAsync("Unattributed", minutes: 400);

        await ChooseAsync(AccountScope.All, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        // Same ids, and — the half that matters — the same figures: in `all`
        // mode the substitution must not fire, so a game one account played less
        // still reports the household total.
        Assert.Equal([withRows, withoutRows], rows.Select(r => r.OwnershipId).Order());
        Assert.All(rows, row => Assert.Equal(400, row.PlaytimeMinutes));
    }

    [Fact]
    public async Task Own_mode_without_a_confirmed_account_shows_everything()
    {
        var mine = await SeedAsync("Mine", accounts: [(Mine, 10, null)]);
        var theirs = await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        // The preference is stored but no key was ever confirmed. A preference
        // must never be able to empty the library it was meant to narrow.
        await _settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([mine, theirs], rows.Select(r => r.OwnershipId).Order());
    }

    // ══ 2. Who is hidden, and who is not ════════════════════════════════════

    [Fact]
    public async Task Own_mode_hides_a_game_only_another_account_holds()
    {
        var mine = await SeedAsync("Mine", accounts: [(Mine, 10, null)]);
        await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([mine], rows.Select(r => r.OwnershipId));
    }

    /// <summary>
    /// Acceptance criterion #2, pinned. A game BOTH accounts own, where the
    /// other one played it more and therefore won <c>ownerships.account_ref</c>.
    /// Filtering on that column — which is all Winnow could do before migration
    /// 0015 — would hide the user's own game because a housemate played it more.
    /// </summary>
    [Fact]
    public async Task A_game_both_accounts_own_survives_when_the_other_won_the_account_ref()
    {
        var shared = await SeedAsync(
            "Shared",
            minutes: 900,
            // Attribution as the old collapse would have written it: the winner.
            accountRef: Theirs,
            accounts: [(Mine, 10, null), (Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([shared], rows.Select(r => r.OwnershipId));
    }

    [Fact]
    public async Task A_game_with_no_account_evidence_stays_visible()
    {
        // Epic and GOG's registry path, and every Steam appid on a multi-account
        // PC whose manifest cannot say who installed it. "Not known" is not
        // "not yours".
        var unattributed = await SeedAsync("Unattributed", minutes: 60);
        var attester = await AttestOwnedAccountPassAsync();
        await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([unattributed, attester], rows.Select(r => r.OwnershipId).Order());
    }

    [Fact]
    public async Task A_seeded_row_naming_another_account_is_not_enough_to_hide()
    {
        // Migration 0015's seed carries the single-winner ambiguity whole: it
        // names whoever played the game most, which on a shared game is
        // routinely not the only owner. Between the migration and the first sync
        // it must not be treated as a complete account list.
        var seeded = await SeedAsync("Seeded", minutes: 900, accountRef: Theirs);
        await SeedRawAccountAsync(
            seeded, Theirs, 900, OwnershipAccountSources.LegacyOwnershipColumn);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([seeded], rows.Select(r => r.OwnershipId));
    }

    [Fact]
    public async Task A_seeded_row_naming_your_account_is_enough_to_keep()
    {
        // The asymmetry, deliberately: evidence of presence and evidence of
        // absence are different claims, and a seed can make the first.
        var seeded = await SeedAsync("Seeded", minutes: 40, accountRef: Mine);
        await SeedRawAccountAsync(
            seeded, Mine, 40, OwnershipAccountSources.LegacyOwnershipColumn);

        // A second game with real evidence, so the filter is demonstrably active.
        var attester = await AttestOwnedAccountPassAsync();
        await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([seeded, attester], rows.Select(r => r.OwnershipId).Order());
    }

    /// <summary>
    /// The other road to acceptance criterion #2's failure, and the one the
    /// <c>account_ref</c> discussion does not name.
    ///
    /// <para>The two kinds of evidence come from different passes. The local
    /// scan attests that a housemate PLAYED something; only GetOwnedGames can
    /// attest that the user OWNS something they never launched — and that call's
    /// failure is caught and logged so a private profile cannot cost the user
    /// their local scan. On a machine where the account is confirmed but that
    /// pass has not yet succeeded, every owned-but-never-launched game a
    /// housemate played carries exactly one non-seed row, the housemate's, and a
    /// predicate reading it as positive evidence would delete the user's backlog
    /// from their own screen.</para>
    /// </summary>
    [Fact]
    public async Task Nothing_is_hidden_until_your_own_evidence_pass_has_run()
    {
        // The housemate's local scan ran; the user's owned-list pass did not.
        var neverLaunched = await SeedAsync(
            "Owned by both, played by them", minutes: 900, accountRef: Theirs,
            accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        Assert.Equal(
            [neverLaunched],
            (await _library.GetOwnershipBucketsAsync(Thresholds)).Select(r => r.OwnershipId));

        // One non-seed row for the user's account ANYWHERE in the store is the
        // proof that their pass has run. It arrives on a different game.
        var elsewhere = await SeedAsync("Something else of mine", accounts: [(Mine, 0, null)]);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        // Now the ordinary predicate applies and the housemate's game goes.
        Assert.Equal([elsewhere], rows.Select(r => r.OwnershipId));
    }

    [Fact]
    public async Task A_seed_row_of_your_own_is_not_proof_that_your_pass_has_run()
    {
        // Migration 0015 seeds a row for whoever won account_ref. If that
        // happens to be the user on one game, it still says nothing about
        // whether anything has enumerated what they own — so it must not unlock
        // hiding on every other game.
        var mineBySeed = await SeedAsync("Mine, by seed", minutes: 40, accountRef: Mine);
        await SeedRawAccountAsync(
            mineBySeed, Mine, 40, OwnershipAccountSources.LegacyOwnershipColumn);

        var theirs = await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([mineBySeed, theirs], rows.Select(r => r.OwnershipId).Order());
    }

    [Fact]
    public async Task A_non_steam_ownership_is_never_hidden_by_a_steam_account_filter()
    {
        // The stored reference is a Steam3 account id. A GOG user id is also a
        // bare integer, so without the store predicate a GOG library would fail
        // the match wholesale and vanish.
        var gog = await SeedAsync(
            "Galaxy game", store: "gog", accounts: [("77777", 500, null)]);
        var attester = await AttestOwnedAccountPassAsync();
        await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var rows = await _library.GetOwnershipBucketsAsync(Thresholds);

        Assert.Equal([gog, attester], rows.Select(r => r.OwnershipId).Order());
    }

    // ══ 3. The substitution ═════════════════════════════════════════════════

    [Fact]
    public async Task Own_mode_shows_your_own_figures_for_a_shared_game()
    {
        var lastPlayedByThem = Utc(2026, 8, 1);
        var lastPlayedByMe = Utc(2024, 2, 3);

        var shared = await SeedAsync(
            "Shared",
            minutes: 900,
            lastPlayed: lastPlayedByThem,
            accountRef: Theirs,
            accounts: [(Mine, 40, lastPlayedByMe), (Theirs, 900, lastPlayedByThem)]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var row = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));

        Assert.Equal(shared, row.OwnershipId);
        Assert.Equal(40, row.PlaytimeMinutes);
        Assert.Equal(lastPlayedByMe, row.LastPlayedAt);
    }

    [Fact]
    public async Task Own_mode_falls_back_to_household_figures_with_no_row_of_your_own()
    {
        // No membership row for anybody, so nothing to substitute and nothing
        // to hide on. The household figures are still the honest answer.
        var unattributed = await SeedAsync(
            "Unattributed", minutes: 900, lastPlayed: Utc(2026, 8, 1));

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var row = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));

        Assert.Equal(unattributed, row.OwnershipId);
        Assert.Equal(900, row.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 1), row.LastPlayedAt);
    }

    [Fact]
    public async Task The_bucket_is_derived_from_your_figures_not_the_households()
    {
        // 5000 household minutes is Retired — excluded from surfacing entirely.
        // 40 of them are the user's, which is Active. Deriving the bucket from
        // the household figure would bury a game they have barely started in the
        // one bucket built to never resurface anything.
        var shared = await SeedAsync(
            "Shared",
            minutes: 5000,
            lastPlayed: Utc(2026, 8, 1),
            accountRef: Theirs,
            accounts: [(Mine, 40, Utc(2026, 7, 1)), (Theirs, 5000, Utc(2026, 8, 1))]);

        var household = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));
        Assert.Equal(LibraryBuckets.Retired, household.Bucket);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var own = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));
        Assert.Equal(shared, own.OwnershipId);
        Assert.Equal(LibraryBuckets.Active, own.Bucket);
    }

    [Fact]
    public async Task A_game_you_hold_but_never_launched_reads_as_never_played()
    {
        // The owned-but-never-launched population GetOwnedGames exists to see:
        // the other account has hours on it, this one has a licence and nothing
        // else. Substituting NULL figures is what makes the bucket honest.
        var shared = await SeedAsync(
            "Shared",
            minutes: 900,
            lastPlayed: Utc(2026, 8, 1),
            accountRef: Theirs,
            accounts: [(Mine, null, null), (Theirs, 900, Utc(2026, 8, 1))]);

        await ChooseAsync(AccountScope.Own, confirmed: Mine);

        var row = Assert.Single(await _library.GetOwnershipBucketsAsync(Thresholds));

        Assert.Equal(shared, row.OwnershipId);
        Assert.Equal(0, row.PlaytimeMinutes);
        Assert.Null(row.LastPlayedAt);
        Assert.Equal(LibraryBuckets.NeverPlayed, row.Bucket);
    }

    // ══ 4. The number on the toggle ═════════════════════════════════════════

    [Fact]
    public async Task The_hidden_count_is_the_same_answer_in_either_mode()
    {
        await SeedAsync("Mine", accounts: [(Mine, 10, null)]);
        await SeedAsync("Theirs one", accounts: [(Theirs, 900, null)]);
        await SeedAsync("Theirs two", accounts: [(Theirs, 20, null)]);
        await SeedAsync("Unattributed", minutes: 5);

        await ChooseAsync(AccountScope.All, confirmed: Mine);
        Assert.Equal(2, await _library.CountHiddenByAccountScopeAsync(Thresholds));

        // The toggle has to state what it does before it is used AND after, so
        // the figure cannot depend on the mode currently in force.
        await _settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        Assert.Equal(2, await _library.CountHiddenByAccountScopeAsync(Thresholds));
    }

    [Fact]
    public async Task The_hidden_count_is_zero_without_a_confirmed_account()
    {
        await SeedAsync("Theirs", accounts: [(Theirs, 900, null)]);

        Assert.Equal(0, await _library.CountHiddenByAccountScopeAsync(Thresholds));
    }

    // ══ Seeding ═════════════════════════════════════════════════════════════

    private static DateTime Utc(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One ordinary Steam game carrying a non-seed row for the owned account.
    ///
    /// <para>The filter refuses to hide anything until it has seen proof that
    /// the pass which can name the user's account has actually run, so a test
    /// about any OTHER property of the filter has to establish that first — the
    /// same way a real library does on its first successful owned-list sync.</para>
    /// </summary>
    private Task<long> AttestOwnedAccountPassAsync()
        => SeedAsync("Mine, attesting", accounts: [(Mine, 0, null)]);

    private async Task ChooseAsync(string scope, string confirmed)
    {
        await _settings.SetAsync(AccountScope.SettingKey, scope);
        await _settings.SetAsync(SteamOwnedAccount.RefSettingKey, confirmed);
    }

    /// <summary>
    /// One game with its household play record and, optionally, the per-account
    /// rows a real reader would have produced. Membership rows go through the
    /// repository so the tests exercise the same upsert the resolver does.
    /// </summary>
    private async Task<long> SeedAsync(
        string name,
        long minutes = 0,
        DateTime? lastPlayed = null,
        string store = "steam",
        string? accountRef = null,
        (string AccountRef, long? Minutes, DateTime? LastPlayed)[]? accounts = null)
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
            AccountRef = accountRef ?? accounts?.FirstOrDefault().AccountRef,
        });

        if (minutes > 0 || lastPlayed is not null)
        {
            await _plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_local",
                ObservedAt = Utc(2026, 8, 26),
            });
        }

        foreach (var account in accounts ?? [])
        {
            await _accounts.UpsertAsync(new OwnershipAccountUpsert(
                ownershipId, account.AccountRef, account.Minutes, account.LastPlayed,
                Source: "steam_local",
                ObservedAt: Utc(2026, 8, 26)));
        }

        return ownershipId;
    }

    /// <summary>A membership row under a chosen source label — how the 0015 seed writes.</summary>
    private Task SeedRawAccountAsync(long ownershipId, string accountRef, long? minutes, string source)
        => _accounts.UpsertAsync(new OwnershipAccountUpsert(
            ownershipId, accountRef, minutes, LastPlayedAt: null,
            Source: source, ObservedAt: Utc(2026, 8, 26)));
}
