using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winnow.Replay;

public static class ReplayCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (args.Length == 3 && args[0] == "capture")
            {
                var manifest = SnapshotBundle.Capture(args[1], args[2]);
                await output.WriteLineAsync(JsonSerializer.Serialize(manifest, SnapshotBundle.JsonOptions));
                return 0;
            }
            if (args.Length >= 5 && args[0] == "compare")
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 5; index < args.Length; index += 2)
                {
                    if (index + 1 >= args.Length || args[index] is not ("--k" or "--window-days" or "--through" or "--as-of")
                        || !values.TryAdd(args[index], args[index + 1]))
                        throw new ArgumentException("Unknown, duplicate or incomplete comparison option.");
                }
                var json = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                var tunings = new[] { args[3], args[4] }.Select(path =>
                    JsonSerializer.Deserialize<NamedTuning>(File.ReadAllText(path), json)
                    ?? throw new InvalidDataException("A tuning file is empty.")).ToArray();
                var defaults = new ReplayOptions();
                var report = await ReplayEvaluation.CompareAsync(args[1], args[2], tunings, defaults with
                {
                    K = values.TryGetValue("--k", out var k) ? int.Parse(k, CultureInfo.InvariantCulture) : defaults.K,
                    OutcomeWindowDays = values.TryGetValue("--window-days", out var days) ? int.Parse(days, CultureInfo.InvariantCulture) : defaults.OutcomeWindowDays,
                    OutcomesThroughUtc = values.TryGetValue("--through", out var through) ? ParseUtc(through) : null,
                }, values.TryGetValue("--as-of", out var instant) ? ParseUtc(instant) : null, ct);
                await output.WriteLineAsync(JsonSerializer.Serialize(report, SnapshotBundle.JsonOptions));
                return 0;
            }
            await error.WriteLineAsync("Usage: Winnow.Replay capture <database> <new-directory> | compare <capture> <outcomes-capture> <tuning-a.json> <tuning-b.json> [--k 10] [--window-days 3] [--through UTC-instant] [--as-of captured-UTC-instant]");
            return 2;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await error.WriteLineAsync("Replay cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or JsonException
            or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or FormatException)
        {
            await error.WriteLineAsync("Replay refused: " + exception.Message);
            return 2;
        }
    }

    private static DateTime ParseUtc(string value)
    {
        if (!value.EndsWith('Z') && !value.EndsWith("+00:00", StringComparison.Ordinal))
            throw new ArgumentException("Dates must explicitly use UTC (Z or +00:00).");
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;
    }
}
