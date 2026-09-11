using Xunit;

namespace Winnow.Covers.Tests;

public sealed class SteamGridDbHeroSourceTests
{
    private const string Hash = "61ba87bf4177f576150389d84d14bb01";
    private const string Asset = Hash + ".png";
    private const string Url = "https://cdn2.steamgriddb.com/hero/" + Asset;

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("webp")]
    public void Legacy_asset_urls_retain_their_cache_keys(string extension)
    {
        var url = "https://cdn2.steamgriddb.com/hero/" + Hash + "." + extension;
        var key = Assert.IsType<CoverKey>(SteamGridDbHeroUrl.Key(url));
        Assert.Equal(CoverKey.SteamGridDbHero(Hash + "." + extension), key);
        Assert.Equal(CoverKey.SteamGridDbHero(Asset), SteamGridDbHeroUrl.Key(Url.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://cdn2.steamgriddb.com/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com.evil.test/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com@evil.test/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com:443/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/grid/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/hero/../" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/hero/%2e%2e/" + Asset)]
    [InlineData(Url + "?redirect=other")]
    [InlineData(Url + "#fragment")]
    [InlineData(Url + "/")]
    [InlineData("https://cdn2.steamgriddb.com/hero/" + Hash + ".gif")]
    [InlineData("https://cdn2.steamgriddb.com/hero/short.png")]
    public void Unsafe_or_unrecognized_urls_have_no_key(string? url)
        => Assert.Null(SteamGridDbHeroUrl.Key(url));

}
