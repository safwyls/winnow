using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.Core.Lifecycle;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class LifecycleRepository(ISqliteConnectionFactory factory) : ILifecycleRepository
{
    internal const string Columns = "id, release_id, source, source_id, observed_at, signals_json, raw_json";

    public async Task<long> AppendAsync(LifecycleObservation observation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Source);
        var signals = observation.Signals with
        {
            LastDevelopmentAt = Utc(observation.Signals.LastDevelopmentAt),
            LastCommunicationAt = Utc(observation.Signals.LastCommunicationAt),
            LastStoreChangeAt = Utc(observation.Signals.LastStoreChangeAt),
            OfficialShutdownAt = Utc(observation.Signals.OfficialShutdownAt),
        };
        using var lease = factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lifecycle_observations (release_id, source, source_id, observed_at, signals_json, raw_json)
            VALUES (@ReleaseId, @Source, @SourceId, @ObservedAt, @SignalsJson, @RawJson);
            SELECT last_insert_rowid();
            """, new
            {
                observation.ReleaseId, observation.Source, observation.SourceId,
                ObservedAt = Utc(observation.ObservedAt),
                SignalsJson = JsonSerializer.Serialize(signals, LifecycleJsonContext.Default.LifecycleSignals),
                observation.RawJson,
            }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public Task<IReadOnlyList<LifecycleObservation>> GetForReleaseAsync(long releaseId, CancellationToken ct = default)
        => ReadAsync("WHERE release_id = @releaseId", new { releaseId }, ct);

    public Task<IReadOnlyList<LifecycleObservation>> GetAllAsync(CancellationToken ct = default)
        => ReadAsync("", null, ct);

    private async Task<IReadOnlyList<LifecycleObservation>> ReadAsync(string where, object? args, CancellationToken ct)
    {
        using var lease = factory.Lease();
        var rows = await lease.Connection.QueryAsync<ObservationRow>(new CommandDefinition(
            $"SELECT {Columns} FROM lifecycle_observations {where} ORDER BY observed_at, id;",
            args, transaction: lease.Transaction, cancellationToken: ct));
        return rows.Select(r => r.ToObservation()).ToArray();
    }

    private static DateTime? Utc(DateTime? date) => date switch
    {
        null => null,
        { Kind: DateTimeKind.Unspecified } => throw new ArgumentException("Lifecycle timestamps must have an explicit timezone."),
        { } value => value.ToUniversalTime(),
    };

    internal sealed record ObservationRow
    {
        public long Id { get; init; }
        public long ReleaseId { get; init; }
        public required string Source { get; init; }
        public string? SourceId { get; init; }
        public DateTime ObservedAt { get; init; }
        public required string SignalsJson { get; init; }
        public string? RawJson { get; init; }
        public LifecycleObservation ToObservation() => new()
        {
            Id = Id, ReleaseId = ReleaseId, Source = Source, SourceId = SourceId,
            ObservedAt = ObservedAt, RawJson = RawJson,
            Signals = JsonSerializer.Deserialize(SignalsJson, LifecycleJsonContext.Default.LifecycleSignals) ?? new(),
        };
    }
}

[JsonSerializable(typeof(LifecycleSignals))]
[JsonSerializable(typeof(LifecycleObservation[]))]
internal partial class LifecycleJsonContext : JsonSerializerContext;
