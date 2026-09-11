using Xunit;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;

namespace Winnow.Tests;

public sealed class BackdropSelectionTests
{
    [Fact]
    public void Landscape_selection_uses_crop_resolution_and_excludes_unsuitable_assets()
    {
        var rows = new[] { Row(ImageKinds.Artwork,
            new GameImage { ImageId = "wide", Width = 6000, Height = 1000 },
            new GameImage { ImageId = "fit", Width = 2560, Height = 1440 },
            new GameImage { ImageId = "portrait", Width = 2000, Height = 3000 },
            new GameImage { ImageId = "alpha", Width = 4000, Height = 2000, AlphaChannel = true },
            new GameImage { ImageId = "animated", Width = 4000, Height = 2000, Animated = true }) };
        Assert.Equal(["fit", "wide"], BackdropSelection.Candidates(null, rows).Select(key => key.Id));
        Assert.Equal("wide", BackdropSelection.Candidates(null, rows, 6).First().Id);
    }

    [Fact]
    public void Hd_screenshot_precedes_unknown_or_small_art_and_legacy_order_is_stable()
    {
        var rows = new[] { Row(ImageKinds.Artwork, new GameImage { ImageId = "unknown" },
                new GameImage { ImageId = "small", Width = 640, Height = 360 }),
            Row(ImageKinds.Screenshot, new GameImage { ImageId = "hd", Width = 1920, Height = 1080 }) };
        Assert.Equal(["hd", "unknown", "small"], BackdropSelection.Candidates(null, rows).Select(key => key.Id));
        var legacy = Row(ImageKinds.Screenshot) with { ImageIds = "first,second" };
        Assert.Equal(["first", "second"], BackdropSelection.Candidates(null, [legacy]).Select(key => key.Id));
    }

    [Fact]
    public void Saved_background_leads_and_duplicates_are_removed()
    {
        var rows = new[] { Row(ImageKinds.Artwork, new GameImage { ImageId = "chosen", Width = 3840, Height = 2160 }) };
        Assert.Equal([CoverKey.IgdbBackdrop("chosen")], BackdropSelection.Candidates(
            "https://images.igdb.com/igdb/image/upload/t_original/chosen.jpg", rows));
    }

    [Fact]
    public void Known_Steam_heroes_bracket_IGDB_after_the_saved_background()
    {
        var rows = new[] { Row(ImageKinds.Artwork, new GameImage { ImageId = "art", Width = 3840, Height = 2160 }) };
        Assert.Equal([CoverKey.User("chosen"), CoverKey.SteamHero("42"), CoverKey.SteamHero("43"),
            CoverKey.IgdbBackdrop("art"), CoverKey.SteamHeroStandard("42"), CoverKey.SteamHeroStandard("43")],
            BackdropSelection.Candidates(UserArtRef.Format("chosen"), rows, steamAppIds: ["42", "42", "invalid", "43"]));
        Assert.Equal([CoverKey.SteamHero("42"), CoverKey.SteamHeroStandard("42")],
            BackdropSelection.Candidates(null, null, steamAppIds: ["42"]));
        Assert.Equal(3840, CoverImaging.SnapWidth(BackdropSelection.DecodeWidth(CoverKey.SteamHero("42"), null, 1920, 1080)));
    }

    [Fact]
    public void Grouped_non_Steam_playable_copy_keeps_known_Steam_hero_ids()
    {
        var tile = TileFixture.Tile(DateTime.UtcNow,
            [TileEntry.For(1, 1, 1, "gog", 0, null, ownership: new Ownership { ReleaseId = 1, Store = "gog", Installed = true }),
             TileEntry.For(2, 2, 1, "steam", 0, null, steamAppId: "42"),
             TileEntry.For(3, 2, 1, "steam", 0, null, steamAppId: "42")],
            1, LibraryBuckets.NeverPlayed);
        Assert.Equal("gog", tile.PlayableEntry.Store);
        Assert.Null(tile.SteamAppId);
        Assert.Equal(["42"], tile.SteamBackdropAppIds);
    }

    private static WorkImages Row(string kind, params GameImage[] images) => new()
    {
        WorkId = 1, Source = ImageSources.Igdb, Kind = kind, ImageIds = string.Join(',', images.Select(image => image.ImageId)),
        Images = images, ObservedAt = DateTime.UtcNow
    };
}
