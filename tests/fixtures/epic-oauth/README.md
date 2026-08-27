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
| `catalog-bulk-items-games.json`, `catalog-bulk-items-engine.json` | **Shape verified against a live authenticated call, 2026-08-26.** `GET catalog-public-service-prod.ol.epicgames.com/catalog/api/shared/namespace/{ns}/bulk/items?id=…` over the author's own session, which answered for all 99 distinct catalog item ids the account owns. Field names, nesting and both `mainGameItem` spellings are the service's; the ids, namespaces, titles and codenames here are the sanitized fixture ones |
| `library-items-mixed.json` | The same library-items shape as the two paged fixtures, in one page, carrying the API-only entitlements the local files never hold |

## What these fixtures deliberately encode

- **`playtime-all.json` omits `Skylark`.** Page 2's title has no playtime entry at all. That
  is the null-not-zero case: absent from Epic's list means "Epic has no figure", which must
  arrive as `null`, never `0`. `Jasper` carries an explicit `0`, which is a real observation
  and is passed through as zero.
- **No entry carries a last-played timestamp**, because no Epic endpoint has one. `lastPlayed`,
  `firstPlayed`, `updatedAt` and `lastModified` were each individually confirmed absent from
  the GraphQL `Playtime` type. Do not add one to these fixtures "for completeness" — it would
  make a test pass against a field that does not exist.

## The catalog fixtures — what each entry is there to prove

The bug they pin: `/library/api/public/items` returns entitlements with **no title and no
categories**, so the API half of Epic ingest could neither name what it owned nor tell a game
from an Unreal Engine build. `library-items-mixed.json` is a library in exactly that state,
and the two `catalog-bulk-items-*.json` files are what the catalog service says about it.

| Catalog item id | What it models |
|---|---|
| `7a70b499…` (Fez) | A game. `public,games,applications`, empty `mainGameItemList` |
| `c30000…0004` | **A DLC that looks exactly like a base game by category** (`application,games,applications`) and is only marked by a non-empty `mainGameItem`. It carries BOTH spellings of the parent field, because the live response does. It is deliberately NOT hidden — the real instance of this shape on the author's account is LEGO Fortnite: Odyssey, with 408 minutes played |
| `d40000…0005` | An Unreal Engine build: `engines,engines/ue4`. Owned, used, and not a game |
| `e50000…0006` | A marketplace asset pack: `assets,assets/showcasedemos` |
| `f60000…0007` | **Categories, no `title`.** Classifiable but unnameable — the row keeps its placeholder and is still hidden |
| `a70000…0008` | **`title`, empty `categories`.** Nameable but unclassifiable — must store NULL, never an empty string, or "not known" stops being distinguishable |
| `b80000…0009` | Owned and **absent from the catalog answer**. A definite miss, which is an answer worth caching — as distinct from a request that failed, which is not |

Do not "complete" the entries that are missing a title or categories. Their incompleteness is
the fixture.

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
