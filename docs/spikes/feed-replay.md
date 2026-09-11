# Frozen feed replay

The offline tool captures the current library and later compares two scorer configurations
against recorded outcomes. It does not alter the app or its tuning defaults. Keep captures
local: each contains the complete library database, including account and settings data.

## Capture and compare

Build `tools/Winnow.Replay/Winnow.Replay.csproj` in Release. The commands below use `dotnet run`;
the built `Winnow.Replay.dll` accepts the same arguments after `dotnet`.

```powershell
dotnet run --project tools/Winnow.Replay -c Release -- capture C:\path\winnow.db C:\replay\before
# After subsequent feed use and recorded outcomes:
dotnet run --project tools/Winnow.Replay -c Release -- capture C:\path\winnow.db C:\replay\after
dotnet run --project tools/Winnow.Replay -c Release -- compare C:\replay\before C:\replay\after baseline.json candidate.json --k 10
```

Destinations must be new directories. Capture opens the source read-only, establishes a
SQLite read transaction, records its UTC instant and uses SQLite's backup API to include the
committed WAL state consistently. It publishes `library.db` and `capture.json` together.
Comparisons verify the copied database bytes against the manifest and work in disposable
private copies. They never migrate a captured database; its complete migration journal must
match the evaluator's schema. Retain the evaluator build with long-lived experiments.
The manifest is local provenance, not a signed attestation of the machine clock.

Each tuning file names its configuration. Omitted tuning properties retain model defaults;
unknown properties are rejected to catch misspellings.

```json
{ "Name": "baseline", "Tuning": {} }
```

```json
{ "Name": "installed-emphasis", "Tuning": { "WeightInstalled": 0.10 } }
```

`--as-of` may specify the manifest's exact UTC instant; any other instant is refused.
`--through` limits the outcome evidence to a UTC instant after replay and no later than the
outcomes capture. Both require `Z` or `+00:00`. `--window-days` sets the common label window
(default 3), independent of either tuning's input-feedback window. The full JSON report goes
to stdout. Exit 2 means invalid input/refused evidence; caller cancellation returns 130.

## Interpreting the report

The report ranks the full captured eligible library for each tuning, then computes metrics
on games with positive or explicit-negative outcomes. It never treats an unshown game as
a rejection. `JudgedCoverage` is judged games divided by ranked games. Precision@k is null
when fewer than k judged games remain; MRR is null when none remain, and zero for a judged
cohort with no positive. With one capture there is one query, so MRR is its reciprocal rank.
The assembly and snapshot hashes, thresholds, complete tunings and seed identify the inputs.

Exposure bias remains: only games the old feed actually surfaced can receive labels.
Weak negatives are listed but excluded because old surfacing rows do not prove actual
visibility. Impression dates cannot establish same-day action order, so those actions are
excluded. Labels require a matching unique external identifier; manual entries without an
anchor and conflicting mappings remain unobserved. Later identity edits do not redefine the
captured game groups. No result claims causal lift or whole-library ranking quality.

## Deterministic verification

`ReplayTests` uses migrated temporary SQLite databases and fake clocks. Its two-tuning fixture
contains an installed positive, a bounced negative, an ignored impression and an unshown game.
The default weights rank the negative above the positive in the judged cohort: precision@1
is 0 and reciprocal rank is 0.5. Raising only the installed weight to 2 reverses those judged
positions: precision@1 and reciprocal rank are both 1. Coverage is 0.5 for both. This weight
is a test instrument, not a suggested production default or an empirical product improvement.

Additional cases establish that a WAL write committed after capture's read transaction starts
stays out; later outcomes and current ownership/title/playtime changes cannot alter captured
rankings; modified bytes and backdated/advanced requests are refused; future observations are
rejected; and same-day, inferred, revoked, conflicting and unanchored outcomes do not acquire
judged labels. The command parser is exercised through capture, compare and a refused date.
`ReplayTimeBoundaryTests` independently verifies lifecycle, patch, play, session, snapshot,
endorsement, verdict and aggregate boundaries through production repositories and the engine.

Windows Release verification on 2026-09-11 passed 183/183 recommendation tests, followed by
19/19 replay tests after adding schema-refusal and cancellation cases (185 distinct current
recommendation cases verified). Related gates passed 72/72 feedback/history/lifecycle checks,
108/108 bucket/account/acknowledgement/history checks, and 4/4 production desktop/fullscreen
feed composition cases. TRX files are `recommend-replay.trx`, `replay-final.trx`,
`replay-query-boundaries.trx` and `ui-replay-composition.trx` under each test project's
`TestResults` directory. No real user outcomes or live service calls were used.
