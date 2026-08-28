namespace Winnow.Enrich.Igdb;

/// <summary>
/// Thrown only when something tries to send an authenticated IGDB request with
/// no credentials configured — a programming error, not a user state.
///
/// <para>The user-facing "no IGDB account" case is never an exception:
/// <see cref="IIgdbClient"/> returns empty results and
/// <see cref="IIgdbClient.IsConfiguredAsync"/> reports false.</para>
/// </summary>
public sealed class IgdbNotConfiguredException : InvalidOperationException
{
    public IgdbNotConfiguredException(string message)
        : base(message)
    {
    }
}
