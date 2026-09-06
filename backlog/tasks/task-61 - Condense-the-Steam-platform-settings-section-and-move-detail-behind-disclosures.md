---
id: TASK-61
title: >-
  Condense the Steam platform settings section and move detail behind
  disclosures
status: Done
assignee:
  - '@claude'
created_date: '2026-09-01 02:51'
updated_date: '2026-09-04 02:36'
labels:
  - ui
dependencies: []
priority: medium
ordinal: 78000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Steam section of platform settings carries too much text at the top level. Every connection method states what it gives and what it costs, health messages are full sentences, and permission explanations sit inline. This was honest but is more than a user needs at a glance. Condense the top level to the minimum that lets someone act, and move the explanatory depth behind expandable disclosures such as a flyout or an expander so it remains available without cluttering the default view. The legibility condition from the section 4.7 second amendment still applies: a failing session must say so plainly at the top level, not only inside a collapsed panel.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The Steam section top-level content fits without scrolling at a reasonable window height and presents each connection method action and status concisely
- [x] #2 Full explanations of what each method gives, what it costs, and how permissions work are available behind an expandable disclosure
- [x] #3 A session that cannot renew surfaces its failure state at the top level, not only inside a collapsed panel
- [x] #4 No informational content is deleted; everything currently shown is still reachable
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Idiom first: the app's existing progressive disclosure is FilterGroupViewModel's IsExpanded plus a Button.linky reading 'Show all N' / 'Show fewer' in Azure, with the extra rows bound to IsVisible. No Expander, no Flyout, nothing animated - so a reduced-motion setting has nothing to suppress, the same argument the permission checkbox already makes. The detail panel has no disclosure idiom of its own. That is what gets reused.

1. Each method on the Steam card becomes: name, terse state, control. New terse state properties on StoresViewModel, rendered in the label/data register beside the heading:
   - SteamLocalStateText (always on)
   - SteamSignInStateText, one short phrase per SteamSessionHealth value
   - SteamApiKeyStateText, three values; the environment case says at top level that it cannot be cleared here, because the Clear button beside it is disabled and a disabled control whose reason is collapsed reads as a bug.
2. Four disclosures, each sitting under the thing it explains, each an [ObservableProperty] bool plus a generated toggle command and a toggle label:
   - LOCAL FILES: SteamConnectionCopy.LocalFiles and NothingConnectedCost/ConnectedAdds
   - WEB API section: SectionIntro, and BothCredentials when both are held
   - Sign in: SignInGives, SignInCosts, the purchase-history permission explanation, SignOutExplanation, the health sentence for the calm states, SIGNED IN AS and EXPIRES, and the account-confirmed sentence
   - Web API key: ApiKeyGives, ApiKeyCosts, and the full ApiKeyStatusMessage sentence
3. THE OVERRIDING CONSTRAINT, condition 8. The Amber session note stays at the TOP level, outside every disclosure, showing the full SteamSessionHealthMessage whenever ShowSteamSessionAttention is true - RenewalFailing, Expired, NotPersisted. So does the status pill's attention state, the sign-in problem note and the WebView2-unavailable note. Only the calm health sentences move into the disclosure. Condensing the healthy states is the goal; hiding a problem is not.
4. Not moved behind a disclosure, deliberately: the two purchase-import route explanations folded in by TASK-59. The section 4.7 amendment's condition 3 makes those the consent surface, read before the button is pressed. Only the two saved-page hints (load-more, licences pagination) go behind a disclosure. Condition 3 outranks brevity the same way condition 8 does.
5. No new visual language: Button.linky in Azure as the filter panel draws it, plus a focus-visible Volt brush swap at constant thickness per section 10.7. Flare appears nowhere. Amber stays the attention colour, as the Epic card uses it. Every number keeps the data face with tnum. AutomationProperties.Name on every disclosure toggle and every button.
6. All copy from docs-writer: terse top-level lines, disclosure toggle labels, and the honest detail unchanged inside. Existing SteamConnectionCopy constants are reused verbatim inside the disclosures, so the copy tests that assert their content keep passing.
7. Tests: SteamConnectionPanelTests gains the four-credential-combination top-level render check, a failing/expired session surfacing at top level rather than only in a disclosure, disclosures starting closed and their content being reachable, and terse state distinctness.

