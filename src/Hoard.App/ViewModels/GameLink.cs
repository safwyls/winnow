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
/// <para>Five schemes are allowed and no others:</para>
/// <list type="bullet">
///   <item><c>https</c> and <c>http</c> — store pages, patch notes, anything the
///     data supplied.</item>
///   <item><c>steam</c> — Valve's own protocol handler, which is what makes
///     <c>Play</c> a real launch rather than a button that opens a web page
///     about launching. <c>steam://run/&lt;appid&gt;</c> starts the game;
///     <c>steam://install/&lt;appid&gt;</c> starts the download.</item>
///   <item><c>com.epicgames.launcher</c> — the Epic Games Launcher's handler.</item>
///   <item><c>goggalaxy</c> — GOG Galaxy's handler.</item>
/// </list>
///
/// <para><b>The two launcher schemes were added by measurement, not by
/// reading.</b> Both are registered on the author's machine with a
/// <c>URL Protocol</c> value under <c>HKCR</c>, and the exact path grammar each
/// one accepts is recorded in <see cref="StoreActions"/> beside the evidence for
/// it. §10 of the design doc is explicit that a widely-circulated answer is not
/// an answer here; a URI this class admits is one somebody watched work.</para>
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
    private GameLink(string label, string uri, string? hint, GameLinkKind kind)
    {
        Label = label;
        Uri = uri;
        Hint = hint;
        Kind = kind;
    }

    /// <summary>Steam's browser protocol.</summary>
    public const string SteamScheme = "steam";

    /// <summary>
    /// The Epic Games Launcher's protocol. Dots are legal in a URI scheme
    /// (RFC 3986 <c>scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>) and
    /// <see cref="System.Uri"/> parses this one without complaint — measured,
    /// because a scheme with two dots in it is exactly the shape a parser is
    /// most likely to quietly reject.
    /// </summary>
    public const string EpicScheme = "com.epicgames.launcher";

    /// <summary>GOG Galaxy's protocol.</summary>
    public const string GogScheme = "goggalaxy";

    /// <summary>What the button says. §7: name the action, not the mechanism.</summary>
    public string Label { get; }

    /// <summary>The validated absolute URI, as a string ready for the launcher.</summary>
    public string Uri { get; }

    /// <summary>Tooltip. Null falls back to showing the URI itself.</summary>
    public string? Hint { get; }

    /// <summary>
    /// What pressing this is expected to DO, as against where it goes.
    ///
    /// <para>M3b needs the distinction and the label cannot carry it: a launch
    /// that is supposed to produce a running game is the only kind worth
    /// declaring an attribution intent for, and the only kind worth waiting on.
    /// <c>steam://install/</c> starts a download that may take an hour and
    /// produces no process to attribute; matching on the string "Play" would
    /// have made the copy load-bearing, which is how a rename becomes a
    /// bug.</para>
    /// </summary>
    public GameLinkKind Kind { get; }

    /// <summary>True when pressing this should end with a game running.</summary>
    public bool StartsGame => Kind == GameLinkKind.Play;

    /// <summary>Tooltip text: the caller's hint, or the destination itself.</summary>
    public string Tooltip => Hint ?? Uri;

    /// <summary>True when this opens Steam rather than a browser.</summary>
    public bool IsSteamProtocol => Uri.StartsWith(SteamScheme + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when this hands the target to a store's own launcher rather than to
    /// a browser — Steam, the Epic Games Launcher or GOG Galaxy.
    /// </summary>
    public bool IsLauncherProtocol
        => IsSteamProtocol
        || Uri.StartsWith(EpicScheme + "://", StringComparison.OrdinalIgnoreCase)
        || Uri.StartsWith(GogScheme + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The only constructor. Returns null — never a placeholder — when the
    /// target is missing, relative, or on a scheme we do not open.
    /// </summary>
    public static GameLink? Create(
        string label, string? uri, string? hint = null, GameLinkKind kind = GameLinkKind.Link)
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
            || parsed.Scheme == SteamScheme
            || parsed.Scheme == EpicScheme
            || parsed.Scheme == GogScheme;

        if (!allowed)
        {
            return null;
        }

        // Round-trip through Uri rather than passing the caller's string
        // through: whatever the launcher gets is then something the framework
        // itself parsed and re-emitted.
        //
        // Two things about that round trip are load-bearing for the launcher
        // schemes, and both were measured rather than assumed:
        //
        //   * Uri.ToString() LOWERCASES the authority. GOG puts its command name
        //     there (goggalaxy://launchGame/...), so what leaves here is always
        //     goggalaxy://launchgame/... — and Galaxy's dispatcher is
        //     case-insensitive, confirmed by firing the lowercased form of a
        //     command at a running client and reading its own log say
        //     "Handling protocol command". See StoreActions.
        //   * Percent escapes in the PATH survive it. Epic's composite key is
        //     namespace%3AcatalogItemId%3AartifactId, and a round trip that
        //     decoded %3A back to ':' would split the path into segments the
        //     launcher does not recognise. It does not: the escapes come out
        //     byte-identical.
        return new GameLink(label, parsed.ToString(), hint, kind);
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

/// <summary>
/// What a <see cref="GameLink"/> is FOR. Three answers, and the middle one is
/// the reason the enum exists rather than a boolean.
/// </summary>
public enum GameLinkKind
{
    /// <summary>A store page, a patch-notes hub, a launcher view. Opens something to read.</summary>
    Link = 0,

    /// <summary>
    /// Starts the game. The only kind that declares a launch intent, and the
    /// only kind the ambient indicator waits on.
    /// </summary>
    Play,

    /// <summary>
    /// Starts a download in the store's own client. Fires and is forgotten:
    /// there is no process to attribute, the wait is measured in hours rather
    /// than seconds, and pretending otherwise would leave an indicator spinning
    /// on the screen for the rest of the evening.
    /// </summary>
    Install,
}
