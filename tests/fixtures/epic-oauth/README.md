# Epic OAuth / library API fixtures

Canned responses for the authenticated Epic client (`src/Hoard.Ingest.Epic/Web/`).
**Nothing in these tests opens a socket** — the enrichment charter's rule that HTTP clients
are tested against fixtures and never against live APIs.

Every value here is **sanitized**. Account ids, tokens and client ids are obviously-fake
constants (`00000000000000000000000000000001`, `FAKE_ACCESS_TOKEN_…`); no real credential,
account id or session has ever been in this directory, and none may be added.

## Provenance

| File | Shape verified how |
|---|---|
| `oauth-invalid-client.json` | **Verbatim from a live probe, 2026-08-26.** `POST` to the token endpoint with a deliberately wrong Basic pair. Only the client id in the request was fake; the response body is Epic's, unedited |
| `library-unauthenticated-401.json` | **Verbatim from a live probe, 2026-08-26.** Unauthenticated `GET /library/api/public/items` |
| `oauth-token.json`, `oauth-token-refreshed.json` | Field names from Legendary's `egs.py` / `core.py`, which read `access_token`, `expires_in`, `expires_at`, `refresh_token`, `account_id`, `displayName`. `refresh_expires` / `refresh_expires_at` are included here **because the client must cope with them being present**; Legendary never reads them, so a real response may omit them — `library-items-*.json` has no bearing on that, but `EpicOAuthTokenTests` covers both cases |
| `oauth-invalid-refresh.json` | Epic's documented error-code vocabulary. The `errorCode`/`error` pair mirrors the verbatim `oauth-invalid-client.json` |
| `library-items-page1.json`, `library-items-page2.json` | Record fields and the `responseMetadata.nextCursor` pagination from Legendary's `egs.py` library-items walk. Ids are the same sanitized ones as `tests/fixtures/epic/` so a merge test can join the API half against the local half |
| `playtime-all.json` | Shape from the live GraphQL schema that fronts this REST route: `Playtime { accountId: String!, artifactId: String!, totalTime: Int! }`. Both REST routes were confirmed to exist on 2026-08-26 by routing discrimination (401 on the real path, 404 on a bogus sibling) |

## What these fixtures deliberately encode

- **`playtime-all.json` omits `Skylark`.** Page 2's title has no playtime entry at all. That
  is the null-not-zero case: absent from Epic's list means "Epic has no figure", which must
  arrive as `null`, never `0`. `Jasper` carries an explicit `0`, which is a real observation
  and is passed through as zero.
- **No entry carries a last-played timestamp**, because no Epic endpoint has one. `lastPlayed`,
  `firstPlayed`, `updatedAt` and `lastModified` were each individually confirmed absent from
  the GraphQL `Playtime` type. Do not add one to these fixtures "for completeness" — it would
  make a test pass against a field that does not exist.

See `docs/spikes/epic-oauth.md` for the full findings and what remains unverified.

## `redirect-no-session.json` — verbatim, and the reason M4.6 was rebuilt

What `https://www.epicgames.com/id/api/redirect?clientId=…&responseType=code` actually returns
to a browser with no Epic cookies. Captured verbatim from a real run of the embedded sign-in
on 2026-08-26, and identical to the unauthenticated probe recorded in `epic-oauth.md` §2.

**Every code field is present and null.** That is the endpoint answering "there is no
authenticated session here" — not a failed capture, and not a changed page. The first build of
the embedded flow started on this URL, so every first-time user landed here, saw no login form,
and got reported "no code captured", which describes the symptom and hides the cause entirely.
`AuthCodeBody` exists to tell the two apart and this file is what pins the distinction.

## `redirect-with-code.json` — the same shape, populated

The signed-in answer. The `authorizationCode` value is fabricated (32 hex characters, the right
shape); no real code has ever been in this repository, and one would be dead within minutes
anyway.
