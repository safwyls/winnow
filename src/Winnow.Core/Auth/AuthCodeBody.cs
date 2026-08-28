using System.Text.Json;

namespace Winnow.Core.Auth;

/// <summary>
/// What a provider's code-bearing JSON body turned out to be.
///
/// <para><b>The middle case is the one that matters</b>, and it is the reason
/// this is an enum rather than a nullable string. A body that carries the code
/// fields with every one of them null is not a failure to find a code — it is
/// the provider saying <i>there is no authenticated session here</i>. Those two
/// have opposite remedies: one means "the page changed, fall back to the manual
/// flow", the other means "sign in first". Collapsing them into "no code
/// captured" describes the symptom and hides the cause, which is exactly how the
/// first real run of this flow ended up looking like a broken capture rather
/// than a missing login step.</para>
/// </summary>
public enum AuthCodeBodyOutcome
{
    /// <summary>
    /// Not a code-bearing body at all: not JSON, not an object, or an object
    /// carrying none of the expected fields. An HTML page, an error body, or any
    /// other response the flow passed through.
    /// </summary>
    NotACodeBody = 0,

    /// <summary>
    /// The code fields are there and every one of them is null or empty. The
    /// endpoint answered; the browser just has no session for it to answer
    /// <i>about</i>. Actionable: authenticate, then ask again.
    /// </summary>
    NoSession = 1,

    /// <summary>A code was found.</summary>
    CodeFound = 2,
}

/// <summary>One reading of a code-bearing body.</summary>
/// <param name="Outcome">What the body turned out to be.</param>
/// <param name="Kind">Which grant <paramref name="Code"/> feeds. Meaningless unless a code was found.</param>
/// <param name="Code">The code, or null. Never logged.</param>
public readonly record struct AuthCodeBodyReading(AuthCodeBodyOutcome Outcome, AuthCodeKind Kind, string? Code)
{
    /// <summary>
    /// Redacted, for the same reason <see cref="AuthCodeResult.ToString"/> is:
    /// the compiler-generated record <c>ToString</c> prints every member, and one
    /// of these members is a full-account credential.
    /// </summary>
    public override string ToString()
        => Outcome == AuthCodeBodyOutcome.CodeFound
            ? $"AuthCodeBodyReading(CodeFound {Kind}, value redacted)"
            : $"AuthCodeBodyReading({Outcome})";
}

/// <summary>
/// Reads a provider's code-bearing JSON body.
///
/// <para><b>Here rather than in the browser host</b> because it is pure parsing
/// over a shape described by <see cref="AuthJsonCodeField"/> values — no IO, no
/// browser, no provider name anywhere in it — and because the distinction it
/// draws is the one thing about this flow that can be tested exhaustively
/// without a real sign-in.</para>
/// </summary>
public static class AuthCodeBody
{
    /// <summary>
    /// Classifies <paramref name="json"/> against the fields a provider is known
    /// to carry codes in.
    ///
    /// <para><b>Presence, not population, is what makes it a code body.</b> A
    /// field that is present and null is the provider answering the question;
    /// that is what separates <see cref="AuthCodeBodyOutcome.NoSession"/> from
    /// <see cref="AuthCodeBodyOutcome.NotACodeBody"/>. Fields are searched in the
    /// order given, so a caller states its own priority.</para>
    /// </summary>
    /// <param name="json">The raw body. Null, blank and non-JSON are all ordinary inputs.</param>
    /// <param name="fields">Field names and the grant each one's value feeds.</param>
    public static AuthCodeBodyReading Read(string? json, IReadOnlyList<AuthJsonCodeField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (string.IsNullOrWhiteSpace(json) || fields.Count == 0)
        {
            return new AuthCodeBodyReading(AuthCodeBodyOutcome.NotACodeBody, default, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AuthCodeBodyReading(AuthCodeBodyOutcome.NotACodeBody, default, null);
            }

            var recognised = false;

            foreach (var field in fields)
            {
                if (!root.TryGetProperty(field.FieldName, out var value))
                {
                    continue;
                }

                // The field exists, so this IS the provider's code body whatever
                // the value turns out to be.
                recognised = true;

                if (value.ValueKind == JsonValueKind.String
                    && value.GetString() is { } code
                    && !string.IsNullOrWhiteSpace(code))
                {
                    return new AuthCodeBodyReading(AuthCodeBodyOutcome.CodeFound, field.Kind, code);
                }
            }

            return new AuthCodeBodyReading(
                recognised ? AuthCodeBodyOutcome.NoSession : AuthCodeBodyOutcome.NotACodeBody,
                default,
                null);
        }
        catch (JsonException)
        {
            return new AuthCodeBodyReading(AuthCodeBodyOutcome.NotACodeBody, default, null);
        }
    }
}
