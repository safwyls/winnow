using System.Data;
using System.Globalization;
using Dapper;

namespace Hoard.Data;

/// <summary>
/// Process-wide Dapper configuration for SQLite, applied once (idempotent)
/// by <see cref="SqliteConnectionFactory"/> before any connection is handed
/// out. Timestamps are stored as TEXT, UTC, 'yyyy-MM-dd HH:mm:ss' —
/// lexicographically sortable and understood by SQLite's datetime().
/// </summary>
internal static class DapperConfig
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly Lock ConfigureLock = new();
    private static bool _configured;

    internal static void EnsureConfigured()
    {
        lock (ConfigureLock)
        {
            if (_configured)
            {
                return;
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
            _configured = true;
        }
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
            parameter.Value = utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        public override DateTime Parse(object value) => value switch
        {
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            string s => DateTime.SpecifyKind(
                DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None),
                DateTimeKind.Utc),
            long unixSeconds => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime,
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateTime."),
        };
    }
}
