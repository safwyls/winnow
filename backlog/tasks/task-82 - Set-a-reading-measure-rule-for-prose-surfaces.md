---
id: TASK-82
title: Set a reading-measure rule for prose surfaces
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
updated_date: '2026-09-06 06:29'
labels: []
dependencies: []
priority: low
ordinal: 109000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
design-system.md section 3 tops out at Body 13/18 and section 4 sets no maximum measure, because until the Stores panel nothing had a paragraph in it. That panel used 12/18 capped at 720px, chosen in the file rather than in the system.

The merge card's 840px ceiling is a separate, measured number for a two-column comparison and does not govern prose; section 6 records why. A prose measure is still unstated.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 design-system.md states a maximum measure for prose, with the size and leading it applies to
- [ ] #2 The Stores panel's 720px cap is either ratified by the rule or changed to match it
- [x] #3 The rule says explicitly that it does not govern the merge card, so section 6's 840px stays undisturbed
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
TASK-133 settled the measure itself, because a card that can be 1582px wide made an unbounded prose run a real fault rather than a latent one. design-system.md §3 now carries a Prose measure subsection: ProseMeasure=410 in tokens.axaml, applied by a .prose class that also sets HorizontalAlignment=Left (a maximum under the default Stretch centres the paragraph off the column's left edge) and Wrap. The number is measured in the shipped face by a headless Avalonia harness, not chosen: 66 characters of Plus Jakarta Sans is 410px at Body 13 and 379px at 12, so one token at 410 is 66 characters at 13/18 and 72 at the 12/18 paragraph body - both inside the 45-75 band. §3 also states that the rule does not govern the merge card's 840px (AC #3) or the Stores panel's 720px. Evidence: docs/spikes/details-modal-scale.md.

AC #2 is deliberately left open. The Stores panel's 720 is a CARD width holding controls and rows, not a paragraph cap, so the rule does not simply overwrite it; the prose that needs capping is the .para runs inside those cards, which measure up to 109 characters a line at 12/18 in a 720px card - outside the band. Putting the measure and the left alignment on :is(TextBlock).para would reach around 100 sites across AccountStatsView, AppearanceView, LibrarySettingsView, MergeQueueView, SteamAccountImportView, StoresView and the consent window, several of them inside grid cells where Stretch to Left is a visible change. That sweep wants its own pass and a running app to look at; it was not done blind inside a modal-scaling bug fix. The comment above .para in controls.axaml was rewritten to say exactly this, so the next reader is not told the design system still owes a number.
<!-- SECTION:NOTES:END -->
