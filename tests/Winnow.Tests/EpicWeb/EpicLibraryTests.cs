using System.Net;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Model;
using Xunit;

namespace Winnow.Tests.EpicWeb;

/// <summary>
/// The library fetch: pagination, playtime, and what each field means when it is
/// absent.
/// </summary>
public sealed class EpicLibraryTests
{
    [Fact]
    public async Task The_library_is_the_union_of_every_page()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.True(library.Succeeded);
        Assert.Equal(3, library.Items.Count);

        // Two requests: page one, then the cursor page.
        Assert.Equal(2, host.Handler.CountFor(EpicEndpoint.LibraryItems));

        var cursorRequest = host.Handler.Requests
            .Last(r => r.Endpoint == EpicEndpoint.LibraryItems);
        Assert.Equal("FAKECURSORPAGE2", cursorRequest.Query("cursor"));
    }

    [Fact]
    public async Task A_truncated_walk_is_discarded_rather_than_passed_off_as_the_whole_library()
    {
        // Page one arrives, page two fails. Half a library is indistinguishable
        // from a library that shrank, so nothing is reported at all.
        using var host = new EpicWebTestHost((request, prior) => request.Endpoint switch
        {
            EpicEndpoint.LibraryItems when request.Query("cursor") is not null =>
                FakeEpicHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            _ => EpicWebTestHost.Healthy()(request, prior),
        });

        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.False(library.Succeeded);
        Assert.Empty(library.Items);
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync());
    }

    [Fact]
    public async Task Playtime_absent_from_Epics_list_is_null_never_zero()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();
        var byId = library.ByCatalogItemId;

        // "Skylark" is in the library fixture and absent from the playtime
        // fixture. Absent means Epic has no figure — which is NOT a claim that
        // the user has played it for zero minutes. Epic's totals only accrue
        // from sessions its own launcher started, so a user who plays through
        // Heroic or through Winnow's process monitor legitimately has no record.
        var unrecorded = byId[EpicFixturesWeb.NoPlaytimeCatalogItemId];
        Assert.Null(unrecorded.TotalPlaytime);
        Assert.Null(unrecorded.PlaytimeMinutes(EpicPlaytimeUnit.Seconds));

        // And the candidate carries the null through to the ingest contract,
        // where null leaves any stored figure alone.
        var candidate = Assert.Single(
            await host.Client.GetOwnershipCandidatesAsync(),
            c => c.ProviderId == EpicFixturesWeb.NoPlaytimeCatalogItemId);
        Assert.Null(candidate.PlaytimeMinutes);
    }

    [Fact]
    public async Task A_reported_zero_is_passed_through_as_zero()
    {
        // The other side of the same coin: Epic went to the trouble of having a
        // record and the record says none. That is a real observation and must
        // not be flattened into "unknown".
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();
        var watchDogs = library.ByCatalogItemId[EpicFixturesWeb.WatchDogsCatalogItemId];

        Assert.Equal(0, watchDogs.TotalPlaytime);
        Assert.Equal(0, watchDogs.PlaytimeMinutes(EpicPlaytimeUnit.Seconds));
    }

    [Fact]
    public async Task The_playtime_unit_is_a_setting_not_a_constant()
    {
        // 9000 raw. Seconds reads 150 minutes; minutes reads 9000. The unit is
        // unverified — see EpicPlaytimeUnit — so it has to be changeable without
        // a rebuild of the conversion.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var fez = (await host.Client.GetOwnedLibraryAsync())
            .ByCatalogItemId[EpicFixturesWeb.FezCatalogItemId];

        Assert.Equal(9000, fez.TotalPlaytime);
        Assert.Equal(150, fez.PlaytimeMinutes(EpicPlaytimeUnit.Seconds));
        Assert.Equal(9000, fez.PlaytimeMinutes(EpicPlaytimeUnit.Minutes));
    }

    [Fact]
    public async Task A_failed_playtime_call_does_not_cost_the_ownership_data()
    {
        using var host = new EpicWebTestHost((request, prior) => request.Endpoint == EpicEndpoint.Playtime
            ? FakeEpicHandler.Json(HttpStatusCode.InternalServerError, "{}")
            : EpicWebTestHost.Healthy()(request, prior));

        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        // The library is the valuable half and it still landed.
        Assert.True(library.Succeeded);
        Assert.Equal(3, library.Items.Count);

        // But nothing pretends to know a playtime.
        Assert.False(library.PlaytimeAnswered);
        Assert.Equal(0, library.WithPlaytime);
        Assert.All(library.Items, i => Assert.Null(i.TotalPlaytime));
        Assert.All(await host.Client.GetOwnershipCandidatesAsync(), c => Assert.Null(c.PlaytimeMinutes));
    }

    [Fact]
    public async Task Candidates_never_claim_to_know_install_state_or_a_last_played_date()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var candidates = await host.Client.GetOwnershipCandidatesAsync();

        Assert.All(candidates, c =>
        {
            Assert.Equal(ExternalIdProviders.Epic, c.Provider);

            // The library service cannot see the local disk. Emitting false here
            // is the bug that emptied the install filter when the Steam Web API
            // did it.
            Assert.Null(c.Installed);
            Assert.Null(c.InstallPath);

            // Epic exposes no last-played timestamp through ANY endpoint —
            // lastPlayed, firstPlayed, updatedAt and lastModified were each
            // confirmed absent from the live GraphQL Playtime type.
            Assert.Null(c.LastPlayedAt);

            // Attribution stays null so the API half and the local half agree:
            // the local reader cannot attribute an account at all, because Epic's
            // manifests are machine-wide.
            Assert.Null(c.AccountRef);

            Assert.Equal(EpicAccountClient.SourceName, c.Source);
        });
    }

    [Fact]
    public async Task Acquisition_dates_are_the_thing_only_the_API_can_answer()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var fez = Assert.Single(
            await host.Client.GetOwnershipCandidatesAsync(),
            c => c.ProviderId == EpicFixturesWeb.FezCatalogItemId);

        // Nothing on disk records when a title was claimed — the local reader
        // leaves this null on purpose, because releaseInfo[0].dateAdded is the
        // STORE RELEASE date, not an acquisition date.
        Assert.Equal(new DateTime(2019, 11, 19, 17, 2, 42, 64, DateTimeKind.Utc), fez.AcquiredAt);
    }

    [Fact]
    public async Task The_library_endpoint_supplies_no_title_and_says_so_with_null()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        // The library service returns identifiers, not display metadata, and
        // Winnow deliberately does not make the extra catalog/bulk/items call —
        // catcache.bin already has every owned title, locally and for free.
        // Null is the ingest contract's "this source has no title", which leaves
        // the local reader's name in charge.
        Assert.All(await host.Client.GetOwnershipCandidatesAsync(), c => Assert.Null(c.Title));
    }

    [Fact]
    public async Task A_second_call_inside_the_TTL_is_served_from_cache()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        await host.Client.GetOwnedLibraryAsync();
        var afterFirst = host.Handler.CountFor(EpicEndpoint.LibraryItems);

        var second = await host.Client.GetOwnedLibraryAsync();

        Assert.True(second.FromCache);
        Assert.Equal(3, second.Items.Count);
        Assert.Equal(afterFirst, host.Handler.CountFor(EpicEndpoint.LibraryItems));
    }

    [Fact]
    public async Task A_cached_library_is_still_served_when_a_later_fetch_fails()
    {
        // Ownership does not un-happen. Yesterday's library is a strictly better
        // answer than an empty one.
        var failLibrary = false;
        using var host = new EpicWebTestHost((request, prior) =>
            request.Endpoint == EpicEndpoint.LibraryItems && failLibrary
                ? FakeEpicHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : EpicWebTestHost.Healthy()(request, prior));

        await host.SignInAsync();
        await host.Client.GetOwnedLibraryAsync();

        failLibrary = true;
        var stale = await host.Client.GetOwnedLibraryAsync(TimeSpan.Zero);

        Assert.True(stale.Succeeded);
        Assert.Equal(3, stale.Items.Count);
        Assert.True(stale.FromCache);
    }

    [Fact]
    public async Task A_failure_is_never_cached_as_an_empty_library()
    {
        using var host = new EpicWebTestHost((request, prior) =>
            request.Endpoint == EpicEndpoint.LibraryItems
                ? FakeEpicHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : EpicWebTestHost.Healthy()(request, prior));

        await host.SignInAsync();

        Assert.False((await host.Client.GetOwnedLibraryAsync()).Succeeded);

        // A second call still tries, rather than serving a cached "owns nothing"
        // for a whole TTL on the strength of one 503.
        var before = host.Handler.CountFor(EpicEndpoint.LibraryItems);
        await host.Client.GetOwnedLibraryAsync();
        Assert.True(host.Handler.CountFor(EpicEndpoint.LibraryItems) > before);
    }

    [Fact]
    public async Task A_429_with_no_Retry_After_is_retried_on_the_exponential_schedule()
    {
        // Epic sends no Retry-After. Legendary crashes on this exact response
        // because it handles 503 and not 429; this pipeline must not.
        using var host = new EpicWebTestHost((request, prior) =>
            request.Endpoint == EpicEndpoint.LibraryItems && prior == 0
                ? FakeEpicHandler.TooManyRequests()
                : EpicWebTestHost.Healthy()(request, prior));

        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.True(library.Succeeded);
        Assert.Equal(3, library.Items.Count);
    }

    [Fact]
    public async Task A_401_on_the_library_refreshes_once_and_retries()
    {
        var libraryCalls = 0;
        using var host = new EpicWebTestHost((request, prior) =>
        {
            if (request.Endpoint == EpicEndpoint.LibraryItems && libraryCalls++ == 0)
            {
                return FakeEpicHandler.Json(HttpStatusCode.Unauthorized, EpicFixturesWeb.Unauthenticated());
            }

            return EpicWebTestHost.Healthy()(request, prior);
        });

        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.True(library.Succeeded);

        // One exchange plus exactly one refresh, driven by the 401.
        var grants = host.Handler.Requests
            .Where(r => r.Endpoint == EpicEndpoint.Token)
            .Select(r => r.GrantType)
            .ToList();
        Assert.Equal(["authorization_code", "refresh_token"], grants);
    }

    [Fact]
    public async Task A_persistent_401_degrades_instead_of_looping()
    {
        using var host = new EpicWebTestHost((request, prior) => request.Endpoint switch
        {
            EpicEndpoint.LibraryItems => FakeEpicHandler.Json(
                HttpStatusCode.Unauthorized, EpicFixturesWeb.Unauthenticated()),
            _ => EpicWebTestHost.Healthy()(request, prior),
        });

        await host.SignInAsync();

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.False(library.Succeeded);

        // Exactly one refresh attempt. A second 401 is returned to the caller
        // rather than looped on — at that point re-minting forever would just
        // burn the rate limit.
        Assert.Equal(1, host.Handler.Requests.Count(r => r.GrantType == "refresh_token"));
    }

    [Fact]
    public async Task Signed_out_but_configured_makes_no_request_at_all()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        Assert.True(await host.Client.IsConfiguredAsync());
        Assert.False(await host.Client.IsSignedInAsync());

        var library = await host.Client.GetOwnedLibraryAsync();

        Assert.False(library.Succeeded);
        Assert.Empty(host.Handler.Requests);
    }
}

