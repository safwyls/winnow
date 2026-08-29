# Stabilization milestone — opened 2026-08-28

`docs/code-review-2026-08-28.md` reviewed the full working tree and found 17 P1 findings, and
declared the tree not release-ready: the solution does not build, the Hoard-to-Winnow data
migration has paths that select partial data, enrichment overwrites established facts, network
calls block first paint, and the embedded OAuth bridge binds neither origin nor state. This
milestone fixes the release blockers and the foundational issues underneath them. Feature work
resumes after it.

**Governing rule for the milestone: no new feature work lands on top of a subsystem that still
has an unresolved P1 finding.** A subsystem is unblocked when its group-1 package is marked
done here, not when a fix is merely written.

This document tracks placement, not outcomes. Statuses are updated by the orchestrator as work
packages land.

## Group 1 — fixed in this milestone

| Finding(s) | Work package | Status |
|---|---|---|
| F01 | Restore a green build; complete the non-game soft-match admission change the tests already assume | done — verified |
| F02, F42 | Rename-migration safety: staged, validated, atomic promotion, plus a pre-migration backup with `quick_check` and a bounded rotation | done — verified |
| F03, F40 (README only) | Restore fill-only enrichment semantics with conflict tests; correct the README's blanket DPAPI claim to distinguish Epic token protection from plaintext keys | done — verified |
| F04, F49 | Split startup from network: no Steam Web or Epic calls before first paint or on the snapshot tick; rename the sync jobs and comments to describe what they actually run | done — verified. The verification hold was a playtime-sawtooth defect between the local and remote passes, resolved by a `PlaytimeView.LowerBound` clamp in `ExternalIdResolver` applied to both sync jobs (local and web playtime are each cumulative lower bounds, so a lower figure from either is a blind spot, not a correction). Covered by `CrossJobPlaytimeSeriesTests` — five end-to-end tests through the real Steam source, resolver, and bucket query, including the sawtooth reproduction, a genuine-progress guard, and a date-advances-without-minutes case |
| F05 | Bind the WebView OAuth bridge to exact origins and redirect ports, with per-attempt cryptographic state | done — verified |
| F06, F07, F08 | Soft-match sweep: resumable truncation so the tail cannot starve, reconciliation of pending pairs made ineligible by metadata, and batched lookups outside the writer transaction | done — verified |
| F14 | Give views their own cover state so wall, feed, and recycled views stop sharing one mutable object | done — verified; the fix also closed deferred F27 (leased cover path) |
| F17 | Acknowledged journal writes — a note cannot be closed until its write succeeds | done — verified |

F09 is a P1 and is deliberately **not** here: merge execution is a feature, not a repair, and
the rule above blocks it only from landing on unrepaired soft matching. It moves to group 2 with
the soft-match packages as its precondition.

**Adjacent fix, no finding number:** `SqliteConnectionFactory` and `SqliteDatabaseCheck` no
longer leak an open connection when opening a corrupt database. Landed with the F02/F42
package. Done — verified.

## Group 2 — fix alongside the next related feature

Each item is carried by the next piece of work that touches its subsystem. The trigger names
that work; the finding does not get its own milestone.

