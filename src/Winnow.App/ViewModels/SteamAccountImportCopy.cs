namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Steam account-page import screen (M5). All strings
/// in one file so the transparency paragraphs and outcome messages can be reviewed
/// together. The two import routes are presented as equal peers; neither may read
/// as a fallback for the other.
/// </summary>
public static class SteamAccountImportCopy
{
    // ══ Header ═════════════════════════════════════════════════════════════

    /// <summary>Screen title, rendered in Display L. Sentence case, matching
    /// the other settings surface screen titles.</summary>
    public const string Title = "Steam purchases";

    /// <summary>
    /// Introduction under the title. Names the three facts this screen fills
    /// in and states the guarantee that no games are added and existing values
    /// are left alone.
    /// </summary>
    public const string Intro =
        "Fills in when you got each game, how you got it, and what you paid. "
        + "Attaches to games already in your library; nothing is added or overwritten.";

    /// <summary>Segment label on the settings surface. Uppercase, matching STORES and APPEARANCE.</summary>
    public const string RailRow = "PURCHASES";

    /// <summary>Segment tooltip on the settings surface. Same register as the rail tooltips, no period.</summary>
    public const string RailTooltip =
        "When you got each Steam game, how you got it, and what you paid";

    // ══ Route A — sign in ══════════════════════════════════════════════════

    /// <summary>Route A heading. Wording is fixed; the view binds it directly.</summary>
    public const string SignInRouteHeading = "Sign in inside Winnow";

    /// <summary>Button label for Route A. Also used as the automation name.</summary>
    public const string SignInRouteButton = "Sign in to Steam";

    /// <summary>
    /// Transparency paragraph read before the user acts. The button press is
    /// the consent, so this paragraph is the consent surface and must carry
    /// four facts in order: a private window opens; the user signs in
    /// themselves and Winnow never sees the password; exactly two pages are
    /// read, by name; the session is forgotten when the window closes.
    /// </summary>
    public const string SignInRouteExplanation =
        "A private browser window opens inside the app. You sign into Steam yourself, "
        + "on Steam's own page; Winnow never sees your password, and Steam Guard works "
        + "normally. It reads exactly two pages: your account licenses and your purchase "
        + "history. The session is forgotten when the window closes.";

    /// <summary>Shown while the sign-in window is open. Neutral, present tense.</summary>
    public const string SignInBusy =
        "The sign-in window is open. Sign in to Steam there; "
        + "this page updates when it finishes.";

    /// <summary>
    /// Shown when the WebView2 runtime is missing. Names the runtime and
    /// points at Route B without demoting it.
    /// </summary>
    public const string SignInUnavailable =
        "This machine does not have the WebView2 runtime, so the sign-in route is "
        + "not available. The other route reads the same two pages from files you "
        + "save yourself.";

    // ══ Route B — saved pages ══════════════════════════════════════════════

    /// <summary>Route B heading. Wording is fixed; the view binds it directly.</summary>
    public const string SavedPagesRouteHeading = "Save the pages yourself";

    /// <summary>Button label for the file picker in Route B.</summary>
    public const string SavedPagesRouteButton = "Choose saved files";

    /// <summary>
    /// Transparency paragraph for Route B. Must carry: the two pages by name
    /// and URL; that the user saves them from their own browser; that Winnow
    /// reads only the picked files; that either can be imported alone though
    /// both together are more complete.
    /// </summary>
    public const string SavedPagesRouteExplanation =
        "Sign into Steam in your own browser and open "
        + "store.steampowered.com/account/licenses/ and "
        + "store.steampowered.com/account/history/. Save each page to a file; "
        + "Ctrl+S with \"Web Page, HTML only\" is fine. Choose the saved files here. "
        + "Winnow reads only the files you pick and nothing else. Either page can be "
        + "imported on its own, though each carries different facts and both together "
        + "give a fuller result.";

    /// <summary>
    /// Tip shown in the Route B panel before the user saves, not as a
    /// scolding after. The purchase history page renders only recent
    /// transactions until the load-more control is clicked.
    /// </summary>
    public const string SavedPagesLoadMoreHint =
        "Steam's purchase history page shows only recent transactions at first. "
        + "Click \"load more transactions\" at the bottom until Steam stops offering "
        + "it, then save the page.";

    /// <summary>
    /// Tip shown in the Route B panel before the user saves. The licenses page
    /// paginates at 100 rather than loading more, so a single saved file holds
    /// only one page of the list.
    /// </summary>
    public const string SavedPagesLicensesHint =
        "Steam's licenses page shows 100 licences at a time across separate pages. "
        + "A saved file holds whichever page you were viewing; a library with more "
        + "than 100 licences needs each page saved as its own file. The sign-in route "
        + "walks all pages automatically.";

    /// <summary>Shown while the picked files are being read.</summary>
    public const string SavedPagesBusy = "Reading the saved files.";

