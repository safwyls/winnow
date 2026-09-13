# Gameplay statistics validation — 2026-09-12

TASK-244 implements the first gameplay scope assessed in
[the Stats recon](stats-store-and-gameplay-recon.md). Current behavior belongs in the build
and visual specifications; this document records the test fixture and measurements.

## Aggregate performance

`GameplayStatsRepositoryTests` creates a migrated temporary SQLite file containing 10,000
ownerships and 100,000 sessions. Each timed call uses a fresh, unpooled connection. Timings
exclude schema migration and fixture writes. The first query is not a controlled cold-cache
measurement: seeding has already touched database pages. No live library or store files are
used, and no time threshold is asserted against CI hardware.

Local Windows Debug test-host results:

| Period | Sessions contributing | First query | Repeated queries | Returned chart rows |
|---|---:|---:|---:|---|
| 30 days | 43,199 | 814.0 ms | 803.7 / 857.1 ms | 5 periods, 10 games, 2 stores, 4 length bands |
| 90 days | 100,000 | 2,043.4 ms | 1,974.8 / 1,966.2 ms | 13 periods, 10 games, 2 stores, 4 length bands |

The repository returns bounded aggregates through six SELECT results in one read snapshot.
It does not return the complete session history to the UI. These timings measure repository
calls, not dispatcher latency, chart layout or physical-controller response. They establish
neither production-library performance nor native Linux performance.

Reproduce the measured fixture from the repository root:

```powershell
dotnet test tests/Winnow.Tests/Winnow.Tests.csproj `
  -p:BaseOutputPath=C:\Temp\winnow-gameplay-data\ --no-restore `
  --filter FullyQualifiedName~GameplayStatsRepositoryTests `
  --logger 'console;verbosity=detailed'
```

## Correctness coverage

The temporary-file repository tests cover visible ownership scope, actual store attribution,
resolved game folding, interval clipping, duplicate evidence, fractional timestamps, DST
bins, invalid and open sessions, session-length boundaries and median, cancellation and
existing transaction leases. View-model tests cover stale completions after visibility or
identity changes, removed-store selection, search independence, deactivation and local dates.

## Presentation checks

`GameplayStatsInteractionTests` exercises store and period choices, custom-date entry,
keyboard section switching, fullscreen trigger navigation and controller text-entry requests.
It checks separate surface state and failed, retried and cancelled reads. Existing account
summary tests continue to check currency switching and focus preservation in Spending.

Rendered fixtures were inspected at desktop widths of 1,200 and 600 pixels, fullscreen
widths of 1,920 and 1,280 pixels, and inside the actual 1,920×1,080 fullscreen shell at
100% and 140% text size. The shell checks exercise its native selected-control underline,
safe margins and real text scaling. Captures include the overview, charts, current library
composition and custom-date controls. Narrow layouts stack chart sections; longer pages
scroll without horizontal overflow. No physical controller or production library was used.

The solution builds with zero warnings. Focused repository, view-model, identity-inventory
and UI checks pass. Broader runs retained two unrelated known failures: the discarded
accessible name on `PluginSettingsView.axaml:33` and the Fullscreen Settings expectation
of `›` where the existing action says `Open`. The new repository's inventory entry was
added after the broad run and its architecture tests passed on rerun.
