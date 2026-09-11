# Spike: Detail modal action band width

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

**Settles:** the action band overrun reported in design-system.md §10.10 (TASK-123). Measured 2026-09-05.

## Method

A throwaway console project outside the repository, at the app's own Avalonia 11.3.20,
configured `.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`
so that text is shaped and measured by Skia rather than by the headless stub -- that is the
part that makes the widths real. It loaded the repository's own Plus Jakarta Sans files, and
the `Button.link` and `Button.launch` style declarations plus the card's
`MinWidth`/`MaxWidth`/margins/column definitions were copied verbatim out of
`src/Winnow.App/Views/GameDetailsView.axaml`. Every figure below is `DesiredSize` or `Bounds`
off a real measure-and-arrange pass, not arithmetic on padding values. The app itself was not
run; this harness is what stands in for running it.

## Findings

The right column measures **420px** at the card's `MinWidth` 700 and **580px** at its
`MaxWidth` 860. TASK-119's estimate of 422 and 582 did not subtract the card's own 1px border
on each side. The card is centred and content-sized between those two bounds, so 860 is only
reached when the content asks for it.

Control widths, each measured alone:

| Control | Width | Height |
|---|---|---|
| Play | 67 | 31 |
| Install | 77 | 31 |
| Store page | 88 | 30 |
| All patch notes | 108 | 30 |
| Open folder | 94 | 30 |
| Wrong game? | 104 | 30 |
| Edit details | 88 | 30 |
| Hide | 51 | 30 |

The 1px height difference on the launch button is its 2px border; the link buttons carry 1px.

The seven-control strip (installed set): **660px**. 240px past the 420px column; 80px past the
580px one.

The six-control strip (not-installed set, no `Open folder`): **566px**. 146px past the 420px
column, but inside the 580px one with 14px to spare. TASK-119's estimate put this set at
574--605px and claimed the full set overran at every card width; the first is wrong and the
second is true only of the installed set.

**The overrun is a clip and not a wrap, and that was arranged rather than inferred.** Given the
420px column the horizontal `StackPanel` reports a desired width of exactly 420 -- it clamps to
the constraint -- while still arranging its last child with its right edge at 660px, 240px
outside the column. The desired size alone would have hidden the fault; the arranged bounds are
what show it.

The strip as it now ships -- primary action, `Store page`, `All patch notes`, `More` --
measures **357px** with `Install` and 347px with `Play`, and 361/351 while the disclosure reads
`Close`. It fits the 420px column with 59 to 73px to spare.

The disclosed list measures **104 x 144px** across its four rows. A fifth row costs 38px of
height and 0px of width, which is the whole argument for the list being vertical.

**Note (2026-09-05).** The disclosed vertical list and the `Close` state of the trigger were
replaced the same day by a menu. The menu measurements are in
`docs/spikes/details-action-band-menu.md`. The strip and column figures above still stand.

## Reproducibility

The harness lives outside the repository and was not kept. The method paragraph above is the
way to reproduce it.
