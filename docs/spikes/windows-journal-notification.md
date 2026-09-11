# Windows journal notification verification

Measured on Windows, 2026-09-11.

`scripts/JournalNotificationSmoke` creates one transparent Avalonia window and attaches the
production `WindowsJournalNotification` adapter. It starts no Winnow host, session watcher or
database. The shell receives a clearly named verification notification. On its real
`NIN_BALLOONSHOW` callback, the harness posts a synthetic `NIN_BALLOONUSERCLICK` to that window,
checks that the adapter reaches its activation callback, then removes the icon and exits.
This proves shell submission, the show callback and Win32-to-Avalonia activation routing;
it does not claim a physical pointer click or every Windows notification policy was tested.

Run from the repository root:

```powershell
dotnet run --project scripts/JournalNotificationSmoke --artifacts-path artifacts/verify-notification-smoke -- --data-dir artifacts/notification-smoke/data
```

Observed output:

```text
Delivery result: Submitted
Native balloon SHOW callback observed.
Native hook activation reached the app callback; no journal write.
```

The harness also reports suppression or unavailable native delivery without treating it as
an application error. Windows can accept a request without displaying it; the adapter waits
five seconds for the show callback before requesting the in-window fallback. It respects
quiet time and requests silent, immediate delivery rather than queued notifications.

`JournalNotificationTests` covers desktop and fullscreen activation, fallback for unavailable,
suppressed and failed delivery, exact-session saving, draft protection, duplicate offers,
stale callbacks, and dismissal without reopening. Ten tests passed. The existing
`JournalPromptTests` and `AccountStatsViewModelTests` also passed, 26 tests together, retaining
the off-by-default preference and completed-session eligibility rules.

API references: [Windows notification data](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw),
[Shell notification delivery](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw),
[Windows notification state](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state),
and [Avalonia Win32 callback hook](https://docs.avaloniaui.net/api/avalonia/controls/win32properties).
