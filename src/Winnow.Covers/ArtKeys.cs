namespace Winnow.Covers.Igdb;

/// <summary>
/// The one place a stored art URL becomes a <see cref="CoverKey"/>. A
/// <c>winnow://user-art/</c> reference becomes <see cref="CoverKey.User"/>;
/// an IGDB image URL becomes <see cref="CoverKey.Igdb"/>; anything else is
/// null rather than a guess. A <c>background_url</c> that happens to be an
/// IGDB image URL is resolved by this same call, so covers and backgrounds
/// share one image path.
/// </summary>
public static class ArtKeys
{
    public static CoverKey? Resolve(string? url)
    {
        if (UserArtRef.Token(url) is { Length: > 0 } token)
        {
            return CoverKey.User(token);
        }

        if (IgdbImageUrl.ImageId(url) is { Length: > 0 } imageId)
        {
            return CoverKey.Igdb(imageId);
        }

        return null;
    }
}
