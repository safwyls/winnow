---
id: TASK-59
title: Fold the Purchases settings screen into the Steam platform section
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-01 02:50'
updated_date: '2026-09-01 03:16'
labels:
  - ui
dependencies: []
priority: medium
ordinal: 76000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Purchases screen in settings is now redundant: Steam sign-in can capture purchase history in the same consented session. The saved-file import route still needs a home for users who decline the browser sign-in, so the content should move into the Steam platform section rather than being deleted outright. The result should be one place to connect Steam and one place to import purchase data, not two screens that overlap.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The standalone Purchases screen is removed from the settings rail
- [ ] #2 The Steam platform section offers both the embedded-session import and the saved-file import for purchase and license history
- [ ] #3 A user who has not signed in can still reach the saved-file import without being forced through sign-in first
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. StoresViewModel gains an optional last ctor parameter SteamAccountImportViewModel? accountImport, exposed as read-only AccountImport plus ShowPurchaseImport => AccountImport is not null. Resolved from DI (both view models are already registered singletons); no Ingest or Auth.WebView type is named.
2. StoresViewModel.RefreshAsync also runs AccountImport.RefreshCommand, so arriving on the section still asks the harvester whether it can run here (the question MainWindowViewModel.ShowAccountImportAsync used to ask) without opening a window or doing IO.
3. SteamAccountImportView.axaml becomes an embeddable section rather than a screen: drop the screen header (Title/Intro) and the outer Grid + ScrollViewer, root becomes the content StackPanel, and the two routes stop being Border.card columns. They become bare heading + explanation + button blocks laid down the card, which is the grammar the Steam card already uses for its two connection methods, and which avoids a Surface card nested inside a Surface card.
4. StoresView.axaml hosts it inside the Steam card under a PURCHASE HISTORY section label, after the two connection methods and before STEAM ACCOUNTS.
5. MainWindow.axaml drops the PURCHASES segment button and the SteamAccountImportView pane. MainWindowViewModel drops IsAccountImportVisible, ShowAccountImportCommand, the AccountImport property and SettingsSection.Purchases, and no longer takes the import view model at all; the 7 test call sites passing DetachedAccountImport.Create() positionally are updated.
6. Amendment condition 6 (both routes to the account pages remain peers) is preserved by construction: same heading weight, same explanation slot, same primary button, laid out vertically the way the two connection methods are. Neither is drawn as a fallback.
7. AC3: the saved-file route is rendered and enabled unconditionally. It is never gated on SteamHasSession, SteamSessionState or the harvester's availability.
8. Copy: docs-writer disambiguates route A's button label from the connection sign-in's, since both now live on one card.
9. Tests: StoresViewModelTests gains coverage that the panel exposes the import section, that it refreshes it, and that ImportFromSavedPagesCommand is executable with SteamConnection.None and no session.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Done, not finalized.

The settings surface has two segments now, PLATFORMS and APPEARANCE. The import is a PURCHASE HISTORY section inside the Steam card, after the two connection methods and before STEAM ACCOUNTS.

Wiring: StoresViewModel takes an optional last constructor parameter SteamAccountImportViewModel? accountImport, resolved from DI, exposed as AccountImport and ShowPurchaseImport. Its RefreshAsync now also runs AccountImport.RefreshCommand, which is the availability question MainWindowViewModel.ShowAccountImportAsync used to ask on arrival; it opens no window and does no IO. MainWindowViewModel no longer takes or knows the import view model, and lost IsAccountImportVisible, ShowAccountImportCommand, the AccountImport property and SettingsSection.Purchases.

SteamAccountImportView.axaml is an embeddable section rather than a screen: no header, no ScrollViewer, no Border.card. The two routes are peers laid down the card, the grammar the Steam card already uses for its two connection methods, which also avoids a Surface card nested inside a Surface card and keeps each explanation at the reading measure section 13 gap 5 settled on. The result table moved from Border.card to Border.note. SteamAccountImportCopy lost Title, RailRow and RailTooltip; Intro survives as the section's lede.

Amendment condition 6 held by construction: same heading weight, same explanation slot, same primary button, neither described in terms of the other. Condition 3 held too, and this is the one place brevity lost on purpose: both route explanations stay at the TOP level, because pressing the button is the consent and the paragraph is the consent surface. Only the two saved-page hints went behind a disclosure.

AC3: nothing in the saved-file route is bound to a session, a credential or the harvester's availability. Asserted, not promised.

Files: src/Winnow.App/ViewModels/StoresViewModel.cs, MainWindowViewModel.cs, SteamAccountImportViewModel.cs, SteamAccountImportCopy.cs, SteamConnectionCopy.cs (PurchaseSectionLabel); src/Winnow.App/Views/StoresView.axaml, SteamAccountImportView.axaml, MainWindow.axaml. Tests: StoresViewModelTests gained four facts (the section exists, it is absent when not composed, a refresh reaches it, and the saved-file route is executable with no sign-in and no key); seven MainWindowViewModel construction sites in AccountStatsViewModelTests, LibraryViewModelTests and ListsViewModelTests dropped the removed argument; SteamAccountImportViewModelTests's consent test reads the new strings instead of the removed ones. No coverage was deleted.

Verified: full suite 2398 + 98 + 70 passed, 0 failed. Not committed.
<!-- SECTION:NOTES:END -->
