using Winnow.Enrich.Igdb.Storage;

namespace Winnow.App.Services;

/// <summary>Persists artwork source order shared by desktop and fullscreen.</summary>
public sealed class ArtworkPreferences(ISettingsStore settings)
{
    public const string Steam = "steam";
    public const string SteamGridDb = "steamgriddb";
    public const string Igdb = "igdb";
    public const string SettingKey = "enrichment.artwork_source_order";
    private static readonly string[] Defaults = [Steam, SteamGridDb, Igdb];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<string> _sourceOrder = Normalize(null);
    public IReadOnlyList<ArtworkSourceOption> AvailableSources { get; private set; } =
        [new(Steam, "High-resolution Steam heroes"), new(SteamGridDb, "SteamGridDB"), new(Igdb, "IGDB")];

    public void ConfigureSources(IEnumerable<ArtworkSourceOption> plugins)
    {
        AvailableSources = new[] { new ArtworkSourceOption(Steam, "High-resolution Steam heroes") }
            .Concat(plugins).Append(new(Igdb, "IGDB")).DistinctBy(s => s.Id).ToArray();
        Publish(NormalizeAvailable(SourceOrder));
        Changed?.Invoke();
    }

    public IReadOnlyList<string> SourceOrder => Volatile.Read(ref _sourceOrder);
    public event Action? Changed;

    public Task LoadAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        bool changed;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stored = await settings.GetAsync(SettingKey, ct).ConfigureAwait(false);
            changed = Publish(NormalizeAvailable(stored?.Split(',')));
        }
        finally { _gate.Release(); }
        if (changed) Changed?.Invoke();
    }, ct);

    public Task SaveAsync(IReadOnlyList<string> sourceOrder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceOrder);
        var normalized = NormalizeAvailable(sourceOrder);
        return Task.Run(async () =>
        {
            bool changed;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await settings.SetAsync(SettingKey, string.Join(',', normalized), ct).ConfigureAwait(false);
                changed = Publish(normalized);
            }
            finally { _gate.Release(); }
            if (changed) Changed?.Invoke();
        }, ct);
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? sourceOrder)
    {
        var result = new List<string>(Defaults.Length);
        foreach (var item in (sourceOrder ?? []).Concat(Defaults))
        {
            var source = item?.Trim().ToLowerInvariant();
            if (source is not null && (Defaults.Contains(source) || source.StartsWith("plugin:", StringComparison.Ordinal)) && !result.Contains(source)) result.Add(source);
        }
        return result.AsReadOnly();
    }

    private IReadOnlyList<string> NormalizeAvailable(IEnumerable<string>? order)
    {
        var available = AvailableSources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return (order ?? []).Select(s => s.Trim().ToLowerInvariant())
            .Select(s => s == SteamGridDb && available.Contains("plugin:steamgriddb") ? "plugin:steamgriddb" : s)
            .Concat(AvailableSources.Select(s => s.Id)).Where(available.Contains).Distinct().ToArray();
    }

    private bool Publish(IReadOnlyList<string> next)
    {
        if (SourceOrder.SequenceEqual(next)) return false;
        Volatile.Write(ref _sourceOrder, next);
        return true;
    }
}

public sealed record ArtworkSourceOption(string Id, string Label);
