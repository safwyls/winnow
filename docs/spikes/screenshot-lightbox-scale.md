# Spike: Screenshot lightbox scale

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The current rules are in `design-system.md`
> §10.7.

**Settles:** the lightbox frame geometry and the shot sizes it produces at each window
size (TASK-134). Measured 2026-09-06.

## Method

A throwaway console project outside the repository at the app's own Avalonia 11.3.20,
configured `.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`
so text is shaped and measured by Skia rather than by the headless stub. It linked the
repository's own `Assets/**` and `Themes/tokens.axaml` as `AvaloniaResource` under an assembly
named `Winnow`, so `avares://Winnow/...` resolves exactly as it does in the app. One correction
worth recording for the next person: `tokens.axaml` keeps its text styles in a keyed
`<Styles x:Key="TextStyles">` block, because a `ResourceDictionary` cannot hold an unkeyed
`Styles` child, and `App.axaml.cs` adds it to `Application.Styles` at startup — a harness that
merges the dictionary and forgets that step measures everything in Fluent's own Inter at
Fluent's own sizes and looks plausible while being wrong. Every control was hosted in a real
(headless) `Window` and shown before measuring.

The lightbox geometry — the 24px margin, the 8px row spacing, the 16px column spacing, the
`Auto,*,Auto` rows and columns, the 1282x722 caps and the 1px border — was copied verbatim out
of `src/Winnow.App/Views/ScreenshotLightboxView.axaml`. Every figure is `Bounds` or
`DesiredSize` off a real measure-and-arrange pass. The app itself was not run.

Before anything new was trusted, the harness reproduced a shipped figure from
`docs/spikes/details-modal-additions-width.md` exactly: the reception figure
`IGDB USERS 82 / 1,234 ratings` came out at 184 x 17 `DesiredSize`, including its own 14px
trailing margin. `Bounds` is 170 x 17 and excludes the margin; `DesiredSize` is 184 x 17 and
includes it. The shipped figure is the latter.

## Findings

**The rule under test.** Frame `MaxWidth` 1282 and `MaxHeight` 722, with
`Stretch="Uniform"`. The overlay occupies the full window less the 36px title bar.

| window | overlay | frame arranged | shot drawn |
|---|---|---|---|
| 1200x640 (the app's own minimum) | 1200 x 604 | 884 x 498 | 882 x 496 |
| 1280x820 (default) | 1280 x 784 | 1138 x 641 | 1136 x 639 |
| 1440x900 | 1440 x 864 | 1282 x 722 | 1280 x 720 |
| 1600x900 | 1600 x 864 | 1282 x 722 | 1280 x 720 |
| 1920x1080 | 1920 x 1044 | 1282 x 722 | 1280 x 720 |
| 2560x1440 | 2560 x 1404 | 1282 x 722 | 1280 x 720 |
| 3440x1440 | 3440 x 1404 | 1282 x 722 | 1280 x 720 |
| 3840x2160 | 3840 x 2124 | 1282 x 722 | 1280 x 720 |

The frame arranged size is the shot plus 2px in each direction at every row (the 1px border).

**Thresholds, found by scanning one pixel at a time.** The frame reaches 1282 wide at an
overlay width of 1424, and 722 tall at an overlay height of 828. A window of 1424 x 864 (828
plus the 36px title bar) is the first window at which the shot is drawn at its native 1280x720.
The default 1280x820 window cannot reach native however lean the chrome gets — the window is
1280 wide and the frame needs 1282 for the shot plus its border, so it is not a chrome budget
that stops it.

**Chrome budget.** 142px of width: the 48px margin (24px each side), two 16px column gaps, and
the two ~31px navigation buttons.

## Reproducibility

The harness lives outside the repository and was not kept; the method paragraph is the way to
reproduce it.
