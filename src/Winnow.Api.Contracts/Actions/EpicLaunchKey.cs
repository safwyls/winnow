namespace Winnow.App.ViewModels;

/// <summary>
/// Epic's composite launch key: <c>namespace:catalogItemId:artifactId</c>.
/// All three parts are validated against a strict ASCII character class before
/// interpolation into a URI.
/// </summary>
public readonly record struct EpicLaunchKey
{
    private EpicLaunchKey(string ns, string catalogItemId, string artifactId)
    {
        Namespace = ns;
        CatalogItemId = catalogItemId;
        ArtifactId = artifactId;
    }

    public string Namespace { get; }

    public string CatalogItemId { get; }

    public string ArtifactId { get; }

    /// <summary>
    /// The key as the launcher's URL wants it: the three parts joined by a
    /// percent-encoded colon, so the whole composite is ONE path segment.
    /// A literal <c>:</c> here would be legal in a path but is not what Epic
    /// writes, and the shape below is copied from a URL Epic generated itself.
    /// </summary>
    public string PathSegment => $"{Namespace}%3A{CatalogItemId}%3A{ArtifactId}";

    /// <summary>Null unless all three parts are present and are ids we recognise.</summary>
    public static EpicLaunchKey? Create(string? ns, string? catalogItemId, string? artifactId)
        => IsIdLike(ns) && IsIdLike(catalogItemId) && IsIdLike(artifactId)
            ? new EpicLaunchKey(ns!, catalogItemId!, artifactId!)
            : null;

    /// <summary>
    /// ASCII letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, 1–64 characters.
    /// Every observed namespace (32-hex, <c>fn</c>, <c>catnip</c>) and every
    /// observed artifact id (<c>Bluebird</c>, <c>CatnipDLC3</c>, 32-hex) fits;
    /// nothing that fits needs escaping, which is why there is none.
    /// </summary>
    private static bool IsIdLike(string? value)
    {
        if (value is not { Length: > 0 and <= 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}
