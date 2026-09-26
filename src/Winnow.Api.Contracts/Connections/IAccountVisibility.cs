namespace Winnow.App.Services;
/// <summary>
/// What the Stores panel needs to draw the account-visibility toggle, already
/// reduced to three answers a control can bind to.
/// </summary>
/// <param name="AccountConfirmed">
/// Whether Winnow knows which Steam account the configured Web API key belongs
/// to. False keeps the toggle disabled: a filter that cannot name the account
/// it is keeping would hide games at random, and no wording makes that
/// acceptable. Becomes true on its own during the Steam history import.
/// </param>
/// <param name="OwnAccountOnly">Whether the filter is currently on. False by default and on every install that has not touched it.</param>
/// <param name="HiddenCount">
/// How many library entries turning the filter on removes. Counted as tiles
/// that actually disappear, so it agrees with what the user sees happen.
/// </param>
public sealed record AccountVisibilityState(
    bool AccountConfirmed, bool OwnAccountOnly, int HiddenCount, int AccountCount = 0)
{
    /// <summary>Nothing known: no confirmed account, filter off, nothing hidden.</summary>
    public static AccountVisibilityState Unknown { get; } = new(false, false, 0, 0);
}

/// <summary>
/// The account-visibility preference, as the settings panel sees it.
///
/// <para>A seam in the same spirit as <see cref="IStoreConnections"/>: the view
/// model asks a question and issues a command, and never learns that a settings
/// table or a bucket query exists.</para>
/// </summary>
public interface IAccountVisibility
{
    /// <summary>Reads the current state. Makes no network call.</summary>
    Task<AccountVisibilityState> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the preference. Reloading the library and the feed is the
    /// caller's job — this writes the fact and returns.
    /// </summary>
    Task SetOwnAccountOnlyAsync(bool ownAccountOnly, CancellationToken ct = default);
}
