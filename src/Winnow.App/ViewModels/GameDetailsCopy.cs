namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the details modal (§10). Every string here is either
/// an accessible group name, a label or a heading; each is constant and read
/// from one place.
/// </summary>
public static class GameDetailsCopy
{
    /// <summary>Accessible group name for Band 1: title, year, publisher, reception and chips.</summary>
    public const string IdentityGroupName = "What this is";

    /// <summary>Accessible group name for Band 2: hours played, the axis, and the sentences under it.</summary>
    public const string HistoryGroupName = "Your history";

    /// <summary>Accessible group name for Band 3: the primary action, outbound links, More and refetch status.</summary>
    public const string ActionsGroupName = "Actions";

    /// <summary>Accessible name for the modal's close button, beside the title.</summary>
    public const string CloseAutomationName = "Close this game";

    /// <summary>Value label in the object column, in the idiom of its neighbours STEAM APPID and ON DISK.</summary>
    public const string AcquiredLabel = "ACQUIRED";

    /// <summary>Section heading for the summary and screenshots in Band 4.</summary>
    public const string AboutHeading = "ABOUT";

    /// <summary>
    /// Section heading for the update list. Constant whether or not anything
    /// landed since the last session. SINCE YOU PLAYED is Band 2's own rail
    /// label; one modal was saying the same words about two different things,
    /// so the list took a name of its own.
    /// </summary>
    public const string UpdatesHeading = "UPDATES";

    public const string JournalHeading = "JOURNAL";

    public const string JournalEmptyPromptOn = "No notes yet. After you play, Winnow will ask how it went.";

    public const string JournalEmptyPromptOff = "Journal prompts are off. Turn them on in Display preferences after a game.";

    public const string JournalEmptyEditProblem = "Add a note or rating, or delete this entry.";

    public const string JournalSaveProblem = "Couldn't save that. Your changes are still here — try again.";

    public const string JournalDeleteProblem = "Couldn't delete that. Your note is still here — try again.";

    /// <summary>
    /// The launch button's accessible name: the action and the game's title,
    /// e.g. "Install Empyrion: Galactic Survival".
    /// </summary>
    public static string LaunchAutomationName(string action, string title) => $"{action} {title}";
}