## AC1, second attempt: per-platform tabs (user decision, 2026-09-03)

The purchase-history disclosure took the Steam card from 1196px to 863px against a 644px
viewport. The remaining 219px is controls and the two binding transparency paragraphs, not
prose, so the user chose the structural option: split Platforms into one tab per platform so
each card owns the viewport instead of sharing it with two others.

1. StoresViewModel gains SelectedPlatform (Steam by default) with IsSteamVisible /
   IsEpicVisible / IsGogVisible and a command per tab, in the shape MainWindowViewModel
   already uses for IsStoresVisible / ShowStoresCommand.
2. StoresView.axaml grows a segmented control under the header, using the shared
   Button.seg tab class from Themes/controls.axaml and the same bordered container the
   SETTINGS segment uses, and each of the three existing cards is gated on its own flag.
   No card content moves; only which one is drawn.
3. A tab whose platform needs attention says so ON THE TAB. Hiding a lapsed Epic session
   behind an unselected tab would bury a failure state, which is the thing ROADMAP §4.7
   condition 8 exists to prevent on the Steam half; the same reasoning applies once a
   platform can be off screen. SteamStatusNeedsAttention and EpicStatusNeedsAttention
   already exist; GOG has no session and never needs one.
4. Tests: Steam is the default tab; selecting a tab shows exactly one card; the labels are
   bound rather than literal, the way TASK-60 required after STORES drifted back; and a
   platform needing attention marks its tab even while another tab is selected.
5. Re-measure by running the app with -- --data-dir and --open-stores at 1280x820 and
   screenshotting each tab, which is the evidence AC1 needs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Done, not finalized.

TOP LEVEL now, per method: its name, its state in a phrase, its control.
- LOCAL FILES / 'Always on' / no control.
- WEB API section label, one summary line ('Nothing connected. You only need one of these.' or 'Connected. You only need one of these.').
- 'Sign in to Steam' / one of six terse states / the sign-in button with its permission checkbox, or the sign-out button.
- 'Web API key' / 'Not set', 'Set', or 'Set outside Winnow, can''t be cleared here' / the masked field, Save, Clear, Get a key.

MOVED INTO DISCLOSURE, four of them, each under the thing it explains, all shut by default:
- 'What local files cover' -> LocalFiles, and NothingConnectedCost or ConnectedAdds.
- 'Which one should I use?' -> SectionIntro, and BothCredentials when both are held.
- 'What signing in gives, and what it costs' -> SignInGives, SignInCosts, the calm health sentence, SIGNED IN AS and the SteamID64, EXPIRES and the timestamp, the account-confirmed sentence, the purchase-history permission explanation, SignOutExplanation.
- 'What a key gives, and what it costs' -> ApiKeyGives, ApiKeyCosts, the full ApiKeyStatusMessage.
Plus one on the folded import: 'Before you save the pages' -> the load-more and licences-pagination hints.
All open to 'Hide'. Nothing was deleted; every constant is unchanged and still reachable.

CONDITION 8, which outranked the brevity goal wherever the two met. Still at the top level, outside every disclosure, whatever is collapsed: the Amber session note carrying the FULL health sentence for RenewalFailing, Expired and NotPersisted; the status pill's attention state; the sign-in problem note; the WebView2-unavailable note. Only the three calm sentences moved. A second place brevity lost: the environment-key state line carries its consequence rather than only its state, because the Clear button beside it is disabled.

IDIOM: the filter panel's, not a new one. A Button.linky-shaped toggle over content bound to IsVisible, no Expander and no Flyout, promoted to Themes/controls.axaml as Button.disclose because two views needed it. Azure ink, 2px focus ring as a brush swap at constant thickness per section 10.7, AutomationProperties.Name on every toggle. Nothing animates, so reduced motion has nothing to suppress. Flare appears nowhere; Amber remains the attention colour; every figure stays in the data face with tnum.

Copy authored by the docs-writer subagent and verified against the tests.

