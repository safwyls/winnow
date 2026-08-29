using Winnow.Ingest.Epic;
using Winnow.Ingest.Gog;

namespace Winnow.Tests;

/// <summary>
/// Epic and GOG sources that are guaranteed to find nothing, for the tests that
/// exercise the Steam half of the sync jobs and only need the other two
/// stores to stay quiet.
///
/// <para>Not the same thing as leaving them at their defaults. A default
/// <see cref="GogLibrarySource"/> reads the machine's real registry, so on a
/// developer box with GOG installed these tests would silently gain candidates
/// they never asked for — and the Steam assertions would start passing or failing
/// on which launchers the machine happens to have.</para>
/// </summary>
internal static class SilentStores
{
    /// <summary>A root that cannot exist, so every reader reports "I never looked".</summary>
    private static string MissingRoot => Path.Combine(
        Path.GetTempPath(), "winnow-tests-no-launcher-here");

    internal static EpicLibrarySource Epic() => new(
        installProbe: new FakeEpicThirdPartyInstallProbe(EpicInstallState.Unknown),
        dataRoot: MissingRoot);

    internal static GogLibrarySource Gog() => new(
        registry: FakeGogInstalledGameRegistry.Empty,
        galaxyRoot: MissingRoot);
}
