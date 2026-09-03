namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Steam connection section of the Platforms screen.
/// All strings in one file so the two connection methods (sign-in and API key)
/// and their costs can be reviewed together. Neither method may read as a
/// fallback for the other.
/// </summary>
public static class SteamConnectionCopy
{
    // ══ Section framing ════════════════════════════════════════════════════

    /// <summary>Section label above the two connection methods. Uppercase,
    /// matching LOCAL FILES and STEAM ACCOUNTS on the same card.</summary>
    public const string SectionLabel = "WEB API";

    /// <summary>
    /// Introduction under the section label. Names the two methods, states
    /// both work, and gives the one-line difference between them so the user
    /// can choose before reading either card.
    /// </summary>
    public const string SectionIntro =
        "Two ways to connect to Steam's Web API. Both work, and you only "
        + "need one. The sign-in also tells Winnow which account is yours "
        + "and can read your purchase history. The key never expires.";

    /// <summary>Label for the local-files row on the Steam card. Uppercase,
    /// matching the other section labels on this card.</summary>
    public const string LocalFilesLabel = "LOCAL FILES";

    /// <summary>Description of what local files provide. Same sentence shape
    /// as the Epic and GOG local-files lines on this screen.</summary>
    public const string LocalFiles =
        "Always on. Reads installed and played games, playtime and "
        + "last-played dates from Steam's local files.";

    /// <summary>
    /// Shown when neither credential is present. States what the user is
    /// missing without scolding; the two remedies are visible on the same
    /// card.
    /// </summary>
    public const string NothingConnectedCost =
        "Local files cover games installed or played on this PC. Games "
        + "you own but have never touched on this machine are not in "
        + "your library yet.";

    /// <summary>Shown when at least one credential is present. States what
    /// the connection added over local files alone.</summary>
    public const string ConnectedAdds =
        "Winnow also reads your full list of owned games from Steam, "
        + "including titles never installed on this PC.";

    // ══ Combined status pill ═══════════════════════════════════════════════

    /// <summary>Status pill when no credential is held. Uppercase, 10px,
    /// letterspaced.</summary>
    public const string StatusNoConnection = "NO CONNECTION";

    /// <summary>Status pill when only an API key is held.</summary>
    public const string StatusKeySet = "KEY SET";

    /// <summary>Status pill when only a sign-in session is live.</summary>
    public const string StatusSignedIn = "SIGNED IN";

    /// <summary>Status pill when both credentials are held.</summary>
    public const string StatusSignedInAndKeySet = "SIGNED IN, KEY SET";

    /// <summary>Status pill when the sign-in's access token has expired but
    /// a refresh token exists. Wins over the key's presence because the user
    /// needs to act.</summary>
    public const string StatusSignInNeedsRenewing = "SIGN-IN NEEDS RENEWING";

    /// <summary>Status pill when the sign-in is dead and only a fresh
    /// attempt recovers it.</summary>
    public const string StatusSignInExpired = "SIGN-IN EXPIRED";

    // ══ Session health ═════════════════════════════════════════════════════

    /// <summary>
    /// Session-health line when no sign-in exists. Must read as an ordinary
    /// state, not a fault, because an API-key-only user sees it too.
    /// </summary>
    public const string HealthNotSignedIn = "No sign-in session is stored.";

    /// <summary>Session-health line when the sign-in is working. Brief; the
    /// status pill already carries the state.</summary>
    public const string HealthLive = "The sign-in is working.";

    /// <summary>
    /// Session-health line when the access token is nearing expiry. Neutral
    /// register; renewal is automatic, so this is the routine steady state
    /// for any signed-in user.
    /// </summary>
    public const string HealthRenewalDue =
        "The access token is due for renewal. Winnow renews it "
        + "automatically in the background.";

    /// <summary>
    /// Session-health line when renewal was attempted and failed. Amber
    /// register. Names two remedies: a fresh sign-in and an API key.
    /// </summary>
    public const string HealthRenewalFailing =
        "Renewal was attempted and did not succeed. Signing in again "
        + "will restore it. An API key, if set, keeps scheduled updates "
        + "running regardless.";

