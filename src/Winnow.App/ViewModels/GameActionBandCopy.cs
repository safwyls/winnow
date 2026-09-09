namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the details modal's action band (§10.3). The trigger label and
/// tooltip serve the More menu; the two no-way-in sentences serve the line
/// the header draws when there is no primary action and no outbound link,
/// stating why rather than sitting silent.
/// </summary>
public static class GameActionBandCopy
{
    /// <summary>The trigger's face — always this word, because the menu owns its open state.</summary>
    public const string OpenLabel = "More";

    /// <summary>Tooltip on the trigger.</summary>
    public const string OpenTooltip = "Store and news links, installation, folder, metadata, corrections and hide";

    /// <summary>
    /// Shown when Winnow holds a store id but no source has established
    /// whether this copy is on disk. Install state is three-valued; the
    /// third value is "nothing looked", not a quieter no (§10.3).
    /// </summary>
    public const string InstallStateUnknown =
        "Winnow has not read this copy's install state yet.";

    /// <summary>
    /// Shown when Winnow does not hold the identifier the store needs — no
    /// Steam appid, no GOG product id, or no complete Epic launch key. For
    /// Epic the three-part key comes from the launcher's local catalog files
    /// at ingest; a missing key means the local catalog did not carry the
    /// game. An uninstalled Epic game with an incomplete key is the same
    /// case: the route exists but cannot be built without the key.
    /// </summary>
    public const string NoStoreId =
        "Winnow does not yet hold the identifier this store needs to reach this game.";

    /// <summary>
    /// Maps a <see cref="NoWayIn"/> reason to the sentence the header draws, or
    /// null for <see cref="NoWayIn.None"/>. The sentence takes <c>Text</c>
    /// ink rather than <c>TextDim</c>, because it carries the fact in the
    /// way §10.2's no-rail sentence does — it is not a status line about an
    /// act the user started.
    /// </summary>
    public static string? NoWayInSentence(NoWayIn reason) => reason switch
    {
        NoWayIn.InstallStateUnknown => InstallStateUnknown,
        NoWayIn.NoStoreId => NoStoreId,
        _ => null,
    };
}
