using Winnow.PluginSdk;

namespace Winnow.Plugin.Psn;

/// <summary>PSN account observations through the public SDK; console presence never implies a local install.</summary>
public sealed class PsnPlugin : ILibrarySourcePlugin, IMetadataProviderPlugin, IArtworkProviderPlugin
{
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPluginContext? _context;
    private PsnAccountClient _account = null!;
    private PsnLibraryClient _library = null!;
    private PsnArtworkClient _artwork = null!;
    private Published? _published;

    public PsnPlugin() : this(TimeProvider.System) { }
    public PsnPlugin(TimeProvider clock) => _clock = clock;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _account = new(context, _clock);
        _library = new(context, _clock);
        _artwork = new(context, _clock);
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        _ = Context;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(100));
            var ct = budget.Token;
            var history = await EnabledAsync("import-history", ct);
            var legacy = await EnabledAsync("include-legacy", ct);
            var session = await _account.GetSessionAsync(ct);
            if (session is null) { Volatile.Write(ref _published, null); return null; }
            var result = await _library.GetAsync(session, history, legacy, ct);
            var published = result is null ? null : new Published(session, history, legacy, result);
            if (published is null || !await IsCurrentAsync(published, ct))
            { Volatile.Write(ref _published, null); return null; }
            Volatile.Write(ref _published, published);
            return result!.Games.Select(x => x.Library).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (PsnAccountClient.SoftFailure(ex) || ex is OperationCanceledException) { return null; }
        finally { _gate.Release(); }
    }

    public async Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default)
        => (await FindAsync(game, cancellationToken))?.Title.Metadata;

    public async Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
    {
        _ = Context;
        cancellationToken.ThrowIfCancellationRequested();
        // The host combines every release of a work; unrelated stores are a complete empty result.
        if (!game.ExternalIds.ContainsKey("plugin:psn")) return [];
        var match = await FindAsync(game, cancellationToken);
        if (match is null) return null;
        var artwork = await _artwork.GetAsync(match.Value.Title.IconUrl, cancellationToken);
        return await IsCurrentAsync(match.Value.Published, cancellationToken) ? artwork : null;
    }

    private async Task<(PsnTitle Title, Published Published)?> FindAsync(PluginGame game, CancellationToken ct)
    {
        _ = Context;
        ct.ThrowIfCancellationRequested();
        if (!game.ExternalIds.TryGetValue("plugin:psn", out var source)) return null;
        var published = Volatile.Read(ref _published);
        if (published is null || !await IsCurrentAsync(published, ct)) return null;
        var title = published.Snapshot.Games.FirstOrDefault(x => x.Library.SourceId == source);
        return title is null ? null : (title, published);
    }

    private async Task<bool> IsCurrentAsync(Published published, CancellationToken ct)
        => published.Snapshot.Scope == published.Session.Scope && await _account.IsCurrentAsync(published.Session, ct)
            && published.History == await EnabledAsync("import-history", ct) && published.Legacy == await EnabledAsync("include-legacy", ct);
    private async Task<bool> EnabledAsync(string key, CancellationToken ct) => bool.TryParse(await Context.Settings.GetAsync(key, ct), out var value) && value;
    private IPluginContext Context => _context ?? throw new InvalidOperationException("The PlayStation plugin has not been initialized.");
    private sealed record Published(PsnSession Session, bool History, bool Legacy, PsnLibrarySnapshot Snapshot);
}