    /// <summary>Session-health line when the sign-in is dead. Amber
    /// register. States that an API key is independent.</summary>
    public const string HealthExpired =
        "The sign-in has expired and cannot be used. Only a fresh "
        + "sign-in can recover it. An API key, if set, is unaffected.";

    /// <summary>
    /// Session-health line when the machine cannot encrypt the session.
    /// Amber register. Mirrors the Epic card's equivalent phrasing.
    /// </summary>
    public const string HealthNotPersisted =
        "The sign-in is working for this session, but this machine "
        + "cannot encrypt it, so Winnow did not save it to disk. You "
        + "will need to sign in again after a restart.";

    /// <summary>
    /// Shown only when both credentials are held. States which credential
    /// serves which purpose. This sentence is the product owner's decision
    /// and the whole reason the dual state is visible to the user.
    /// </summary>
    public const string BothCredentials =
        "Scheduled updates use the API key, because keys do not expire. "
        + "The sign-in is used when you ask Winnow to do something.";

    // ══ Method A — sign in ═════════════════════════════════════════════════

    /// <summary>Method A heading. Sentence case, matching the API key
    /// heading's weight.</summary>
    public const string SignInHeading = "Sign in to Steam";

    /// <summary>
    /// What the sign-in provides: immediate account identification, optional
    /// purchase-history access, and no key registration. Three facts in
    /// three sentences.
    /// </summary>
    public const string SignInGives =
        "Winnow learns which Steam account is yours the moment you sign "
        + "in, so the account filter works straight away with no extra "
        + "requests. Your purchase history can be read in the same "
        + "session if you allow it. There is no key to register on "
        + "Steam's website.";

    /// <summary>
    /// What the sign-in gives up. States the expiry, that renewal is
    /// automatic but untested against live servers, and that an API key
    /// is the unconditionally reliable alternative.
    /// </summary>
    public const string SignInCosts =
        "The credential lasts about a day. Winnow renews it "
        + "automatically, but renewal has not been tested against "
        + "Valve's live servers and may not work. Signing in to "
        + "Steam elsewhere can also invalidate it. If renewal "
        + "fails, a fresh sign-in is the only fix; an API key does "
        + "not expire and needs no renewal.";

    /// <summary>Button label for a first-time sign-in. Matches the Epic
    /// card's "Sign in to Epic".</summary>
    public const string SignInButton = "Sign in to Steam";

    /// <summary>Button label when a session exists but needs renewing or
    /// has expired. Matches the Epic card's "Sign in again".</summary>
    public const string SignInAgainButton = "Sign in again";

    /// <summary>Button label shown beside the busy message while the
    /// sign-in window is open.</summary>
    public const string SignInCancelButton = "Cancel";

    /// <summary>Shown while the sign-in window is open. Same shape as
    /// SteamAccountImportCopy.SignInBusy.</summary>
    public const string SignInBusy =
        "The sign-in window is open. Sign in to Steam there; this page "
        + "updates when it finishes.";

    /// <summary>
    /// Shown when WebView2 is missing. Points at the API key as a working
    /// alternative without demoting it.
    /// </summary>
    public const string SignInUnavailable =
        "This machine does not have the WebView2 runtime, so the "
        + "sign-in window cannot open. A Web API key works without "
        + "a browser.";

    /// <summary>Label above the signed-in account's SteamID64. Uppercase
    /// register, matching the card's other field labels.</summary>
    public const string SignedInAsLabel = "SIGNED IN AS";

    /// <summary>
    /// Button label. Paired with SignOutExplanation, which explains what
    /// signing out removes and what it does not.
    /// </summary>
    public const string SignOutButton = "Sign out";

    /// <summary>
    /// States what signing out deletes and what it does not. Names the
    /// consequence for the account filter because a user who loses the
    /// filter after signing out needs to have been told.
    /// </summary>
    public const string SignOutExplanation =
        "Signing out deletes the stored session. Your Steam games stay; "
        + "they come from local files. An API key, if set, keeps "
        + "working. Winnow also forgets which account the sign-in "
        + "identified as yours, so the account filter turns off unless "
        + "a key has already confirmed it.";

