using System.Text;
using System.Text.Json;

namespace Winnow.Core.Auth;

/// <summary>
/// What a Steam access token's payload says about itself, decoded locally.
///
/// <para><b>Nothing here is validated.</b> No signature check, no issuer check,
/// no audience check, no expiry check, and no network call of any kind. Steam is
/// the only party that decides whether a token is good, and it does so on every
/// request; a client-side signature check would need Valve's key and would buy
/// nothing, because a forged token fails at the API anyway.</para>
///
/// <para>What these claims are for is the three facts a client genuinely needs
/// before it sends anything: <b>when</b> the token stops working, so a renewal
/// can be scheduled and a dead credential is never chosen; <b>whose</b> account
/// it belongs to, so a sign-in can be refused when the page and the token
/// disagree about who just signed in; and the audience and issuer, which are the
/// only two facts that explain a 401 the API will not explain itself.</para>
///
/// <para>It lives in Core rather than beside the Steam Web client because two
/// assemblies need it and they cannot see each other: the sign-in session, which
/// decodes a token the moment a store page hands it over, and the Web API
/// module, which reads the same four claims off a stored session. One decoder,
/// so the two can never disagree about a token.</para>
/// </summary>
/// <param name="Readable">Whether a JSON object payload was decoded at all. False for anything that is not a JWT.</param>
/// <param name="ExpiresAt">The <c>exp</c> claim, as an absolute instant. Null when absent or not an integer.</param>
/// <param name="Subject">The <c>sub</c> claim, which on a Steam token is the SteamID64 of the signed-in account.</param>
/// <param name="Audiences">The <c>aud</c> claim. Steam issues an array; a bare string is accepted as a one-element list.</param>
/// <param name="Issuer">The <c>iss</c> claim.</param>
public readonly record struct SteamJwtClaims(
    bool Readable,
    DateTimeOffset? ExpiresAt,
    string? Subject,
    IReadOnlyList<string> Audiences,
    string? Issuer)
{
    /// <summary>What an unreadable or absent token yields. Never an exception.</summary>
    public static readonly SteamJwtClaims Unreadable = new(false, null, null, [], null);

    /// <summary>
    /// Reads a JWT's payload claims. Never validates, never throws. A malformed
    /// token yields <see cref="Unreadable"/>, which every caller treats as "no
    /// session" rather than as an error.
    /// </summary>
    public static SteamJwtClaims Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unreadable;
        }

        // Only the payload is touched. The header is not read and the signature
        // segment is never decoded, because nothing here verifies one.
        var segments = token.Split('.');
        if (segments.Length < 2 || DecodePayload(segments[1]) is not { } payload)
        {
            return Unreadable;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unreadable;
            }

            return new SteamJwtClaims(
                Readable: true,
                ExpiresAt: ReadExpiry(root),
                Subject: ReadString(root, "sub"),
                Audiences: ReadAudiences(root),
                Issuer: ReadString(root, "iss"));
        }
        catch (JsonException)
        {
            return Unreadable;
        }
    }

    private static DateTimeOffset? ReadExpiry(JsonElement root)
        => root.TryGetProperty("exp", out var exp)
            && exp.ValueKind == JsonValueKind.Number
            && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadAudiences(JsonElement root)
    {
        if (!root.TryGetProperty("aud", out var aud))
        {
            return [];
        }

        // Steam sends an array (["web:store"] on a store-minted token, verified
        // in docs/spikes/steam-web-session-auth.md), but the JWT spec permits a
        // bare string and a reader that only handled the array would silently
        // report no audience at all if Valve ever changed shape.
        if (aud.ValueKind == JsonValueKind.String)
        {
            return aud.GetString() is { } single ? [single] : [];
        }

        if (aud.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var element in aud.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>Base64url, which is base64 with two characters swapped and the padding dropped.</summary>
    private static string? DecodePayload(string segment)
    {
        if (segment.Length == 0)
        {
            return null;
        }

        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => null,
        };

        if (padded.Length % 4 != 0)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return null;
        }
    }
}
