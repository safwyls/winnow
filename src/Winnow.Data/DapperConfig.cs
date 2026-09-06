using System.Data;
using System.Globalization;
using Dapper;

namespace Winnow.Data;

/// <summary>
/// Process-wide Dapper configuration for SQLite, applied once (idempotent)
/// by <see cref="SqliteConnectionFactory"/> before any connection is handed
/// out. Timestamps are stored as TEXT, UTC, with optional fractional seconds —
/// lexicographically sortable and understood by SQLite's datetime().
/// </summary>
internal static class DapperConfig
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

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
            // Built-in parameter mappings take precedence over handlers when writing.
            SqlMapper.RemoveTypeMap(typeof(DateTime));
            SqlMapper.RemoveTypeMap(typeof(DateTime?));
            SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
            _configured = true;
        }
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            if (value.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException(
                    "A persisted timestamp must have DateTimeKind.Utc or DateTimeKind.Local; "
                    + "Unspecified has no unambiguous UTC instant.", nameof(value));
            }

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
