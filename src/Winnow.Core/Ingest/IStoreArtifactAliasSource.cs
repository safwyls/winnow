namespace Winnow.Core.Ingest;

/// <summary>
/// The <i>other</i> id a store's local files know an artifact by, when the id
/// Winnow stores in <c>external_ids</c> is not the one an external service will
/// answer to.
///
/// <para><b>Why this exists at all, stated plainly, because it looks like
/// over-abstraction until you hit the case.</b> Epic writes two identifiers for
/// every title: <c>CatalogItemId</c> (a 32-hex product id) and <c>AppName</c> (a
/// per-artifact codename — "Bluebird" is Fez). Winnow's <c>external_ids</c> row
/// holds the catalog item id, correctly: <c>docs/spikes/epic-gog-local-files.md</c>
/// section 3 establishes it as the stable identity, and <c>AppName</c> is a
/// release artifact id that must never be rendered. But every service that can
/// resolve an Epic title to anything else keys on <c>AppName</c> —
/// <c>gamesdb.gog.com</c>'s cross-store graph takes <c>epic/Bluebird</c> and
/// nothing else, and IGDB's Epic rows (source 26) key on store <i>offer</i> ids
/// that appear nowhere on disk at all. So the id we store and the id we must
/// ask with are different strings, and something has to hold the map between
/// them.</para>
///
/// <para><b>Why the map is not simply a second <c>external_ids</c> row.</b> That
/// table's primary key is <c>(provider, provider_id)</c> — globally unique, one
/// release per id. A second <c>epic</c> row per release would work, but every
/// existing reader of <c>external_ids</c> ("the Epic id for this release")
/// would silently start returning two answers, with nothing in the row to say
/// which kind each is. The alias is also not identity: it is a lookup key for
/// one external service, cheap to re-read from a local file the launcher
/// rewrites on every login, and worth no migration.</para>
///
/// <para>Implementations read local store files and must be as forgiving as the
/// rest of ingest: a machine without that launcher, an unreadable directory or a
/// file the store has changed the shape of all return an empty map. <b>An empty
/// map means "this source cannot say", never "these titles have no alias"</b> —
/// callers must leave rows untouched rather than record the silence as an
/// answer.</para>
/// </summary>
public interface IStoreArtifactAliasSource
{
    /// <summary>
    /// Aliases for one provider: the id stored in <c>external_ids.provider_id</c>
    /// mapped to the id external services key on. Empty when this source knows
    /// nothing about <paramref name="provider"/>, which is the normal answer for
    /// every provider but its own.
    /// </summary>
    /// <param name="provider">An <see cref="Domain.ExternalIdProviders"/> value.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string provider, CancellationToken ct = default);
}
