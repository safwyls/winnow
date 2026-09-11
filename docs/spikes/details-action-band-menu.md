# Spike: Detail modal action band menu

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

**Settles:** how the action band's menu behaves for the keyboard (TASK-125). Measured 2026-09-05.

## Method

A throwaway console project outside the repository, at the app's own Avalonia 11.3.20. It
references the built `Winnow.dll` and loads `avares://Winnow/Themes/controls.axaml` and
`avares://Winnow/Themes/tokens.axaml` exactly as `App.axaml` does, so the styles under test
are the shipped ones and not a copy. Band 3's strip and the menu markup were copied verbatim
out of `GameDetailsView.axaml`, with compiled bindings on as the app has them, over a stand-in
view model. Key presses are delivered as `KeyDown` then `KeyUp` routed events on whatever
holds focus, so both passes run identical code.

It ran twice: headless (Skia, `UseHeadlessDrawing = false`, the repository's own Plus Jakarta
Sans), where Avalonia hosts the popup in the window's overlay layer; and on the real Win32
backend, where the popup is its own `PopupRoot` window, with the probe window parked
off-screen. Both row sets were walked: not-installed, where `Open folder` is absent, and
installed. The application itself was not run. Every result below was identical on both
backends.

## Findings

- `FlyoutPresenterClasses="actions"` reaches the `MenuFlyoutPresenter` from an
  application-level style sheet even inside a `PopupRoot`: ground `#1D3437`
  (`SurfaceRaised`), edge `#2B4A4C` (`Line`), padding 4, rows 10,7.

- **The keyboard's position in a menu opened from a button is `:focus`, not `:selected`.**
  On open, every row reported `IsSelected` false; the first drawn row reported `IsFocused`
  true and carried `:focus` and `:focus-within`. A treatment that answers `:selected` alone
  therefore leaves unmarked the one row a keyboard user meets first. The arrow walk then
  drives `:selected` as it always did. This is why the shipped treatment now names both.

- The mark is drawn in the item template: `PART_LayoutRoot` takes `#254042` (`SurfaceHigh`)
  and a `#4DE8C2` (`Volt`) edge on a border held at 2,0,0,0 in every state, so the row does
  not reflow as it lights up. No adorner layer is involved.

- A row that is not drawn is skipped by the walk in both directions, and the walk wraps
  rather than dead-ending. With `Open folder` absent the walk is `Wrong game?`,
  `Edit details`, `Hide`, and back.

- Escape closes the menu and focus returns to the trigger button. Enter runs the row the
  keyboard is on, closes the menu, and returns focus to the trigger. Both come free; neither
  needed code of ours.

- **A `MenuItem` raises its `Click` event already marked handled** (observed: `Handled=True`,
  bubbling, sourced at the row). A handler wired the ordinary way -- including XAML's
  `Click="..."` -- never runs. A XAML-wired handler stayed at zero invocations while the
  same event drove the row's command. Only `AddHandler(..., handledEventsToo: true)` sees it,
  which is how the view attaches the three rows that do work beyond their command.

- **A name inside a flyout does not reach a code-behind field.** The trigger button resolved
  from its field; the rows inside its flyout were null. They are found through the trigger's
  own `Flyout.Items`.

- The menu card measures 134 x 103px at three rows and 134 x 134px at four, with the real
  font -- about 31px per row. It floats over the modal rather than sitting in a row of the
  card's grid, so it costs the strip no width and the modal no height.

- The strip itself was not re-measured, because it did not change: the same four controls in
  the same classes, and the trigger keeps the `More` label
  `docs/spikes/details-action-band-width.md` already measured. What went away is the `Close`
  state, so the strip's widest form is now that spike's 357px (`Install`) rather than its
  361px.

**Honest limit:** the harness replicates the band's markup rather than instantiating the real
`GameDetailsView`.

## Reproducibility

The harness lives outside the repository and was not kept. The method paragraph above is the
way to reproduce it.
