namespace Winnow.Core.Reading;

/// <summary>Whether a web address may be rendered by the isolated Winnow browser.</summary>
public enum PatchNotesNavigation
{
    Allow = 0,
    Block = 2,
}

/// <summary>
/// Allows HTTP and HTTPS browsing without granting any sign-in or host capability.
/// Entry, redirects, popups and frames use the same web-only boundary. Unlike sign-in
/// policies this reader has no trusted origins, capture strategies or host bridge.
/// </summary>
public sealed class PatchNotesPolicy
{
    private PatchNotesPolicy(Uri start) => Start = start;

    /// <summary>The address the panel navigates to on open.</summary>
    public Uri Start { get; }

    public static bool IsReadable(Uri? url)
        => url is { IsAbsoluteUri: true }
            && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(url.Host);

    public static bool IsReadable(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) && IsReadable(parsed);

    public static PatchNotesPolicy? For(Uri? url)
        => IsReadable(url) ? new PatchNotesPolicy(url!) : null;

    public static PatchNotesPolicy? For(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? For(parsed) : null;

    public PatchNotesNavigation ClassifyNavigation(Uri? uri)
        => IsReadable(uri) ? PatchNotesNavigation.Allow : PatchNotesNavigation.Block;

    public PatchNotesNavigation ClassifyNavigation(string? uri)
        => ClassifyNavigation(Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed : null);

    public PatchNotesNavigation ClassifyPopup(Uri? uri) => ClassifyNavigation(uri);
    public PatchNotesNavigation ClassifyFrame(Uri? uri) => ClassifyNavigation(uri);
    public PatchNotesNavigation ClassifyFrame(string? uri) => ClassifyNavigation(uri);
}
