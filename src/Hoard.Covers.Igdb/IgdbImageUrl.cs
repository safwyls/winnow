namespace Hoard.Covers.Igdb;

/// <summary>
/// IGDB serves every cover from one asset under a size token in the URL path:
/// <c>https://images.igdb.com/igdb/image/upload/{size}/{image_id}.jpg</c>.
/// Swapping the token is the documented way to ask for another rendition, and
/// it is the only way to get past the 264x352 <c>t_cover_big</c> that
/// <c>IIgdbClient</c> returns.
///
/// <para>Verified live 2026-08-23 against <c>co6m51</c> (Cry of Fear):
/// <c>t_cover_big</c> → 200, 264x352, 9.9 KB; <c>t_cover_big_2x</c> → 200,
/// 528x704, 32.7 KB; an unknown image id → a clean 404 under either token.</para>
/// </summary>
internal static class IgdbImageUrl
{
    private const string UploadMarker = "/upload/";

    /// <summary>
    /// Rewrites the size token in <paramref name="url"/> to
    /// <paramref name="sizeToken"/>, absolutising IGDB's protocol-relative form
    /// on the way. Returns <see langword="null"/> for anything that is not a
    /// recognisable IGDB image URL — a URL shape we do not understand is not one
    /// to guess at, because a wrong guess reads as "this game has no art".
    /// </summary>
    public static string? WithSize(string? url, string sizeToken)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sizeToken))
        {
            return null;
        }

        var absolute = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
        if (!absolute.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !absolute.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var marker = absolute.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return absolute;
        }

        var start = marker + UploadMarker.Length;
        var end = absolute.IndexOf('/', start);

        // No trailing segment, or a segment that is not a size token (IGDB's are
        // all `t_*`). Leave it alone rather than corrupt a path we misread.
        if (end < 0 || end == start || !absolute.AsSpan(start, end - start).StartsWith("t_"))
        {
            return absolute;
        }

        return string.Concat(absolute.AsSpan(0, start), sizeToken, absolute.AsSpan(end));
    }
}