    /// <summary>Title of the OS file-picker dialog.</summary>
    public const string FilePickerTitle = "Choose Steam account pages";

    // ══ Route A outcomes ═══════════════════════════════════════════════════

    /// <summary>
    /// Both pages were captured. Counts appear underneath, so this does not
    /// restate them.
    /// </summary>
    public const string OutcomeCaptured = "Both pages were read.";

    /// <summary>One page came back. The import is partial but still useful.</summary>
    public const string OutcomePartial =
        "One page arrived and the other did not. The import ran on what it received.";

    /// <summary>
    /// The window was closed before the pages were read. This is a neutral
    /// fact, not an error, and carries no "try again" language.
    /// </summary>
    public const string OutcomeCancelled =
        "The window was closed before the pages were read. "
        + "Nothing was imported and nothing was changed.";

    /// <summary>
    /// Nobody signed in, so Steam never rendered an account page. Neutral
    /// fact, not an error. The remedy is to sign in, not to retry blindly.
    /// </summary>
    public const string OutcomeNoSession =
        "The window closed without a sign-in. Steam shows your account pages only "
        + "after signing in, so there was nothing to read.";

    /// <summary>
    /// The session ran and produced nothing. Points at Route B without
    /// demoting it.
    /// </summary>
    public const string OutcomeFailed =
        "The session ran but did not produce usable pages. The other route reads "
        + "the same pages from files you save yourself.";

    /// <summary>
    /// Same substance as <see cref="SignInUnavailable"/>, phrased as the
    /// outcome of a button press rather than a standing state.
    /// </summary>
    public const string OutcomeUnavailable =
        "The WebView2 runtime is not installed on this machine, so the sign-in "
        + "window could not open. The other route reads the same two pages from "
        + "files you save yourself.";

    // ══ Route B outcomes ═══════════════════════════════════════════════════

    /// <summary>The file picker was dismissed without choosing anything. Neutral.</summary>
    public const string NothingPicked = "No files were chosen.";

    /// <summary>
    /// Files were picked but none is one of the two account pages. Says what
    /// Winnow looked for so the user knows which files to try.
    /// </summary>
    public const string NothingRecognized =
        "None of the chosen files contains a Steam account licenses page or a "
        + "purchase history page. Winnow identifies pages by their content, not "
        + "by filename.";

    // ══ Truncation notices ═════════════════════════════════════════════════

    /// <summary>
    /// The saved purchase history file held only the first page of
    /// transactions. Tells the user how to capture the rest and that Route A
    /// does it automatically.
    /// </summary>
    public const string HistoryTruncatedNotice =
        "This file holds only the first page of purchase history. Clicking "
        + "\"load more transactions\" on the page before saving captures the rest; "
        + "the sign-in route does that clicking itself.";

    /// <summary>
    /// The saved licenses file held one page of a paginated list. Notes that
    /// Route A walks all pages.
    /// </summary>
    public const string LicensesTruncatedNotice =
        "This file holds one page of the licenses list. The sign-in route walks "
        + "all pages automatically.";

    /// <summary>
    /// The sign-in route hit its own safety ceiling on "load more transactions"
    /// rather than reaching the end of the list. Rare. Does not tell the user to
    /// click load-more themselves (that is Route B's advice, not this route's).
    /// Amber register.
    /// </summary>
    public const string SignInHistoryReachedCapNotice =
        "The sign-in route stopped loading purchase history at its safety ceiling, "
        + "so this run may not have seen every transaction. Running it again is safe; "
        + "rows already imported are not changed.";

    /// <summary>
    /// The sign-in route hit its own safety ceiling on licences pages rather
    /// than reaching the last page. Same shape and register as
    /// <see cref="SignInHistoryReachedCapNotice"/>: Winnow stopped, not Steam;
    /// re-running is safe; existing rows are unchanged. Amber register.
    /// </summary>
    public const string SignInLicensesReachedCapNotice =
        "The sign-in route stopped loading licences pages at its safety ceiling, "
        + "so this run may not have seen every licence. Running it again is safe; "
        + "rows already imported are not changed.";

    /// <summary>
    /// The sign-in route finished its purchase-history walk but did not capture
    /// everything: either a step stopped producing new rows, or Steam's page
    /// still indicated more existed. Amber register.
    /// </summary>
    public const string SignInHistoryIncompleteNotice =
        "The purchase history walk finished but did not capture every transaction. "
        + "Running it again is safe; rows already imported are not changed.";

    /// <summary>
    /// The sign-in route's licences walk could not fetch every page. Amber register.
    /// </summary>
    public const string SignInLicensesIncompleteNotice =
        "The licences walk could not fetch every page, so this run did not see the "
        + "whole licences list. Running it again is safe; rows already imported are "
        + "not changed.";

    // ══ Parse failures (Amber problem notes) ═══════════════════════════════

