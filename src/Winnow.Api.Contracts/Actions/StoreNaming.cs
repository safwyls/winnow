namespace Winnow.App.ViewModels;

/// <summary>
/// One vocabulary for store names, so the chip on a tile, the chip in the
/// list column, the option in the filter panel and the sentence in a launch
/// failure all spell a store the same way.
/// </summary>
public static class StoreNaming
{
    /// <summary>Display name. Known stores keep their own casing (GOG, not Gog).</summary>
    public static string Label(string store) => store.ToLowerInvariant() switch
    {
        "" => store,
        "steam" => "Steam",
        "gog" => "GOG",
        "epic" => "Epic",
        "plugin:xbox" => "Xbox",
        "plugin:psn" => "PlayStation",
        var value when value.StartsWith("plugin:", StringComparison.Ordinal) && value.Length > 7
            => string.Concat(char.ToUpperInvariant(store[7]), store[8..]),
        _ => string.Concat(char.ToUpperInvariant(store[0]), store[1..]),
    };

    /// <summary>The chip face: the display name uppercased.</summary>
    public static string Badge(string store) => Label(store).ToUpperInvariant();

    /// <summary>
    /// The first letter of the display name. The front of a tile at the
    /// 108px density floor cannot hold word-chips, so the resting mark on a
    /// multi-store tile is initials; the words arrive on hover and in the
    /// modal. The mark is therefore decorative-redundant,
    /// which §8 requires of anything the grid encodes.
    /// </summary>
    public static string Initial(string store)
    {
        var label = Label(store);
        return label.Length == 0 ? string.Empty : label[..1].ToUpperInvariant();
    }
}