Tests added to SteamConnectionPanelTests: all four credential combinations render each method's state and control with the disclosures shut; a session that cannot renew surfaces at the top level in all three of those states, with a control beside it; the calm three keep their sentence in the disclosure; the four disclosures start shut, open on their own command and relabel; the disclosures still carry all eleven strings that left the top level; the terse lines are one per state; and the environment key says at the top level that it cannot be cleared here.

Verified: full suite 2398 + 98 + 70 passed, 0 failed. Not committed.

Not verified by running the app: the acceptance criterion about fitting without scrolling at a reasonable window height is a visual measurement, and the user's app may hold src/Winnow.App/bin. Worth a look before this is finalized.

2026-09-03 finalization review. Criteria 2, 3 and 4 verified and checked; criterion 1 left unchecked because it cannot be proven without running the app.

AC2: StoresView.axaml carries four disclosure toggles, each a Classes=disclose button over a StackPanel gated on its own open flag - SteamLocalDetailsOpen (line 280), SteamMethodsDetailsOpen (321), SteamSignInDetailsOpen (449) and the API-key panel (566).

AC3: StoresViewModel.ShowSteamSessionAttention covers RenewalFailing, Expired and NotPersisted, and ShowSteamSessionCalmHealth is its exact boolean complement, so an attention state can never be drawn only inside a collapsed panel. In StoresView.axaml the attention Border (line 358) sits at the METHOD 1 top level, outside every disclosure panel - the SteamMethodsDetailsOpen panel closes at line 330 - while the calm sentence (line 457) is inside the sign-in disclosure. The comment above it names ROADMAP condition 8.

AC4: commit 5327392 removed no copy constant from SteamConnectionCopy.cs (zero deleted const lines), and diffing the Steam bindings in StoresView.axaml shows no binding that was present before and absent after. Nothing informational was deleted; the condense moved content behind disclosures.

AC1 REMAINS: fitting without scrolling at a reasonable window height is a visual measurement. It needs a run with -- --data-dir pointed at a throwaway directory and a look at the Steam section. The original notes flagged this same gap.

2026-09-03: AC1 measured on a real run, and materially improved but NOT met. The task stays In Progress.

HOW IT WAS MEASURED. Built to a scratch artifacts path and launched with -- --data-dir into a throwaway directory plus --open-stores --no-sync, at the default 1280x820 window (MinHeight is 640). Screenshotted, scrolled by a known wheel delta of 50px per notch, and located the Steam card's top and bottom in content coordinates. The throwaway directory was deleted afterwards; the real library was never opened.

BEFORE: the Steam card measured 1196px in a 644px viewport, overflowing by 552px. The largest single block was PURCHASE HISTORY, which TASK-59 folded into this card and which kept the full prose of the standalone screen it came from while everything around it had been condensed behind disclosures.

THE CONSTRAINT THAT SHAPED THE FIX. The two purchase-import route paragraphs cannot simply be hidden. ROADMAP section 4.7 condition 3 makes each one the transparency surface a user reads before acting and makes the two routes equal peers; SteamAccountImportViewModel.cs:149 already records this, saying a peer whose explanation is a click away is no longer an equal peer. Condition 8 separately pins a failing session at the top level.

WHAT WAS BUILT, agreed with the user. One disclosure now holds the WHOLE purchase-history section rather than the prose inside it. Closed, no button is reachable, so nothing can be consented to unread; open, both routes and both paragraphs arrive together, neither nearer than the other. The section label and a one-line summary stay at the top level so a closed panel still says what is behind it. Verified by clicking it in the running app: the toggle reads Import purchase history, then Hide, and opens the intro, Sign in inside Winnow with its full paragraph and button, and Save the pages yourself beneath it.

AFTER: 863px in the same 644px viewport. 333px removed, a 28% reduction. Still 219px over, so criterion 1 remains unchecked.

WHY MORE DISCLOSURES WILL NOT CLOSE THE GAP, measured rather than assumed. Putting the STEAM ACCOUNTS two-line paragraph behind a disclosure of its own saved 5px: a toggle button costs very nearly what the two lines it hides cost. That change was reverted, because it bought a click and no space, and those two lines are what make the toggle under them mean anything. What remains in the card is controls and the two binding paragraphs, not prose: five labelled sections with their statuses, the sign-in block, the Web API key entry block, the account toggle and its state note.

