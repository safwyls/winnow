namespace Hoard.App.ViewModels;

/// <summary>
/// One outbound affordance on the detail view, and the only way the app is
/// allowed to build one.
///
/// <para><b>A link exists only if its URI passed <see cref="Create"/>.</b>
/// Nothing in the view constructs a URI, concatenates a URL into a binding, or
/// hands raw text to the shell — the view binds a label and a validated string,
/// and a target that failed validation is a null this object, which renders as
/// no button at all rather than as a dead or dangerous one. That is the whole
/// reason this is a factory over a private constructor.</para>
///
/// <para>Three schemes are allowed and no others:</para>
/// <list type="bullet">
///   <item><c>https</c> and <c>http</c> — store pages, patch notes, anything the
///     data supplied.</item>
///   <item><c>steam</c> — Valve's own protocol handler, which is what makes
///     <c>Play</c> a real launch rather than a button that opens a web page
///     about launching. <c>steam://run/&lt;appid&gt;</c> starts the game;
///     <c>steam://install/&lt;appid&gt;</c> starts the download.</item>
/// </list>
///
/// <para>Everything else is refused, including the ones that look harmless:
/// <c>file:</c> (the shell would open arbitrary local paths from stored data),
/// <c>javascript:</c> and <c>data:</c> (script and payload delivery), and any
/// relative or malformed string. update_events.url is captured from a network
/// response (§4.5), so it is untrusted input, and "we only ever write Steam
/// URLs there" is a property of today's poller rather than of this view.</para>
/// </summary>
public sealed record GameLink
{
    private GameLink(string label, string uri, string? hint)
    {
        Label = label;
        Uri = uri;
        Hint = hint;
    }

    /// <summary>Steam's browser protocol. Documented, and the one non-web scheme we open.</summary>
    public const string SteamScheme = "steam";

    /// <summary>What the button says. §7: name the action, not the mechanism.</summary>
    public string Label { get; }

    /// <summary>The validated absolute URI, as a string ready for the launcher.</summary>
    public string Uri { get; }

    /// <summary>Tooltip. Null falls back to showing the URI itself.</summary>
    public string? Hint { get; }

    /// <summary>Tooltip text: the caller's hint, or the destination itself.</summary>
    public string Tooltip => Hint ?? Uri;

    /// <summary>True when this opens Steam rather than a browser.</summary>
    public bool IsSteamProtocol => Uri.StartsWith(SteamScheme + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The only constructor. Returns null — never a placeholder — when the
    /// target is missing, relative, or on a scheme we do not open.
    /// </summary>
    public static GameLink? Create(string label, string? uri, string? hint = null)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        // Control characters never appear in a legitimate URL and are how a
        // stored string smuggles a second line past a naive consumer.
        foreach (var c in uri)
        {
            if (char.IsControl(c))
            {
                return null;
            }
        }

        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var allowed = parsed.Scheme == System.Uri.UriSchemeHttps
            || parsed.Scheme == System.Uri.UriSchemeHttp
            || parsed.Scheme == SteamScheme;

        if (!allowed)
        {
            return null;
        }

        // Round-trip through Uri rather than passing the caller's string
        // through: whatever the launcher gets is then something the framework
        // itself parsed and re-emitted.
        return new GameLink(label, parsed.ToString(), hint);
    }

    /// <summary>
    /// The Steam affordances for an appid, or nothing when we do not hold one.
    ///
    /// <para>The appid is validated as digits here rather than trusted from the
    /// database: external_ids.provider_id is TEXT, and a URL is not a place to
    /// interpolate an unchecked string.</para>
    /// </summary>
    public static bool IsSteamAppId(string? appId)
        => appId is { Length: > 0 and <= 10 } && appId.All(char.IsAsciiDigit);
}
