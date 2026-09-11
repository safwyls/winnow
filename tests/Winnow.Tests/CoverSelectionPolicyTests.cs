using Winnow.App.Services;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

public sealed class CoverSelectionPolicyTests
{
    private const string Igdb = "https://images.igdb.com/igdb/image/upload/t_cover_big/co42.jpg";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void User_art_survives_pin_and_provider_availability(bool pin, bool providersAvailable)
    {
        var policy = new CoverSelection(providersAvailable ? ["steam", "igdb"] : []);
        Assert.Equal(CoverKey.User("mine"), policy.Select(UserArtRef.Format("mine"), "42", pin));
    }

    [Theory]
    [InlineData(true, true, "igdb")]
    [InlineData(false, true, "steam")]
    [InlineData(true, false, "steam")]
    public void Pin_uses_the_same_work_image_and_falls_back_when_its_source_is_unavailable(bool pin, bool igdbAvailable, string expected)
    {
        var policy = new CoverSelection(igdbAvailable ? ["steam", "igdb"] : ["steam"]);
        Assert.Equal(expected == "igdb" ? CoverKey.Igdb("co42") : CoverKey.Steam("42"), policy.Select(Igdb, "42", pin));
        Assert.Equal(CoverKey.Steam("42"), policy.Select(null, "42", pin));
    }

    [Fact]
    public void Work_previews_use_typed_plugin_refs_only_while_the_provider_is_available()
    {
        var plugin = PluginArtRef.Key("fixture", "https://images.example.test/cover.jpg")!.Value;
        var reference = PluginArtRef.Reference(plugin);
        Assert.Equal(plugin, new CoverSelection(["plugin:fixture"]).Select(reference));
        Assert.Null(new CoverSelection().Select(reference));
        Assert.Null(new CoverSelection(["plugin:fixture"]).Select("https://images.example.test/cover.jpg"));
        Assert.Equal(CoverKey.Igdb("co42"), new CoverSelection().Select(Igdb));
    }
}
