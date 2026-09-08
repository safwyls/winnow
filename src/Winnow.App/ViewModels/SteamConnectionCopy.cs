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

    /// <summary>Introduction under the section label. States two methods,
    /// only one needed.</summary>
    public const string SectionIntro =
        "Two ways to connect to Steam's Web API. You only need one.";

    /// <summary>Label for the local-files row on the Steam card. Uppercase,
    /// matching the other section labels on this card.</summary>
    public const string LocalFilesLabel = "LOCAL FILES";

    /// <summary>What local files provide: always on, playtime and
    /// last-played from Steam's local files.</summary>
    public const string LocalFiles =
        "Always on. Reads playtime and last-played from Steam's local files.";

    /// <summary>Shown when neither credential is present. States what is
    /// missing without a connection.</summary>
    public const string NothingConnectedCost =
        "Games never touched on this PC are not in your library yet.";

    /// <summary>Shown when at least one credential is present. States what
    /// the connection added.</summary>
    public const string ConnectedAdds =
        "Adds games never installed on this PC.";

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

    /// <summary>Session-health line when the access token is nearing expiry.
    /// States that renewal is automatic.</summary>
    public const string HealthRenewalDue = "Renews automatically.";

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

    /// <summary>Shown only when both credentials are held. Names the key as
    /// the scheduled-update credential and why: keys do not expire.</summary>
    public const string BothCredentials =
        "Scheduled updates use the API key because keys do not expire.";

    // ══ Method A — sign in ═════════════════════════════════════════════════

    /// <summary>Method A heading. Sentence case, matching the API key
    /// heading's weight.</summary>
    public const string SignInHeading = "Sign in to Steam";
    public const string SignedInHeading = "Signed in";

    /// <summary>What the sign-in provides: account identification and
    /// optional purchase-history access.</summary>
    public const string SignInGives =
        "Identifies your account and can read your purchase history.";

    /// <summary>What the sign-in gives up: short-lived credential, automatic
    /// renewal that may not work, and the API-key alternative.</summary>
    public const string SignInCosts =
        "Lasts about a day. Winnow renews it automatically, but this may not work against live servers. An API key does not expire.";

    /// <summary>Button label for a first-time sign-in. Matches the Epic
    /// card's "Sign in to Epic".</summary>
    public const string SignInButton = "Sign in to Steam";

    /// <summary>Button label when a session exists but needs renewing or
    /// has expired. Matches the Epic card's "Sign in again".</summary>
    public const string SignInAgainButton = "Sign in again";

    /// <summary>Button label shown beside the busy message while the
    /// sign-in window is open.</summary>
    public const string SignInCancelButton = "Cancel";

    /// <summary>Shown while the sign-in window is open.</summary>
    public const string SignInBusy =
        "Sign in in the window that opened; this page updates automatically.";

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

    /// <summary>Shown after a sign-in that recorded the account. The
    /// account filter is now available.</summary>
    public const string AccountConfirmed =
        "Account confirmed. The account filter is available.";

    /// <summary>Shown after a sign-in that did not record the account.
    /// Names the remedy and the consequence.</summary>
    public const string AccountNotConfirmed =
        "The account filter is still unavailable. Signing in again should resolve this.";

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

    /// <summary>What the API key provides: never expires, so scheduled
    /// updates keep working.</summary>
    public const string ApiKeyGives =
        "Never expires, so scheduled updates keep working.";

    /// <summary>What the API key gives up: no account identity until an
    /// import, no purchase history.</summary>
    public const string ApiKeyCosts =
        "The account filter is unavailable until a Steam import confirms your account. A key cannot read your purchase history.";

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

    /// <summary>Status line when a key is stored. States it can be
    /// replaced or cleared.</summary>
    public const string ApiKeySet =
        "Key stored. Replace or clear it here.";

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

    /// <summary>Notice after saving. Confirms the key is active.</summary>
    public const string ApiKeySaved = "Saved and in use.";

    /// <summary>
    /// Notice when saving was refused because this host cannot encrypt at
    /// rest. Says the two things that matter: the key was NOT stored, and the
    /// environment variable is the alternative that still works.
    /// </summary>
    public const string ApiKeySaveRefused =
        "The key was not stored: this system cannot encrypt saved "
        + "credentials. You can still set the Steam__ApiKey environment "
        + "variable.";

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
    /// trailing period.</summary>
    public const string SegmentTooltip = "Platform connections and import settings";

    /// <summary>Terse state phrase for local files. Always available, no setup
    /// required, so this never changes. One word, matching the WEB API line
    /// beneath it, so the two states read as one column rather than as two
    /// differently-worded facts.</summary>
    public const string StateLocalAlwaysOn = "On";

    // ── The WEB API state word ──────────────────────────────────────────────
    //
    // Three values, and which credential is in force is part of the state
    // rather than a separate sentence: a user who has both wants to know which
    // one is doing the work, and a user with neither wants one word.

    /// <summary>No credential of either kind.</summary>
    public const string StateWebApiOff = "Off";

    /// <summary>A signed-in session is carrying the Web API calls.</summary>
    public const string StateWebApiOnLogin = "On - Login";

    /// <summary>A Web API key is carrying them. Preferred when both are held,
    /// because keys do not expire.</summary>
    public const string StateWebApiOnApi = "On - API";

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
    /// <summary>
    /// Disclosure toggle label on the sign-in method.
    ///
    /// <para>It no longer says "what signing in gives, and what it costs",
    /// because that is now the body of the consent modal, on the near side of
    /// the button. What is left behind the toggle is the state of a session
    /// that already exists: which account it identified, when its credential
    /// expires, the calm health sentence, and what signing out removes. The
    /// label names that instead, because a toggle whose label promises text
    /// that moved is worse than no toggle.</para>
    /// </summary>
    public const string DisclosureSignIn = "Session details";

    // ── The modals ──────────────────────────────────────────────────────────
    //
    // Three of this card's panels became modals. A disclosure costs the card
    // the height of everything it opens, which is what kept the card from
    // fitting its viewport; a modal costs it one line. The two that carry a
    // decision — which method to use, and what signing in reads — are the ones
    // a user opens once and never again, so a surface that covers the card
    // while it is being read and leaves nothing behind is the right shape.

    /// <summary>Title of the comparison modal. Answers the question the link asks.</summary>
    public const string MethodsModalTitle = "Which one should I use?";

    /// <summary>Right-aligned link on the WEB API line. Opens the comparison.</summary>
    public const string MethodsModalLink = "Which one should I use?";

    /// <summary>Closes a modal that only informs. Not "cancel": nothing was started.</summary>
    public const string ModalClose = "Close";

    /// <summary>
    /// Title of the consent modal shown before the embedded sign-in opens.
    /// Names the act rather than the screen, because pressing Continue is what
    /// grants the consent the body describes.
    /// </summary>
    public const string SignInConsentTitle = "Before you sign in";

    /// <summary>
    /// The button that grants consent and opens the sign-in window. ROADMAP
    /// §4.7 condition 3 makes the paragraph above it the transparency surface
    /// read BEFORE acting; a modal that must be dismissed by this button is
    /// that requirement made structural rather than hoped for.
    /// </summary>
    public const string SignInConsentContinue = "Continue";

    /// <summary>Backs out of the consent modal without starting anything.</summary>
    public const string SignInConsentCancel = "Cancel";

    /// <summary>Title of the purchase-history modal, which holds both import routes.</summary>
    public const string PurchaseModalTitle = "Import purchase history";

    /// <summary>Title of the accounts detail modal.</summary>
    public const string AccountsModalTitle = "What the account filter can and cannot do";

    /// <summary>Right-aligned link on the accounts summary line. Opens the caveat.</summary>
    public const string AccountsModalLink = "What this covers";

    /// <summary>
    /// The separator between the two connection methods.
    ///
    /// <para>It says OR and not "choose one", because the two are not
    /// exclusive: holding both is a supported state, and the state word on the
    /// WEB API line above names which one is carrying the calls when they are.
    /// What the separator marks is that EITHER is sufficient — the thing a user
    /// looking at two credential controls most needs to know.</para>
    /// </summary>
    public const string MethodSeparator = "OR";

    /// <summary>
    /// The marker on whichever method is carrying the Web API calls.
    ///
    /// <para>It names the SAME credential the WEB API state word names, from
    /// the same rule, so the line at the top of the section and the marker
    /// beside the method can never disagree. When both are held the key wears
    /// it, because scheduled work takes the key; the tooltip carries
    /// <see cref="BothCredentials"/> so the split is one hover away rather than
    /// an implication.</para>
    /// </summary>
    public const string MethodInUse = "IN USE";

    // ══ TASK-59 — the folded purchase import ═══════════════════════════════

    /// <summary>Section label above the two import routes on the Steam card.
    /// Uppercase, matching LOCAL FILES, WEB API and STEAM ACCOUNTS.</summary>
    public const string PurchaseSectionLabel = "PURCHASE HISTORY";

}
