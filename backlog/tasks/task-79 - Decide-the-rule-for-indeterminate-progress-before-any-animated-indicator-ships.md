---
id: TASK-79
title: Decide the rule for indeterminate progress before any animated indicator ships
status: Done
assignee:
  - '@claude'
created_date: '2026-09-03 00:58'
updated_date: '2026-09-04 19:48'
labels: []
dependencies: []
priority: low
ordinal: 106000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
design-system.md section 8's reduced-motion guidance names only the hover saturation ramp, and there is no reduced-motion setting to hang a spinner off. Building the Stores panel hit this: a spinner was deliberately not invented, and sign-in shows a Volt-edged status field saying where to look, plus Cancel.

That interim choice is fine and is what ships. This task is the gate: if an animated indicator is ever wanted, the accessibility floor needs the rule first.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 design-system.md section 8 states what an indeterminate indicator may be and how reduced motion affects it
- [x] #2 The rule covers the case where there is nothing to animate, so the current status-field pattern is either ratified or replaced
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Settle the rule, then write it into design-system.md §8 so a later implementer can follow it.
1. The rule: Winnow states, it does not spin. When a proportion cannot be stated, the surface says what it is doing and what it is waiting for, in words, in a status field, and offers the way out where there is one. That ratifies the Stores panel's interim pattern rather than replacing it (AC2), and matches §7's 'never a spinner' for placeholder tiles.
2. The escape hatch, so the rule is followable rather than a prohibition: motion may be ADDED to a status field and may never REPLACE one. Conditions: the words are the indicator and the motion is decoration over them; at most one moving thing per screen; removed entirely under reduced motion by a style and never a local Transitions value (§12.5); never the only thing saying work is happening.
3. Replace §8's 'There is no rule yet ... TASK-79' sentence with the rule; keep the determinate bullet beside it. Copy authored by docs-writer.
4. Append the sentence §8 used to say to docs/decisions.md, per AGENTS.md.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
THE RULE, settled: Winnow states; it does not spin. When the interface cannot state a real proportion, it says what it is doing and what it is waiting for, in words, in a status field, and offers Cancel where there is one.

It RATIFIES the Stores panel's interim pattern rather than replacing it (AC2), and it is not a new idea: §7 already said the same thing about the first-run grid — placeholder tiles with the title in Bricolage on a Surface field, never a spinner, never an empty grid. The rule is those two cases generalised.

The reduced-motion half falls out rather than being bolted on: because the indicator is words, reduced motion has nothing to disable and the surface is the same in both motion settings. An accessibility floor with no branch in it cannot be got wrong, and that is the argument FOR the rule rather than a consequence of it.

The nothing-to-animate case is therefore not an exception. When nothing is yet known the surface says what it is waiting for; it never draws an empty box, and never a placeholder claiming a measurement it does not have.

THE ESCAPE HATCH, so the rule is followable rather than a prohibition and TASK-90 is unblocked: motion may be ADDED to a status field; a status field may never be REPLACED by motion. Four conditions on any animated indeterminate indicator that ships — (a) the words are the indicator and the motion is decoration over them, so a screen reader reads a state rather than nothing; (b) at most one moving element on a screen; (c) it is removed entirely under reduced motion, leaving the words, removed by a style and never present as a local Transitions value, which is §12.5's rule and the reason §12.5 exists; (d) it is never the only thing saying work is happening.

WHERE IT LIVES: design-system.md §8, two bullets replacing the old 'There is no rule yet ... TASK-79' sentence, beside the determinate-indicator bullet which is unchanged. The superseded sentence is in docs/decisions.md under a 2026-09-04 entry, per AGENTS.md. Prose authored by docs-writer.

VERIFIED: dotnet build clean; tests/Winnow.Tests 2973 passed, 0 failed — including Enforcement/DocumentationConsistencyTests, whose Every_section_cross_reference_names_a_heading_that_exists covers the new §16 references the change introduced elsewhere in the document.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
design-system.md §8 now states the rule for indeterminate progress, and the gate is closed.

The rule: Winnow states, it does not spin. Where it cannot state a real proportion it says what it is doing and what it is waiting for, in words, in a status field, with Cancel where there is one — which ratifies the Stores panel's existing pattern and generalises §7's 'never a spinner' for the first-run grid. Because the indicator is words, reduced motion has nothing to disable, so the surface is identical in both motion settings; an accessibility floor with no branch in it cannot be got wrong. The nothing-to-animate case is covered by the same sentence rather than by an exception.

So that the rule is followable rather than a prohibition (TASK-90 depends on it): motion may be added to a status field and may never replace one, under four conditions — the words are the indicator, one moving element per screen, removed entirely under reduced motion by a style and never a local Transitions value (§12.5), and never the only thing saying work is happening.

Verified: the rule is written in design-system.md §8 with the superseded sentence recorded in docs/decisions.md (2026-09-04), per AGENTS.md; dotnet build clean, and tests/Winnow.Tests 2973 passed / 0 failed, including the documentation-consistency enforcement tests.
<!-- SECTION:FINAL_SUMMARY:END -->
