using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// <see cref="IOwnershipAccountRepository"/> over the migration 0015
/// <c>ownership_accounts</c> table.
///
/// <para>Leases rather than opening its own connection, so a membership row
/// written inside the resolver's unit of work commits or rolls back with the
/// ownership it describes. A pass that fails after writing memberships but
/// before writing the ownership would otherwise leave rows pointing at an id
/// that never existed.</para>
/// </summary>
public sealed class OwnershipAccountRepository : IOwnershipAccountRepository
{
    private const string Columns = """
        ownership_id     AS OwnershipId,
        account_ref      AS AccountRef,
        playtime_minutes AS PlaytimeMinutes,
        last_played_at   AS LastPlayedAt,
        source           AS Source,
        first_seen_at    AS FirstSeenAt,
        last_seen_at     AS LastSeenAt
        """;

    private readonly ISqliteConnectionFactory _factory;

    public OwnershipAccountRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <inheritdoc/>
    public async Task UpsertAsync(OwnershipAccountUpsert row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.AccountRef))
        {
            // A row naming no account is evidence about nobody, and storing one
            // would put a key in the table that no filter can match while still
            // counting as "this ownership has per-account evidence" — which
            // would make the filter hide a game on the strength of a blank.
            return;
        }

        // ── The figure rules, and why they are not a max() ──────────────────
        //
        // A null incoming figure is "this reader could not tell", never a
        // correction: GetOwnedGames returns no playtime at all for an account
        // whose key it is not holding, and localconfig.vdf names accounts it has
        // no minutes for. Writing that null over a stored figure would erase a
        // measurement in favour of a shrug. So null never overwrites, and a
        // stored null always yields to a real figure.
        //
        // Where both sides have a figure, three bands, and the middle one is the
        // whole point:
        //
        //   WITHIN @Tolerance, either direction → the LOWER figure wins.
        //
        //     This is the err-low ruling, applied here for the same reason it
        //     was applied to the ownership series: localconfig.vdf and
        //     GetOwnedGames disagree by a minute on real appids (Portal reports
        //     280 and 279), and each pass would otherwise see the other's figure
        //     as new. On the ownership path the resolver settles that at the
        //     LOWER value. A max() here would settle it at the higher one, and a
        //     library filtered to one account would then report a minute more
        //     than the same library unfiltered — for precisely the ownerships
        //     the band exists to quiet. The two layers share
        //     PlaytimeTolerance.Minutes so they cannot drift apart.
        //
        //   A rise beyond the band → recorded. Two minutes or more is play.
        //
        //   A FALL beyond the band → recorded only when the date corroborates.
        //
        //     These rows are refreshed current facts, not an append-only series,
        //     so unlike play_records nothing downstream depends on them never
        //     going down and a genuine correction has somewhere to land. But
        //     most large falls are not corrections: localconfig.vdf on a machine
        //     that has not synced sees a stale floor of a counter another PC has
        //     carried further, which is the blind spot PlaytimeView.LowerBound
        //     exists for.
        //
        //     The tell is the date. A reader reporting FEWER minutes while its
        //     last-played is at least as current as the stored one is correcting
        //     its own count; a reader reporting fewer minutes with an OLDER date
        //     (or with none at all) is simply behind, and its figure is ignored.
        //     Without the date test this column could only ever ratchet upward
        //     and one spurious high reading would be permanent — which in
        //     filtered mode is a wrong number on the tile and a wrong bucket
        //     under it, with no pass able to repair either.
        //
        // ── What moves and what does not ────────────────────────────────────
        //
        // first_seen_at is absent from the SET clause on purpose. It is the
        // earliest moment Winnow could prove this account holds this game, and
        // re-observing a fact does not make it newer.
        //
        // source DOES move, and that is load-bearing rather than cosmetic:
        // migration 0015's seed writes 'ownerships.account_ref', and the bucket
        // query refuses to hide a game whose only evidence carries that mark. A
        // real reader re-reporting the membership is exactly the event that
        // should retire the seed's caveat, and this is where it happens.
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ownership_accounts (
                ownership_id, account_ref, playtime_minutes, last_played_at,
                source, first_seen_at, last_seen_at)
            VALUES (@OwnershipId, @AccountRef, @PlaytimeMinutes, @LastPlayedAt,
                    @Source, @ObservedAt, @ObservedAt)
            ON CONFLICT (ownership_id, account_ref) DO UPDATE SET
                playtime_minutes =
                    CASE
                        WHEN excluded.playtime_minutes IS NULL
                            THEN ownership_accounts.playtime_minutes
                        WHEN ownership_accounts.playtime_minutes IS NULL
                            THEN excluded.playtime_minutes
                        -- Inside the band, either direction: err low.
                        WHEN abs(excluded.playtime_minutes
                                 - ownership_accounts.playtime_minutes) <= @Tolerance
                            THEN min(excluded.playtime_minutes,
                                     ownership_accounts.playtime_minutes)
                        -- A real rise is play.
                        WHEN excluded.playtime_minutes > ownership_accounts.playtime_minutes
                            THEN excluded.playtime_minutes
                        -- A real fall is a correction only if this reader is not
                        -- simply behind. Its last-played must be at least as
                        -- current as the stored one; a null incoming date is no
                        -- corroboration at all.
                        WHEN excluded.last_played_at IS NOT NULL
                             AND (ownership_accounts.last_played_at IS NULL
                                  OR datetime(excluded.last_played_at)
                                     >= datetime(ownership_accounts.last_played_at))
                            THEN excluded.playtime_minutes
                        ELSE ownership_accounts.playtime_minutes
                    END,
                last_played_at =
                    CASE
                        WHEN excluded.last_played_at IS NULL
                            THEN ownership_accounts.last_played_at
                        WHEN ownership_accounts.last_played_at IS NULL
                            THEN excluded.last_played_at
                        WHEN datetime(excluded.last_played_at)
                             > datetime(ownership_accounts.last_played_at)
                            THEN excluded.last_played_at
                        ELSE ownership_accounts.last_played_at
                    END,
                source       = excluded.source,
                last_seen_at = excluded.last_seen_at;
            """,
            new
            {
                row.OwnershipId,
                AccountRef = row.AccountRef.Trim(),
                row.PlaytimeMinutes,
                row.LastPlayedAt,
                row.Source,
                row.ObservedAt,
                Tolerance = PlaytimeTolerance.Minutes,
            },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OwnershipAccount>> GetByOwnershipAsync(
        long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<OwnershipAccount>(new CommandDefinition(
            $"""
            SELECT {Columns}
            FROM ownership_accounts
            WHERE ownership_id = @ownershipId
            ORDER BY account_ref;
            """,
            new { ownershipId },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetAccountRefsAsync(
        string store, CancellationToken ct = default)
    {
        // Joined to ownerships for the store, because an account reference is
        // only meaningful inside the store that issued it: a GOG user id and a
        // Steam3 account id are both bare integers and nothing but the store
        // column tells them apart.
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT DISTINCT oa.account_ref
            FROM ownership_accounts oa
            JOIN ownerships o ON o.id = oa.ownership_id
            WHERE o.store = @store
            ORDER BY oa.account_ref;
            """,
            new { store },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }
}