WHAT WOULD ACTUALLY CLOSE IT, neither attempted. Remove controls rather than prose - for instance collapse the key paste box, Save and Clear when a key is already set, which on this machine is about 130px of disabled controls - or split Platforms into per-platform sub-pages so each card owns its own viewport. Both are structural and are the user's call.

Criteria 2, 3 and 4 were verified on 2026-09-03 and are unaffected: the disclosure count went up by one, nothing was deleted, and the session-failure block still sits at the METHOD 1 top level outside every disclosure.

Verification. dotnet build --artifacts-path: Build succeeded, 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors. Winnow.Tests 2775/2775, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures. Winnow.Tests was 2773 before this slice; +2 and none removed, which is the two StoresViewModelTests cases added here. StoresViewModelTests scoped run 32/32.

Two tests added. The_purchase_import_arrives_closed_behind_one_disclosure pins that the section is closed on arrival, that the toggle text flips between the open and hide labels, and that the section label and summary stay at the top level so a closed panel still names what is behind it. Neither_import_route_has_its_explanation_hidden_behind_the_other pins the condition 3 property: one toggle governs both routes, so the two explanations are always in the same state as each other and there is no per-route disclosure that could privilege one peer, with the saved-page hints remaining the only nested disclosure and still closed by default.

Files: src/Winnow.App/ViewModels/SteamConnectionCopy.cs (PurchaseSummary, DisclosurePurchase), StoresViewModel.cs (SteamPurchaseDetailsOpen, its command, toggle text and summary), Views/StoresView.axaml (the disclosure and the reverted STEAM ACCOUNTS block), tests/Winnow.Tests/StoresViewModelTests.cs.

Prose written inline rather than by the docs-writer agent, because this session is under a standing instruction not to invoke agents; that owner should review the two new copy strings and the comments.

2026-09-03, second slice: per-platform tabs, on the user's instruction. AC1 is still NOT met, and the criterion stays unchecked.

WHAT WAS BUILT. StoresViewModel gains SelectedPlatform (StorePlatform enum, Steam by default), IsSteamVisible / IsEpicVisible / IsGogVisible, a command and a bound label per tab, and SteamTabNeedsAttention / EpicTabNeedsAttention / GogTabNeedsAttention. StoresView.axaml grows a STEAM / EPIC / GOG segmented control under the header using the shared Button.seg tab class, and each of the three existing cards is gated on its own flag. No card content moved; only which one is drawn.

THE RULE THE TABS OWED. A card that can be off screen can hide a state the user must act on, which is what ROADMAP section 4.7 condition 8 exists to prevent for the Steam session; the condition was written when every card was drawn at once. Each tab therefore carries an Amber dot when its platform needs acting on - a failing or expired Steam session, a lapsed Epic one. Amber, never Flare. GOG has nothing to sign into and never marks.

MEASURED ON A REAL RUN, same method as before: --data-dir into a throwaway directory, --open-stores, 1280x820, screenshotted and scrolled by a known 50px per notch.
- EPIC tab: the whole card fits, roughly 234 to 666, no scrollbar.
- GOG tab: fits trivially, roughly 234 to 402.
- STEAM tab: unchanged at 863px. The tab strip costs about 36px of header, so the viewport went from 644px to about 608px and the Steam card is now over by 255px rather than 219px.

So the tabs fixed reachability and fixed two of the three platforms, but they cannot fix the one the criterion names. Splitting a scroller does not shrink the tallest thing in it. Epic and GOG were never what overflowed; Steam was, and it still is.

WHAT WOULD BE LEFT TO TRY, none of it attempted. Collapse the Web API key entry - the paste box, Save and Clear - when a key is already set, which is about 130px of controls that do nothing in that state; that reaches roughly 733px, still over 608. Beyond that the Steam card would have to lose a section entirely, which means either sub-tabs inside Steam or moving PURCHASE HISTORY back off the card, and the latter would undo TASK-59.

