# Steam capture policy and contract parity

The September 29 package restores the original account-page address rules, private-browser
popup behavior and capture counters. Store account paths accept case and trailing-slash
variants while queries remain pagination data. Capture scripts repeat the same guard before
reading a document. Valve help/login popups stay in the private window; other validated web
links go to the default browser. The handoff rejects application URLs, launcher commands,
files and loopback services. No popup creates an unguarded browser window.

The original licence counter measures pages followed after the first. Electron now reports
that count, including nine further pages for a ten-page capture. Licence pagination and
history expansion share one bounded-growth decision, with exhaustion before cap, cap before
stall, and growth required after a step. An initially empty table may still follow its next
page. Existing identity checks, cancellation, document-generation checks, size bounds and
incomplete-range safeguards remain in effect. An actual gap in advertised licence ranges is
still reported as incomplete even if the last visible paginator has disappeared.

Consent refusal and browser unavailability return a safe reason, no pages and zero counters.
Sign-in diagnostics use an allowlist formatter with UTF-8 byte counts and an explicit content
redaction marker; they never serialize provider results. The import API continues to consume
the Core SteamAccountPages DTO. Its immutable page replacement, whitespace detection,
presence properties, timestamp, source and byte counting remain backend behavior.

Verification:

- 269 Electron cases passed across seven policy, capture, sign-in, diagnostics, connection and
  import-report files. They include the original URL matrix, actual capture script execution,
  twelve history clicks, nine-page completion, fifty-page cap, empty-page growth, cancellation,
  failure outcomes and the default fifteen-minute deadline.
- Both native desktop/fullscreen cases passed in 4.5 seconds against intercepted provider
  pages using the production account host, policy installer and capture reader. They verify
  provider isolation, no child window, normalized redirects and the corrected page count.
  The external-open callback is recorded; no live account or real external browser was used.
- Five production HTTP cases passed with temporary databases. Raw frontend JSON survives
  both embedded and saved provenance, nullable history, multiple licence documents and
  capture timestamps; account attribution and idempotent stored facts are checked. The DTO's
  redacted string representation is also checked at that boundary.
- All thirteen original SteamAccountPagesContractTests cases passed unchanged. Only five
  methods that test the still-used Core page DTO are classified retained backend. Twenty
  policy methods and seven capture-result methods use explicit Electron replacement evidence.

One original source-compatibility assertion remains partial: a C# harvest-result factory
overload defaults an omitted licence stop reason to null. Electron's bridge fields remain
optional and actual single-page capture reports zero further pages, but the retired factory
is not part of its production capture path. That C# overload assertion is not claimed as a
migrated behavior. The evidence is in migration-steam-page-policy.json; no whole-class backend
retention or full migration completion is claimed.

These fixtures do not verify the current authenticated Steam website, Steam Guard or physical
controller hardware. Those device/account checks remain separate from this measured run.