    /// <summary>
    /// Shown after a sign-in that recorded the account. Names the account
    /// filter's availability because that is the user-visible effect.
    /// </summary>
    public const string AccountConfirmed =
        "Winnow now knows which Steam account is yours. The account "
        + "filter is available; no import needed.";

    /// <summary>Shown after a sign-in that did not record the account.
    /// Rare; names only the sign-in as the remedy.</summary>
    public const string AccountNotConfirmed =
        "The sign-in worked, but Winnow did not record which account "
        + "is yours. The account filter is still unavailable. Signing "
        + "in again should resolve this.";

    // ══ Purchase-history permission (acceptance criterion 2) ═══════════════

    /// <summary>Checkbox label for the purchase-history permission. First
    /// person so it reads as a consent being given, not a feature being
    /// advertised.</summary>
    public const string PurchaseHistoryPermissionLabel =
        "Also read my purchase history";

    /// <summary>
    /// Explanation under the permission checkbox. Names the two pages by
    /// the same names SteamAccountImportCopy uses and states that unticking
    /// is a complete answer. The sign-in still delivers identity and
    /// playtime with this unticked.
    /// </summary>
    public const string PurchaseHistoryPermissionExplanation =
        "Reads your account licenses page and your purchase history "
        + "page inside the same private window, filling in when you got "
        + "each game, how you got it and what you paid. Leaving this "
        + "unticked is a complete answer; the sign-in still works for "
        + "account identity and playtime, and those pages are never "
        + "opened.";

    // ══ Sign-in outcomes ═══════════════════════════════════════════════════

    /// <summary>Outcome when the sign-in succeeded. Brief; the state above
    /// carries the detail.</summary>
    public const string OutcomeSignedIn = "Signed in.";

    /// <summary>
    /// Outcome when the sign-in completed but produced no credential. Amber
    /// register. Retrying is safe.
    /// </summary>
    public const string OutcomeNoToken =
        "The sign-in completed, but no Steam page handed Winnow a "
        + "credential, so there is no session. Trying again is safe.";

    /// <summary>Outcome when the window closed without a sign-in. Neutral
    /// fact, not an error.</summary>
    public const string OutcomeNotSignedIn =
        "The window closed without anyone signing in. Nothing was stored.";

    /// <summary>
    /// Outcome when the page and credential named different accounts. Amber
    /// register. Named as a safety measure so it does not read as a bug.
    /// </summary>
    public const string OutcomeIdentityMismatch =
        "The page and the credential named different Steam accounts, "
        + "so Winnow refused the session and stored nothing. This is a "
        + "safety measure; trying again is safe.";

    /// <summary>Outcome when the window was closed early. Neutral fact.
    /// Same shape as SteamAccountImportCopy.OutcomeCancelled.</summary>
    public const string OutcomeCancelled =
        "The window was closed before it finished. Nothing was changed.";

    /// <summary>Outcome variant of SignInUnavailable, phrased as what just
    /// happened rather than as a standing state.</summary>
    public const string OutcomeUnavailable =
        "The WebView2 runtime is not installed on this machine, so the "
        + "sign-in window could not open. A Web API key works without "
        + "a browser.";

    /// <summary>Outcome when the sign-in broke. Amber register. States
    /// that a retry is safe and an API key is unaffected.</summary>
    public const string OutcomeFailed =
        "The sign-in ran and did not succeed. Trying again is safe; an "
        + "API key, if set, is unaffected.";

    /// <summary>
    /// Outcome when Steam issued no refresh token. Not a failure; names the
    /// cause (the "remember me" checkbox on Steam's own form) and the
    /// consequence (about a day, then repeat).
    /// </summary>
    public const string OutcomeNoRefreshToken =
        "Signed in and working. Steam did not issue anything that can "
        + "renew this session, so it lasts about a day and then needs "
        + "repeating. Steam issues that only when \"remember me\" is "
        + "ticked on its own login form, which Winnow does not touch.";

    // ══ Method B — Web API key ═════════════════════════════════════════════

    /// <summary>Method B heading. Sentence case, same visual weight as the
    /// sign-in heading.</summary>
    public const string ApiKeyHeading = "Web API key";