/// <summary>
/// The union with the local readers — the half of this feature that is not about
/// OAuth at all.
/// </summary>
public sealed class EpicSourceMergeTests
{
    private static CandidateOwnership LocalCandidate(
        string catalogItemId,
        string title,
        bool? installed,
        string? installPath,
        DateTime observedAt) =>
        new(
            Provider: ExternalIdProviders.Epic,
            ProviderId: catalogItemId,
            Title: title,
            AccountRef: null,
            InstallPath: installPath,
            Installed: installed,
            // Epic writes no playtime and no last-played to disk, anywhere.
            PlaytimeMinutes: null,
            LastPlayedAt: null,
            AcquiredAt: null,
            Source: EpicLibrarySource.SourceName,
            ObservedAt: observedAt);

    [Fact]
    public void The_API_half_cannot_clear_install_state_the_local_half_established()
    {
        // This is the regression that emptied the whole library's install filter
        // once already, via the Steam Web API. The rule that prevents it is that
        // a source with no opinion sends null, and the merge takes the answer
        // from whichever source actually looked.
        var observedAt = new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

        var local = LocalCandidate(
            EpicFixturesWeb.FezCatalogItemId, "Fez", installed: true, @"D:\Epic\Fez", observedAt);

        var api = new EpicLibraryItem(
                EpicFixturesWeb.FezCatalogItemId, EpicFixturesWeb.FezAppName, "ns", null,
                new DateTime(2019, 11, 19, 17, 2, 42, DateTimeKind.Utc), 9000)
            .ToCandidate(EpicAccountClient.SourceName, EpicPlaytimeUnit.Seconds, observedAt);

        var localFirst = Assert.Single(CandidateOwnershipMerge.Coalesce([local, api]));
        var apiFirst = Assert.Single(CandidateOwnershipMerge.Coalesce([api, local]));

        foreach (var merged in new[] { localFirst, apiFirst })
        {
            // Install state survives from the only source that inspected a disk.
            Assert.True(merged.Installed);
            Assert.Equal(@"D:\Epic\Fez", merged.InstallPath);

            // Each source contributes what only it knows.
            Assert.Equal("Fez", merged.Title);
            Assert.Equal(150, merged.PlaytimeMinutes);
            Assert.Equal(new DateTime(2019, 11, 19, 17, 2, 42, DateTimeKind.Utc), merged.AcquiredAt);

            // Neither source has a last-played date to give.
            Assert.Null(merged.LastPlayedAt);
        }

        // Resolution order is presentation, not precedence: both orders agree on
        // every field. When that property did not hold, the failure was silent.
        Assert.Equal(localFirst.Installed, apiFirst.Installed);
        Assert.Equal(localFirst.InstallPath, apiFirst.InstallPath);
        Assert.Equal(localFirst.PlaytimeMinutes, apiFirst.PlaytimeMinutes);
        Assert.Equal(localFirst.Title, apiFirst.Title);
        Assert.Equal(localFirst.AcquiredAt, apiFirst.AcquiredAt);
    }

