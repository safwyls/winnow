# Spike: Detail modal additions width

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The current rules are in `design-system.md`
> §10.1 and §10.3.

**Settles:** the reception line wrap behaviour, the screenshot strip geometry and the
acquisition block fit for the details modal restructuring. Measured 2026-09-05.

## Method

A throwaway console project outside the repository at the app's own Avalonia 11.3.20,
configured `.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`
so text is shaped and measured by Skia rather than by the headless stub. It loaded the
repository's own font files. Every control was hosted in a real (headless) `Window` and shown
before measuring, so the Fluent theme's own templates and setters were applied — a control
measured detached carries no template and reports a width that has nothing to do with the
shipped one. The harness reproduced the shipped-strip figures exactly (347px with `Play`, 357px
with `Install`), which is what makes the new figures trustworthy. The app itself was not run.

## Findings

- Reception figures, each measured alone and including its own 14px trailing margin:
  `IGDB USERS 82 / 1,234 ratings` = 184 x 17; `IGDB CRITICS 85 / 42 critic scores` = 210 x 17;
  `STEAM 92% / 12,345 reviews` = 170 x 17. Sum 564px.
- Three figures in a `WrapPanel`: two rows (34px) at both 420px and 500px, one row (17px) at
  580px. The full line fits on one row only at the card's `MaxWidth`, and wraps rather than
  clipping at every narrower width.
- Two figures, the ordinary case when IGDB has no aggregated critic score: 354px, one row at
  420px.
- The action-band strip is unchanged by this pass and still measures 347px with `Play` and
  357px with `Install` against the 420px column.
- Six screenshot thumbnails at 120x68 with 8px spacing: 760 x 68. Three whole thumbnails and
  part of a fourth are visible in the 420px column, which is the same "there is more" signal
  §10.9's candidate list relies on.
- The `ACQUIRED` block — label, date in Plex Mono, licence in words — measures 180 x 49 in
  the object column's 180px of content width (200px column less the 20px gutter).

## Reproducibility

The harness lives outside the repository and was not kept; the method paragraph is the way to
reproduce it.
