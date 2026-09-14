# PR 18 pipeline audit

Reviewed CI run `34807594859` at commit `aa8413e`, release-build run `34807597144`,
and promo-site run `34807594875`. Evidence comes from GitHub job logs, retained test
summaries, workflow definitions, evidence scripts and the affected test source.

## Results and cause

Windows restore/audit, analyzer build and migration checks passed. All eight test assemblies
finished; the main assembly passed all 4,725 tests. The UI assembly passed 698 of 699.
The sole failure was the Search case of
`FullscreenRowNavigationTests.Mouse_wheel_moves_grid_rows_through_the_same_viewport`.
Its target-row assertion passed; its subsequent `IsAnimating` assertion failed.

The test mixed manual advancement of a 220 ms animation with the real Avalonia frame
scheduler. Headless pointer dispatch can process render frames before returning. On a slow
runner, checking that the transition is still active after dispatch is a timing assumption.
The viewport now accepts an internal frame scheduler. Row navigation and viewport tests own
frame time, while the real-frame scheduling integration test retains the normal scheduler.
The mouse-wheel test checks the start, midpoint and completion using production callbacks.
Additional coverage delivers superseded callbacks late and callbacks after detachment.

## Pipeline review

- Linux native/Proton tests, Windows/Linux release packaging and their smoke checks passed.
- The promo-site static build and link checks passed. Publishing steps correctly skipped on PRs.
- PRs always run the full CI suite. Reuse for other events checks tree, SDK, runner and
  dependency provenance; fresh restore/audit and migration gates remain mandatory.
- Failed test jobs do not upload successful-validation evidence. TRX outputs and summaries
  remain available on failure. There was no output collision or hang timeout in this run.
- The Windows test step took about 16 minutes. Main tests took 15m55s, recommendation tests
  7m42s and UI tests 9m15s, with assemblies overlapping. Heavy real SQLite fixture work
  accounts for much of this: large merge fixtures and repeated migration/backup cycles.
  These durations do not justify weakening durability checks or increasing animation deadlines.

No workflow change or automatic retry is needed for this assertion failure. Local verification
uses a separate Release build followed by `dotnet test --no-build`, matching CI's separation
and preventing test hosts from locking shared scratch build outputs during compilation.