ONE EXISTING TEST WAS NARROWED, not deleted. Gog_states_that_there_is_nothing_to_sign_into asserted that NO property named for GOG ended in Command, which was equivalent to its real rule - GOG must never grow a sign-in affordance - only while the screen had no per-platform navigation. ShowGogCommand selects a card and connects to nothing. The assertion now names ShowGogCommand as the one permitted GOG command and separately forbids any GOG command whose name carries SignIn, SignOut or Connect, so a second one cannot arrive unnoticed.

TESTS ADDED. The_platform_tabs_open_on_steam, Exactly_one_platform_card_is_visible_at_a_time (every tab, in several orders, asserting exactly one visible flag each time), and A_platform_needing_attention_marks_its_tab_from_another_tab (a lapsed Epic session marks its tab while Steam is selected, and a failing Steam session marks its own tab from the GOG tab).

VERIFIED. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2803/2803, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures. StoresViewModelTests scoped 37/37.

NOTE FOR WHOEVER COMMITS THIS. The working tree also holds the uncommitted work of TASK-78 (encrypting the Steam Web API key and the IGDB client secret at rest), which was To Do at the start of this session and is now Done. It touches README.md, ROADMAP.md, game-library-design.md, Program.cs, the Igdb and SteamWeb credential sources and their tests, and it accounts for most of the rise in the Winnow.Tests count. None of it belongs to TASK-61.

2026-09-03, third slice: the Steam card redesign the user specified. AC1 is now MET and checked.

SEVEN CHANGES, all as asked.
1. LOCAL FILES is one line: label, the state word On in Volt, and What local files cover right-aligned on the same row. Always on became On so it reads as one column with the WEB API state beneath it.
2. WEB API carries Off, On Login or On API in the same Volt treatment, with Which one should I use right-aligned on its row. It opens a modal instead of expanding the card. Which credential is in force is now part of the state word, so a user holding both can see which one does the work without opening anything; the key wins when both are held, which is the standing decision that scheduled work takes the key because keys do not expire.
3. The purchase-history permission and the what-signing-in-gives paragraphs moved into a consent modal that opens BEFORE the WebView, with Continue beneath them. The card button no longer signs in: it opens that surface, and only Continue starts the flow.
4. The What a key gives link is gone. What a key gives and costs are half of the comparison the Which one should I use modal draws, and the key-state sentence went with them, under the key's own heading in that modal.
5. PURCHASE HISTORY is one line with a link that opens a modal holding both import routes.
6. Show only your account moved to the header line, beside the title count it changes.
7. Every one of those rows is a Grid whose cells are centred on it, so the label cap-height, the state word and the link sit on one optical line instead of three near-misses.

WHY THIS IS THE RIGHT SHAPE FOR CONDITION 3 rather than a violation of it. ROADMAP section 4.7 condition 3 makes each transparency paragraph the surface read BEFORE acting, and makes the two import routes equal peers. A modal that must be dismissed by Continue puts the paragraph on the near side of the act, which is stronger than a paragraph beside a button that can be pressed without reading it. The purchase modal holds both routes together, so neither is nearer than the other. The consent modal has no scrim dismissal at all: a click landing outside it must never be able to read as consent, so backing out is the explicit Cancel. Condition 8 is untouched: the failing-session block still draws at the METHOD 1 top level, outside every disclosure and every modal.

TWO THINGS FIXED ON THE WAY. The sign-in disclosure label still promised text that had moved, so it is now Session details and holds only what a signed-in user looks up - the account, the expiry, the calm health sentence, what signing out removes - and it is offered only once a session exists. The key-state paragraph was duplicating the heading line that already says the key came from outside and cannot be cleared here, so it moved into the modal.

MEASURED ON A REAL RUN, 1280x820, --data-dir into a throwaway directory. The Steam card went 1196px (before any of this task) to 863px (purchase disclosure) to 601px. The viewport is 608px. THE CARD FITS. All three modals were opened and screenshotted in the running app: the comparison, the consent gate with its unticked permission and its Cancel and Continue, and the import modal with both routes and both paragraphs.

