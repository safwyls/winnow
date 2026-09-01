---
id: TASK-61
title: >-
  Condense the Steam platform settings section and move detail behind
  disclosures
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-01 02:51'
updated_date: '2026-09-01 03:16'
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
- [ ] #1 The Steam section top-level content fits without scrolling at a reasonable window height and presents each connection method action and status concisely
- [ ] #2 Full explanations of what each method gives, what it costs, and how permissions work are available behind an expandable disclosure
- [ ] #3 A session that cannot renew surfaces its failure state at the top level, not only inside a collapsed panel
- [ ] #4 No informational content is deleted; everything currently shown is still reachable
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
<!-- SECTION:NOTES:END -->
