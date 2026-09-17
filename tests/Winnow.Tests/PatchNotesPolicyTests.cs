using Winnow.Core.Reading;
using Xunit;

namespace Winnow.Tests;

public sealed class PatchNotesPolicyTests
{
    [Theory]
    [InlineData("https://store.steampowered.com/app/440/")]
    [InlineData("https://store.epicgames.com/en-US/p/hades")]
    [InlineData("https://www.gog.com/en/game/stardew_valley")]
    [InlineData("https://steamcommunity.com/games/413150/announcements/detail/1")]
    [InlineData("https://steamstore-a.akamaihd.net/news/externalpost/1")]
    [InlineData("https://example.com/")]
    [InlineData("http://example.com:8080/article?q=hello#section")]
    [InlineData("HTTPS://EXAMPLE.COM/")]
    [InlineData("https://www.youtube.com/embed/1")]
    public void Web_addresses_open_and_stay_inside_the_browser(string url)
    {
        var address = new Uri(url);
        Assert.True(PatchNotesPolicy.IsReadable(url));
        Assert.Equal(address, PatchNotesPolicy.For(url)!.Start);
        // The destination need not share the initial page's origin.
        var policy = PatchNotesPolicy.For("https://start.example.com/")!;
        Assert.Equal(PatchNotesNavigation.Allow, policy.ClassifyNavigation(address));
        Assert.Equal(PatchNotesNavigation.Allow, policy.ClassifyPopup(address));
        Assert.Equal(PatchNotesNavigation.Allow, policy.ClassifyFrame(address));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("steam://run/440")]
    [InlineData("mailto:test@example.com")]
    [InlineData("ms-msdt:/id")]
    [InlineData("blob:https://example.com/1234")]
    [InlineData("about:blank")]
    [InlineData("about:settings")]
    [InlineData("/news/app/440")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_web_addresses_are_refused_at_every_page_controlled_gate(string? url)
    {
        Assert.False(PatchNotesPolicy.IsReadable(url));
        Assert.Null(PatchNotesPolicy.For(url));
        Uri.TryCreate(url, UriKind.Absolute, out var address);
        var policy = PatchNotesPolicy.For("https://example.com/")!;
        Assert.Equal(PatchNotesNavigation.Block, policy.ClassifyNavigation(address));
        Assert.Equal(PatchNotesNavigation.Block, policy.ClassifyPopup(address));
        Assert.Equal(PatchNotesNavigation.Block, policy.ClassifyFrame(address));
    }

    [Fact]
    public void Relative_uri_objects_are_refused_without_throwing()
    {
        var uri = new Uri("relative", UriKind.Relative);
        Assert.False(PatchNotesPolicy.IsReadable(uri));
        Assert.Null(PatchNotesPolicy.For(uri));
        Assert.Equal(PatchNotesNavigation.Block,
            PatchNotesPolicy.For("https://example.com/")!.ClassifyNavigation(uri));
    }
}
