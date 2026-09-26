using Winnow.App.ViewModels;
using Winnow.Monitor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.App.Services;

/// <summary>
/// M3b: dispatches a store URI (Play/Install) and declares a launch intent
/// BEFORE the dispatch so the session watcher can attribute the process.
/// Shows no dialog, asks no question, never throws into the UI.
/// </summary>
public sealed class GameLaunchService : IGameLaunchService
{
    private readonly IUriDispatcher _dispatcher;
    private readonly LaunchIntents? _intents;
    private readonly TimeProvider _clock;
    private readonly ILogger<GameLaunchService> _logger;
    private readonly PluginGameActionService? _pluginActions;

    public GameLaunchService(
        IUriDispatcher dispatcher,
        LaunchIntents? intents = null,
        TimeProvider? clock = null,
        ILogger<GameLaunchService>? logger = null,
        PluginGameActionService? pluginActions = null)
    {
        _dispatcher = dispatcher;
        _intents = intents;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GameLaunchService>.Instance;
        _pluginActions = pluginActions;
    }

    /// <summary>Fires a store action for one ownership. Only Play declares an intent; Install does not.</summary>
    public async Task<LaunchDispatch> LaunchAsync(long ownershipId, GameLink action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Re-parse rather than trust the string that reached us: the only way to
        // build a GameLink is through its factory, but the dispatch is the
        // boundary and a boundary checks. Same rule the view's own handlers
        // followed before this service existed.
        if (!Uri.TryCreate(action.Uri, UriKind.Absolute, out var uri))
        {
            return LaunchDispatch.Refused;
        }

        var declared = false;
        if (action.StartsGame && _intents is not null)
        {
            declared = _intents.Declare(ownershipId, _clock.GetUtcNow().UtcDateTime);
            if (!declared)
            {
                // Already in flight. Nothing is dispatched and nothing is said:
                // the indicator for the first click is still on screen, and it
                // is already the answer to "did that work?".
                return LaunchDispatch.AlreadyRunning;
            }
        }

        // Belt and braces, and both are load-bearing. TopLevelUriDispatcher
        // promises never to throw and keeps that promise; this catch is here
        // because the caller is an async command handler, and an exception
        // escaping one of those is unobserved at best and a torn-down window at
        // worst. "None of these may throw into the UI" is a property of the
        // launch path, not a property of one implementation of one seam.
        bool opened;
        try
        {
            opened = action.PluginId is not null
                ? _pluginActions is not null && await _pluginActions.ExecuteAsync(ownershipId, action).ConfigureAwait(false)
                : await _dispatcher.OpenAsync(uri).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dispatching {Scheme} threw.", uri.Scheme);
            opened = false;
        }

        if (opened)
        {
            _logger.LogInformation(
                "Handed {Kind} for ownership {OwnershipId} to {Scheme}.",
                action.Kind, ownershipId, uri.Scheme);
            return LaunchDispatch.HandedOff;
        }

        if (declared)
        {
            // Withdrawn rather than left to expire: an intent whose URI never
            // reached a handler must not be sitting there ninety seconds later
            // ready to claim whatever the user starts instead.
            _intents!.Abandon(ownershipId);
        }

        _logger.LogWarning(
            "The platform declined {Uri} for ownership {OwnershipId}; the store client is "
            + "probably not installed or its protocol handler is not registered.",
            uri.Scheme, ownershipId);

        return LaunchDispatch.Refused;
    }
}
