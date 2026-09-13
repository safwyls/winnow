# GOG authenticated session-history probe

Observed 2026-09-13 UTC (2026-09-12 Pacific), for TASK-49.

The authorized sessions route returned only an aggregate, with no dated sessions worth
importing. Keep GOG sign-in deferred on desktop and fullscreen. This result applies to
the route and account tested; it does not establish that GOG has no history elsewhere.

## Method and identity

The user signed in through GOG's browser authorization flow using the Galaxy client
identity also used by [gogdl](https://github.com/Heroic-Games-Launcher/heroic-gogdl/blob/main/gogdl/auth.py).
The code exchange at `POST https://auth.gog.com/token` returned HTTP 200 and a bearer
token. The returned `user_id` was used unchanged in all account paths below. Evidence
aliases that account as `ACCOUNT_A`; no account identifier, token, authorization code,
library certificate or refresh token is included in the evidence artifact.

`GET https://galaxy-library.gog.com/users/{uid}/releases` with that bearer returned
HTTP 200: `total_count: 46`, `limit: 500`, and 46 `items`. Filtering `platform_id == gog`
and `owned == true` yielded 45 releases; the other release was Steam. All items fitted
in one response, whose top-level fields were `total_count`, `limit`, and `items`.

Each of those 45 GOG release IDs was requested sequentially at
`GET https://gameplay.gog.com/games/{gid}/users/{uid}/sessions`, without query parameters,
with the same bearer. No session writes were performed and no launcher files were read.

## Results

All 45 requests returned HTTP 200 and `application/json`. Every body contained exactly
one numeric field, `time_sum`: 43 returned zero, product `1207664643` returned 50, and
product `1971477531` returned 54. For example:

```json
{"time_sum":50}
```

No session list, start/end timestamp, date, timezone, session ID, cursor, page count or
next-page field appeared. The nonzero control was repeated and returned the same body;
its `Link` and `Content-Range` headers were absent. Pagination beyond this response is
unverified; no undocumented query parameters were guessed. Units of `time_sum` cannot
be established from this payload alone and must not be assumed to be minutes or seconds.

The same control URL without a bearer returned HTTP 401:

```json
{"error":"access_denied","error_description":"OAuth2 authentication required"}
```

This distinguishes successful authorized aggregate responses from authentication failure.
There were no transport failures, rate-limit responses or service errors in this sample.
The zero aggregates do not prove an empty session history or that a game was never played.
The two nonzero aggregates establish that the absence of dates is also observed for games
with recorded time, rather than only for zero results.

The [sanitized response artifact](gog-sessions-2026-09-13.json) records all 45 results,
the account-binding method, library counts, capture time and both controls.

## Comparison and scope

`GalaxyLibraryReader` already reads `GameTimes.minutesInGame` and
`LastPlayedDates.lastPlayedDate`, joining both by release and account. `GogLibrarySource`
keeps the winning account's minutes, last-played date and attribution together. Registry
fallback supplies installation evidence, with unknown playtime and last-played values.

The remote payload adds no dated sessions to those local facts. This is a comparison of
available fields, not a fresh numerical reconciliation against a live Galaxy database.
The aggregate's units, retention and relationship to Galaxy totals remain unverified.
An aggregate-only response does not justify credential storage, refresh handling or new
sign-in UI for this session-history objective.

Retain local Galaxy and registry discovery on both surfaces. No sign-in implementation
task is warranted by this probe. Reconsider only with an authorized response containing
additional dated history, documented account binding and pagination, and evidence that
it improves on local facts. No application behavior changed, so UI/build tests were not
run; verification consists of the live requests, controls and evidence validation.
