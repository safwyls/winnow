# Spike: Steam GDPR / account-data export (what it actually contains)

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

Date: 2026-08-28; updated 2026-08-29
Verified against: public/primary sources only until 2026-08-28, listed in the Sources
section. **Two store account pages were obtained and parsed on 2026-08-29:**
`store.steampowered.com/account/licenses/` and `store.steampowered.com/account/history/`.
Selectors for those two pages are now VERIFIED; the `help.steampowered.com/en/accountdata/*`
pages remain unverified. See §8 for the 2026-08-29 findings.

Resolves (partially) the `[VERIFY]` in `game-library-design.md` §5.4 and the §10 open
question "Actual file contents of a current Steam GDPR export".

### Confidence legend

- **VERIFIED**: primary source, or two independent sources that agree.
- **REPORTED**: one credible but unverified source.
- **UNKNOWN**: needs a real export or a live authenticated session to determine.

---

## 1. There is no downloadable archive: VERIFIED

The design doc assumes the user requests their data, receives it, and points the app at
the file. That does not match what Valve provides.

Valve's Privacy Policy (updated 2025-02-14) states data portability is satisfied by making
Personal Data available in "structured HTML format" through the **Privacy Dashboard**,
reached by logging in at `help.steampowered.com` and navigating **My Account > Data Related
to Your Steam Account**. The policy describes logging in as "the only way to access,
rectify or delete your data". It describes no archive, no ZIP, no email delivery, and no
file. The "structured HTML" phrase the design doc quotes is real and current, but it
describes live web pages, not an exported document set.

The Privacy Dashboard is an index of links. `SteamDatabase/SteamTracking-GDPR` (archived
repo, last content update 2022-11-15, archived 2025-08-28) contains a machine-scraped
table of every link on the page, grouped under headings: Account; Community & Profile;
Inventory & Items; Steamworks Game Data; Uploaded Content; Store; Store & Community Events;
Broadcasts; Remote Play; Chat. Its `scan_pages.py` logs in with real credentials via
`steam.webauth` and scrapes for anchors, confirming the page is an authenticated link
index, not a generator.

The support-ticket flow at `help.steampowered.com/en/wizard/HelpAccountDataQuestion`
(fetched live, anonymous, 2026-08-28) renders as "Submit an Account Data or Deletion of
Data Question" with an email field, a free-text details box, an attachment dropzone and a
CAPTCHA. There is no archive request button and no stated format or turnaround.

JustGetMyData's Steam entry lists only the dashboard URL, difficulty "medium", noting that
data is accessed/managed in the Privacy Dashboard or via a support ticket.

The 2018 launch coverage (Game Developer, 2018-06-14) described "a new page that rounds up
all the data the company has on an individual to one very packed hub page" with "over 80
different categories." Eight years later, the structure is unchanged: a hub of ~100 links,
many pointing at pages the user already had (`steamcommunity.com/my/games`,
`store.steampowered.com/account/licenses`, the wishlist, the inventory). A meaningful
minority are real generated data pages under `help.steampowered.com/en/accountdata/<PageName>`.

## 2. The single source behind the design doc's claims: REPORTED, likely wrong

Every claim in §5.4 beyond "structured HTML" traces to one page: **Digital Takeout Day**
(`takeoutday.org/services/steam`, updated 2026-05-30). It says verbatim:

- Steps: "Go to store.steampowered.com > Your profile name / Navigate to Account > Privacy
  / Click 'Request a copy of your Steam account data'", then "Click 'Request data' / Wait
  for the email (can take several weeks) / Download the archive".
- "Steam's GDPR export is comprehensive. Most interesting file is the playtime breakdown —
  gives you a full record of every game played and for how long."
- "Review the 'ExternalLicenses' file which shows any games acquired via third-party keys."

Treat this as **REPORTED at best, and likely wrong**, for three reasons:

1. The UI path it describes does not match Valve's own privacy policy, which names the
   help-site Privacy Dashboard. `store.steampowered.com/account` has no "Privacy" tab
   offering an archive request.
2. The exact string "Request a copy of your Steam account data" returns zero hits in a
   GitHub code search. That is unusual for a real Steam UI string, which typically appears
   in localization dumps and scraper projects.
