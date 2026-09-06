# Windows accessibility navigation

Measured on 6 September 2026, using Winnow's Avalonia 11.3.20 build and the Windows
UI Automation client. The audit launches an isolated sample library with `--seed-sample`,
`--no-sync` and a unique `--data-dir`, and closes only the process it started.

Run against a built executable:

```powershell
./docs/spikes/accessibility-navigation.ps1 -AppPath C:/Temp/winnow-accessibility/Debug/net10.0/Winnow.exe
```

The script navigates by the buttons' exposed names and invokes their UIA patterns. It visits
Feed, the library grid and list, Filters, Platforms, Library settings, Appearance, Merges,
Account stats, feed history, game details, and the return to the library. Each surface must
expose a nonempty name on every visible focusable control; a framework or view-model type
name is a failure. It checks named screen groups, the Games list and its named list items,
and the details surface's `Window` role and game-specific name. It moves actual keyboard
focus to a game list item and to `Export acquisition CSV` and reads the focused element
back from Windows.

All twelve surface checks passed. The run found 68 named focusable controls on the sample
feed, 40 in list view, 47 with Filters open, 28 on Platforms, 20 on Library settings, 28 on
Appearance, 46 on Merges, 13 on Account stats, 14 in feed history and 71 with details open.
These totals include the persistent shell and depend on the sample data and window size.

The first run exposed an unnamed focusable details root even though its three inner bands
were named. Naming only those groups was insufficient; the root now identifies the game
and exposes the same `Window` role the screenshot overlay uses. A regression test covers
explicitly focusable custom roots as well as buttons and fields.

`InteractiveControlNameTests` checks authored interactive controls for explicit names or
plain text content/header. `AutomationNameReachabilityTests` separately proves that names
are on controls whose peers reach the control view, including direct calls to Avalonia's
real automation peers. Six focused tests passed, including count and rename notifications
for the list/filter names that also provide live item status.

This verifies navigation through the live provider that Windows screen readers consume.
It does not record Narrator or NVDA speech, certify either reader's announcement order,
or exercise external sign-in/browser content. The script leaves its sample directory for
inspection and never opens the user's library.
