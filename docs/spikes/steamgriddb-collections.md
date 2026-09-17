# SteamGridDB collection feasibility

Observed 2026-09-17 for TASK-330. This records a documentation and public-page inspection,
not a live integration test. No API key, account session, library data or artwork download
was used. Requests were read-only and bounded to individual documentation/page resources.

## Outcome

**Unavailable through a verified supported interface.** The published API supports browsing
artwork for an identified game, but its captured specification does not document collection
enumeration. The website exposes collection metadata through an undocumented route. That
does not establish a supported way to enumerate a whole collection, associate every asset
with an exact library identifier, or know that pagination is complete.

TASK-332 should remain dependent on a supported collection endpoint or export contract.
Do not ship a collection apply action backed by website scraping. This does not block the
per-game SteamGridDB artwork browser. Revisit when SteamGridDB documents collection access
or supplies an integration contract; this result is evidence for this date only.

## Primary-source checks

| Resource | Observation |
|---|---|
| [API documentation](https://www.steamgriddb.com/api/v2) | HTTP 200; a ReDoc shell referencing `/static/openapi.yml`. |
| [Published OpenAPI](https://www.steamgriddb.com/static/openapi.yml) | HTTP 200; OpenAPI 3.1.0, API version 2.10.0; 19 path entries. No occurrence of `collection`, no collection schema and no collection filter. |
| [Collection 254](https://www.steamgriddb.com/collection/254) | HTTP 200; JavaScript application shell, not an asset manifest. |
| [Collection metadata](https://www.steamgriddb.com/api/public/collection/254) | One unauthenticated GET returned HTTP 200 and collection name, description, author, counts and editability. Counts were grid 61, hero 82, logo 51, icon 0. No asset entries or game identifiers. |
| [Terms](https://www.steamgriddb.com/terms) | The served Terms component identifies an effective date of 2019-07-31 and includes restrictions on automated access and scraping. No collection-specific integration permission was established. |

The OpenAPI response was 59,095 bytes, SHA-256
`7D390F6E6F52B0EB048CAFC2BFE230039D5852C6254A31A78A420A7A2EA5631D`.

The public application shell referenced
`/static/assets/233e475efea66025d29a.js`. Its module map identified these resources:

- [Collection component](https://www.steamgriddb.com/static/assets/79b8e2380fbc1cb2801b.js):
  reads `/api/public/collection/{id}` and renders separate grids, heroes, logos and icons
  routes using a `collection_id` website filter. Its page route defaults to 1.
- [Terms component](https://www.steamgriddb.com/static/assets/ed6b9cabbbcb8e2128d3.js):
  source for the terms observation above. Hashed asset URLs may disappear after deployment.

The main bundle also contains collection editing calls under `/api/v2/collections`.
Those are website implementation details; the `v2` spelling does not make them documented
third-party APIs. No editing calls were made, and no private endpoints were probed.

## What the documented API does establish

The specification describes Bearer-key authentication, game lookup by Steam or SteamGridDB
ID, and separate grids, heroes, logos and icons queries. Artwork queries accept a page
parameter defaulting to zero; list envelopes describe page, total and limit. This is a
per-game contract, not a collection pagination contract.

Asset schemas include asset IDs, image/thumbnail URLs and author information. They do not
promise a per-asset external game identifier. Requesting by a known Steam ID supplies that
correlation from the request. Optional game platform data is explicitly described as
potentially inaccurate or stale. The capture contains no numeric rate-limit contract,
collection caching rules or collection-specific attribution requirements. Winnow's existing
one-request-per-second policy is a local choice, not a measured provider allowance.

Preserve available author and source-page attribution for selected artwork. An accessible
image is not evidence of unrestricted redistribution rights. The served terms do not
resolve whether an undocumented collection downloader is permitted; provider clarification
would be needed before relying on one.

## Matching implications

There is no supported collection-entry fixture to demonstrate end-to-end exact matching.
The observed metadata cannot produce one. The following are requirements for a future
interface, illustrated with synthetic inputs rather than claimed provider responses:

| Input | Required outcome |
|---|---|
| An entry carries Steam app ID `220`, and a library member has that exact ID | Match artwork to that member and slot without modifying identity. |
| An entry has only a title or an uncorrelated SteamGridDB game ID | Mark unmatched; do not infer a Steam ID from its title. |
| One external ID maps to several unconfirmed library targets | Mark ambiguous; do not pick a target or merge records. |
| Two distinct assets target the same exact game and hero slot | Show a duplicate choice in preview; collection order alone is insufficient. |
| The same asset appears on overlapping pages | Deduplicate by provider, kind and asset ID. |
| A matched game has no icon entry | Preserve the existing icon. |

An approved interface must define stable collection/asset IDs, complete pagination, supported
artwork kinds, exact game correlation, attribution and request limits. Tests should then use
sanitized canned responses for multiple pages, duplicate assets, missing IDs and ambiguous
matches. These cases were not exercised against the live service.

## Reproduction

Run these read-only PowerShell checks without an Authorization header or browser cookies:

```powershell
$response = Invoke-WebRequest 'https://www.steamgriddb.com/static/openapi.yml' -TimeoutSec 25
$bytes = if ($response.Content -is [byte[]]) {
    $response.Content
} else {
    [Text.Encoding]::UTF8.GetBytes($response.Content)
}
$spec = [Text.Encoding]::UTF8.GetString($bytes)
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
$spec -split '\n' | Where-Object { $_ -match "^  '/|version:|collection" }

$collection = Invoke-RestMethod 'https://www.steamgriddb.com/api/public/collection/254' -TimeoutSec 25
$collection.data.PSObject.Properties.Name
$collection.data.counts
```

The second request only verifies the dated metadata observation; it is not a recommended
application API. Repeat documentation inspection after a provider change, rather than
assuming these hashes or counts stay current.

## Desktop and fullscreen

No collection UI was implemented or verified in this spike. A future flow needs collection
URL/ID entry, preview, replacement choices, cancellation and undo on both surfaces. Keyboard
paste on desktop does not establish controller or text-entry coverage in fullscreen.
Until enumeration is supported, neither surface should imply that collections can be applied.
