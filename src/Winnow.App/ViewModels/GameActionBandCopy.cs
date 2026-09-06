namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the details modal's action band trigger. Play, Store page and
/// All patch notes stay on the strip; Open folder, Wrong game?, Edit details
/// and Hide fold behind the menu this trigger opens.
/// </summary>
public static class GameActionBandCopy
{
    /// <summary>The trigger's face — always this word, because the menu owns its open state.</summary>
    public const string OpenLabel = "More";

    /// <summary>Tooltip on the trigger.</summary>
    public const string OpenTooltip = "Folder, metadata, corrections and hide";
}
