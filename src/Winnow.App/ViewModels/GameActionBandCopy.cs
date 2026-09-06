namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the details modal's action band (§10.3). The trigger label and
/// tooltip serve the More menu; the three no-way-in sentences serve the line
/// Band 3 draws when there is no primary action and no outbound link,
/// stating why rather than sitting silent.
/// </summary>
public static class GameActionBandCopy
{
    /// <summary>The trigger's face — always this word, because the menu owns its open state.</summary>
    public const string OpenLabel = "More";

    /// <summary>Tooltip on the trigger.</summary>
    public const string OpenTooltip = "Folder, metadata, corrections and hide";

    /// <summary>
    /// Shown when Winnow holds a store id but no source has established
    /// whether this copy is on disk. Install state is three-valued; the
    /// third value is "nothing looked", not a quieter no (§10.3).
    /// </summary>
    public const string InstallStateUnknown =
        "Winnow has not read this copy's install state yet.";

    /// <summary>
    /// Shown when Winnow does not hold the identifier the store needs —
    /// no Steam appid, no GOG product id, or no Epic launch key. For Epic
    /// the key arrives via a background catalogue backfill, so this is
    /// often "not yet" rather than "never".
    /// </summary>
    public const string NoStoreId =
        "Winnow does not yet hold the identifier this store needs to reach this game.";

    /// <summary>
    /// Shown for an Epic game that is owned and not installed. The Epic
    /// Games Launcher carries no verified install route (verified
    /// 2026-09-05, build 20.2.9), and Winnow does not ship an unverified
    /// URI (§10.3). The sentence names Epic and tells the user where to go.
    /// </summary>
    public const string EpicHasNoInstallRoute =
        "The Epic Games Launcher offers Winnow no way to start an install, so this game needs to be installed from Epic directly.";

    /// <summary>
    /// Maps a <see cref="NoWayIn"/> reason to the sentence Band 3 draws, or
    /// null for <see cref="NoWayIn.None"/>. The sentence takes <c>Text</c>
    /// ink rather than <c>TextDim</c>, because it carries the fact in the
    /// way §10.2's no-rail sentence does — it is not a status line about an
    /// act the user started.
    /// </summary>
    public static string? NoWayInSentence(NoWayIn reason) => reason switch
    {
        NoWayIn.InstallStateUnknown => InstallStateUnknown,
        NoWayIn.NoStoreId => NoStoreId,
        NoWayIn.NoInstallRoute => EpicHasNoInstallRoute,
        _ => null,
    };
}
