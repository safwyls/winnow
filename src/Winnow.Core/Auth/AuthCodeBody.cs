using System.Text.Json;

namespace Winnow.Core.Auth;

/// <summary>
/// What a provider's code-bearing JSON body turned out to be. Three-valued so
/// "no code fields at all" and "code fields present but null (no session)" have
/// distinct remedies.
/// </summary>
public enum AuthCodeBodyOutcome
{
    /// <summary>Not a code-bearing body at all (not JSON, no expected fields, etc.).</summary>
    NotACodeBody = 0,

    /// <summary>Code fields present but all null/empty: no authenticated session. Remedy: sign in first.</summary>
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
    /// <summary>Redacted to keep the code out of logs.</summary>
    public override string ToString()
        => Outcome == AuthCodeBodyOutcome.CodeFound
            ? $"AuthCodeBodyReading(CodeFound {Kind}, value redacted)"
            : $"AuthCodeBodyReading({Outcome})";
}

/// <summary>Pure-parsing reader for a provider's code-bearing JSON body.</summary>
public static class AuthCodeBody
{
    /// <summary>
    /// Classifies <paramref name="json"/> against the expected code fields.
    /// Field presence (even if null) distinguishes NoSession from NotACodeBody.
    /// Fields are searched in order; first non-empty value wins.
    /// </summary>
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