    /// <summary>
    /// The file was read but does not match the licenses page structure. A
    /// parser reason string is appended by the view model, so the sentence
    /// ends cleanly before one.
    /// </summary>
    public const string LicensesNotRecognized =
        "This file was read but does not look like the account licenses page.";

    /// <summary>
    /// The file was read but does not match the purchase history structure.
    /// A parser reason string is appended by the view model.
    /// </summary>
    public const string HistoryNotRecognized =
        "This file was read but does not look like the purchase history page.";

    // ══ Result block ═══════════════════════════════════════════════════════

    /// <summary>Heading over the count rows. Uppercase label register.</summary>
    public const string ResultHeading = "RESULTS";

    /// <summary>Heading over the skipped-row breakdown. Uppercase label register.</summary>
    public const string SkippedHeading = "SKIPPED";

    /// <summary>
    /// Every row was read and none filled in a new fact. Not a failure; the
    /// library either already had these values or nothing matched.
    /// </summary>
    public const string NothingApplied =
        "Every row was read and none of them filled in a new value. The library "
        + "already had these facts, or nothing matched a game in it.";

    /// <summary>
    /// Shown under the results table when the reported and found licence counts
    /// differ. Informational, not a warning: Steam's paginator routinely
    /// advertises a higher total than the rows it renders, so the difference
    /// does not indicate missed licences.
    /// </summary>
    public const string LicensesCountMismatchNote =
        "Steam's licences page advertises a total that is larger than the number "
        + "of rows it renders. The difference is in Steam's own counting, not "
        + "licences that were missed.";

    // ══ Count labels (UPPERCASE, left of a number) ═════════════════════════

    /// <summary>Rows read from the licenses page.</summary>
    public const string LabelLicencesFound = "LICENCES FOUND";

    /// <summary>Total advertised by Steam's own paginator, which is larger than
    /// the number of rows Steam actually renders.</summary>
    public const string LabelLicencesReported = "LICENCES REPORTED";

    /// <summary>Rows read from the purchase history page.</summary>
    public const string LabelPurchasesFound = "PURCHASES FOUND";

    /// <summary>Licence rows that resolved to a game in the library.</summary>
    public const string LabelMatched = "LICENCES MATCHED";

    /// <summary>Purchase rows that matched a game and carried a usable price.</summary>
    public const string LabelPricesMatched = "PRICES MATCHED";

    /// <summary>Ownership rows that gained at least one new fact. This is the number that matters.</summary>
    public const string LabelGamesUpdated = "GAMES UPDATED";

    /// <summary>Matched, but every fact on offer was already present.</summary>
    public const string LabelAlreadyComplete = "ALREADY COMPLETE";

    // ══ Skip labels (sentence case, left of a number) ══════════════════════

    /// <summary>One price covering several games with no way to split it.</summary>
    public const string SkipBundles = "Bundle purchases";

    /// <summary>Money the user did not ultimately spend.</summary>
    public const string SkipRefunds = "Refunded purchases";

    /// <summary>Gifts bought for others, and purchases inside a game rather than of one.</summary>
    public const string SkipGiftsAndInGame = "Gifts and in-game purchases";

    /// <summary>Wallet top-ups and gift-card redemptions, not products.</summary>
    public const string SkipWallet = "Wallet and gift cards";

    /// <summary>Two owned games normalise to the same name, so neither is touched.</summary>
    public const string SkipAmbiguous = "Ambiguous titles";

    /// <summary>Row names something not in the library (DLC, a package name, a delisted title).</summary>
    public const string SkipNoMatch = "No match in library";

    /// <summary>Two rows resolved to the same game with different facts.</summary>
    public const string SkipDisagreed = "Disagreeing rows";

    // ══ Picked-file outcome labels (UPPERCASE, beside a filename) ══════════

    /// <summary>The file was read successfully.</summary>
    public const string FileLoaded = "LOADED";

    /// <summary>The path no longer exists on disk.</summary>
    public const string FileNotFound = "NOT FOUND";

    /// <summary>The file exists but could not be read.</summary>
    public const string FileUnreadable = "UNREADABLE";

    /// <summary>The file was read but is not one of the two account pages.</summary>
    public const string FileNotRecognized = "NOT RECOGNIZED";

    /// <summary>A file of this page kind was already read from an earlier pick.</summary>
    public const string FileDuplicate = "ALREADY READ";

    /// <summary>
    /// Shown when at least one picked file was labelled <see cref="FileDuplicate"/>.
    /// Explains that Winnow reads the first file of each page kind and does not
    /// read the others, and that further pages of the licences list need the
    /// sign-in route.
    /// </summary>
    public const string DuplicatePagesNotice =
        "Winnow read the first file of each page kind and did not read the others. "
        + "If those were later pages of the licences list, only the first page was "
        + "imported; the sign-in route reads every page automatically.";
}
