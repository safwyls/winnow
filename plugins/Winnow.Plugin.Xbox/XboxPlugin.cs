using System.Text.Json;
using Winnow.PluginSdk;
using static Winnow.Plugin.Xbox.XboxProtocol;

namespace Winnow.Plugin.Xbox;

/// <summary>SDK-only Xbox provider. Installation and played history remain distinct from entitlement evidence.</summary>
public sealed class XboxPlugin : ILibrarySourcePlugin, IMetadataProviderPlugin, IArtworkProviderPlugin, IPluginAccount, IPluginGameActions
{
    private readonly IXboxLocalLibrary _local;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly HashSet<string> _knownGames = new(StringComparer.OrdinalIgnoreCase);
    private IPluginContext? _context;
    private XboxAccountClient _account = null!;
    private XboxHistoryClient _history = null!;
    private XboxCatalogClient _catalog = null!;

    public XboxPlugin() : this(new WindowsXboxLocalLibrary(), TimeProvider.System) { }
    public XboxPlugin(IXboxLocalLibrary local, TimeProvider? clock = null) { _local = local; _clock = clock ?? TimeProvider.System; }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _account = new(context, _clock);
        _history = new(context, _account, _clock);
        _catalog = new(context, _clock);
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        var context = Context;
        await _libraryGate.WaitAsync(cancellationToken);
        try
        {
            using var libraryBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            libraryBudget.CancelAfter(TimeSpan.FromSeconds(100));
            XboxLocalScan? local;
            try { local = await _local.ScanAsync(libraryBudget.Token); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { local = null; }
            var historyEnabled = await EnabledAsync("import-history", cancellationToken);
            var consoles = await EnabledAsync("include-console", cancellationToken);
            XboxHistory? history = null;
            if (historyEnabled)
            {
                try { history = await _history.GetAsync(libraryBudget.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }
            if (history is not null && history.Scope != await _account.CacheScopeAsync(cancellationToken)) history = null;
            if (local is null && history is null) return null;
            var result = new Dictionary<string, PluginLibraryGame>(StringComparer.OrdinalIgnoreCase);
            var classifiedPackages = new List<XboxLocalGame>();
            foreach (var played in history?.Games.Where(x => x.Pc && Pfn(x.Pfn)) ?? [])
            {
                var matches = (local?.Packages ?? []).Where(x => string.Equals(x.PackageFamilyName, played.Pfn, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1) classifiedPackages.Add(matches[0] with { Title = played.Title, IsTitleProvisional = false });
            }
            var remembered = await RememberAsync(local, classifiedPackages, cancellationToken);
            foreach (var prior in remembered)
            {
                var registrations = (local?.Packages ?? []).Where(p => string.Equals(p.PackageFamilyName, prior.Pfn, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (registrations.Length == 1)
                {
                    result[Source(prior.Pfn)] = Local(prior.IsTitleProvisional ? registrations[0] : registrations[0] with { Title = prior.Title, IsTitleProvisional = false });
                    continue;
                }
                result[Source(prior.Pfn)] = new(Source(prior.Pfn), prior.Title)
                {
                    Installed = local?.IsComplete == true ? false : null,
                    TitleIsProvisional = prior.IsTitleProvisional,
                    LibrarySourceLabel = "Previously installed Xbox PC game · ownership unverified",
                    Actions = [PluginGameActionKind.OpenStore]
                };
            }
            foreach (var game in local?.Games ?? [])
            {
                if (!Pfn(game.PackageFamilyName) || Clean(game.Title) is null) continue;
                var selected = game.IsTitleProvisional && result.TryGetValue(Source(game.PackageFamilyName), out var known) && !known.TitleIsProvisional
                    ? game with { Title = known.Title, IsTitleProvisional = false } : game;
                result[Source(game.PackageFamilyName)] = Local(selected);
            }
            foreach (var game in history?.Games ?? [])
            {
                if (!game.Pc && !(consoles && game.Console)) continue;
                // PC identity stays the package family even when a title's catalog decoration changes.
                if (game.Pc && !Pfn(game.Pfn)) continue;
                var source = game.Pc ? Source(game.Pfn!) : "title:" + game.TitleId;
                var installed = game.Pc && game.Pfn is not null
                    ? (local?.Packages ?? []).Where(p => string.Equals(p.PackageFamilyName, game.Pfn, StringComparison.OrdinalIgnoreCase)).ToArray() : [];
                var match = installed.Length == 1 ? installed[0] : null;
                var hasLocal = result.TryGetValue(source, out var existing) && existing.Installed == true;
                var title = Clean(game.Title) ?? existing?.Title;
                if (title is null) continue;
                var canPlay = game.Pc && match?.AppUserModelId is not null;
                result[source] = new(source, title)
                {
                    AccountRef = history!.Xuid,
                    Installed = hasLocal || match is not null ? true : game.Pc && local?.IsComplete == true ? false : null,
                    InstallPath = match?.InstallPath ?? (hasLocal ? existing?.InstallPath : null),
                    PlaytimeMinutes = game.Minutes,
                    LastPlayedAt = game.LastPlayed,
                    LibrarySourceLabel = match is not null || hasLocal ? "Installed Xbox PC game and played history · ownership unverified" : game.Pc ? "Xbox PC played history · ownership unverified" : "Xbox console played history · ownership unverified",
                    Actions = canPlay ? [PluginGameActionKind.Play, PluginGameActionKind.OpenStore] : [PluginGameActionKind.OpenStore]
                };
            }
            // A settings change or disconnect during a request cannot publish the old account's history.
            if (history is not null && (!await EnabledAsync("import-history", cancellationToken) || consoles != await EnabledAsync("include-console", cancellationToken)
                || history.Scope != await _account.CacheScopeAsync(cancellationToken)))
                return local?.Games.Where(x => Pfn(x.PackageFamilyName)).Select(Local).ToArray();
            lock (_knownGames)
            {
                _knownGames.Clear();
                foreach (var key in result.Keys) _knownGames.Add(key);
            }
            return result.Values.ToArray();
        }
        finally { _libraryGate.Release(); }
    }

    public async Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default)
    {
        _ = Context;
        cancellationToken.ThrowIfCancellationRequested();
        return game.ExternalIds.TryGetValue("plugin:xbox", out var source) ? (await _catalog.GetAsync(source, cancellationToken))?.Metadata : null;
    }
    public async Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
    {
        _ = Context;
        cancellationToken.ThrowIfCancellationRequested();
        return game.ExternalIds.TryGetValue("plugin:xbox", out var source) ? (await _catalog.GetAsync(source, cancellationToken))?.Artwork : [];
    }
    public Task<PluginAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default) { _ = Context; return _account.StatusAsync(cancellationToken); }
    public Task<PluginSignInChallenge?> BeginSignInAsync(CancellationToken cancellationToken = default) { _ = Context; return _account.BeginAsync(cancellationToken); }
    public Task<PluginSignInResult> PollSignInAsync(string attemptId, CancellationToken cancellationToken = default) { _ = Context; return _account.PollAsync(attemptId, cancellationToken); }
    public Task SignOutAsync(CancellationToken cancellationToken = default) { _ = Context; return _account.SignOutAsync(cancellationToken); }
    public Task CancelSignInAsync(string attemptId, CancellationToken cancellationToken = default) { _ = Context; return _account.CancelAsync(attemptId, cancellationToken); }

    public async Task<PluginGameActionResult> ExecuteGameActionAsync(string sourceId, PluginGameActionKind action, CancellationToken cancellationToken = default)
    {
        _ = Context;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (action == PluginGameActionKind.OpenStore)
            {
                var product = await _catalog.GetAsync(sourceId, cancellationToken);
                return new(product is not null && await _local.OpenStoreAsync(product.ProductId, cancellationToken));
            }
            if (action != PluginGameActionKind.Play || !sourceId.StartsWith("pfn:", StringComparison.Ordinal) || !Pfn(sourceId[4..])) return new(false);
            var scan = await _local.ScanAsync(cancellationToken);
            var matches = (scan?.Packages ?? []).Where(x => string.Equals(Source(x.PackageFamilyName), sourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].AppUserModelId is not { } aumid) return new(false);
            var game = matches[0];
            bool known;
            lock (_knownGames) known = _knownGames.Contains(sourceId);
            if (!known && !(scan?.Games.Any(x => string.Equals(x.PackageFamilyName, game.PackageFamilyName, StringComparison.OrdinalIgnoreCase)) ?? false)) return new(false);
            var separator = aumid.LastIndexOf('!');
            if (separator <= 0 || !string.Equals(aumid[..separator], game.PackageFamilyName, StringComparison.OrdinalIgnoreCase)) return new(false);
            return new(await _local.LaunchAsync(game.PackageFamilyName, aumid[(separator + 1)..], cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { return new(false); }
    }

    private static PluginLibraryGame Local(XboxLocalGame game) => new(Source(game.PackageFamilyName), game.Title)
    {
        Installed = true, InstallPath = game.InstallPath,
        TitleIsProvisional = game.IsTitleProvisional,
        LibrarySourceLabel = "Installed Xbox PC game · ownership unverified",
        Actions = game.AppUserModelId is not null ? [PluginGameActionKind.Play, PluginGameActionKind.OpenStore] : [PluginGameActionKind.OpenStore]
    };
    private async Task<bool> EnabledAsync(string key, CancellationToken ct) => bool.TryParse(await Context.Settings.GetAsync(key, ct), out var enabled) && enabled;
    private static string Source(string pfn) => "pfn:" + pfn.ToLowerInvariant();
    private IPluginContext Context => _context ?? throw new InvalidOperationException("The Xbox plugin has not been initialized.");

    private async Task<IReadOnlyList<RememberedGame>> RememberAsync(XboxLocalScan? scan, IReadOnlyList<XboxLocalGame> classifiedPackages, CancellationToken ct)
    {
        var cached = await Context.Cache.GetAsync("local-inventory:v1", ct);
        var games = new Dictionary<string, RememberedGame>(StringComparer.OrdinalIgnoreCase);
        if (cached is not null && cached.Payload.Length <= MaxBytes)
        {
            try
            {
                var prior = JsonSerializer.Deserialize<RememberedInventory>(cached.Payload, Json);
                if (prior is { Version: 1, Games: not null } && prior.Games.Count <= 10000)
                    foreach (var game in prior.Games.Where(x => x is not null && Pfn(x.Pfn) && Clean(x.Title) is not null)) games[game.Pfn] = game;
            }
            catch (JsonException) { }
        }
        foreach (var game in (scan?.Games ?? []).Concat(classifiedPackages))
        {
            if (!Pfn(game.PackageFamilyName) || Clean(game.Title) is null || games.Count >= 10000) continue;
            if (game.IsTitleProvisional && games.TryGetValue(game.PackageFamilyName, out var known) && !known.IsTitleProvisional) continue;
            games[game.PackageFamilyName] = new(game.PackageFamilyName, game.Title, game.IsTitleProvisional);
        }
        var values = games.Values.ToArray();
        if (scan is not null)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RememberedInventory(1, values), Json);
            if (payload.Length <= MaxBytes) await Context.Cache.SetAsync("local-inventory:v1", new(payload, _clock.GetUtcNow().AddDays(30)), ct);
        }
        return values;
    }
    private sealed record RememberedInventory(int Version, IReadOnlyList<RememberedGame> Games);
    private sealed record RememberedGame(string Pfn, string Title, bool IsTitleProvisional = false);
}
