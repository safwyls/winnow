namespace Winnow.Core.Ingest;

/// <summary>What happened when one file was loaded. Per-file so one bad path does not fail the rest.</summary>
public enum SteamAccountPageFileOutcome
{
    /// <summary>The file was read and identified as one of the two account pages.</summary>
    Loaded = 0,

    /// <summary>No file at this path.</summary>
    NotFound = 1,

    /// <summary>The file exists but could not be read (IO error, permissions, or over the size ceiling).</summary>
    Unreadable = 2,

    /// <summary>The file was read but its content is not a recognisable account page.</summary>
    NotRecognized = 3,

    /// <summary>A file of this page kind was already loaded from an earlier path.</summary>
    Duplicate = 4,
}

/// <summary>The outcome of loading one user-picked file.</summary>
/// <param name="Path">The path the user supplied.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Kind">Which account page this file turned out to be, when it was successfully identified.</param>
/// <param name="Detail">A human-readable reason when the outcome is not <see cref="SteamAccountPageFileOutcome.Loaded"/>.</param>
public sealed record SteamAccountPageFile(
    string Path,
    SteamAccountPageFileOutcome Outcome,
    SteamAccountPageKind? Kind,
    string? Detail);

/// <summary>The result of loading one or more user-picked files into a <see cref="SteamAccountPages"/> set.</summary>
/// <param name="Pages">The assembled pages, with <see cref="SteamAccountPageSource.SavedFile"/> as the source.</param>
/// <param name="Files">Per-file outcomes, one entry per path the caller supplied.</param>
public sealed record SteamAccountPageLoadResult(
    SteamAccountPages Pages,
    IReadOnlyList<SteamAccountPageFile> Files)
{
    /// <summary>Whether at least one file was successfully loaded and identified.</summary>
    public bool AnythingLoaded => !Pages.IsEmpty;
}

/// <summary>
/// The saved-file route: turns user-picked paths into a <see cref="SteamAccountPages"/>.
/// Contract only (no IO); the implementation lives in Winnow.Ingest.Steam.
///
/// <para>This interface exists so that the import screen's view model can offer
/// the saved-file route without naming an ingest type. It is the same seam
/// <c>EpicSignInService</c> draws for the Epic sign-in and
/// <c>IStoreConnections</c> draws for the Stores panel: a view model that
/// constructs a reader has put ingest in its constructor and deleted the §5.1
/// boundary, and the boundary is what keeps the UI reading the database and
/// raising commands rather than doing the work itself.</para>
///
/// <para>The result types live here beside <see cref="SteamAccountPages"/> for
/// the same reason it does: they are the shape of an answer, they carry no IO,
/// and both sides of the seam have to name them.</para>
/// </summary>
public interface ISteamAccountPageFileLoader
{
    /// <summary>
    /// Reads each path, identifies which account page it is from its content,
    /// and assembles the set. Per-file outcomes, so one bad path does not fail
    /// the rest, and the first successfully identified file of each kind wins.
    /// </summary>
    Task<SteamAccountPageLoadResult> LoadAsync(
        IEnumerable<string> paths, CancellationToken ct = default);
}
