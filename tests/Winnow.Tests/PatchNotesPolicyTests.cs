using Winnow.Core.Reading;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="PatchNotesPolicy"/>: the entry gate (which URLs may open in the
/// embedded panel) and the navigation gate (where the panel may go once open).
///
/// <para><c>update_events.url</c> is captured from a network response, so every
/// address is untrusted input. The gates are allowlists for that reason, and
/// these tests walk the shapes an allowlist is supposed to stop.</para>
/// </summary>
public sealed class PatchNotesPolicyTests
{
    private const string StoredNoteUrl =
        "https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1786573930663336";

    [Theory]
    [InlineData(StoredNoteUrl)]
    [InlineData("https://store.steampowered.com/news/app/440")]
    [InlineData("https://store.steampowered.com/news/app/440/view/123456")]
    [InlineData("https://steamcommunity.com/games/413150/announcements/detail/1786573930663336")]
    [InlineData("https://steamcommunity.com/app/440/announcements/")]
    [InlineData("HTTPS://STORE.STEAMPOWERED.COM/news/app/440")]
    public void A_storefront_news_address_opens_the_panel(string url)
    {
        Assert.True(PatchNotesPolicy.IsReadable(url));
        Assert.NotNull(PatchNotesPolicy.For(url));
    }

    [Theory]
    [InlineData("https://store.steampowered.com/app/440/Team_Fortress_2/")]
    [InlineData("https://store.steampowered.com/")]
    [InlineData("http://store.steampowered.com/news/app/440")]
    [InlineData("https://store.steampowered.com:8443/news/app/440")]
    [InlineData("https://news.example.com/news/app/440")]
    [InlineData("https://store.steampowered.com.example.com/news/app/440")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("steam://run/440")]
    [InlineData("/news/app/440")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_never_opens_a_panel(string? url)
    {
        Assert.False(PatchNotesPolicy.IsReadable(url));
        Assert.Null(PatchNotesPolicy.For(url));
    }

    [Theory]
    [InlineData("https://store.steampowered.com/news/app/440/view/1")]
    [InlineData("https://store.steampowered.com/app/440/")]
    [InlineData("https://steamcommunity.com/games/413150/announcements/detail/1")]
    [InlineData("https://www.steamcommunity.com/app/440")]
    [InlineData(StoredNoteUrl)]
    [InlineData("about:blank")]
    public void The_panel_may_go_to_the_news_origins_and_to_about_blank(string url)
    {
        Assert.Equal(PatchNotesNavigation.Allow, Policy().ClassifyNavigation(Address(url)));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=1")]
    [InlineData("https://example.com/")]
    [InlineData("http://store.steampowered.com/news/app/440")]
    [InlineData("https://store.steampowered.com:8443/news/app/440")]
    [InlineData("https://steamcommunity.evil.example/games/1/announcements")]
    public void An_address_off_the_allowlist_is_handed_to_the_users_own_browser(string url)
    {
        Assert.Equal(PatchNotesNavigation.OpenExternally, Policy().ClassifyNavigation(Address(url)));
        Assert.Equal(PatchNotesNavigation.OpenExternally, Policy().ClassifyPopup(Address(url)));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("steam://run/440")]
    [InlineData("ms-msdt:/id")]
    [InlineData("blob:https://store.steampowered.com/1234")]
    public void An_address_that_is_not_a_web_page_is_refused_outright(string url)
    {
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyNavigation(Address(url)));
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyPopup(Address(url)));
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyFrame(Address(url)));
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/1")]
    [InlineData("https://example.com/frame")]
    public void A_frame_from_anywhere_else_is_blocked_rather_than_opened_externally(string url)
    {
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyFrame(Address(url)));
    }

    [Fact]
    public void A_null_address_is_refused()
    {
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyNavigation((Uri?)null));
        Assert.Equal(PatchNotesNavigation.Block, Policy().ClassifyFrame((Uri?)null));
    }

    [Fact]
    public void The_navigable_origins_are_the_declared_news_origins()
    {
        var policy = Policy();

        Assert.Equal(
            PatchNotesPolicy.NewsOrigins
                .Select(o => Winnow.Core.Auth.AuthFlowPolicy.OriginOf(o))
                .Order(StringComparer.Ordinal),
            policy.NavigableOrigins.Order(StringComparer.Ordinal));
    }

    private static PatchNotesPolicy Policy()
        => PatchNotesPolicy.For(StoredNoteUrl)!;

    private static Uri? Address(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;
}
