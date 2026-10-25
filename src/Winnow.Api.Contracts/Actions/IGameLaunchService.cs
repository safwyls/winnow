using Winnow.App.ViewModels;
namespace Winnow.App.Services;
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


public interface IGameLaunchService
{
    Task<LaunchDispatch> LaunchAsync(long ownershipId, GameLink action);
}
