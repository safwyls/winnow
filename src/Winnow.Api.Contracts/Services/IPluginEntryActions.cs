using Winnow.App.ViewModels;
using Winnow.Core.Queries;

namespace Winnow.App.Services;

public sealed record PluginEntryActions(string? SourceLabel, GameLink? Play, GameLink? Store);

public interface IPluginEntryActions
{
    Task<IReadOnlyDictionary<long, PluginEntryActions>> ReadAsync(LibrarySnapshot snapshot, CancellationToken ct);
}

public interface IPluginActionDispatcher
{
    Task<bool> ExecuteAsync(long ownershipId, GameLink action, CancellationToken ct = default);
}
