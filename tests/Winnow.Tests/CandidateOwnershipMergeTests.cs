using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The rule that stops one ownership being described twice in a single ingest
/// pass. No database, no filesystem: this is a pure function and the properties
/// it has to hold — commutativity over the monotonic fields, one entry out per
/// ownership in, order preserved — are worth pinning where nothing can hide.
/// </summary>
public class CandidateOwnershipMergeTests
{
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0)
        => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static CandidateOwnership Local(
        string appId,
        string? title = "Portal",
        long? playtime = null,
        DateTime? lastPlayed = null,
        bool? installed = true,
        DateTime? observedAt = null)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: installed is true ? @"C:\Steam\steamapps\common\Portal" : null,
            Installed: installed,
            PlaytimeMinutes: playtime,
            LastPlayedAt: lastPlayed,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt ?? Utc(2026, 8, 25, 16, 0));

    private static CandidateOwnership Web(
        string appId,
        string? title = "Portal",
        long? playtime = null,
        DateTime? lastPlayed = null,
        DateTime? observedAt = null)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: null,
            Installed: null,
            PlaytimeMinutes: playtime,
            LastPlayedAt: lastPlayed,
            AcquiredAt: null,
            Source: "steam_web_api",
            ObservedAt: observedAt ?? Utc(2026, 8, 25, 12, 0));

    [Fact]
    public void Candidates_for_different_appids_are_left_alone_and_keep_their_order()
    {
        var merged = CandidateOwnershipMerge.Coalesce(
            [Local("400"), Local("620"), Local("70")]);

        Assert.Equal(["400", "620", "70"], merged.Select(c => c.ProviderId));
    }

    /// <summary>
    /// The live disagreement, exactly: Portal reports 279 minutes through
    /// GetOwnedGames and 280 through localconfig.vdf. Both figures are the same
    /// monotonic counter, so the larger is simply the less stale reading.
    /// </summary>
    [Fact]
    public void Two_sources_a_minute_apart_become_one_candidate_holding_the_larger_figure()
    {
        var lastPlayed = Utc(2018, 5, 25, 3, 7);

        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", playtime: 280, lastPlayed: lastPlayed),
            Web("400", playtime: 279, lastPlayed: lastPlayed),
        ]));

        Assert.Equal(280, merged.PlaytimeMinutes);
        Assert.Equal(lastPlayed, merged.LastPlayedAt);

        // Provenance follows the figure that won, so play_records.source names
        // the reader the stored number actually came from.
        Assert.Equal("steam_local", merged.Source);
    }

    /// <summary>
    /// §4.1 makes the local files primary for playtime, but the merge does not
    /// need to know that: max reaches the same answer from either side, so a
    /// stale local scan cannot pull the Web API's newer figure backwards either.
    /// </summary>
    [Fact]
    public void Max_protects_whichever_source_happens_to_be_the_stale_one()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", playtime: 279, lastPlayed: Utc(2018, 5, 25)),
            Web("400", playtime: 280, lastPlayed: Utc(2026, 8, 24)),
        ]));

        Assert.Equal(280, merged.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 24), merged.LastPlayedAt);
        Assert.Equal("steam_web_api", merged.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_merged_playtime_and_date_do_not_depend_on_union_order(bool webFirst)
    {
        var local = Local("400", playtime: 280, lastPlayed: Utc(2026, 8, 24));
        var web = Web("400", playtime: 279, lastPlayed: Utc(2026, 8, 20));

        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
            webFirst ? [web, local] : [local, web]));

        Assert.Equal(280, merged.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 24), merged.LastPlayedAt);
        Assert.Equal("steam_local", merged.Source);
    }

    /// <summary>
    /// The sentinel case after the reader fix: both sources say "played, date
    /// unknown". Null must survive the merge rather than being read as "no
    /// observation" or backfilled with anything.
    /// </summary>
    [Fact]
    public void An_unknown_last_played_stays_unknown_on_both_sides()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("60", playtime: 3, lastPlayed: null, installed: false),
            Web("60", playtime: 3, lastPlayed: null),
        ]));

        Assert.Equal(3, merged.PlaytimeMinutes);
        Assert.Null(merged.LastPlayedAt);
    }

    /// <summary>
    /// A date one source has and the other does not is not a conflict — it is
    /// the only answer available, and null is "unknown", never "earlier".
    /// </summary>
    [Fact]
    public void A_date_only_one_source_knows_survives_the_merge()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", playtime: 280, lastPlayed: null),
            Web("400", playtime: 280, lastPlayed: Utc(2018, 5, 25)),
        ]));

        Assert.Equal(Utc(2018, 5, 25), merged.LastPlayedAt);
    }

    /// <summary>
    /// Install state and its path are one answer. The Web API has no opinion at
    /// all (§4.2 cannot see the disk), so the local scan's answer carries in
    /// either order — including the <c>false</c> that makes an uninstall show.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_source_that_can_see_the_disk_decides_install_state(bool webFirst)
    {
        var installed = Local("400", playtime: 280, installed: true);
        var web = Web("400", playtime: 280);

        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
            webFirst ? [web, installed] : [installed, web]));
        Assert.True(merged.Installed);
        Assert.Equal(@"C:\Steam\steamapps\common\Portal", merged.InstallPath);

        var uninstalled = Local("400", playtime: 280, installed: false);
        var gone = Assert.Single(CandidateOwnershipMerge.Coalesce(
            webFirst ? [web, uninstalled] : [uninstalled, web]));
        Assert.False(gone.Installed);
        Assert.Null(gone.InstallPath);
    }

    /// <summary>
    /// Neither source can see the disk: the merged candidate has to keep saying
    /// so, because null is what leaves the stored flag alone.
    /// </summary>
    [Fact]
    public void Two_opinionless_sources_produce_an_opinionless_candidate()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
            [Web("400", playtime: 1), Web("400", playtime: 2)]));

        Assert.Null(merged.Installed);
        Assert.Null(merged.InstallPath);
    }

    [Fact]
    public void A_titleless_candidate_takes_the_title_the_other_source_has()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", title: null, playtime: 280, installed: false),
            Web("400", title: "Portal", playtime: 280),
        ]));

        Assert.Equal("Portal", merged.Title);
    }

    [Fact]
    public void A_blank_title_is_not_a_title()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", title: "   ", playtime: 280, installed: false),
            Web("400", title: "Portal", playtime: 280),
        ]));

        Assert.Equal("Portal", merged.Title);
    }

    /// <summary>
    /// One of the pair may have been served from the §4.2 cache and be hours
    /// stale. The merged observation is as of the freshest input that fed it,
    /// which is what keeps play_records.observed_at monotonic across syncs — and
    /// what stopped a cached 1970 row winning the "latest record" query the
    /// moment the cache happened to refresh mid-sync.
    /// </summary>
    [Fact]
    public void The_merged_observation_is_stamped_with_the_later_of_the_two_reads()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Web("400", playtime: 280, observedAt: Utc(2026, 8, 25, 2, 25)),
            Local("400", playtime: 280, observedAt: Utc(2026, 8, 25, 16, 1)),
        ]));

        Assert.Equal(Utc(2026, 8, 25, 16, 1), merged.ObservedAt);
    }

    /// <summary>
    /// Ownership is keyed on (release, store) and the resolver finds the release
    /// by (provider, provider_id). A different provider is a different release,
    /// so the same id under two providers must not collapse.
    /// </summary>
    [Fact]
    public void The_same_id_under_two_providers_is_two_ownerships()
    {
        var gog = Local("400") with { Provider = ExternalIdProviders.Gog };

        var merged = CandidateOwnershipMerge.Coalesce([Local("400"), gog]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void An_empty_pass_merges_to_nothing()
        => Assert.Empty(CandidateOwnershipMerge.Coalesce([]));

    /// <summary>Three views collapse to one, not to two.</summary>
    [Fact]
    public void More_than_two_sources_still_produce_a_single_observation()
    {
        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce(
        [
            Local("400", playtime: 278, lastPlayed: Utc(2018, 5, 20)),
            Web("400", playtime: 279, lastPlayed: Utc(2018, 5, 25)),
            Web("400", playtime: 280, lastPlayed: Utc(2018, 5, 22)),
        ]));

        Assert.Equal(280, merged.PlaytimeMinutes);
        Assert.Equal(Utc(2018, 5, 25), merged.LastPlayedAt);
    }
}
