# Architecture review resolution — 2026-09-11

> Dated delivery and verification record. The counts and results below apply to the recorded
> commits. Current behavior is stated directly in [the architecture](../game-library-design.md),
> and remaining scope and validation in [ROADMAP.md](../ROADMAP.md).

The 38 findings in [the original review](architecture-review-2026-09-10.md) have been addressed
through TASK-189–226. The related replay and dormancy tasks, TASK-135 and TASK-27, are also
complete. TASK-227 coordinates delivery. The original review remains a record of the inspected
baseline; current architecture and behavior belong in the governing specifications.

## Corrections by domain

| Domain | Findings / tasks | Result |
|---|---|---|
| Identity and metadata | R01–03, R08; TASK-189–191, 196 | Undo revalidates graph invariants; values, provenance and pins commit atomically; manual corrections use the authoritative operations. IGDB observations carry a mapping revision, and reassignment retires stale projections. |
| Launcher and account evidence | R04–07, R10, R13–15; TASK-192–195, 198, 201–203 | Steam/Epic caches identify the captured account and credential generation. Galaxy reads a coherent snapshot. Incomplete Epic scans preserve installations. Complete Steam inventory evidence is separate from positive local observations. Account-page identity survives imports and exports; known GOG registry installations reconcile conservatively; review caches retain only the required lifecycle projection. |
| Update state | R09; TASK-197 | Production composition wires acknowledgement on both surfaces; grouped unread counts use consistent update and last-play semantics. |
| Schema integrity | R11–12; TASK-199–200 | Unsupported future journals are refused before startup writes; one append-only checksum manifest serves tests and CI. |
| Refresh orchestration | R16; TASK-204 | Startup, scheduled and account-triggered ownership changes share serialized downstream operations and committed-state publication. IGDB credential refresh reuses the relevant steps. |
| Provider infrastructure | R17–18; TASK-205–206 | Shared transport bounds attempts, total time and payloads while keeping provider retry/rate policies explicit. Corrupt, incomplete, incompatible and stale cache data have tested authority rules. |
| Recommendations and extensions | R19–21, R24; TASK-207–209, 212, 135 | Resolved-game evidence and playable-copy selection agree; cold uninstalled libraries receive an honest shelf; feedback identity survives filtering. Optional providers cannot hold up initial built-in shelves. Frozen replay compares tunings with separate later outcomes and rejects unsupported historical reconstruction. |
| Artwork | R22–23, R37; TASK-210–211, 225, 27 | Missing art expires and retries; library, merge and preview share selection policy. Queue/decode limits, lease ownership and shutdown cover cancellation and failure interleavings. Dormancy has one numeric authority with unchanged rendering values. |
| Sessions | R25; TASK-213 | Qualifying monitored sittings checkpoint a durable identity, recover across restarts and finish the same row. Unknown end times remain unknown; an unobserved interval is not fabricated. |
| Startup | R26; TASK-214 | Configuration and host construction enter the same logged startup failure boundary as migrations and hosted services. Explicit data-directory refusal remains distinct. |
| Shared application behavior and presentation | R27–34, R38; TASK-215–222, 226 | Details publish coherent snapshots and retain drafts; obsolete reloads cannot overwrite newer settings. Missing filter choices remain clearable. List operations commit atomically. Prompt, year and note semantics agree across desktop/fullscreen. History uses bounded paging and worker reads. Runtime accessibility and focus tests cover code-built fullscreen controls. |
| Documentation and packaging | R35–36; TASK-223–224 | Active specifications match current sources, the old mock is explicitly historical, and paired agent charters agree. Plugin-only changes trigger packaging verification and bundled packages validate their manifest, assembly and entry type. |

Independent integration checks also corrected cover bitmap/cancellation races, queued feed
backfill loss, optional deadline handling during shared reads, and fullscreen disposal of a
borrowed feed. These corrections and their failing-before evidence are attached to the
owning tasks and [verification record](spikes/architecture-fixes-2026-09-10.md).

## Effect on application behavior

Winnow keeps its local-first product model, module layout and two presentation paths. The
largest visible changes correct existing inconsistencies: recommendations can choose a better
owned copy, account filters require stronger evidence before excluding a game, unread counts
and acknowledgement agree, incomplete scans retain installations, and session recovery avoids
duplicate history. These changes can alter particular counts, rankings and displayed states.

Long weekly Activity histories load 50 events at a time, with **Load more** retaining access to
older events. Optional recommendation shelves arrive after built-in shelves. Error states offer
retry while retaining committed content. Dormancy's saturation, brightness and hue values are
unchanged. The replay tool is separate from the app and does not change production tuning.

Six migrations, 0033–0038, extend the existing database. Shipped migrations remain unchanged.
The migration tests and temporary-database checks establish the covered upgrade behavior;
no production library was used for interaction during this work.

## Verification and remaining work

The [consolidated evidence](spikes/architecture-fixes-2026-09-10.md) records Release builds,
Windows tests, real Linux process checks, migration integrity, packaging and failure-path
regressions: 5,285 Windows tests and both Linux process tests passed; all 38 migration hashes
and clean self-contained Windows/Linux publishes passed. Windows publishing used the normal
ReadyToRun setting. Implementation commits are `bde7ca4` and `e84ba07` on
`codex/architecture-review-fixes`. Separate records describe [large-history measurements](spikes/large-history-read-responsiveness.md)
and [frozen replay and its limits](spikes/feed-replay.md). Headless interaction tests exercise
both actual presentation paths; they do not establish physical TV or controller usability.

TASK-4 is at the end of the backlog with `needs-user`. Its remaining acceptance criteria need
a real controller and the intended TV/display at normal seating distance: every operation must
be reachable, focus visible, and completion possible without a mouse or keyboard. Record the
controller model, operating system, display setup and any failing screen or operation. No
product-policy decision is waiting on the user.

Steam collection import is explicitly deferred in DRAFT-1; the review corrected a stale claim
that it already existed. The other pre-existing feature/research items listed in the original
review retain their separate scope. Live provider sign-in, installer smoke tests on disposable
release runners and real Wine/Proton game compatibility are not inferred from these checks.