3. The site is a general "download your data" aggregator covering dozens of unrelated
   services with an identical template; its Steam steps read like the generic
   Google/Spotify flow.

Note the circularity risk: this page (or an ancestor of it) is the most likely origin of
§5.4's own wording, so it must not be treated as independent corroboration.

**`ExternalLicenses`: no distinct page observed, verified authenticated 2026-09-06.**
The signed-in dashboard has no such link. Opening
`help.steampowered.com/en/accountdata/ExternalLicenses` while authenticated renders the
ordinary account-data dashboard: its 112 content links match the index exactly, and no
separate license table appears. This refutes the proposed page for the tested account;
it does not establish what a support-request export might contain. Use the dashboard's
actual `store.steampowered.com/account/licenses` link for license acquisition data.

What does verifiably exist is `store.steampowered.com/account/licenses` ("View licenses
and product key activations"), listed on the Privacy Dashboard as "Licenses". Community
documentation consistently describes it as a dated list of every product added to the
account with its acquisition route (Steam Store purchase, Retail/third-party key
activation, gift, complimentary/free grant).

## 3. Playtime, the export gives totals, not history: VERIFIED by structure

The Privacy Dashboard's "Steamworks Game Data" group routes playtime to
`steamcommunity.com/my/games?tab=all`, the ordinary profile games list. That page carries
current cumulative playtime, two-week playtime and last-played: **exactly the same shape
Winnow already ingests from `IPlayerService/GetOwnedGames` and `localconfig.vdf`.** There
is no per-period, per-month or per-session breakdown anywhere on the dashboard's index.

The dashboard does not supply historical playtime. Steam Replay provides the monthly
series described in section 4; saved account pages provide acquisition and spending data.

Useful detail: the games-list page is JS-rendered from an embedded JSON array, and the
same data is available as XML at
`steamcommunity.com/profiles/<SteamID64>/games?tab=all&xml=1`. Prefer the XML.

---

## 4. Where the historical playtime actually is, Steam Replay / Year in Review: VERIFIED

This is the spike's most valuable finding.

Source: `SteamDatabase/SteamTracking`, file
`ProtobufsWebui/service_salefeature.proto` (Valve's own service definitions, extracted
from the live Steam web UI). Endpoint:
**`ISaleFeatureService/GetUserYearInReview`** on `api.steampowered.com`. Confirmed live:
`GET https://api.steampowered.com/ISaleFeatureService/GetUserYearInReview/v1/?steamid=<id>&year=<year>`
returns HTTP 200 with `{"response":{}}` when unauthenticated/private. The endpoint exists
and is reachable. Parameters per xPaw's generated `api.json`: `key` (required), `steamid`,
`year`, `force_regenerate`, `access_source`, `fetch_previous_year_summary`. Marked
undocumented.

Proto field reference. The live JSON places months inside each game; the observed
envelope and parser support are described immediately after this reference:

```protobuf
message CUserYearInReviewStats {
    optional uint32 account_id = 1;
    optional uint32 year = 2;
    optional .CUserPlaytimeStats playtime_stats = 3;
    optional int32 privacy_state = 4;      // enum
}

message CUserPlaytimeStats {
    optional .CPlaytimeStats total_stats = 1;
    repeated .CGamePlaytimeStats games = 2;
    optional .CPlaytimeStreak playtime_streak = 3;
    repeated .CMonthlyPlaytimeStats months = 5;     // <- the longitudinal axis
    repeated .CGameSummary game_summary = 6;
    ...
}

message CMonthlyPlaytimeStats {
    optional uint32 rtime_month = 1;
    optional .CPlaytimeStats stats = 2;
    repeated .CGamePlaytimeStats appid = 4;         // <- per-game, within the month
    repeated .CSimpleGameSummary game_summary = 6;
}

message CGamePlaytimeStats {
    optional uint32 appid = 1;
    optional .CPlaytimeStats stats = 2;
    optional .CPlaytimeStreak playtime_streak = 3;
    optional .CPlaytimeRanks playtime_ranks = 4;
    optional uint32 rtime_first_played = 5;
}

message CPlaytimeStats {
    optional uint32 total_playtime_seconds = 1;
    optional uint32 total_sessions = 20;
    optional uint32 vr_sessions = 21;
    optional uint32 deck_sessions = 22;
    optional uint32 controller_sessions = 23;
    optional uint32 linux_sessions = 24;
    optional uint32 macos_sessions = 25;
    optional uint32 windows_sessions = 26;
    ...
}

message CGameSummary {
    optional uint32 appid = 1;
    optional bool new_this_year = 2;
    optional uint32 rtime_first_played_lifetime = 3;
    optional bool demo = 4;
    optional bool playtest = 5;
    optional bool played_during_early_access = 6;
    optional uint32 total_sessions = 14;
    optional uint32 rtime_release_date = 15;
    optional uint32 parent_appid = 16;
    ...
}
```

**Live JSON envelope, verified 2026-08-28:** the monthly axis placement observed live does NOT
match this proto's layout. The proto puts `months[]` at the `CUserPlaytimeStats` level,
each month carrying its own repeated `appid[]` of per-game stats. What came back from a
real authenticated call was `response.stats.{account_id, year,
playtime_stats.{total_stats, games[]}}`, with each entry of `games[]` carrying `appid`,
`stats`, `playtime_streak`, `playtime_ranks`, `rtime_first_played`,
`relative_game_stats`, and its own `months[]` array of per-game monthly breakdowns. In
other words, the months live INSIDE each game, not alongside the games list. Both are
plausible wire encodings of the same protobuf (the proto's field numbers allow either
nesting) and only the games-with-per-game-months form was observed end to end. The proto
above is preserved because its field NAMES and types are still the best evidence of what
each field carries; its nesting of the monthly axis should not be relied on.

Winnow's parser (`src/Winnow.Enrich.SteamWeb/Model/SteamHistoryJson.cs`) reads BOTH
shapes, so a Valve-side change of placement costs data points rather than the whole import.

What this means for Winnow, mapped onto the §6 data model:

- The per-game `months[]` (and, if ever observed, `playtime_stats.months[].appid[]`) gives
  **playtime seconds and session counts per game per calendar month**, a real longitudinal
  series. This is what `playtime_snapshots(ownership_id, playtime_minutes, observed_at)` was
  designed to accumulate over months of running the app. Year in Review hands it over on
  install day, one row per game per month.
- `CGamePlaytimeStats.rtime_first_played` and
  `CGameSummary.rtime_first_played_lifetime` give a **first-played date**, which nothing
  else in Winnow's sources provides. It converts "120 minutes, last played 2019" from a
  point into a span, exactly the bounced-vs-retired discrimination §6.1 turns on.
- `total_sessions` per game per month is a genuine session *count*, not the session
  start/end pairs M3a records, but a strong prior for the same signal.
- **Coverage:** Steam Replay ran first for **2022** (published January 2023) and annually
  since, so a full backfill is roughly four years deep, not lifetime.

### Related endpoints from `service_player.proto`

**`IPlayerService/ClientGetLastPlayedTimes`: VERIFIED 2026-08-28.** `GET
https://api.steampowered.com/IPlayerService/ClientGetLastPlayedTimes/v1/?key=..&format=json`
returns HTTP 200 with no `steamid` parameter required; the key alone identifies the
account. Per-game fields observed: `appid`, `last_playtime`, `playtime_2weeks`,
`playtime_forever`, `first_playtime`, per-platform `playtime_*_forever` and
`first_*_playtime`/`last_*_playtime`, and `playtime_disconnected`.

**`first_playtime` is 0 for many entries, VERIFIED, and this is a trap.** Zero means "not
tracked", never 1970-01-01. This is the same placeholder convention §4.2 already documents
for `rtime_last_played` and that `Winnow.Core.Domain.SteamTime` exists to apply. The spike
originally sold `first_playtime` as the cheapest, highest-value item in M5 ("converts
every ownership from a point into a span"), and that value is materially smaller than
implied: it is present for a subset of apps, not for all of them.

Also present, as a curiosity that is not generally usable:
`IPlayerService/GetRecentPlaytimeSessionsForChild` returns literal
`{time_start, time_end, appid, device_type, disconnected}` session records, but only for
Family View child accounts, so it is not a path for the ordinary case.

### Auth question resolved: VERIFIED 2026-08-28

**`GetUserYearInReview` accepts an ordinary user-supplied Steamworks Web API key** for the
key-holder's own account. `GET
https://api.steampowered.com/ISaleFeatureService/GetUserYearInReview/v1/?key=..&steamid=..&year=YYYY&format=json`
returns HTTP 200 with populated stats. The §4.2 privacy caveat applies as expected: data is
returned only when the key belongs to the queried account. Coverage confirmed at 2022
onward, matching the REPORTED note about Steam Replay's first year.

Together these endpoints provide per-game monthly playtime, first-played dates,
cumulative totals and session counts for historical backfill. Account-page HTML supplies
acquisition data instead.

### M5 implementation

The implementation lives in `src/Winnow.Enrich.SteamWeb/` (`SteamHistoryClient`,
`SteamHistoryJson`, `PlaytimeSeriesReconstruction`) and
`src/Winnow.App/Services/SteamPlaytimeBackfillService.cs`.

The reconstruction design: anchor on `playtime_forever` from `ClientGetLastPlayedTimes` and
walk the monthly deltas BACKWARDS so the series converges on present truth. A forward walk
from an assumed zero baseline is wrong for any account with pre-2022 play, silently, by the
full amount of the account's pre-Replay history.

---

## 5. What the dashboard does uniquely offer Winnow: VERIFIED (pages exist); contents VERIFIED for store pages (2026-08-29)

Not playtime history. Acquisition facts:

- `store.steampowered.com/account/licenses`: every product ever added, with date and
  acquisition route including retail/third-party key activations. Maps to
  `ownerships.acquired_at` and `ownerships.license_type`. Nothing else Winnow can reach
  carries this. **Structure VERIFIED 2026-08-29; see §8.**
- `store.steampowered.com/account/history`: purchase history with amounts. Maps to
  `ownerships.price_paid_cents` / `price_source`, subject to the bundle-attribution problem
  §4.7 already documents. **Structure VERIFIED 2026-08-29; see §8.**
- `help.steampowered.com/en/accountdata/AccountSpend`: "External Funds Used", a lifetime
  total.
- `help.steampowered.com/en/accountdata/ShoppingCartHistory`,
  `.../DiscoveryQueueHistory`, `.../AppUserTagVotes`, `.../MyGameEventSeen`,
  `.../NextFestDemoPlays`, `.../Giveaways`: real generated pages with no other source.
  `MyGameEventSeen` ("Game Event or Announcement First Seen/Read") is interesting for §6's
  `update_events`, since it records when the user first saw an announcement.

This study distinguishes the page contents from how Winnow obtains them. The current
user-present WebView harvest and saved-page import are specified in the build spec,
sections 4.7 and 5.4.

## 6. Known open-source parsers: VERIFIED, there are none

There is no maintained open-source parser of a Steam GDPR/account-data export. Nearest
things found:

- `SteamDatabase/SteamTracking-GDPR`: scrapes the dashboard's link index, not the data
  pages. Archived 2025. Still the best evidence of the page inventory; `scan_pages.py` is a
  useful shape reference for "log in, walk the index".
- `Pravv/steam-gdpr-interpreter` (2018, 0 stars): a single JS file for Dota 2 commends.
  Not a general parser.
- The various `steam-library-exporter` / `export_STEAM_games_and_stats` projects all read
  the Web API, not an export.

No license-compatible parser exists to learn patterns from.

## 7. Request flow and turnaround: VERIFIED for dashboard, UNKNOWN for ticket

- **Dashboard:** instant, self-serve, requires a logged-in Steam web session. No wait.
- **Support ticket** at `help.steampowered.com/en/wizard/HelpAccountDataQuestion`:
  free-text, no published SLA, no published format. Whether a ticket asking for a full
  Article 15 copy yields files rather than a pointer back to the dashboard is UNKNOWN. The
  one source claiming an emailed archive "can take several weeks" is the same unreliable
  takeoutday.org page.

---

## Evidence limits

The two store account pages have verified selectors and sanitized fixtures. The live
`ExternalLicenses` route rendered the dashboard index on 2026-09-06, not a separate
data page. The API key successfully authenticated Year in Review on 2026-08-28.

Still unverified in this study:

- Markup on the other `help.steampowered.com/en/accountdata/*` pages.
- Whether a support ticket yields files, and in what container.
- Whether licenses distinguish retail-key vendors. The captured account had no retail
  activations, so it cannot answer that question.

## 8. Store account pages, verified from real saved HTML: 2026-08-29

The repo owner saved `store.steampowered.com/account/licenses/` and
`store.steampowered.com/account/history/` from a signed-in browser (US locale). The
originals are git-ignored; sanitized fixtures are in `tests/fixtures/steam-account-pages/`.
See that directory's README for sanitization details.

### Licences page (`account/licenses/`)

- Table is `table.account_table`. Header row carries `th.license_date_col`, a plain `th`
  for the item, and `th.license_acquisition_col`.
- Each data row is a bare `<tr data-panel=... role="button">` with three cells:
  `td.license_date_col`, an unclassed `td` holding the product name, and
  `td.license_acquisition_col`.
- Date format observed: "MMM d, yyyy" (three-letter month, day with no leading zero,
  four-digit year).
- Acquisition method is free text. Three values occurred: "Steam Store", "Complimentary",
  "Gift/Guest Pass". "Retail" did NOT occur; this account has no retail activations, so
  item 4 (vendor discrimination) remains UNKNOWN.
- **No appid and no package id for ordinary rows.** A package id exists only for
  free/Complimentary licences, and only incidentally: it is an argument to a
  `RemoveFreeLicense( <packageid>, '<base64 name>' )` `javascript:` href on a "Remove"
  link inside the item cell. In the sample, 13 of 96 rows carried one. Every other row is
  a name only. The names are package names, not app names; many are DLC, bundle or cosmetic
  package names that never correspond to an app. This caps what any importer can match by
  identifier and forces name matching for the large majority.
- **Paginated, not load-more.** `div.license_paginator_ctn` holds a `<span>` reading
  "Showing licenses 1-100 of 979" and an `a.license_paginator_next` whose href carries
  `?continuationToken=<ts>:<token>&offset=100`. There is no `#load_more_button` on this
  page. A capture that reads only the first document sees 100 rows of however many the
  account holds.

### Purchase-history page (`account/history/`)

- Table is `table.wallet_history_table` with a two-row `thead`. Column classes: `wht_date`,
  `wht_items`, `wht_type`, `wht_baseprice` (header) / `wht_base_price` (cells; they
  differ), `wht_tax`, `wht_shipping`, `wht_total`, `wht_wallet_change`,
  `wht_wallet_balance`.
- Rows are `tr.wallet_table_row`; wallet-affecting rows add `wallet_table_row_amt_change`.
- Note the transposed class name: the payment sub-element inside the type cell is
  `wth_payment` (w-t-h), not `wht_payment`. It also appears inside the items cell for gift
  recipients and in-game item quantities.
- Items cell holds one `div style="clear: both"` per product, so a bundle purchase is N
  item divs under one price, the §4.7 bundle-attribution problem made concrete.
- Type values observed: "Purchase" (dominant), "Gift Purchase", "In-Game Purchase",
  "Refund", and empty (gift-card redemption).
- Discounts render as `.wht_base_price_discounted` containing `.wht_discount_pct`,
  `.wht_original_price` and `.wht_discounted_price`.
- **Two distinct refund signals, which must not be conflated.** (a) A purchase row later
  refunded is marked by class `wht_item_refunded` on the items cell and `wht_refunded` on
  the type and total cells, while its type text still reads "Purchase". (b) A separate
  transaction whose type text is literally "Refund".
- **Appids:** the row's `onclick` carries a help-wizard URL. Ordinary purchases use
  `HelpWithTransaction?transid=...` with no appid. Only in-game purchases use
  `HelpWithItemPurchase?transid=...&appid=<appid>`. The history page yields an appid for
  in-game purchases only.
- Payment cell renders the card issuer and last four digits, PayPal, "Wallet", or a split
  payment rendered as "\<amount\> Wallet". The parser classifies this coarsely
  (wallet/card/paypal/other) and discards the text so no card fragment leaves the parser.
- Money: "$"-prefixed, "," grouping, "." decimals; negative wallet changes render with a
  leading "-", positive with "+".
- `#load_more_button` exists on this page and is the correct selector, with
  `onclick="WalletHistory_LoadMore()"`. It posts to
  `store.steampowered.com/account/AjaxLoadMoreHistory/` with a cursor and sessionid.
  Steam's script hides the button with jQuery when exhausted rather than removing it, so
  an exhaustion check must test visibility, not existence.

### Harvest selector findings

- `#account_pulldown` (signed-in probe): **CORRECT.** It is a
  `<button id="account_pulldown">` holding the persona name, present on both pages.
  `#global_action_menu` is also present.
- `#load_more_button`: **CORRECT**, but only on the history page, and visibility must be
  tested.
- `#store_transactions`: **WRONG.** It is not an element id anywhere. It is a fragment of
  the wallet-balance href in the global header (`/account/store_transactions/`), present on
  every store page. `getElementById` always returned null and row counting silently fell
  back to counting every `tr` in the document. Corrected to
  `table.wallet_history_table tbody tr.wallet_table_row`.
- The licenses paginator is an in-page fetch followed by DOMParser append. The
  2026-08-29 implementation walked up to `MaxLicensesPages` (default 50), replaced the
  paginator element, and reported `LicensesPagesWalked` and `LicensesStoppedBecause`.

**Live paginator recheck, 2026-09-06 (TASK-47).** In an authenticated browser on
`store.steampowered.com/account/licenses`, the user ran a read-only console probe that
selects `a.license_paginator_next`, verifies its same origin, fetches it with
`credentials: 'include'` and `cache: 'no-store'`, then parses it with `DOMParser`.
The response was HTTP 200 and contained 100 non-header rows in the account table
identified by `th.license_date_col`. No CSP-related fetch failure was observed. This
checks one live page transition, not an entire embedded-WebView harvest. The automation
browser's restricted evaluator lacked `fetch`; that tool limitation was not a Steam CSP
failure. No account HTML, cookies, or paginator tokens were saved.

If a future CSP change blocks the in-page fetch, preserve the existing incomplete-harvest
diagnostic and offer the saved-page import route. Do not bypass CSP or export browser
credentials to make unattended requests. A navigation-based walk inside the user-present
sign-in WebView would require separate implementation and verification.

### Still UNKNOWN

- Whether these selectors hold for non-US locales.
- Whether "Retail" activations render differently.
- Whether a very large account paginates the history page differently.

---

## Sources

- https://store.steampowered.com/privacy_agreement/ (Valve, updated 2025-02-14). VERIFIED:
  "structured HTML format", Privacy Dashboard as the mechanism, no archive.
- https://help.steampowered.com/en/accountdata. The dashboard itself (login required).
- https://help.steampowered.com/en/wizard/HelpAccountDataQuestion. VERIFIED: free-text
  ticket form, fetched 2026-08-28.
- https://github.com/SteamDatabase/SteamTracking-GDPR. VERIFIED: page inventory, snapshot
  2022-11-15, archived 2025-08-28.
- https://github.com/AChep/keyguard-app/blob/master/common/src/commonMain/composeResources/files/justgetmydata.json.
  VERIFIED: privacy-rights aggregator's Steam entry names only the Privacy Dashboard URL
  and a support ticket, with no archive or download.
- https://github.com/SteamDatabase/SteamTracking, files
  `ProtobufsWebui/service_salefeature.proto` and `ProtobufsWebui/service_player.proto`.
  VERIFIED: response shapes.
- https://github.com/xPaw/SteamWebAPIDocumentation. VERIFIED: endpoint parameters.
- https://www.gamedeveloper.com/business/following-gdpr-steam-now-discloses-a-ton-of-collected-account-data-to-users
  (2018-06-14). VERIFIED: origin and character of the dashboard.
- https://takeoutday.org/services/steam (updated 2026-05-30). REPORTED and doubted; sole
  source for "archive", "playtime breakdown file", "ExternalLicenses".
- https://www.techpowerup.com/330009/steam-re-launches-steam-replay-for-you-to-check-out-your-statistics.
  REPORTED: Steam Replay history and year coverage.
- https://github.com/Pravv/steam-gdpr-interpreter. The only "GDPR parser" found;
  Dota-specific, 2018, not useful.
