---
name: frontend-design
description: Guidance for intentional visual design in Winnow and related web artifacts.
license: Complete terms in LICENSE.txt
---

# Frontend design

## Winnow app work

Read the relevant component in `design-system.md` and its palette, typography and
accessibility rules. Use `src/Winnow.App/Themes/tokens.axaml` for resources. The visual
specification contains the current desktop and fullscreen choices; mockups and exported
design bundles are examples, not separate requirements.

Follow the app's established identity and Avalonia patterns. A routine UI change needs a
focused implementation, not a new palette or design process. Assess desktop and fullscreen
behavior separately, including focus, reduced motion, empty states and errors.

## New design choices and web artifacts

When the brief leaves a design choice open, make it specific to the subject, audience and
main user action. State any assumption that materially shapes the result.

- Choose typography and layout that help people read and act. Let structure express real
  groups or sequences; do not add numbered labels without a sequence behind them.
- Use real content from the brief. Keep decorative elements subordinate to the main task.
- Use the project's existing tokens. For a new visual identity, define a compact palette,
  type scale and spacing system before building.
- Use motion only when it explains a transition or provides useful feedback. Respect reduced
  motion and keep keyboard focus visible.
- For web work, check narrow and wide layouts and keep CSS selectors simple. For app work,
  use Avalonia controls and the app's existing styles.

Review the design against the brief, build it, then inspect the rendered result. Correct
clipping, weak contrast, inconsistent spacing and unclear focus before delivery. Explain
concrete choices and limitations without a running account of discarded alternatives.

## Interface writing

Write from the user's perspective, with familiar words and active verbs. Name controls for
what they do: `Save changes`, `Open game`, `Choose file`. Keep the same action name throughout
its flow. Use sentence case and Winnow's copy rules.

Labels identify controls; explanations clarify a choice or consequence. Error messages say
what failed and how to recover. Empty states say what belongs there and how it appears.
Keep implementation details out of user flows unless they help someone make a decision.
