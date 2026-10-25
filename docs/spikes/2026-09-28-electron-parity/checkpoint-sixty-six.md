# Names, editions and header preferences checkpoint — 2026-09-30

TASK-381.11 covers nineteen frozen source contracts. The user authorized a sequential
batch through TASK-381.20; each task retains a separate verified milestone.

## Implementation

Saved Header store preferences now project the visible owned member's title and cover
through Library, Details and theme cover consumers. Canonical identity, editable metadata,
release year and hero artwork stay attached to the group root. The fullscreen backdrop
uses the selected cover when its canonical hero is unavailable. Explicit saved artwork
remains group-wide. Projection waits for matching live group links and leaves the API
cache unchanged, so independently refreshed library and workspace snapshots cannot
replace a newly grouped title with stale member data.

Desktop selectors retain their saved value and focus after a write. Fullscreen choices
stay open while saving, show failures inside the dialog and return focus to Header store
after success. Both an HTTP conflict and an HTTP 200 response with `changed: false` retain
the original selection and offer focused recovery. The latter occurs when ownership
disappears without changing the review revision. Fullscreen A Choose and B Back hints
use complete normalized SVG glyphs. Desktop selectors retain a 160px minimum width.

User names, search/sort precedence, provisional names after Automatic reset, member labels
and edition separation already had the required behavior. New evidence exercises those
paths without replacing the backend implementation. The Electron README and theme guide
describe the optional header cover identity and preserved canonical metadata in place.

## Source equivalence

`migration-names-headers.json` maps eighteen ported methods and one retained backend
method against the frozen source inventory. The retained method directly tests repository
eligibility for expansion children, unlinked games and missing stores; a new HTTP case
also checks refusal without history, revision or record changes.

Header fixtures preserve Steam title, GOG title and year 2011 without adding external IDs,
platforms or play records. User-name fixtures retain Automatic Name, Storefront Title,
Almanac, Zenith and the original Prey identities. Member-label assertions retain identical
work IDs in the source's synthetic faces; labels never depend on database IDs. Electron's
existing provider wording is Epic Games where Avalonia uses Epic.

The guarded test backend runs the actual ReleaseEditionEvidenceAcquirer and
GamesDbIdentitySyncService with the original frozen external-service substitutes. Its
Epic offer and page-ID lookups reproduce the source, including the missing page result.
Native checks require one eligible link, no pending suggestion, one tile with two owned
copies, separation to two tiles, and a protected subsequent sync. External IDs, original
records and identity history remain inspectable through actual HTTP and SQLite.

Distinct cached automatic covers supplement the original fixture to verify preferred
artwork. They do not use uploaded saved covers, which intentionally apply to the whole
group. Native tests intercept OS dispatch only and keep the real preload, API and database.

## Verification

The final production build and typecheck pass in `.tmp/task38111-build-final2.log`.
All **3,595 component/live API cases across 179 files** pass without skips in **89.61
seconds**, recorded in `.tmp/task38111-components-final2.log`. The prior run exposed the
workspace publication timing bug and outdated fixtures; the final run includes their
corrections. Changed Electron files pass Prettier and whitespace checks.

All seventeen original GroupHeaderPreferenceTests, UserSetNameTests and MergeMemberLabelTests
pass; all eleven new NameAndHeaderParityTests pass through authenticated HTTP and temporary
SQLite. Logs are `.tmp/task38111-source-tests.log` and `.tmp/task38111-api-tests.log`;
TRX files are in `.tmp/task38111-dotnet-results/`.

The focused names/header matrix passes 38 cases, including original fixtures on both
surfaces, delayed publication, refused saves, unavailable ownership, regrouping, undo and
preferred-store launch priority. All 100 existing Details cases pass alongside it in
`.tmp/task38111-projection-refresh-components.log`. The existing API Settings fixture now
supplies the shared workspace query and verifies it succeeds exactly once; its original
hidden/manual titles, dates and counts remain unchanged.

**Fourteen distinct native cases pass**, seven per surface. Passing evidence is in
`.tmp/task38111-native-final2-results/results.json` (seven desktop and two fullscreen),
`task38111-native-final4-results/results.json` (desktop edition at its original 1100×900
size and fullscreen preferred cover), and `task38111-native-final5-results/results.json`
(fullscreen edition and three name workflows). Other desktop header cases use the original
1000×850 viewport; fullscreen uses 1920×1080. Failed intermediate attempts exposed harness
assumptions about landmarks, metadata save navigation and fractional screenshot borders;
the final targeted runs preserve all product assertions and capture the visible content.

Screenshots in those directories show saved choices, focused refusal recovery, complete
controller hints, preferred covers, edition-owned rows and retained provisional names.
Visual review confirms desktop selector bounds and the fullscreen cover fallback; the
shared merge-sheet typography limitation below remains visible.

The migration report validates with **1,401 ported, 637 retained backend, 32
framework-specific, 301 pending and 64 partial** methods. **365 remain unresolved**, nineteen
fewer than checkpoint 65. `.tmp/task38111-migration-report.log` records the result. The
complete migration gate remains incomplete because later tasks are still outstanding.

## Remaining validation

Fullscreen merge sheets still use compact option and description text. This existing
shared typography gap belongs to TASK-381.12 and is visible in the header screenshots;
this checkpoint does not claim its correction. The next task includes the shared sheet
and will recheck the header choices after adjusting typography.

Controller checks simulate standard Gamepad API frames in the real Electron runtime.
They do not establish physical-device behavior or TV-distance readability. The complete
.NET suite and packaged installers are outside this focused checkpoint. Every interactive
run uses a disposable data directory; no real library or launcher files are modified.
