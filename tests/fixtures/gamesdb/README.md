# gamesdb.gog.com fixtures

Captured live on 2026-08-26 from
`GET https://gamesdb.gog.com/platforms/epic/external_releases/Bluebird`
(`Bluebird` is Epic's codename for Fez).

**Nothing is sanitized, because there is nothing personal in it.** This endpoint
is unauthenticated and account-independent: it answers "which stores sell this
game" and returns the same body for every caller. No account id, no entitlement
and no token appears in a response — which is exactly why the identity route
described in `docs/spikes/epic-gog-local-files.md` section 20 is usable at all.

**What was trimmed and why.** The live body is ~14 KB of titles, summaries,
artwork URLs, genres, developers and popularity ranks. `GamesDbClient` reads two
things from it — `game_id` and `game.releases[].{platform_id, external_id}` — so
the fixture keeps those verbatim and keeps a handful of the other fields
(`title`, `summary`, `slug`, `availability`, `release_per_platform_id`)
specifically so the parser tests prove it ignores them. Anything IGDB is the
authority for (§4.4) must never be read from here; a second source of titles and
covers is how two rows for one game start disagreeing about what they are called.

**The fixture is a contract pin.** This service is undocumented and unversioned —
Galaxy's backing service, not a published API — so `GamesDbContractTests` asserts
the field names the client depends on are still the field names in the capture.
If it ever fails, re-capture and re-verify rather than adjusting the assertion:
the shape changing is the event the pin exists to announce.
