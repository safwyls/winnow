using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// How the per-account membership rows behind the visibility filter get written:
/// the merge that unions them, the resolver that stores them, and the settings
/// the toggle is gated on.
///
/// <para><see cref="AccountScopeTests"/> covers what the filter DOES with them.
/// This file is about them being correct in the first place, which is the half
/// that decides whether acceptance criterion #3's honesty claim holds.</para>
/// </summary>
public class AccountMembershipTests
{
    private const string Mine = "11111";
    private const string Theirs = "22222";

    private static DateTime Utc(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, DateTimeKind.Utc);

    // ══ The union ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_unions_accounts_from_both_candidates()
    {
        // The local scan saw two accounts; the web call answered for one of
        // them. The play tuple keeps ONE coherent answer, so a winner-only list
        // would drop whichever account lost — the exact population the filter
        // has to be able to see.
        var local = Candidate(
            playtime: 900,
            accounts: [Account(Mine, 40), Account(Theirs, 900)]);
        var web = Candidate(playtime: 40, source: "steam_web", accounts: [Account(Mine, 40)]);

        var merged = CandidateOwnershipMerge.Merge(local, web);

        Assert.Equal([Mine, Theirs], merged.Accounts.Select(a => a.AccountRef).Order());
        Assert.Equal(900, merged.PlaytimeMinutes);
    }

