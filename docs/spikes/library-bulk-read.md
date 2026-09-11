# Library bulk-read measurement

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

Measured on 2026-09-06 in Windows Release mode with .NET 10.0.11. The fixture uses a
temporary, migrated SQLite file with pooling disabled, matching the repository tests.
It contains 1,000 works, releases, ownerships and Steam external IDs, plus one manual list.
No launcher files, live library or network services are used.

`LibrarySnapshotTests.Cached_library_read_measurement_compares_legacy_N_plus_one_with_bulk`
warms the snapshot query once, then times the previous library data-read shape: buckets,
ownerships, works, one release query per work and one external-ID query per release.
It then times the bulk snapshot against the same database. Both paths fully materialize
their results. The bulk snapshot also includes all lists and list items.

| Cached data-read phase | Time | Repository leases |
|---|---:|---:|
| Previous per-item reads | 3,705.3 ms | 2,003 |
| Bulk snapshot | 20.6 ms | 1 |

The bulk read uses one multi-result command inside a deferred read transaction. The test
asserts result size and lease counts; it reports elapsed time without a timing assertion,
because machine load can change it. A separate equivalence test compares bucket facts,
works and ownerships with the existing repository reads and checks list order.

This measures the cached library data-read phase, not total process startup, migrations,
cover decoding or UI layout. Ancillary facet, identity, pin and storefront-cache reads
remain fixed-count bulk operations. The pre-window appearance bootstrap is unchanged.

Reproduce from the repository root:

```powershell
dotnet test tests/Winnow.Tests -c Release --filter FullyQualifiedName~LibrarySnapshotTests -p:BaseOutputPath=C:\Temp\winnow-task6\ --logger 'console;verbosity=detailed' --verbosity quiet
```