    /// <summary>What the API key provides: no expiry, no renewal, no
    /// browser. Two sentences, two benefits.</summary>
    public const string ApiKeyGives =
        "The key never expires and needs no renewal, so scheduled "
        + "background updates keep working indefinitely. There is no "
        + "browser sign-in; you paste a key from Steam's own website.";

    /// <summary>What the API key gives up: no account identity until an
    /// import, no purchase history. Same honesty as SignInCosts.</summary>
    public const string ApiKeyCosts =
        "A key does not identify which Steam account it belongs to, so "
        + "the account filter is unavailable until a Steam import "
        + "confirms it. A key cannot read your purchase history.";

    /// <summary>Label above the key input field. Uppercase register,
    /// matching the card's other field labels.</summary>
    public const string ApiKeyFieldLabel = "STEAM WEB API KEY";

    /// <summary>Placeholder text inside the masked input field.</summary>
    public const string ApiKeyWatermark = "Paste your key";

    /// <summary>Save button label. The key takes effect immediately; the
    /// notice confirms it.</summary>
    public const string ApiKeySaveButton = "Save";

    /// <summary>Clear button label. Disabled when the key came from the
    /// environment, because this screen cannot remove it.</summary>
    public const string ApiKeyClearButton = "Clear";

    /// <summary>Button that opens Steam's key registration page in the
    /// user's browser.</summary>
    public const string ApiKeyGetButton = "Get a key";

    /// <summary>The page Steam issues keys on. Opened through the shared URI dispatcher.</summary>
    public const string ApiKeyRegistrationUrl = "https://steamcommunity.com/dev/apikey";

    /// <summary>Status line when no key is stored.</summary>
    public const string ApiKeyNotSet = "No API key is set.";

    /// <summary>Status line when a key is stored and managed by this
    /// screen.</summary>
    public const string ApiKeySet =
        "A key is stored on this machine. It can be replaced or "
        + "cleared here.";

    /// <summary>
    /// Status line when the key came from the environment. Says all three
    /// parts: where it came from, that saving here takes precedence, and
    /// that clearing here cannot remove it.
    /// </summary>
    public const string ApiKeyFromEnvironment =
        "The key in use came from the Steam__ApiKey environment "
        + "variable or appsettings.local.json beside the executable. "
        + "Saving a key here takes precedence over it. Clearing here "
        + "cannot remove it.";

    /// <summary>Notice after saving. States that it is in use with no
    /// restart, so the user knows the change took effect.</summary>
    public const string ApiKeySaved =
        "The key is saved and in use. No restart needed.";

    /// <summary>Notice after clearing.</summary>
    public const string ApiKeyCleared = "The key has been removed.";

    /// <summary>
    /// Notice when the browser could not be opened. Includes the literal
    /// URL so the user can navigate manually.
    /// </summary>
    public const string ApiKeyOpenFailed =
        "The browser could not be opened. Register a key at "
        + "https://steamcommunity.com/dev/apikey.";

    // ══ Account scope, three branches ══════════════════════════════════════

    /// <summary>Why the account filter is disabled when only a key is set.
    /// Names the automatic fix at the next import.</summary>
    public const string AccountScopeBlockedKeyOnly =
        "Your API key is set, but Winnow has not confirmed which "
        + "account it belongs to yet. This happens automatically "
        + "during the next Steam import.";

    /// <summary>Why the account filter is disabled when nothing is
    /// connected. Names both remedies evenly.</summary>
    public const string AccountScopeBlockedNothingConnected =
        "Winnow does not know which Steam account is yours yet. "
        + "Signing in tells it immediately; an API key finds out at "
        + "the next Steam import.";

    /// <summary>Why the account filter is disabled despite a sign-in.
    /// Rare; names only the sign-in as the remedy.</summary>
    public const string AccountScopeBlockedSignedIn =
        "The sign-in did not record which account is yours. Signing "
        + "in again should resolve this.";

    // ══ TASK-61 — the condensed top level ══════════════════════════════════

