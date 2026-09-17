using System.Net;
using Winnow.Enrich.Updates.Model;
using Xunit;

namespace Winnow.Tests.Updates;

public sealed class SteamIconMetadataTests
{
    [Theory]
    [InlineData("25a5a16b2423bf7487ac5340b5b0948cef48c5f8", "25a5a16b2423bf7487ac5340b5b0948cef48c5f8")]
    [InlineData("25A5A16B2423BF7487AC5340B5B0948CEF48C5F8", "25a5a16b2423bf7487ac5340b5b0948cef48c5f8")]
    [InlineData("../../unexpected", null)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", null)]
    [InlineData("25a5a16b2423bf7487ac5340b5b0948cef48c5f", null)]
    [InlineData("", null)]
    public async Task Common_icon_accepts_only_a_complete_hex_hash(string icon, string? expected)
    {
        using var host = new UpdateSignalTestHost((_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK,
            """{"data":{"620":{"common":{"name":"Portal 2","icon":"ICON"}}}}""".Replace("ICON", icon, StringComparison.Ordinal)));
        var result = await host.Builds.GetAppInfoAsync("620");
        Assert.Equal(AppInfoOutcome.Ok, result.Outcome);
        Assert.Equal(expected, result.Info!.IconHash);
        var cached = await host.Builds.GetAppInfoAsync("620");
        Assert.True(cached.ServedFromCache);
        Assert.Equal(expected, cached.Info!.IconHash);
    }

    [Fact]
    public async Task Icon_only_common_block_is_useful_artwork_metadata()
    {
        using var host = new UpdateSignalTestHost((_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK,
            """{"data":{"620":{"common":{"icon":"25a5a16b2423bf7487ac5340b5b0948cef48c5f8"}}}}"""));
        var result = await host.Builds.GetAppInfoAsync("620");
        Assert.Equal(AppInfoOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Info!.IconHash);
    }
}
