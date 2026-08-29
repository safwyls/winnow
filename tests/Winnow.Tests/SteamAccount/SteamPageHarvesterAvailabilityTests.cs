using Winnow.Auth.WebView;
using Winnow.Core.Auth;
using Xunit;

namespace Winnow.Tests.SteamAccount;

/// <summary>
/// What the harvester does when it cannot run, which in this test process is
/// always: xUnit starts no Avalonia application, so there is no window to host a
/// browser in and no path by which a real session could open.
///
/// <para>That makes these the only two things about the host worth asserting
/// without a browser — and both are refusals, which is the half of the behaviour
/// that must never regress. Everything else it does is
/// <see cref="SteamAccountPagePolicy"/>'s, and is asserted directly.</para>
/// </summary>
public class SteamPageHarvesterAvailabilityTests
{
    [Fact]
    public async Task A_console_less_host_is_told_so_rather_than_left_waiting()
    {
        var harvester = new WebView2SteamPageHarvester();

        Assert.False(await harvester.IsAvailableAsync());

        var result = await harvester.HarvestAsync(new SteamPageHarvestRequest { ConsentGranted = true });

        // Unavailable, not Failed: the caller's remedy is the saved-file route,
        // not a retry.
        Assert.Equal(SteamPageHarvestOutcome.Unavailable, result.Outcome);
        Assert.NotNull(result.Detail);
        Assert.Null(result.Pages);
    }

    [Fact]
    public async Task Nothing_opens_without_consent_having_been_recorded()
    {
        var harvester = new WebView2SteamPageHarvester();

        var result = await harvester.HarvestAsync(new SteamPageHarvestRequest { ConsentGranted = false });

        // Checked before the runtime probe and before any window could exist, so
        // this is the same answer on a machine where the browser would have
        // worked.
        Assert.Equal(SteamPageHarvestOutcome.Cancelled, result.Outcome);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task A_null_request_is_a_programming_error_not_an_outcome()
    {
        var harvester = new WebView2SteamPageHarvester();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harvester.HarvestAsync(null!));
    }
}