    /// <summary>Tooltip on the PLATFORMS segment button. Sentence fragment, no
    /// trailing period, same register as the Appearance segment tooltip.</summary>
    public const string SegmentTooltip =
        "Where your library comes from, what each platform cannot know, and Steam purchase history";

    /// <summary>Terse state phrase for local files. Always available, no setup
    /// required, so this never changes.</summary>
    public const string StateLocalAlwaysOn = "Always on";

    /// <summary>Summary line under the WEB API section label when no credential
    /// is held. States the two-method choice so the user knows both exist before
    /// opening either.</summary>
    public const string SectionSummaryNothing = "Nothing connected. You only need one of these.";

    /// <summary>Summary line under the WEB API section label when at least one
    /// credential is held. Carries the same two-method reminder.</summary>
    public const string SectionSummaryConnected = "Connected. You only need one of these.";

    /// <summary>Terse state for a live sign-in session. Compressed form of
    /// <see cref="HealthLive"/>.</summary>
    public const string StateSignInLive = "Working";

    /// <summary>Terse state for an access token due for renewal. Compressed
    /// form of <see cref="HealthRenewalDue"/>; must not read as a fault,
    /// because automatic renewal is the routine steady state.</summary>
    public const string StateSignInRenewalDue = "Renewing automatically";

    /// <summary>Terse state for a renewal that was attempted and failed.
    /// Compressed form of <see cref="HealthRenewalFailing"/>. Stays at the top
    /// level with the Amber treatment (ROADMAP §4.7 condition 8).</summary>
    public const string StateSignInRenewalFailing = "Renewal failing";

    /// <summary>Terse state for a dead sign-in. Compressed form of
    /// <see cref="HealthExpired"/>. Stays at the top level with the Amber
    /// treatment.</summary>
    public const string StateSignInExpired = "Expired";

    /// <summary>Terse state for a sign-in that works now but was not written to
    /// disk. Compressed form of <see cref="HealthNotPersisted"/>; will need
    /// repeating after a restart.</summary>
    public const string StateSignInNotPersisted = "Working, not saved";

    /// <summary>Terse state when no sign-in session exists. Compressed form of
    /// <see cref="HealthNotSignedIn"/>; reads as an ordinary state, not a
    /// fault.</summary>
    public const string StateSignInNone = "Not signed in";

    /// <summary>Terse state when no API key is stored.</summary>
    public const string StateApiKeyNotSet = "Not set";

    /// <summary>Terse state when an API key is stored and managed by this
    /// screen.</summary>
    public const string StateApiKeySet = "Set";

    /// <summary>Terse state when the API key came from outside Winnow. Carries
    /// the consequence (cannot be cleared here) because the Clear button beside
    /// it is disabled and the full explanation is inside the disclosure.</summary>
    public const string StateApiKeyExternal = "Set outside Winnow, can't be cleared here";

    /// <summary>Label all four disclosure toggles take when open. Read as
    /// "close this panel".</summary>
    public const string DisclosureHide = "Hide";

    /// <summary>Disclosure toggle label: opens what local files cover and what
    /// is missing while nothing is connected.</summary>
    public const string DisclosureLocalFiles = "What local files cover";

    /// <summary>Disclosure toggle label: opens the two-method comparison and,
    /// when both credentials are held, which one does the scheduled work.</summary>
    public const string DisclosureMethods = "Which one should I use?";

    /// <summary>Disclosure toggle label: opens what signing in gives, what it
    /// costs, the calm health sentence, the identified account, when the token
    /// expires, the purchase-history permission, and what signing out
    /// removes.</summary>
    public const string DisclosureSignIn = "What signing in gives, and what it costs";

    /// <summary>Disclosure toggle label: opens what a key gives, what it costs,
    /// and the full sentence for whichever key state holds.</summary>
    public const string DisclosureApiKey = "What a key gives, and what it costs";

    // ══ TASK-59 — the folded purchase import ═══════════════════════════════

    /// <summary>Section label above the two import routes on the Steam card.
    /// Uppercase, matching LOCAL FILES, WEB API and STEAM ACCOUNTS.</summary>
    public const string PurchaseSectionLabel = "PURCHASE HISTORY";
}