The page still scrolls about 41px in one particular state, because the outer margins add 48px and this throwaway profile is showing the transient your-API-key-is-set-but-the-account-is-not-confirmed-yet note. That note is absent once an import confirms the account, and the criterion is about the section's content rather than the page padding around it.

VERIFIED. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2807/2807, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures. StoresViewModelTests 41/41; SteamConnectionPanelTests and StoresViewModelTests together 109/109.

FOUR TESTS ADDED, and two existing ones updated rather than deleted. Added: the three modals start closed and are mutually exclusive; the card button opens consent and signs nothing in; opening consent leaves the permission unticked; and the two state words say what is on and which credential carries it. Updated: the disclosure sweep is now three rather than four, and the two tests that asserted the key disclosure was shut assert no modal is open instead.

2026-09-03, fourth slice: the WEB API separator and the STEAM ACCOUNTS section.

SEPARATOR. On, API and On, Login became On - API and On - Login. Read as a separator change rather than a rewording; if the intent was different it is two constants in SteamConnectionCopy.

STEAM ACCOUNTS, rebuilt to the user's instruction. The section is now the label, a PENDING FETCH clarifier while the account is unconfirmed, and one line reading N games across M accounts. The two-line paragraph explaining that every account on the PC is read is gone, and so is the amber note explaining why the toggle is disabled.

WHY THERE IS NO PER-ACCOUNT BREAKDOWN, which is worth recording because the first instruction asked for one. No account NAME is stored anywhere in Winnow. AccountFacts states it as a rule rather than an omission: presence only, never identity, no name, persona or profile link. The only per-account identifier that exists is the SteamID64 in ownership_accounts.account_ref, and a line reading 412 games from 76561198… is worse than the total. The user chose the aggregate on being told.

WHERE THE COUNT COMES FROM. AccountVisibilityState gained AccountCount, and AccountVisibilityService now takes an optional IOwnershipAccountRepository and counts distinct refs through GetAccountRefsAsync. It has to be the membership rows and not ownerships.account_ref: that column holds the one account that won the play tuple, so on a shared PC — the machine this whole feature exists for — it under-counts by exactly the accounts that never played anything the most. The game figure is SteamTitleCount, the same number the card header already shows, so the two cannot disagree.

WHAT SURVIVED, and why. The caveat note stays: it is a fact about what the filter cannot promise rather than an explanation of what the section is. The removed paragraph is still reachable — it is the toggle tooltip on the header line. Nothing was deleted outright, which keeps criterion 4 true.

PENDING FETCH is the clarifier. A disabled toggle is already visible; what a user could not see is that the answer is coming rather than missing, and that is the one thing it adds.

MEASURED. With both slices the Steam card now draws from 234 to 722 at 1280x820 and the section fits with room to spare, well inside the 608px viewport. Verified in the running app; the summary line is correctly absent on a throwaway profile holding no games, because it is gated on both figures being non-zero rather than drawing a line of zeroes.

VERIFIED. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2811/2811, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures. Three tests added: the summary text over singular, plural and empty; that it is absent until there is something to count; and that an unconfirmed account marks the section pending while a confirmed one enables the toggle.

2026-09-03, fifth slice: the accounts caveat modal, the OR separator, and the in-use marker.

THE CAVEAT MOVED. Games Winnow cannot attribute to a specific account stay visible, and the sentence about a filtered library reporting one account's figures, are now the body of a fourth modal reached from a What this covers link right-aligned on the N games across M accounts line. It used to be a note under the toggle drawn only while the filter was on; it is a fact about what the filter can and cannot promise, so it belongs beside the figures it qualifies. The modal also absorbed the why-the-toggle-is-disabled sentence and the how-many-would-be-hidden figure, both of which were top-level notes.

THE SEPARATOR. A rule-OR-rule divider between the sign-in and the Web API key. It says OR and not choose one, deliberately: the two are peers, holding both is a supported state, and nothing on this card may imply that picking one deselects the other.