    [Fact]
    public void Merge_takes_the_higher_figure_within_one_account()
    {
        // Two partial views of ONE cumulative counter, so the larger reading is
        // the closer one — the same reasoning as PlaytimeView.LowerBound.
        var first = Candidate(accounts: [Account(Mine, 40, Utc(2026, 1, 1))]);
        var second = Candidate(accounts: [Account(Mine, 61, Utc(2025, 6, 1))]);

        var merged = CandidateOwnershipMerge.Merge(first, second);

        var only = Assert.Single(merged.Accounts);
        Assert.Equal(61, only.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 1, 1), only.LastPlayedAt);
    }

    [Fact]
    public void Merge_never_combines_two_different_accounts()
    {
        // The whole point of the table. Folding 40 and 900 into 940 would
        // recreate the household collapse it exists to undo.
        var first = Candidate(accounts: [Account(Mine, 40)]);
        var second = Candidate(accounts: [Account(Theirs, 900)]);

        var merged = CandidateOwnershipMerge.Merge(first, second);

        Assert.Equal(2, merged.Accounts.Count);
        Assert.Equal(40, merged.Accounts.Single(a => a.AccountRef == Mine).PlaytimeMinutes);
        Assert.Equal(900, merged.Accounts.Single(a => a.AccountRef == Theirs).PlaytimeMinutes);
    }

    [Fact]
    public void Merge_keeps_a_null_figure_as_unknown_rather_than_zero()
    {
        // An account that holds a licence and has never launched it.
        var first = Candidate(accounts: [Account(Mine, null)]);
        var second = Candidate(accounts: [Account(Mine, null)]);

        var only = Assert.Single(CandidateOwnershipMerge.Merge(first, second).Accounts);

        Assert.Null(only.PlaytimeMinutes);
    }

    // ══ The write ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task The_resolver_writes_one_row_per_account()
    {
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 900, accounts: [Account(Mine, 40), Account(Theirs, 900)]));

        var rows = await harness.MembershipAsync();

        Assert.Equal([Mine, Theirs], rows.Select(r => r.AccountRef));
        Assert.Equal(40, rows.Single(r => r.AccountRef == Mine).PlaytimeMinutes);
    }

    [Fact]
    public async Task The_resolver_falls_back_to_the_candidates_own_account()
    {
        // Every reader written before the per-account list existed, and the
        // ordinary single-account machine, where "the winner" and "the only
        // account" are the same answer.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(playtime: 40, accountRef: Mine));

        var only = Assert.Single(await harness.MembershipAsync());

        Assert.Equal(Mine, only.AccountRef);
        Assert.Equal(40, only.PlaytimeMinutes);
    }

    [Fact]
    public async Task A_candidate_naming_nobody_writes_nothing()
    {
        // GOG's machine-wide registry, every Epic reader. Silence here is what
        // makes the filter leave the row visible.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(playtime: 40, accountRef: null));

        Assert.Empty(await harness.MembershipAsync());
    }

    [Fact]
    public async Task Re_resolving_keeps_the_first_sighting_and_the_higher_figure()
    {
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 40, observedAt: Utc(2026, 1, 1), accounts: [Account(Mine, 40)]));

        // A later pass whose figure went BACKWARDS — a source that could not see
        // the whole counter. It must not write the membership row down.
        await harness.ResolveAsync(Candidate(
            playtime: 30, observedAt: Utc(2026, 8, 1), accounts: [Account(Mine, 30)]));

        var only = Assert.Single(await harness.MembershipAsync());

        Assert.Equal(40, only.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 1, 1), only.FirstSeenAt);
        Assert.Equal(Utc(2026, 8, 1), only.LastSeenAt);
    }

    [Fact]
    public async Task A_real_reader_retires_the_seed_marker()
    {
        // The window between migration 0015 and the first sync is the only time
        // a seeded row is load-bearing, and this is how it closes.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(playtime: 40, accountRef: Mine));
        await harness.Accounts.UpsertAsync(new OwnershipAccountUpsert(
            (await harness.MembershipAsync())[0].OwnershipId,
            Mine, 40, null,
            OwnershipAccountSources.LegacyOwnershipColumn, Utc(2026, 1, 1)));

        Assert.Equal(
            OwnershipAccountSources.LegacyOwnershipColumn,
            (await harness.MembershipAsync())[0].Source);

        await harness.ResolveAsync(Candidate(playtime: 41, accountRef: Mine));

        Assert.Equal("steam_local", (await harness.MembershipAsync())[0].Source);
    }

    [Fact]
    public async Task Account_refs_are_listed_per_store()
    {
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 900, accounts: [Account(Mine, 40), Account(Theirs, 900)]));
        await harness.ResolveAsync(Candidate(
            appId: "gog-1", provider: ExternalIdProviders.Gog,
            playtime: 10, accounts: [Account("77777", 10)]));

        Assert.Equal(
            [Mine, Theirs],
            await harness.Accounts.GetAccountRefsAsync(ExternalIdProviders.Steam));
        Assert.Equal(
            ["77777"],
            await harness.Accounts.GetAccountRefsAsync(ExternalIdProviders.Gog));
    }

    // ══ The gate on the toggle ══════════════════════════════════════════════

    [Fact]
    public async Task The_toggle_is_disabled_until_an_account_is_confirmed()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var visibility = new AccountVisibilityService(
            settings, new LibraryQueryRepository(db.Factory));

        var panel = new StoresViewModel(new FakeStoreConnections(), accountVisibility: visibility);

        await panel.RefreshCommand.ExecuteAsync(null);
        Assert.False(panel.CanChooseAccountScope);
        Assert.True(panel.ShowAccountScopeBlocked);
        Assert.Contains("Set a Steam Web API key first", panel.AccountScopeBlockedMessage);

        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await panel.RefreshCommand.ExecuteAsync(null);

        Assert.True(panel.CanChooseAccountScope);
        Assert.False(panel.ShowAccountScopeBlocked);
    }

    [Fact]
    public async Task The_toggle_defaults_to_every_account_and_persists_the_choice()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var visibility = new AccountVisibilityService(
            settings, new LibraryQueryRepository(db.Factory));

        var panel = new StoresViewModel(new FakeStoreConnections(), accountVisibility: visibility);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await panel.RefreshCommand.ExecuteAsync(null);

        // Acceptance criterion #4: nothing changes until the user acts.
        Assert.False(panel.ShowOwnAccountOnly);
        Assert.Null(await settings.GetAsync(AccountScope.SettingKey));

        var reloaded = 0;
        panel.ReloadLibrary = () =>
        {
            reloaded++;
            return Task.CompletedTask;
        };

        panel.ShowOwnAccountOnly = true;
        await panel.PendingAccountScopeSave;

        Assert.Equal(AccountScope.Own, await settings.GetAsync(AccountScope.SettingKey));
        Assert.Equal(1, reloaded);
        Assert.True(panel.ShowAccountScopeCaveat);
    }

    [Fact]
    public void The_toggle_states_what_it_hides_with_the_figure_in_the_data_face()
    {
        var panel = new StoresViewModel(new FakeStoreConnections());

        // The switch face carries no number: design-system.md renders every
        // figure in Plex Mono tnum, and a control's content face is the UI face.
        Assert.Equal("Show only your account", panel.AccountScopeToggleLabel);
        Assert.DoesNotContain(
            panel.AccountScopeToggleLabel, c => char.IsDigit(c));

        // Nothing from another account: no line at all rather than a "0".
        Assert.False(panel.ShowAccountScopeCount);

        panel.AccountScopeHiddenCount = 1;
        Assert.True(panel.ShowAccountScopeCount);
        Assert.Equal("1", panel.AccountScopeHiddenCountText);
        Assert.Equal("game from other accounts", panel.AccountScopeHiddenUnitLabel);

        // Grouped, because the figure renders in a tnum face beside the words.
        panel.AccountScopeHiddenCount = 1234;
        Assert.Equal("1,234", panel.AccountScopeHiddenCountText);
        Assert.Equal("games from other accounts", panel.AccountScopeHiddenUnitLabel);
    }

    // ══ The err-low band on the stored figure (TASK-50's ruling) ════════════

    [Fact]
    public async Task A_within_band_disagreement_settles_at_the_lower_figure()
    {
        // localconfig.vdf says 280 for Portal, GetOwnedGames says 279. The
        // ownership series settles at the lower reading; if these rows settled
        // at the higher one, a library filtered to one account would report a
        // minute MORE than the same library unfiltered.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(playtime: 279, accounts: [Account(Mine, 279)]));
        await harness.ResolveAsync(Candidate(
            playtime: 280, source: "steam_web", accounts: [Account(Mine, 280)]));

        Assert.Equal(279, (await harness.MembershipAsync())[0].PlaytimeMinutes);

        // And in the other order, so the stored answer does not depend on which
        // source happened to run first.
        using var reversed = new ResolverHarness();

        await reversed.ResolveAsync(Candidate(playtime: 280, accounts: [Account(Mine, 280)]));
        await reversed.ResolveAsync(Candidate(
            playtime: 279, source: "steam_web", accounts: [Account(Mine, 279)]));

        Assert.Equal(279, (await reversed.MembershipAsync())[0].PlaytimeMinutes);
    }

    [Fact]
    public async Task A_rise_beyond_the_band_is_recorded_as_play()
    {
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(playtime: 279, accounts: [Account(Mine, 279)]));
        await harness.ResolveAsync(Candidate(playtime: 281, accounts: [Account(Mine, 281)]));

        Assert.Equal(281, (await harness.MembershipAsync())[0].PlaytimeMinutes);
    }

    [Fact]
    public async Task A_fall_from_a_reader_that_is_behind_is_ignored()
    {
        // localconfig.vdf on a machine that has not synced: a stale floor of a
        // counter another PC carried further. Its older last-played is the tell.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 900,
            accounts: [Account(Mine, 900, Utc(2026, 8, 1))]));

        await harness.ResolveAsync(Candidate(
            playtime: 400,
            observedAt: Utc(2026, 8, 27),
            accounts: [Account(Mine, 400, Utc(2026, 1, 1))]));

        Assert.Equal(900, (await harness.MembershipAsync())[0].PlaytimeMinutes);
    }

    [Fact]
    public async Task A_fall_a_current_reader_corroborates_is_a_correction()
    {
        // Same account, same source, last-played no older than the stored one,
        // and fewer minutes: a reader correcting its own count. Without this
        // path the column could only ratchet upward, and one spurious high
        // reading would be a permanently wrong tile in filtered mode.
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 9000,
            accounts: [Account(Mine, 9000, Utc(2026, 8, 1))]));

        await harness.ResolveAsync(Candidate(
            playtime: 400,
            observedAt: Utc(2026, 8, 27),
            accounts: [Account(Mine, 400, Utc(2026, 8, 2))]));

        Assert.Equal(400, (await harness.MembershipAsync())[0].PlaytimeMinutes);
    }

    [Fact]
    public async Task A_fall_with_no_date_at_all_corroborates_nothing()
    {
        using var harness = new ResolverHarness();

        await harness.ResolveAsync(Candidate(
            playtime: 900, accounts: [Account(Mine, 900, Utc(2026, 8, 1))]));
        await harness.ResolveAsync(Candidate(
            playtime: 400, accounts: [Account(Mine, 400)]));

        Assert.Equal(900, (await harness.MembershipAsync())[0].PlaytimeMinutes);
    }

    [Fact]
    public void The_two_layers_share_one_tolerance_band()
    {
        // Two literals would let the filtered library settle a minute above the
        // unfiltered one, for exactly the ownerships the band exists to quiet.
        Assert.Equal(PlaytimeTolerance.Minutes, ExternalIdResolver.PlaytimeToleranceMinutes);
    }

    // ══ Fixtures ════════════════════════════════════════════════════════════

    private static CandidateAccount Account(
        string accountRef, long? minutes, DateTime? lastPlayed = null)
        => new(accountRef, minutes, lastPlayed);

    private static CandidateOwnership Candidate(
        string appId = "400",
        string provider = ExternalIdProviders.Steam,
        string? accountRef = Mine,
        long? playtime = null,
        DateTime? lastPlayed = null,
        DateTime? observedAt = null,
        string source = "steam_local",
        CandidateAccount[]? accounts = null)
        => new(
            Provider: provider,
            ProviderId: appId,
            Title: "Portal",
            AccountRef: accountRef,
            InstallPath: null,
            Installed: null,
            PlaytimeMinutes: playtime,
            LastPlayedAt: lastPlayed,
            AcquiredAt: null,
            Source: source,
            ObservedAt: observedAt ?? Utc(2026, 8, 26))
        {
            Accounts = accounts ?? [],
        };

    /// <summary>A migrated database with the real repositories and a resolver over them.</summary>
    private sealed class ResolverHarness : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly ExternalIdResolver _resolver;

        public ResolverHarness()
        {
            Accounts = new OwnershipAccountRepository(_db.Factory);
            _resolver = new ExternalIdResolver(
                new WorkRepository(_db.Factory),
                new ReleaseRepository(_db.Factory),
                new OwnershipRepository(_db.Factory),
                new PlayRecordRepository(_db.Factory),
                new PlaytimeSnapshotRepository(_db.Factory),
                _db.Factory,
                Accounts);
        }

        public OwnershipAccountRepository Accounts { get; }

        public Task ResolveAsync(CandidateOwnership candidate)
            => _resolver.ResolveAsync([candidate], default, PlaytimeView.LowerBound);

        /// <summary>Every membership row in the database, ordered by ownership then account.</summary>
        public async Task<IReadOnlyList<OwnershipAccount>> MembershipAsync()
        {
            var ownerships = new OwnershipRepository(_db.Factory);
            var rows = new List<OwnershipAccount>();

            foreach (var ownership in await ownerships.GetAllAsync())
            {
                rows.AddRange(await Accounts.GetByOwnershipAsync(ownership.Id));
            }

            return rows;
        }

        public void Dispose() => _db.Dispose();
    }

}
