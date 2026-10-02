# Spending dashboard checkpoint — 2026-09-29

Spending now shares one currency selection between its charts and detailed breakdown.
Desktop and fullscreen keep independent selections for the frontend session. Refresh
retains a currency still present in the new capture; removal selects the first remaining
currency. Summary amounts continue to show each currency independently.

Year bars extend from zero, including negative and zero-valued years. Product composition
uses a donut with visible amounts and shares in existing theme inks. Negative categories
withhold the donut and retain the signed breakdown. Desktop details use a disclosure;
fullscreen uses a scrolling reading panel with Back and controller focus restoration.
Fullscreen statistics, headings and explanatory text now meet their intended sizes without
requiring increased text scaling.

## Verification

| Check | Result |
|---|---|
| Electron build and TypeScript | Passed; `.tmp/spending-final-build.log`. |
| Full component/live-backend suite | 2,442 cases in 126 files passed, no skips; `.tmp/spending-final-integration.log`. |
| Focused account/App suite | 76 cases passed; `.tmp/spending-dashboard-components.log`. |
| Native Spending and core parity | 24 cases passed together; `.tmp/spending-final-native.log`. |
| Source inventory | 838 ported, 540 retained backend, 13 framework-specific, 881 pending, 163 partial, from 2,435 original methods. |

The native Spending matrix includes the original single/mixed transaction fixtures on both
surfaces, backed by temporary SQLite and the production API. It checks the exact exclusion
note, 33.3% refund share, 50% bundle share, currency boundaries and 22/40px statistic minima.
The original fake-repository chart fixture is substituted only at the test main-process
HTTP boundary. It uses isolated desktop panel widths 1,200/600 and fullscreen window widths
1,920/1,280, with 140% text at the smaller fullscreen width. Tests verify exact composition,
peak/average values, controller Right/Accept, selector focus, refresh retention, selected
detail rows, zero-baseline geometry, reading-panel bounds and return focus.

Desktop and fullscreen screenshots were inspected, including the final 1,280-pixel
fullscreen composition and reading panel at 140% text. This caught undersized explanatory
copy after the first passing native run; corrected sizes have explicit native assertions.
Screenshots remain under `.tmp/spending-final-native`. The original core parity suite also
passes after adapting its fullscreen currency interaction to the restored buttons.

The full migration remains incomplete. The migration completion gate still fails for
pending and partial source contracts. No backend production code changed in this checkpoint;
the prior full .NET run and subsequent repository-enforcement repairs remain the recorded
.NET evidence. Hardware controller and physical display validation are not established by
simulated controller samples or mode-change events.