THE RADIO BUTTONS WERE NOT BUILT, and the user agreed on being shown why. A radio group asserts an exclusive choice. These two methods are not exclusive: ShowSteamBothCredentials exists precisely because holding both is supported, SteamConnectionCopy.BothCredentials states the split (scheduled updates take the key because keys do not expire, the sign-in serves interactive work), and TASK-55 established them as peers where neither is a fallback. There is also no stored preferred-method setting for a radio to bind to, so radios would either do nothing or would require making the methods genuinely exclusive, which is a product change contradicting the existing design.

WHAT WAS BUILT INSTEAD, which is what the radios were reaching for. An IN USE marker on whichever credential is carrying the Web API calls. It is derived from the SAME rule as the WEB API state word - the key wins whenever it exists, the session carries it otherwise - so the line at the top of the section and the marker beside the method cannot disagree. Exactly one is ever drawn. When both credentials are held the tooltip becomes the BothCredentials sentence, so the marker cannot be read as saying the sign-in is idle.

VERIFIED IN THE RUNNING APP. The OR divider sits between the two methods; the IN USE pill draws on the Web API key and not on the sign-in, matching the On - API state word above it. The accounts summary line and its link are correctly absent on a throwaway profile holding no games, because the line is gated on both figures being non-zero; the formatting is covered by unit test instead.

VERIFIED. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2813/2813, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures. Two tests added: that the in-use marker names the same credential the state word does across all four credential states and is never drawn on both at once, and that the accounts caveat is reachable from the summary line and stays mutually exclusive with the other three modals.

2026-09-03, sixth slice: the purchase-history control became a button.

Import purchase history was a disclose link and is now Button.act.quiet with the same 14,5 padding Get a key uses, so the card's two secondary buttons sit at one weight.

The reasoning, since the card mixes both idioms deliberately. The other right-aligned controls on this card - What local files cover, Which one should I use, What this covers - open surfaces that EXPLAIN, and they stay links. This one opens the surface where an import is actually performed, so it reads as the action it is. Quiet rather than primary because the card's primary action is the connection above it and the import is optional; a second Volt button would have put two primaries on one card.

Verified in the running app at 1280x820: the button draws right-aligned on the PURCHASE HISTORY row at the same weight as Get a key, and the card still fits its viewport. Build succeeded, 0 warnings under TreatWarningsAsErrors; Winnow.Tests 2813/2813 with no test touched, because the control's command and automation name are unchanged - only its style is.

2026-09-03, seventh slice: Get a key moved, the purchase summary removed, and the card width pinned.

GET A KEY moved from under the field to the right end of the Web API key heading row. It is the one control in that block that does not act on the field beside it - it opens Steam's own registration page - so sitting under the field put it in the row of things that do. On the heading row it reads as belonging to the method, and the field, Save and Clear are left as one uninterrupted group.

THE PURCHASE SUMMARY went. Optional. Fills in when you got each game and what you paid. was saying on the card what the modal behind the button opens on, so a user who had not asked was being told twice. The section is now the label and the button.

DEAD CODE REMOVED WITH IT. SteamConnectionCopy.PurchaseSummary and StoresViewModel.SteamPurchaseSummary had no other reader. The purchase DISCLOSURE members went too - SteamPurchaseDetailsOpen, ToggleSteamPurchaseDetails, SteamPurchaseDetailsToggleText and SteamConnectionCopy.DisclosurePurchase - which had been dead since the modal replaced the disclosure two slices earlier and were still being exercised by two tests. Both tests were repointed at the modal rather than deleted, and one was renamed from arrives_closed_behind_one_disclosure to arrives_closed_behind_one_button.

THE CARD WIDTH, which was not asked for and is a consequence of the removal. The container held MaxWidth 720 with HorizontalAlignment Left, so it was only a ceiling and each card sized to its own widest line: taking the summary sentence out narrowed the Steam card by about 145px, and the three platform cards would have sat at three different widths. Stretch is not the fix either - a stretched child under a max width centres, which pulled the card off the left edge the header sits on, and I verified that on screen before changing it again. It is now Width 720 with Left, so all three cards hold one size aligned with the header whatever their content. The window MinWidth is 1200, so 720 plus the margins always fits.

