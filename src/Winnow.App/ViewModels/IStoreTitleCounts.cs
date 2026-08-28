namespace Winnow.App.ViewModels;

/// <summary>
/// How many titles each store contributes to the library, across the WHOLE
/// library rather than the current cut.
///
/// <para><b>Why an interface for one dictionary.</b> The Stores panel's claim is
/// "these are the sources your library comes from", and a claim like that is
/// worth nothing without the number beside it. The number lives in
/// <see cref="LibraryViewModel"/>, which already holds every tile — but the
/// panel taking a whole <c>LibraryViewModel</c> would drag five repositories
/// into its constructor and make its tests need a migrated database to assert
/// on a sign-in button. One method is the entire dependency, so it is the
/// entire seam.</para>
///
/// <para><b>Deliberately per tile, not per release</b>, which is the same rule
/// §11.2 states for the filter panel's counts: a game owned on two stores is two
/// rows in the library and must be two here. Collapsing them would make the
/// three numbers stop summing to the library total, on the one screen whose
/// whole job is to account for where the total came from.</para>
/// </summary>
public interface IStoreTitleCounts
{
    /// <summary>
    /// Titles per store, keyed by the store as stored (<c>"steam"</c>,
    /// <c>"epic"</c>, <c>"gog"</c>). Empty before the library has loaded — which
    /// the caller must treat as "not known yet" and not as zero.
    /// </summary>
    IReadOnlyDictionary<string, int> TitlesByStore();
}
