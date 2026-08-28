using Hoard.App.ViewModels;
using Hoard.Monitor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.App.Services;

/// <summary>What came of pressing the button. Three outcomes, and no dialog for any of them.</summary>
public enum LaunchDispatch
{
    /// <summary>
    /// The URI reached the store's handler. The game is not running yet — that
    /// is a separate fact, arriving later from the watcher — and this deliberately
    /// does not claim otherwise.
    /// </summary>
    HandedOff,

    /// <summary>
    /// A launch of this game is already in flight. The second click of a double
    /// click, and doing nothing is the whole point: two dispatches means two
    /// store prompts for one impatient user.
    /// </summary>
    AlreadyRunning,

    /// <summary>
    /// The platform refused the URI. The store client is not installed, its
    /// protocol registration is broken, or the shell's own confirmation was
    /// declined.
    /// </summary>
    Refused,
}

/// <summary>
/// M3b: pressing Play, and telling the session watcher which game it was.
///
/// <para><b>The user's brief for this milestone is one sentence — "click Play,
/// the game starts, and nothing else happens" — and this class is where that is
/// either true or not.</b> It shows no dialog, asks no question, blocks on
/// nothing and reports no error the user can do anything about. The store client
/// starting up is not an error. A store prompt the user cancels is not an error.
/// A game that takes forty seconds to appear is not an error. Exactly one thing
/// is worth saying out loud — the URI never reached a handler at all — and even
/// that is a line of text that fades, not a box to dismiss.</para>
///
/// <para><b>The intent is declared BEFORE the dispatch, and that ordering is the
/// milestone.</b> A warm Steam client can have the game's process running before
/// <c>LaunchUriAsync</c> returns; an intent declared afterwards would have missed
/// its own launch and the session would have fallen back to inference — the exact
/// guess §5.2 documents as fragile. Declaring first costs nothing when the launch
/// fails, because <see cref="LaunchIntents.Abandon"/> withdraws it in the same
/// method.</para>
///
/// <para>§5.1: this is an App-layer service, the way <c>StoreConnections</c> and
/// <c>EpicSignInService</c> are. View models raise a command against it; it owns
/// the platform reach and the registry handoff, and it touches no repository and
/// no ingest module.</para>
/// </summary>
public sealed class GameLaunchService
{
    private readonly IUriDispatcher _dispatcher;
    private readonly LaunchIntents? _intents;
    private readonly TimeProvider _clock;
    private readonly ILogger<GameLaunchService> _logger;

    public GameLaunchService(
        IUriDispatcher dispatcher,
        LaunchIntents? intents = null,
        TimeProvider? clock = null,
        ILogger<GameLaunchService>? logger = null)
    {
        _dispatcher = dispatcher;
        _intents = intents;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GameLaunchService>.Instance;
    }

    /// <summary>
    /// Fires a store action for one ownership.
    ///
    /// <para>Only a <see cref="GameLinkKind.Play"/> declares an intent. An
    /// Install starts a download that produces no process for minutes or hours,
    /// and an attribution window held open across that would be a window in
    /// which anything the user starts becomes this game.</para>
    /// </summary>
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
            opened = await _dispatcher.OpenAsync(uri).ConfigureAwait(false);
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
