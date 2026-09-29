# Electron parity checkpoint seven

Spending now refreshes on each open, including a return before the query cache expires.
Both surfaces retain captured transaction, year and currency counts when a monetary
aggregate cannot be shown. A single largest transaction keeps its own currency and date;
bundle prices remain whole. Missing coverage dates omit their rows, page dates do not
shift with the local timezone, and licence methods use their readable names.

Twenty-six new component cases reproduce the original account fixtures on desktop and
fullscreen, including licence-only captures, mixed and missing currencies, wallet credit,
dated and undated spending, bundles and all-refunded purchases. Two App cases verify
navigation away from Spending and a new read on return. The complete Electron suite
passed 2,419 cases across 125 files without skips against its isolated Debug backend.
Build and TypeScript checks pass. Two native Spending cases pass for currency boundaries,
focus, narrow panels and enlarged text. Their screenshots were inspected on both surfaces.
Runs use temporary data directories with syncing disabled.

The filter audit checks each original assertion against the existing production filtering
and rendered panel tests. Coverage includes OR within groups, AND across groups and the
rail, unknown years, residual counts, enabled checked zero-count options, hidden empty
groups, arriving GOG/plugin stores, ordered cut chips and removal back to visible games.
Two residual-count assertions were made explicit during the audit.

The frozen inventory remains 2,435 methods across 299 files. It now records 807 ported,
540 retained backend, 13 framework-specific, 911 pending and 164 partial. The migration
completion gate still fails for the remaining contracts. All twelve account view-model
methods and thirteen filter-panel methods now have replacement evidence; the unavailable
statistics matrix also includes the previously missing all-refunded case.

The original full native statistics matrix, controller currency changes, retained currency
selection during refresh and detailed summary-copy checks remain partial. No backend
production code changed in this package. The fifth checkpoint records the latest .NET
and migration-hash checks; the release frontend has not been switched.
