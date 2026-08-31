using System.Text;
using System.Text.Json;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// What a Steam access token's payload says about itself.
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
/// <para>Promoted from the TASK-56 probe, which read the same four claims to
/// print them. The probe's reader is now a thin call onto this one, so there is
/// exactly one base64url decoder and one claim vocabulary in the codebase.</para>
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
    public static SteamTokenClaims Read(string? token)
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

            return new SteamTokenClaims(
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
