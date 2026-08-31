using Winnow.Core.Queries;
using Winnow.Core.Repositories;

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
    bool AccountConfirmed, bool OwnAccountOnly, int HiddenCount)
{
    /// <summary>Nothing known: no confirmed account, filter off, nothing hidden.</summary>
    public static AccountVisibilityState Unknown { get; } = new(false, false, 0);
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

/// <summary>
/// Implements <see cref="IAccountVisibility"/> over the settings table and the
/// derived-bucket query.
///
/// <para>Note what is NOT here: no filtering logic of any kind. The preference
/// is stored, and <c>LibraryQueryRepository</c> reads it from inside the one
/// query every surface goes through, so the grid, the rail counts, the chips,
/// the recommender and the feed narrow together. A second implementation of the
/// filter living beside the toggle that sets it is precisely how those surfaces
/// would begin to disagree.</para>
/// </summary>
public sealed class AccountVisibilityService : IAccountVisibility
{
    private readonly ISettingsRepository _settings;
    private readonly ILibraryQueryRepository _library;

    public AccountVisibilityService(ISettingsRepository settings, ILibraryQueryRepository library)
    {
        _settings = settings;
        _library = library;
    }

    /// <inheritdoc/>
    public async Task<AccountVisibilityState> GetAsync(CancellationToken ct = default)
    {
        var confirmed = SteamOwnedAccount.Clean(
            await _settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct)) is not null;

        var ownOnly = AccountScope.IsOwnOnly(
            await _settings.GetAsync(AccountScope.SettingKey, ct));

        // Not asked when there is no account to filter to. The query would
        // answer zero anyway — both its modes read the same absent reference —
        // and paying for two full library reads to be told so is a cost the
        // settings panel takes on every open.
        //
        // The non-game preference is carried through because the count has to
        // be the number of tiles that DISAPPEAR: a soundtrack the user has
        // hidden was not on screen to be hidden again, and counting it would
        // send them looking for a game that was never there.
        var hidden = 0;
        if (confirmed)
        {
            var thresholds = BucketThresholds.Default with
            {
                ShowNonGameEntries = BucketThresholds.ParseShowNonGameEntries(
                    await _settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey, ct)),
            };

            hidden = await _library.CountHiddenByAccountScopeAsync(thresholds, ct);
        }

        return new AccountVisibilityState(confirmed, ownOnly, hidden);
    }

    /// <inheritdoc/>
    public Task SetOwnAccountOnlyAsync(bool ownAccountOnly, CancellationToken ct = default)
        => _settings.SetAsync(
            AccountScope.SettingKey, AccountScope.Format(ownAccountOnly), ct);
}
