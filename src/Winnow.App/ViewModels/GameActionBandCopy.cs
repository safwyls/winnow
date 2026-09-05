namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the details modal's action band overflow control.
/// The band keeps the primary action, Store page and All patch notes visible;
/// Open folder, Wrong game?, Edit details and Hide are folded behind this
/// disclosure and drawn inline beneath the band when it is open.
/// </summary>
public static class GameActionBandCopy
{
    /// <summary>Face of the disclosure control when the list is closed.</summary>
    public const string OpenLabel = "More";

    /// <summary>Face of the disclosure control while the list is open.</summary>
    public const string CloseLabel = "Close";

    /// <summary>Tooltip on the disclosure control.</summary>
    public const string OpenTooltip = "Folder, corrections and hide";
}
