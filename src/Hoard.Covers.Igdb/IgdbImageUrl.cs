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
public static class IgdbImageUrl
{
    private const string UploadMarker = "/upload/";

    /// <summary>
    /// The <c>image_id</c> out of a stored IGDB cover URL — the <c>co1r76</c> in
    /// <c>https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg</c> —
    /// or <see langword="null"/> for anything that is not one.
    ///
    /// <para>This is how a work with no Steam appid gets a cover key at all: the
    /// enrichment pass already stores the URL in <c>works.cover_url</c>, and the
    /// id inside it names the asset without needing credentials, an API call, or
    /// the <c>igdb_id</c> that a duplicate cross-store pair can only give to one
    /// of its two rows.</para>
    ///
    /// <para>Strict on purpose. A URL shape we do not recognise returns null
    /// rather than a guess, because a guessed id becomes a 404 and a 404 becomes
    /// a 30-day negative marker — the user would see a month of placeholder art
    /// for a game whose cover we were actually holding.</para>
    /// </summary>
    public static string? ImageId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var marker = url.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var afterUpload = url.AsSpan(marker + UploadMarker.Length);

        // Everything after the size token, minus the extension. IGDB has only
        // ever served `<size>/<image_id>.<ext>`, but a missing size segment is
        // read as "the id is the whole remainder" rather than treated as an
        // error — the id is what matters and the token is decoration.
        var slash = afterUpload.IndexOf('/');
        var tail = slash < 0 ? afterUpload : afterUpload[(slash + 1)..];

        var dot = tail.LastIndexOf('.');
        var id = dot < 0 ? tail : tail[..dot];

        // IGDB image ids are lowercase alphanumeric. Anything else — a query
        // string, a nested path, a CDN we do not know — is not one.
        if (id.Length == 0 || id.Length > 64)
        {
            return null;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return null;
            }
        }

        return id.ToString();
    }

    /// <summary>The URL for one <c>image_id</c> at one size token.</summary>
    public static string ForImageId(string imageId, string sizeToken)
        => $"https://images.igdb.com/igdb/image/upload/{sizeToken}/{imageId}.jpg";

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
