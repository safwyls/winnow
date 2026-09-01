using Dapper;

namespace Winnow.Data.Repositories;

/// <summary>
/// Writes <c>merge_undo_rows</c> for one merge application, inside the merge's
/// own transaction. Every capture runs immediately before the statement it
/// describes and carries that statement's WHERE clause, so the journal names the
/// rows the next statement is about to touch and no others.
///
/// <para><c>seq</c> is a per-application counter advanced by the number of rows
/// each capture wrote, which makes it dense and monotonic and satisfies 0017's
/// UNIQUE (<c>application_id</c>, <c>seq</c>). It records capture order; the
/// restore's order is the undo repository's own (parents before children) and
/// does not read it.</para>
/// </summary>
internal sealed class MergeUndoJournalWriter
{
    private readonly DbLease _lease;
    private readonly long _applicationId;
    private long _seq;

    public MergeUndoJournalWriter(DbLease lease, long applicationId)
    {
        _lease = lease;
        _applicationId = applicationId;
    }

    public long ApplicationId => _applicationId;

    public async Task<int> CaptureAsync(string sql, object identifiers, CancellationToken ct)
    {
        var parameters = new DynamicParameters(identifiers);
        parameters.Add("applicationId", _applicationId);
        parameters.Add("seqBase", _seq);

        var rows = await _lease.Connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, _lease.Transaction, cancellationToken: ct));

        _seq += rows;
        return rows;
    }
}
