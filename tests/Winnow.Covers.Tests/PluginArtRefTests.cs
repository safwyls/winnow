using Winnow.Covers;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class PluginArtRefTests
{
    [Fact]
    public void Cache_keys_are_isolated_and_references_round_trip()
    {
        var first = PluginArtRef.Key("first", "https://cdn.example/art.png?size=large")!.Value;
        var second = PluginArtRef.Key("second", "https://cdn.example/art.png?size=large")!.Value;
        var other = PluginArtRef.Key("first", "https://cdn.example/art.png?size=small")!.Value;
        Assert.NotEqual(first.CacheStem, second.CacheStem);
        Assert.NotEqual(first.CacheStem, other.CacheStem);
        Assert.Equal(first, PluginArtRef.Parse(PluginArtRef.Reference(first)));
        Assert.Equal(64, first.Id.Length);
        Assert.DoesNotContain("?", first.CacheStem);
    }

    [Theory]
    [InlineData("http://cdn.example/art.png")]
    [InlineData("file:///C:/art.png")]
    [InlineData("https://secret@cdn.example/art.png")]
    [InlineData("https://cdn.example:8443/art.png")]
    [InlineData("https://cdn.example/art.png#fragment")]
    [InlineData(null)]
    public void Unsafe_urls_have_no_key(string? url) => Assert.Null(PluginArtRef.Key("example", url));
}
