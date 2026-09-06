namespace Winnow.Core.Queries;

/// <summary>
/// The source vocabulary for <c>work_images</c> (migration 0028). Enrichment
/// clients that populate that table must produce exactly these tokens, the same
/// arrangement <see cref="MaturitySources"/> has with <c>work_maturity</c>.
/// </summary>
public static class ImageSources
{
    public const string Igdb = "igdb";
}

/// <summary>
/// The kind vocabulary for <c>work_images</c> (migration 0028). Screenshots and
/// artworks are separate IGDB assets; a game can have one and not the other.
/// </summary>
public static class ImageKinds
{
    public const string Screenshot = "screenshot";

    public const string Artwork = "artwork";
}

/// <summary>
/// The source vocabulary for <c>work_ratings</c> (migration 0028). The three
/// figures are stored apart and are never blended: IGDB's user body, IGDB's
/// aggregation of external critics, and Steam's reviewers are three different
/// populations answering three different questions.
/// </summary>
public static class RatingSources
{
    public const string IgdbUsers = "igdb_users";

    public const string IgdbCritics = "igdb_critics";

    public const string Steam = "steam";
}

/// <summary>
/// Comma-joined IGDB image ids: the stored form in <c>work_images.image_ids</c>.
/// <see cref="Join"/> preserves IGDB's order, collapses duplicates ordinally
/// (IGDB image ids are lowercase alphanumeric, so two ids differing in case
/// would be two different assets), and returns null when nothing survives —
/// which is what makes "no screenshots" reach the database as no row.
/// <see cref="IsImageId"/> is ASCII alphanumeric, 1–64 characters, strict for
/// the reason <c>IgdbImageUrl.ImageId</c> is strict: an id that is not one
/// becomes a 404, and a 404 becomes a 30-day negative marker in the cover
/// cache.
/// </summary>
public static class ImageIdList
{
    public const int MaxIdLength = 64;

    public static string? Join(IEnumerable<string>? imageIds)
    {
        if (imageIds is null)
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var imageId in imageIds)
        {
            var trimmed = imageId?.Trim();
            if (IsImageId(trimmed) && !kept.Contains(trimmed!, StringComparer.Ordinal))
            {
                kept.Add(trimmed!);
            }
        }

        return kept.Count == 0 ? null : string.Join(',', kept);
    }

    public static IReadOnlyList<string> Split(string? imageIds)
    {
        if (string.IsNullOrWhiteSpace(imageIds))
        {
            return [];
        }

        var parts = imageIds.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 0 ? [] : parts;
    }

    public static bool IsImageId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxIdLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
