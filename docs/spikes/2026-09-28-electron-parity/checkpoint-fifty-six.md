# Details structure and retired chart audit

Desktop Details now keeps expansion/base-game relationships in Overview and linked editions
and variants in Library. Fullscreen retains all relationships in Library, following its
separate source composition. Both surfaces retain the child-specific separation operation.

## Verification

- Build/typecheck passes: `.tmp/details-structure-build.log`.
- All 3,324 component/live API cases pass across 162 files without skips in 54.22 seconds:
  `.tmp/details-structure-integration.log`.
- All 18 native populated/empty Details cases pass in 1.4 minutes:
  `.tmp/details-structure-native-first.log` and
  `.tmp/details-structure-native-first/electron-rendered-results/results.json`.
- All 23 native desktop/layout/gallery cases pass in 1.6 minutes:
  `.tmp/details-structure-native-final.log` and
  `.tmp/details-structure-native-final/results.json`. Three overlap with the first batch,
  giving 38 distinct cases. Neither batch retries or skips tests.
- Desktop checks preserve the source five-tab ordering and exclusive content ownership at
  both original populated fixture sizes. Component cases additionally exercise expansion,
  same-game and variant links while excluding retracted and unrelated relationships.
- Native text-node checks reject the faint token and contrast below 4.5:1 against the
  composited flat-card background. They cover all populated desktop tabs and empty/populated
  fullscreen Updates, Journal and Library at both source scale boundaries. Disabled controls
  are exempt, matching the original rule. This default-palette check complements the existing
  separate theme/artwork contrast matrix; it does not measure arbitrary artwork pixels.
- Every checked reading region reserves a stable scrollbar gutter and trailing padding.
  The last keyboard-focused screenshot clears the horizontal scrollbar and stays within
  the strip. Reception remains inline; More creates one action popup inside the original
  modal/window. The separate screenshot gallery returns focus without creating an inline hero.
- The rendered Overview relationship rows were inspected at 1200×640. Existing full-window
  card-cap and gallery geometry matrices also pass after the section change.

## Retired source projection

At frozen source `cf45d9f1127243a987d3cf6e664a32fc767ecb67`, neither desktop nor fullscreen
instantiates `PlayAxis` or binds the `Axis` properties. `git grep` across all source views
finds only the control definition. `DetailsModalStructureTests` explicitly prohibits that
control and requires `ActivityTrackerView`. Compatibility calculations remain in
`GameDetailsViewModel`, but no presentation consumes their result.

The seven `PlayAxisSeriesTests` therefore describe an already unused Avalonia rendering
projection. Their classification records each original assertion and the frozen source
evidence. They remain in the .NET suite and are not counted as executed Electron tests.
The active `ActivityTimelineSeries` and its missing/zero coverage, baseline, sessions and
update-marker rules have separate TypeScript and rendered coverage.

Seven structure methods gain Electron evidence; seven retired projection methods receive
an explicit framework-specific disposition. The inventory is 1,264 ported, 625 retained
backend, 30 framework-specific, 420 pending and 96 partial out of 2,435. Full parity, the
complete native aggregate and the primary Electron release cutover remain open.
