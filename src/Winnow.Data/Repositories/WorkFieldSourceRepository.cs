using System.Globalization;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>work_field_sources</c> (migration 0027). One row per
/// (work, field), replaced in place: the table answers "where is this value
/// from", and there is exactly one value per field. The column name in every
/// UPDATE is a whitelisted constant from <see cref="ColumnFor"/> — an
/// unrecognised field is <see cref="WorkFieldEditOutcome.UnknownField"/>
/// before any SQL is built.
/// </summary>
public sealed class WorkFieldSourceRepository : IWorkFieldSourceRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public WorkFieldSourceRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSourcesAsync(
        long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<FieldSourceRow>(
            new CommandDefinition("""
                SELECT field, source
                FROM work_field_sources
                WHERE work_id = @workId;
                """,
                new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[row.Field] = row.Source;
        }

        return map;
    }

    public async Task<IReadOnlyList<WorkFieldState>> GetStateAsync(
        long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var values = await lease.Connection.QuerySingleOrDefaultAsync<FieldValueRow>(
            new CommandDefinition("""
                SELECT name               AS Name,
                       first_release_year AS FirstReleaseYear,
                       summary            AS Summary,
                       cover_url          AS CoverUrl,
                       publisher          AS Publisher,
                       background_url     AS BackgroundUrl
                FROM works
                WHERE id = @workId;
                """,
                new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        if (values is null)
        {
            return [];
        }

        var sources = await lease.Connection.QueryAsync<FieldSourceRow>(
            new CommandDefinition("""
                SELECT field, source
                FROM work_field_sources
                WHERE work_id = @workId;
                """,
                new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        var sourceByField = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in sources)
        {
            sourceByField[row.Field] = row.Source;
        }

        var states = new List<WorkFieldState>(WorkFields.All.Count);
        foreach (var field in WorkFields.All)
        {
            states.Add(new WorkFieldState
            {
                Field = field,
                Value = values.ValueOf(field),
                Source = sourceByField.GetValueOrDefault(field),
            });
        }

        return states;
    }

    public async Task<WorkFieldEditOutcome> SetFieldAsync(
        long workId, string field, string? value, CancellationToken ct = default)
    {
        if (ColumnFor(field) is not { } column)
        {
            return WorkFieldEditOutcome.UnknownField;
        }

        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Range-checked rather than merely parsed, so a typo becomes a
        // refusal instead of a stored absurdity.
        int? year = null;
        if (WorkFields.IsNumeric(field) && trimmed is not null)
        {
            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 1900
                || parsed > 2200)
            {
                return WorkFieldEditOutcome.InvalidValue;
            }

            year = parsed;
        }

        // works.name is NOT NULL: a blank name is InvalidValue, not a
        // column set to NULL.
        if (field == WorkFields.Name && trimmed is null)
        {
            return WorkFieldEditOutcome.InvalidValue;
        }

        using var lease = _factory.Lease();

        var exists = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM works WHERE id = @workId;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        if (exists == 0)
        {
            return WorkFieldEditOutcome.WorkNotFound;
        }

        // name is special twice: it is NOT NULL so blank is InvalidValue
        // (above), and setting it clears name_is_provisional so the
        // provisional-name pass cannot rename the work afterwards.
        var sql = field == WorkFields.Name
            ? $"UPDATE works SET {column} = @value, name_is_provisional = 0 WHERE id = @workId;"
            : $"UPDATE works SET {column} = @value WHERE id = @workId;";

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { workId, value = WorkFields.IsNumeric(field) ? (object?)year : trimmed },
            transaction: lease.Transaction, cancellationToken: ct));

        await WorkFieldSourceWrites.StampAsync(
            lease.Connection,
            lease.Transaction,
            workId,
            new Dictionary<string, string>(StringComparer.Ordinal) { [field] = FieldSources.User },
            _clock.GetUtcNow().UtcDateTime,
            ct);

        return WorkFieldEditOutcome.Applied;
    }

    /// <summary>
    /// Hands a field back to automatic. Deletes the stamp AND empties the
    /// column, because the automatic pass is fill-only: leaving the value
    /// there would mean the field never came back. <c>name</c> cannot be
    /// emptied (<c>works.name</c> is NOT NULL), so its reset sets
    /// <c>name_is_provisional = 1</c> and leaves the text standing as the
    /// placeholder the next pass will promote over.
    /// </summary>
    public async Task<WorkFieldEditOutcome> ResetFieldAsync(
        long workId, string field, CancellationToken ct = default)
    {
        if (ColumnFor(field) is not { } column)
        {
            return WorkFieldEditOutcome.UnknownField;
        }

        using var lease = _factory.Lease();

        var exists = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM works WHERE id = @workId;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        if (exists == 0)
        {
            return WorkFieldEditOutcome.WorkNotFound;
        }

        var sql = field == WorkFields.Name
            ? "UPDATE works SET name_is_provisional = 1 WHERE id = @workId;"
            : $"UPDATE works SET {column} = NULL WHERE id = @workId;";

        await lease.Connection.ExecuteAsync(new CommandDefinition(
            sql, new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM work_field_sources
            WHERE work_id = @workId AND field = @field;
            """,
            new { workId, field }, transaction: lease.Transaction, cancellationToken: ct));

        return WorkFieldEditOutcome.Applied;
    }

    /// <summary>
    /// Maps a whitelisted constant to a literal column name. An unrecognised
    /// field returns null and becomes <see cref="WorkFieldEditOutcome.UnknownField"/>
    /// before any SQL is built — a caller's string is never interpolated into
    /// a query.
    /// </summary>
    private static string? ColumnFor(string? field) => field switch
    {
        WorkFields.Name => "name",
        WorkFields.FirstReleaseYear => "first_release_year",
        WorkFields.Summary => "summary",
        WorkFields.CoverUrl => "cover_url",
        WorkFields.Publisher => "publisher",
        WorkFields.BackgroundUrl => "background_url",
        _ => null,
    };

    private sealed class FieldSourceRow
    {
        public string Field { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;
    }

    private sealed class FieldValueRow
    {
        public string? Name { get; init; }

        public int? FirstReleaseYear { get; init; }

        public string? Summary { get; init; }

        public string? CoverUrl { get; init; }

        public string? Publisher { get; init; }

        public string? BackgroundUrl { get; init; }

        public string? ValueOf(string field) => field switch
        {
            WorkFields.Name => Name,
            WorkFields.FirstReleaseYear =>
                FirstReleaseYear?.ToString(CultureInfo.InvariantCulture),
            WorkFields.Summary => Summary,
            WorkFields.CoverUrl => CoverUrl,
            WorkFields.Publisher => Publisher,
            WorkFields.BackgroundUrl => BackgroundUrl,
            _ => null,
        };
    }
}
