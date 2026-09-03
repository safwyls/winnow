using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// <c>design-system.md</c> §10.3: every outbound target is built by
/// <see cref="GameLink.Create"/> and nothing else, and a target that fails
/// validation is a null link that renders no button.
///
/// <para><c>update_events.url</c> is captured from a network response, so a
/// link's target is untrusted input. The rule is an allowlist rather than a
/// blocklist for that reason, and this walks the shapes an allowlist is
/// supposed to stop.</para>
/// </summary>
public sealed class OutboundLinkTests
{
    [Theory]
    [InlineData("https://store.steampowered.com/app/440")]
    [InlineData("http://example.com/patch-notes")]
    [InlineData("steam://run/440")]
    [InlineData("steam://install/440")]
    [InlineData("com.epicgames.launcher://apps/ns%3Acatalog%3Aartifact?action=launch")]
    [InlineData("goggalaxy://launchGame/1207658930")]
    public void The_five_allowed_schemes_survive(string uri)
    {
        Assert.NotNull(GameLink.Create("Open", uri));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-msdt:/id")]
    [InlineData("ftp://example.com/x")]
    public void A_scheme_that_is_not_on_the_list_is_refused(string uri)
    {
        Assert.Null(GameLink.Create("Open", uri));
    }

    [Theory]
    [InlineData("/patch-notes")]
    [InlineData("../notes")]
    [InlineData("store.steampowered.com/app/440")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_target_that_is_not_an_absolute_uri_is_refused(string? uri)
    {
        Assert.Null(GameLink.Create("Open", uri));
    }

    [Theory]
    [InlineData("https://example.com/a\nSet-Cookie: x=1")]
    [InlineData("https://example.com/a\rb")]
    [InlineData("https://example.com/a\0b")]
    [InlineData("https://example.com/a\tb")]
    public void A_target_carrying_a_control_character_is_refused(string uri)
    {
        // A control character never appears in a legitimate URL and is how a
        // stored string smuggles a second line past a naive consumer.
        Assert.Null(GameLink.Create("Open", uri));
    }

    [Fact]
    public void A_link_with_no_label_is_refused()
    {
        // A button with no words on it is worse than no button, which is the
        // same reason a failed validation renders nothing at all.
        Assert.Null(GameLink.Create("", "https://example.com"));
        Assert.Null(GameLink.Create("   ", "https://example.com"));
    }

    [Fact]
    public void The_target_that_leaves_is_one_the_framework_parsed_and_re_emitted()
    {
        // The caller's string is never passed through: whatever the launcher
        // receives has been round-tripped through Uri, so a shape the parser
        // would not accept cannot reach it.
        var link = GameLink.Create("Store page", "https://store.steampowered.com/app/440/Team_Fortress_2/");

        Assert.NotNull(link);
        Assert.Equal(new Uri("https://store.steampowered.com/app/440/Team_Fortress_2/").ToString(), link.Uri);
    }

    [Fact]
    public void Epic_percent_escapes_survive_the_round_trip()
    {
        // Epic's composite key is namespace%3AcatalogItemId%3AartifactId, and a
        // round trip that decoded %3A back to ':' would split the path into
        // segments the launcher does not recognise.
        var link = GameLink.Create(
            "Play",
            "com.epicgames.launcher://apps/fn%3A1234%3AFortnite?action=launch");

        Assert.NotNull(link);
        Assert.Contains("%3A", link.Uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("440", true)]
    [InlineData("2686630", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("44a", false)]
    [InlineData("44/../x", false)]
    [InlineData("-440", false)]
    public void An_appid_is_digits_or_it_is_not_an_appid(string appId, bool expected)
    {
        // external_ids.provider_id is TEXT, and a URL is not a place to
        // interpolate an unchecked string.
        Assert.Equal(expected, GameLink.IsSteamAppId(appId));
    }
}
