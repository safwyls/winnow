# Dawn control contrast

Measured 2026-09-16 for TASK-309, on the two bundled Dawn palettes at their default
opaque setting. Measurements use sRGB relative luminance and `(lighter + .05) /
(darker + .05)`, as implemented in `Colorimetry.Contrast`.

## Findings

The application requested Fluent's Dark variant for every palette. This left ordinary
controls using dark framework states while custom Winnow controls used light colors.
Both Dawn themes now declare `variant: light`; files without the field infer polarity
from ground and text luminance. Export includes the resolved variant.

Bright fills were also reused as foregrounds. Rosé Pine Dawn's gold measured 2.05:1
on Ground and 2.16:1 on Surface. Its Amber icon measured 2.74:1 on Surface and Azure
link text 3.30:1. Both destructive hover labels were below 4.5:1.

The fix retains accent fills and derives darker, hue-preserving foregrounds for light
themes. Desktop and fullscreen labels, action glyphs, sort indicators, selection and
focus outlines consume these resources. The Dawn `Line` colors and destructive fills
were adjusted separately. Dark-theme foreground tokens preserve the original colors.

## Palette results

“Worst neutral” is the minimum over Well, Ground, Surface, SurfaceRaised and SurfaceHigh.

| Measurement | Rosé Pine Dawn | SilkCircuit Dawn |
|---|---:|---:|
| VoltForeground, worst neutral | 4.55:1 | 5.55:1 |
| AmberForeground, worst neutral | 4.50:1 | 4.51:1 |
| AzureForeground, worst neutral | 4.51:1 | 4.82:1 |
| DangerForeground, worst neutral | 4.50:1 | 4.54:1 |
| Line, worst neutral | 3.29:1 | 3.34:1 |
| Destructive normal label | 5.14:1 | 5.22:1 |
| Destructive hover label, before → after | 3.13 → 4.81:1 | 3.97 → 5.65:1 |
| Destructive pressed label | 5.51:1 | 7.05:1 |

## Verification method and limits

`DawnThemeContrastTests` checks actual derived palette values, filled action states,
legacy inference, explicit variants, export round trips and dark-theme accent preservation.
`DawnControlContrastTests` drives real Avalonia templates through pointer hover/press and
keyboard focus, resolves their painted foreground/background colors, and checks desktop
primary/quiet/destructive actions, selected controls and fullscreen actions. It also switches
Fluent controls Dark → Light → Dark. Captures can be saved with `WINNOW_UI_CAPTURE_DIR`.
Local captures in `C:/Temp/winnow-309-captures` were inspected for both Dawn palettes.

This verifies opaque controls and their tested interaction states. It does not certify
all text over arbitrary game artwork, nonzero window transparency, custom user palette
overrides, or the light-theme dormancy encoding. Local files with a bundled theme's ID
continue to override the bundled palette; they receive variant inference and derived
foregrounds, but keep their authored border and destructive fill colors.
