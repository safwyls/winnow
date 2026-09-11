using Winnow.Core.Domain;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.Services;

/// <summary>Shared landscape policy; source dimensions measure detail surviving the display crop.</summary>
public static class BackdropSelection
{
    public static IReadOnlyList<CoverKey> Candidates(string? backgroundUrl, IReadOnlyList<WorkImages>? rows,
        double aspectRatio = 16d / 9, IReadOnlyList<string>? steamAppIds = null)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0) aspectRatio = 16d / 9;
        var result = new List<CoverKey>();
        if (UserArtRef.Token(backgroundUrl) is { } token) result.Add(CoverKey.User(token));
        else if (IgdbImageUrl.ImageId(backgroundUrl) is { } id) result.Add(CoverKey.IgdbBackdrop(id));
        var steamIds = (steamAppIds ?? []).Where(GameLink.IsSteamAppId).Distinct().ToArray();
        result.AddRange(steamIds.Select(CoverKey.SteamHero));

        var candidates = (rows ?? []).Where(row => row.Source == ImageSources.Igdb
                && row.Kind is ImageKinds.Artwork or ImageKinds.Screenshot)
            .SelectMany(row => row.Ids.Concat(row.Images.Select(image => image.ImageId)).Distinct()
                .Select(id => (Kind: row.Kind, Image: row.Images.FirstOrDefault(image => image.ImageId == id)
                    ?? new GameImage { ImageId = id })))
            .Where(item => ImageIdList.IsImageId(item.Image.ImageId)
                && !string.Equals(item.Image.ImageType, "logo", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Image.ImageType, "cover", StringComparison.OrdinalIgnoreCase)
                && item.Image.AlphaChannel != true && item.Image.Animated != true
                && item.Image.Width is not <= 0 && item.Image.Height is not <= 0
                && !(item.Image.Width is { } w && item.Image.Height is { } h && w <= h))
            .Select(item => (item.Kind, item.Image, Pixels: CroppedPixels(item.Image, aspectRatio)))
            // Known HD art wins, then known HD screenshots. Missing metadata stays a fallback
            // until enrichment supplies dimensions; it must not hide a verified good screenshot.
            .OrderBy(item => item.Pixels >= 1280d * 720 ? 0 : item.Pixels > 0 ? 2 : 1)
            .ThenBy(item => item.Kind == ImageKinds.Artwork ? 0 : 1)
            .ThenByDescending(item => item.Pixels);
        result.AddRange(candidates.Select(item => CoverKey.IgdbBackdrop(item.Image.ImageId)));
        result.AddRange(steamIds.Select(CoverKey.SteamHeroStandard));
        return result.Distinct().ToArray();
    }

    /// <summary>Decode enough source width to fill the crop vertically as well as horizontally.</summary>
    public static double DecodeWidth(CoverKey key, IReadOnlyList<WorkImages>? rows,
        double displayWidthPixels, double displayHeightPixels)
    {
        var image = key.Provider == CoverProviders.IgdbBackdrop
            ? (rows ?? []).Where(row => row.Source == ImageSources.Igdb)
                .SelectMany(row => row.Images).FirstOrDefault(image => image.ImageId == key.Id
                    && image.Width is > 0 && image.Height is > 0)
            : null;
        var ratio = IsSteamHero(key) ? SteamHeroRatio
            : image is { Width: { } width, Height: { } height } ? (double)width / height : 16d / 9;
        return Math.Max(displayWidthPixels, displayHeightPixels * ratio);
    }

    public const double SteamHeroRatio = 3840d / 1240;

    public static bool IsSteamHero(CoverKey key) =>
        key.Provider is CoverProviders.SteamHero or CoverProviders.SteamHeroStandard;

    private static double CroppedPixels(GameImage image, double ratio)
    {
        if (image.Width is not { } width || image.Height is not { } height) return 0;
        var cropWidth = Math.Min(width, height * ratio);
        return cropWidth * cropWidth / ratio;
    }
}