| Finding(s) | Work package | Trigger |
|---|---|---|
| F13 | Bulk read models replacing UI-thread N+1 queries | Next library or startup view-model change |
| F11, F12 | Update-poll fairness under persistent failure; poll raw build history independently of announcements | Next update-signal work |
| F15, F32, F33, F38 | Recommendation correctness: missing coverage is not negative evidence, score-bound shortlist pruning, unbiased maturity tier, Work collapse before shortlist capacity | Next recommender scoring change |
| F37 | One-sentence explanation contract restored | Same, when explanation copy is next edited |
| F16 | Impressions recorded when a card is actually shown | Next feed presentation change |
| F18 | Transactional repository batches — logical batches commit atomically | Next repository write path added or reworked |
| F09, F20 | Real merge execution; canonicality enforced in the repository, not by callers | After the F06–F08 soft-match packages land |
| F10, F19 | Coherent candidate coalescing that cannot manufacture a play observation; idempotent out-of-order observations | Next ingest or session-attribution change. Residual from F04/F49 verification: when the cross-pass clamp raises minutes above what a source reported, the appended play record still carries that source's name (e.g. `steam-localconfig` labeled with the web's 900 minutes); nothing reads `play_records.source` on a decision path today, so this is forensic quality only, but it belongs to the F10 fix when that lands |
| F39 | Single-instance enforcement so session and scheduler work is not duplicated | Next startup composition change |
| F41 | Persistent rolling diagnostics under the data directory, with redaction tests | Next work that needs post-hoc diagnosis of a soft-failed path |
| F40 (remainder) | Route stored secrets through a platform secret store; migrate plaintext rows on first read | Next credential or settings work |
| F43 | CI gates: restore, build, test, analyzers, migration-hash verification, dependency advisories | Before the next milestone boundary |
| F36 | Startup error boundary around `async void` initialization | With F39, same startup pass |
| F34 | Feed invalidation coalescing so events are not dropped during an active load | Next feed refresh change |
| F21 | Validated bucket threshold invariants | Next bucket or staleness query change |
| F22 | Per-edition release year instead of a Work-level year | Next release-matching change |
| F23 | `Dead` bucket raw facts and query branch | When the `Dead` bucket is implemented |
| F24 | Achievement support past schema creation | When achievements become scope |
| F25 | Steam manifest path containment; a bad library root must not abort startup | Next Steam ingest change |
| F26, ~~F27~~, F28 | Cover pipeline: negative cache reopens on capability change, ~~shared work does not inherit the first caller's cancellation~~ (F27 closed by F14's per-view cover state), bounded responses and decoded dimensions | Next cover loading change |
| F29 | Metadata cache payload version; corrupt entries cannot poison repeated runs | Next enrichment cache change |
| F31 | Persist the Epic owned-library cache beyond the process | Next Epic ownership change |
| F35 | Accessibility names and control hierarchy | Next view-authoring pass |
| F50 | Single authority for dormancy brightness (`0.60` vs `0.68` conflict) | Next dormancy or token change |

## Group 3 — pre-beta hardening

Not blocking a milestone; blocking beta. Each lands with the acceptance test named in its
finding.

| Finding(s) | Item | Note |
|---|---|---|
| F44 | Parser size and depth limits on storefront-owned files | Needs oversize and deep fixtures, sanitized as usual |
| F47 | Reduced-motion coverage completed across animated surfaces | Currently partial, not absent |
| F48 | Unread-update accessible copy includes counts | Flare-marked counts are currently visual only |
| F46 | Immutable hash verification for shipped migrations | Enforces the append-only rule the convention already states |
| F30 | Cross-platform session support | Session collection is Windows-only; the flywheel depends on it |
| F45 | `DateTimeKind.Unspecified` handling at persistence boundaries | Reject it, or standardize on `DateTimeOffset` |
| — | Documentation and naming consistency sweep | Honors CLAUDE.md: the common noun "hoard" stays where the premise uses it |
| — | `SoftMatchSweepOptions.MaxComparisons` rejects zero and negative values | Same class as F21: a validation gap that lets a zero truncate the sweep forever without progress |
| — | Remove synchronous `PRAGMA quick_check` from the steady-state launch path | Runs on every launch while the legacy data directory exists; small today, but it is new pre-window I/O that persists until the user deletes the old directory |

## Re-review cadence

- **Exit gate for this milestone:** a green full `dotnet build` and `dotnet test` run, with a
  regression test for every group-1 P1. A fix without a test does not close its row.
- **Status:** all group-1 rows are done and verified. Full suite: 1,737 + 74 + 70 = 1,881
  tests passing, zero warnings.
- **Re-review at the next milestone boundary**, scoped to the group-1 subsystems plus whatever
  group-2 items their features carried in.
- **A further full-repository review before beta**, after group 3.
