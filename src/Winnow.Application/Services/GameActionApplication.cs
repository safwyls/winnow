using System.Collections.Concurrent;
using System.Diagnostics;
using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;

namespace Winnow.Application;

/// <summary>Resolves action IDs against current ownership facts. Clients cannot supply executable paths or URIs.</summary>
public sealed class GameActionApplication(IOwnershipRepository ownerships, IReleaseRepository releases,
    ILibraryQueryRepository library, IEpicLaunchKeys epicKeys, PluginGameActionService plugins,
    IGameLaunchService launcher, IApplicationChangePublisher changes, TimeProvider clock)
{
    private sealed record Attempt(long OwnershipId, GameActionKind Action, DateTimeOffset Started, Lazy<Task<LaunchDispatch>> Result);
    private readonly ConcurrentDictionary<Guid, Attempt> _attempts = new();

    public Task<LaunchDispatch> ExecuteAsync(long ownershipId, GameActionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.OperationId == Guid.Empty || ownershipId <= 0 || !Enum.IsDefined(request.Action))
            throw new ArgumentException("An operation ID and valid game action are required.");
        var now = clock.GetUtcNow();
        foreach (var entry in _attempts.Where(x => x.Value.Started < now.AddMinutes(-10) && x.Value.Result.IsValueCreated && x.Value.Result.Value.IsCompleted))
            _attempts.TryRemove(entry.Key, out _);
        if (_attempts.Count >= 1024 && !_attempts.ContainsKey(request.OperationId))
            throw new ApplicationConflictException("Too many game actions are pending.");
        var attempt = _attempts.GetOrAdd(request.OperationId, _ => new(ownershipId, request.Action, now,
            new Lazy<Task<LaunchDispatch>>(() => DispatchAsync(ownershipId, request.Action))));
        if (attempt.OwnershipId != ownershipId || attempt.Action != request.Action)
            throw new ApplicationConflictException("This operation ID belongs to a different game action.");
        // A disconnected frontend cannot cancel an OS action which may already have been handed off.
        return attempt.Result.Value.WaitAsync(ct);
    }

    private async Task<LaunchDispatch> DispatchAsync(long ownershipId, GameActionKind kind)
    {
        var ownership = await ownerships.GetAsync(ownershipId)
            ?? throw new ApplicationNotFoundException("Game entry not found.");
        GameLink? action;
        if (ownership.Store.StartsWith("plugin:", StringComparison.Ordinal))
        {
            var snapshot = await library.GetSnapshotAsync(Winnow.Core.Queries.BucketThresholds.Default);
            var actions = await plugins.ReadAsync(snapshot, CancellationToken.None);
            action = kind switch
            {
                GameActionKind.Play or GameActionKind.Primary => actions.GetValueOrDefault(ownershipId)?.Play,
                GameActionKind.OpenStore => actions.GetValueOrDefault(ownershipId)?.Store,
                _ => null
            };
            if (kind == GameActionKind.OpenStore)
                return action is not null && await plugins.ExecuteAsync(ownershipId, action)
                    ? LaunchDispatch.HandedOff : LaunchDispatch.Refused;
        }
        else
        {
            if (kind == GameActionKind.OpenStore) return LaunchDispatch.Refused;
            var identifiers = await releases.GetExternalIdsAsync(ownership.ReleaseId);
            var steam = identifiers.FirstOrDefault(x => x.Provider == "steam")?.ProviderId;
            var gog = identifiers.FirstOrDefault(x => x.Provider == "gog")?.ProviderId;
            var epic = identifiers.FirstOrDefault(x => x.Provider == "epic")?.ProviderId;
            EpicLaunchKey? key = null;
            if (epic is not null && (await epicKeys.GetAllAsync()).TryGetValue(epic, out var found)) key = found;
            action = kind is GameActionKind.Uninstall or GameActionKind.Manage
                ? StoreActions.ManagementFor(ownership.Store, ownership.Installed, steam, gog)
                : StoreActions.PrimaryFor(ownership.Store, ownership.Installed, steam, gog, key);
            if (kind == GameActionKind.Play && action?.Kind != GameLinkKind.Play
                || kind == GameActionKind.Install && action?.Kind != GameLinkKind.Install
                || kind == GameActionKind.Uninstall && action?.Kind != GameLinkKind.Uninstall)
                action = null;
        }
        if (action is null) return LaunchDispatch.Refused;
        var result = await launcher.LaunchAsync(ownershipId, action);
        changes.Publish("launch.changed", ownershipId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return result;
    }
}

public sealed class SystemUriDispatcher : IUriDispatcher
{
    public Task<bool> OpenAsync(Uri uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        { return Task.FromResult(false); }
    }
}
