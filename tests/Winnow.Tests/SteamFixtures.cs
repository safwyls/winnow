namespace Winnow.Tests;

/// <summary>
/// Locates the sanitized Steam fixture files copied to the test output
/// directory (see tests/fixtures/steam/README.md for what each preserves).
/// </summary>
internal static class SteamFixtures
{
    internal static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "steam", fileName);

    internal static DateTime Epoch(long seconds)
        => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
