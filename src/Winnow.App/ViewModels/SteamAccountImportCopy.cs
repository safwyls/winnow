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

    /// <summary>
    /// Lede under the PURCHASE HISTORY label. Names what the import fills in
    /// and the guarantee that nothing is added or overwritten.
    /// </summary>
    public const string Intro =
        "Fills in when you got each game and what you paid. Nothing is added or overwritten.";

    /// <summary>Closing label on this section's disclosure toggle. The same
    /// string as <see cref="SteamConnectionCopy.DisclosureHide"/>, because the
    /// two sit on one card.</summary>
    public const string DisclosureHide = "Hide";

    /// <summary>Disclosure toggle label: opens the two hints about saving
    /// Steam's pages before picking them. Advice for the step before the
    /// button, which is why it sits behind a toggle.</summary>
    public const string DisclosureSavedPagesHints = "Before you save the pages";

    // ══ Route A — sign in ══════════════════════════════════════════════════

    /// <summary>Route A heading. Wording is fixed; the view binds it directly.</summary>
    public const string SignInRouteHeading = "Sign in inside Winnow";

    /// <summary>Button label for Route A. Also used as the automation name.</summary>
    public const string SignInRouteButton = "Sign in to Steam";

    /// <summary>
    /// Consent surface before the sign-in button. Carries four facts: private
    /// window, user signs in, two named pages, session forgotten on close.
    /// </summary>
    public const string SignInRouteExplanation =
        "A private window opens inside the app. You sign in yourself; Winnow never "
        + "sees your password. It reads exactly two pages: your account licenses and "
        + "your purchase history. The session is forgotten when the window closes.";

    /// <summary>Shown while the sign-in window is open.</summary>
    public const string SignInBusy = "Sign in to Steam in the window that opened.";

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
    /// Consent and instruction surface for Route B. Names the two pages by
    /// URL; Winnow reads only the picked files.
    /// </summary>
    public const string SavedPagesRouteExplanation =
        "Sign into Steam in your own browser and open "
        + "store.steampowered.com/account/licenses/ and "
        + "store.steampowered.com/account/history/. Save each page to a file, "
        + "then choose the saved files here. Winnow reads only the files you pick.";

    /// <summary>Tip for Route B: click load-more before saving the history page.</summary>
    public const string SavedPagesLoadMoreHint =
        "Click \"load more transactions\" until Steam stops offering it, then save the page.";

    /// <summary>Tip for Route B: the licenses page paginates at 100, so each
    /// page needs its own file.</summary>
    public const string SavedPagesLicensesHint =
        "The licenses page paginates at 100. Each page needs its own saved file.";

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

    /// <summary>The saved history file held only the first page. Tells the
    /// user to click load-more before saving.</summary>
    public const string HistoryTruncatedNotice =
        "This file holds only the first page of purchase history. "
        + "Click \"load more transactions\" before saving to capture the rest.";

    /// <summary>The saved licenses file held one page of a paginated list.</summary>
    public const string LicensesTruncatedNotice =
        "This file holds one page of the licenses list.";

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

    /// <summary>Every row was read and nothing new was filled in.</summary>
    public const string NothingApplied =
        "Every row was read but nothing new was filled in.";

    /// <summary>Shown when reported and found licence counts differ. The
    /// difference is Steam's counting.</summary>
    public const string LicensesCountMismatchNote =
        "Steam reports more licences than it shows. The difference is Steam's own counting, not missed licences.";

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

    /// <summary>Shown when at least one picked file was labelled
    /// <see cref="FileDuplicate"/>. Only the first file of each page kind
    /// is read.</summary>
    public const string DuplicatePagesNotice =
        "Only the first file of each page kind was read.";
}