ONE PROCESS NOTE. The first verification run showed neither change, because the preceding build had gone to the test artifacts path rather than the run path and the launched exe was stale. Rebuilt to the run path and re-verified; both changes and the width fix are confirmed on screen at 1280x820.

VERIFIED. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2813/2813, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures.

Also confirmed from the user's own screenshot of a real library, which is the first sight of two things the throwaway profile could not show: the accounts line rendering as 912 games across 2 accounts with What this covers right-aligned beside it, and the header count reading Steam 912 TITLES beside the account toggle.

2026-09-03, eighth slice: the N TITLES baseline, on all three cards.

THE CAUSE. The count and its unit were separate TextBlocks, each VerticalAlignment=Center. Centring aligns layout BOXES, not baselines, and the row carries three faces at three sizes - Display Bold 18 for the platform name, Plex Mono 12 for the figure, Body SemiBold 10 for TITLES. Three different sets of font metrics put their glyphs at three different heights inside boxes that are themselves centred correctly, which is why it looked like a bug with no obvious source. It was on Steam, Epic and GOG, because all three cards share the markup.

THE FIX. The figure and the unit now share ONE TextBlock as two Runs. Inlines sit on a common baseline by construction, and since digits and small caps are both cap-height, a shared baseline is a shared optical line. No nudge, no measured margin, nothing that a font change would invalidate.

WHAT MOVED WITH IT. The Runs carry their faces explicitly rather than through the .data and .label classes, because LetterSpacing is a TextBlock property and cannot ride a Run; the label's 1px tracking is dropped on TITLES as a result. FontFeatures=tnum is set on the figure Run explicitly, so section 3's every-figure-is-Plex-Mono-tnum rule still holds - the enforcement test agrees, and the numeric-style test passes untouched.

A SIDE BENEFIT: a screen reader now reads 27 TITLES as one string rather than two.

VERIFIED. Screenshotted at 1280x820 with --seed-sample so a count exists, then cropped and magnified 5x: the bottoms of the digits and of the capitals land on one row, and the platform name sits on it too. Build succeeded, 0 warnings under TreatWarningsAsErrors. Winnow.Tests 2813/2813, Winnow.Recommend.Tests 145/145, Winnow.Covers.Tests 70/70, zero failures.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Condensed the Steam platform settings section and moved its depth off the card. All four criteria met.

The section was rebuilt in three slices. First, PURCHASE HISTORY went behind one disclosure holding both import routes, taking the card from 1196px to 863px. Second, Platforms was split into STEAM / EPIC / GOG tabs, which fixed Epic and GOG outright and gave each platform the viewport, though it did not shrink the Steam card. Third, the card itself was redesigned to the user's specification: LOCAL FILES and WEB API each became one line carrying a state word in Volt with its link right-aligned on the same row; the comparison, the sign-in consent and the purchase import each became a modal; the What a key gives link was removed and folded into the comparison; and Show only your account moved to the header line beside the count it changes.

That took the Steam card to 601px against a 608px viewport, so criterion 1 is met and checked. Criteria 2, 3 and 4 were verified earlier and survive: every explanation is still reachable, nothing was deleted, and the failing-session block still draws at the top level outside every disclosure and every modal.

The consent modal is the part worth keeping in view. ROADMAP section 4.7 condition 3 makes each transparency paragraph the surface read before acting and makes the two import routes equal peers, which is why those paragraphs could not simply be hidden. Putting them in a modal whose only way forward is Continue puts them on the near side of the act instead of beside it, which is stronger than what it replaced; the purchase modal holds both routes together so neither is nearer than the other; and the consent modal has no scrim dismissal, because a click landing outside it must never read as consent.

Verified by running the app at 1280x820 against a throwaway data directory, screenshotting the card and opening all three modals, and by the full suite: 2807 + 145 + 70, zero failures, build clean under TreatWarningsAsErrors. Four tests added and two updated rather than deleted.

Prose was written inline rather than by the docs-writer agent, because this session is under a standing instruction not to invoke agents; that owner should review the new copy strings. The working tree also holds the unrelated uncommitted work of TASK-78.
<!-- SECTION:FINAL_SUMMARY:END -->
