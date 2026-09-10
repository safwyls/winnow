using Xunit;
using Winnow.App.Services;
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

    private static WorkImages Row(string kind, params GameImage[] images) => new()
    {
        WorkId = 1, Source = ImageSources.Igdb, Kind = kind, ImageIds = string.Join(',', images.Select(image => image.ImageId)),
        Images = images, ObservedAt = DateTime.UtcNow
    };
}
