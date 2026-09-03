using Winnow.Core.Auth;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// What a Steam access token's payload says about itself, plus the one thing
/// this module can say that Core cannot: which account the subject names.
///
/// <para><b>Nothing here is validated.</b> No signature check, no issuer check,
/// no audience check, no expiry check. Steam is the only party that decides
/// whether a token is good, and it does so on every request; a client-side
/// signature check would need Valve's key and would buy nothing, because a
/// forged token fails at the API anyway. What these claims are for is the two
/// facts the client genuinely needs before it sends anything: <b>when</b> the
/// token stops working, so a renewal can be scheduled and a dead credential is
/// never chosen, and <b>whose</b> account it belongs to, so the signed-in
/// account can be recorded without a second disclosure call.</para>
///
/// <para><b>The decoder itself is <see cref="SteamJwtClaims"/>, in Core, and
/// this type is a thin projection of it.</b> S3's sign-in session lives in
/// Winnow.Auth.WebView, which cannot see this assembly and must decode the token
/// the instant a store page hands it over — so the base64url decoder and the
/// claim vocabulary moved down to where both callers can reach them. Everything
/// this type adds is <see cref="SteamId"/>, which needs a Steam-specific parser
/// Core has no business owning.</para>
/// </summary>
/// <param name="Readable">Whether a JSON object payload was decoded at all. False for anything that is not a JWT.</param>
/// <param name="ExpiresAt">The <c>exp</c> claim, as an absolute instant. Null when absent or not an integer.</param>
/// <param name="Subject">The <c>sub</c> claim, which on a Steam token is the SteamID64 of the signed-in account.</param>
/// <param name="Audiences">The <c>aud</c> claim. Steam issues an array; a bare string is accepted as a one-element list.</param>
/// <param name="Issuer">The <c>iss</c> claim.</param>
public readonly record struct SteamTokenClaims(
    bool Readable,
    DateTimeOffset? ExpiresAt,
    string? Subject,
    IReadOnlyList<string> Audiences,
    string? Issuer)
{
    /// <summary>What an unreadable or absent token yields. Never an exception.</summary>
    public static readonly SteamTokenClaims Unreadable = new(false, null, null, [], null);

    /// <summary>
    /// The account the token was minted for, parsed from <see cref="Subject"/>,
    /// or null when the subject is missing or is not an individual SteamID64.
    /// </summary>
    public SteamId? SteamId => SteamWeb.SteamId.TryParse(Subject, out var id) ? id : null;

    /// <summary>
    /// Reads a JWT's payload claims. Never validates, never throws. A malformed
    /// token yields <see cref="Unreadable"/>, which every caller treats as "no
    /// session" rather than as an error.
    /// </summary>
    public static SteamTokenClaims Read(string? token) => From(SteamJwtClaims.Read(token));

    /// <summary>Projects Core's claim set onto this one. The only place the two shapes meet.</summary>
    public static SteamTokenClaims From(SteamJwtClaims claims) => new(
        claims.Readable, claims.ExpiresAt, claims.Subject, claims.Audiences, claims.Issuer);
}