    [Fact]
    public void A_local_uninstall_still_shows_through_the_merge()
    {
        // The mirror image, and the reason "installed: false" from the local
        // reader must NOT be treated as timid. False is an observation there —
        // the manifests directory was read and this title has no manifest.
        var observedAt = new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

        var local = LocalCandidate(
            EpicFixturesWeb.FezCatalogItemId, "Fez", installed: false, installPath: null, observedAt);

        var api = new EpicLibraryItem(
                EpicFixturesWeb.FezCatalogItemId, EpicFixturesWeb.FezAppName, "ns", null, null, 9000)
            .ToCandidate(EpicAccountClient.SourceName, EpicPlaytimeUnit.Seconds, observedAt);

        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce([api, local]));

        Assert.False(merged.Installed);
        Assert.Null(merged.InstallPath);
    }

    [Fact]
    public void A_title_only_the_API_owns_survives_the_union()
    {
        // The API sees the true entitlement list. catcache.bin is only rewritten
        // when the launcher starts and logs in, so a title bought since then is
        // API-only — and must not be dropped for being absent from one side.
        var observedAt = new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

        var local = LocalCandidate(
            EpicFixturesWeb.FezCatalogItemId, "Fez", installed: true, @"D:\Epic\Fez", observedAt);

        var api = new EpicLibraryItem(
                EpicFixturesWeb.NoPlaytimeCatalogItemId, EpicFixturesWeb.NoPlaytimeAppName,
                "ns", null, observedAt, null)
            .ToCandidate(EpicAccountClient.SourceName, EpicPlaytimeUnit.Seconds, observedAt);

        var merged = CandidateOwnershipMerge.Coalesce([local, api]);

        Assert.Equal(2, merged.Count);

        var apiOnly = Assert.Single(merged, c => c.ProviderId == EpicFixturesWeb.NoPlaytimeCatalogItemId);

        // It arrives knowing nothing about the disk and nothing about playtime,
        // and says so with nulls rather than with a false and a zero.
        Assert.Null(apiOnly.Installed);
        Assert.Null(apiOnly.PlaytimeMinutes);
        Assert.Null(apiOnly.Title);
    }

    [Fact]
    public async Task The_API_and_local_halves_key_on_the_same_catalog_item_id()
    {
        // The whole merge depends on this. The API's ProviderId must be the
        // catalogItemId, not the appName — "Bluebird" would never join to Fez.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var candidates = await host.Client.GetOwnershipCandidatesAsync();
        var ids = candidates.Select(c => c.ProviderId).ToList();

        Assert.Contains(EpicFixturesWeb.FezCatalogItemId, ids);
        Assert.DoesNotContain(EpicFixturesWeb.FezAppName, ids);
    }
}
