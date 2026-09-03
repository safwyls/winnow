using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The feed obeys the account-visibility filter without knowing it exists.
///
/// <para>This is the claim the whole design rests on. The filter is applied
/// inside <c>LibraryQueryRepository.GetOwnershipBucketsAsync</c>, which is the
/// engine's only source of library rows, so a game the user has filtered away
/// cannot be recommended to them — and no code in <c>Winnow.Recommend</c>
/// mentions accounts, scopes or settings to make that true. A feed that could
/// still surface a hidden game would be the most visible way this feature could
/// fail: the user asked to stop seeing a housemate's library and the app would
/// go on suggesting it.</para>
/// </summary>
public class AccountScopeFeedTests : IDisposable
{
    private const string Mine = "11111";
    private const string Theirs = "22222";

    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task The_feed_never_recommends_a_game_the_filter_hides()
    {
        var mine = await _harness.SeedGameAsync("Mine, unplayed");
        var theirs = await _harness.SeedGameAsync("Theirs, unplayed");

        await AttributeAsync(mine.OwnershipId, Mine);
        await AttributeAsync(theirs.OwnershipId, Theirs);

        // Unfiltered, both are candidates: neither has been played, which is the
        // population the feed exists to surface.
        var everything = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        Assert.Contains(everything.Items, i => i.ReleaseId == theirs.ReleaseId);

        await _harness.Settings.SetAsync(SteamOwnedAccount.RefSettingKey, Mine);
        await _harness.Settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);

        var filtered = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        Assert.DoesNotContain(filtered.Items, i => i.ReleaseId == theirs.ReleaseId);
        Assert.Contains(filtered.Items, i => i.ReleaseId == mine.ReleaseId);
    }

    /// <summary>One membership row from a real reader, which is what makes absence meaningful.</summary>
    private Task AttributeAsync(long ownershipId, string accountRef)
        => _harness.OwnershipAccounts.UpsertAsync(new OwnershipAccountUpsert(
            ownershipId,
            accountRef,
            PlaytimeMinutes: 0,
            LastPlayedAt: null,
            Source: "steam_web",
            ObservedAt: RecommendHarness.AsOf));
}
