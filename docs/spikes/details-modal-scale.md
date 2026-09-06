# Spike: Detail modal scale

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The current rules are in `design-system.md`
> §10.1 and §3.

**Settles:** the scaling rule for the details modal's card width, card height and hero
height (TASK-133). Measured 2026-09-05.

## Method

A throwaway console project outside the repository at the app's own Avalonia 11.3.20,
configured `.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`
so text is shaped and measured by Skia rather than by the headless stub. It loaded the
repository's own font files as `AvaloniaResource` under an assembly named `Winnow`, so
`avares://Winnow/Assets/Fonts#...` resolves exactly as it does in the app. Every control was
hosted in a real (headless) `Window` and shown before measuring. The card geometry —
`MinWidth`, `Margin`, the `200,*` columns, the 26px column spacing, the 26/24/26 grid margin,
the 1px card border and the 20px `InnerScrollGutter` — was copied verbatim out of
`src/Winnow.App/Views/GameDetailsView.axaml`. Every figure below is `Bounds` or `DesiredSize`
off a real measure-and-arrange pass. The app itself was not run.

The harness was validated before it was trusted, by reproducing three already-shipped
measurements from `details-modal-additions-width.md` and `details-action-band-width.md`: the
three reception figures at 184x17, 210x17 and 170x17 including their 14px trailing margins;
the same three in a `WrapPanel` taking two rows (34px) at 420px and 500px and one row (17px)
at 580px; and the right column at 420px against the card's `MinWidth` 700 and 580px against
its old `MaxWidth` 860. All three came out exact. One correction worth recording for the next
person: those figures are reproducible only with the count strings the view model actually
builds — `1,234 ratings`, not `/ 1,234 ratings`. The oblique in §10.1's sketch is not in the
string, and adding it makes every figure 12px too wide, which is two characters of Plex Mono
at 10px.

## Findings

**The rule under test.** Card `MaxWidth` = clamp(half the window's width, 860, 1582). Card
`MaxHeight` = max(two-thirds of the window's height, 720). Hero `MaxHeight` = max(three-tenths
of the window's height, 200). `MinWidth` 700 and `Margin` 40 unchanged.

**Coercion, verified rather than assumed.** A `Border` with `MinWidth` 700 and `MaxWidth` 500
arranges at 700: the minimum wins over a smaller maximum. That is what makes the computed cap
safe to floor at 860 without a second guard.

**The card.** Measured with content that asks for more than any window can give, hosted in the
window less its 36px title bar:

| window | cap | card arranged | right column | reception line |
|---|---|---|---|---|
| 1200x640 (the app's own minimum) | 860 x 720 | 860 x 524 | 580 | one row |
| 1280x820 (default) | 860 x 720 | 860 x 704 | 580 | one row |
| 1600x900 | 860 x 720 | 860 x 720 | 580 | one row |
| 1920x1080 | 960 x 720.36 | 960 x 721 | 680 | one row |
| 2560x1440 | 1280 x 960.48 | 1280 x 961 | 1000 | one row |
| 3440x1440 | 1582 x 960.48 | 1582 x 961 | 1302 | one row |
| 3840x2160 | 1582 x 1440.72 | 1582 x 1441 | 1302 | one row |

The right column is the card less 280 at every width, which is the same relation the earlier
spike measured at 700 and 860. The reception line takes two rows only at the 420px column the
card's `MinWidth` produces, which is unchanged.

**Where the ceiling comes from.** With the hero's height unconstrained, the shot is drawn at
1258x707.63 in a 1560 card, 1278x718.88 in a 1580 card, exactly 1280x720 in a 1582 card, and
1282x721.13 in a 1584 card. 1582 is therefore the card width at which a screenshot is drawn at
the native size IGDB's `t_screenshot_huge` delivers, and the first width past which the card is
paying for upscale.

**The hero.** A 1280x720 shot, `Stretch="Uniform"`, in a left-aligned 1px-bordered box inside
content carrying the 20px `InnerScrollGutter`. The last column is what shipped before:
`UniformToFill` in a full-width box capped at 200px, which crops.

| window | card | hero cap | box | shot drawn | shot today |
|---|---|---|---|---|---|
| 1200x640 | 860 | 200 | 354 x 200 | 352 x 198 | 558 x 198 cropped |
| 1280x820 | 860 | 246 | 436 x 246 | 434 x 244 | 558 x 198 cropped |
| 1600x900 | 860 | 270 | 479 x 270 | 476 x 268 | 558 x 198 cropped |
| 1920x1080 | 960 | 324 | 575 x 324 | 572 x 322 | 658 x 198 cropped |
| 2560x1440 | 1280 | 432 | 767 x 432 | 764 x 430 | 978 x 198 cropped |
| 3440x1440 | 1582 | 432 | 767 x 432 | 764 x 430 | 1280 x 198 cropped |
| 3840x2160 | 1582 | 648 | 1151 x 648 | 1148 x 646 | 1280 x 198 cropped |

The drawn box is narrower than the crop it replaces at every window size, because a whole
16:9 frame in a height-capped box is narrower than a strip of it that fills the column. The
shot's area is about equal at the default window, larger from 1600x900 up, and 2.9 times
larger at 3840x2160 — and at every size it is the whole picture instead of a horizontal slice.
At the app's smallest window it is smaller in area than the crop; that is the price of showing
all of it.

**The prose measure.** Plus Jakarta Sans, the repository's own file, measured as a single
unwrapped run:

| characters | at Body 13 | at 12 |
|---|---|---|
| 45 | 275px | 254px |
| 60 | 374px | 345px |
| 66 | 410px | 379px |
| 72 | 444px | 409px |
| 75 | 460px | 425px |

And the same paragraph wrapped, counting the characters that fit one line: at a 410px cap, up
to 66 characters at 13/20 and up to 72 at 12/18. At 580px, 95 and 102. At 672px — a paragraph
inside the Stores panel's 720px card — 109 and 117. At the 1302px column the new ceiling
produces, 215 characters on a line. One token at 410 therefore sits inside the 45–75 band at
both prose sizes, which is why the design system states one number rather than one per size.

**The bindings, driven live.** The three caps ship as `ScaledLength` resources bound to
`$parent[Window].Bounds`. Building the same tree with the real converter class and resizing a
headless window between passes: the caps update, the card re-arranges, and no layout cycle is
raised — the window's own bounds do not depend on the card inside it. One quirk that is the
harness's and not the app's: a binding built in code has no XAML namespace table, so
`$parent[Window]` needs a `TypeResolver`; the shipped bindings are compiled and resolve the
type at build time.

## Reproducibility

The harness lives outside the repository and was not kept; the method paragraph is the way to
reproduce it.
