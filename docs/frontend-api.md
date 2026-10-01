# Frontend API

Winnow's backend runs independently of its frontends. Avalonia desktop and fullscreen use
the same HTTP/JSON API available to other local applications. An Electron frontend can attach
alongside Avalonia; both observe the same committed library while keeping their own selection,
navigation and scroll state. The repository includes [Winnow Electron](../src/Winnow.Electron/README.md),
an Electron/TypeScript frontend with its own desktop/fullscreen layouts and
[replaceable React themes](electron-themes.md).

## Start and discover

Use a throwaway directory for development:

```powershell
dotnet run --project src/Winnow.Backend -- --data-dir C:\Temp\winnow-api --no-sync
dotnet run --project src/Winnow.App -- --data-dir C:\Temp\winnow-api --no-sync
```

`--no-sync` suppresses automatic scans and refresh workers. `--seed-sample` also disables those
workers and, in Debug builds, seeds an empty library. These options affect a newly started backend; attaching
to an existing backend does not reconfigure it. Avalonia starts its packaged `backend/`
companion when discovery does not identify a responsive host. Data-directory resolution and
the legacy data migration run in the backend executable, before the frontend connects.

The backend binds an ephemeral port on `127.0.0.1`. After startup it writes
`<data-dir>/backend/endpoint.json` with `address`, `token`, `epoch`, `processId` and `apiVersion`.
The containing directory is restricted to the current user. Treat the token as a secret:
never log it, embed it in a URL, or expose it to remote content. Reread discovery after a
disconnect because the address and token can change on restart. The backend owns an exclusive
file lock and named mutex; starting two hosts cannot create two worker sets for one directory.

Closing a frontend disposes its connection, leaving the backend running. To stop it explicitly,
send authenticated `POST /api/v1/lifecycle/shutdown`. Updates stop the backend before replacing
the installation; an installation lease prevents replacement while a backend remains alive.
Other frontends should handle the disconnect and reread discovery when the service returns.
Before replacing frontend binaries, close any other frontend running from that installation;
the updater does not terminate unrelated client processes.

## Transport and contracts

Every request, including images, events and OpenAPI, requires `Authorization: Bearer <token>`.
All routes are under `/api/v1`. The host accepts only loopback peers and its exact IP authority;
cross-origin browser requests are rejected. An Electron app should keep this client in its
main process and expose a narrow preload bridge to its renderer. Do not disable browser
security or distribute the token to untrusted pages.

`GET /api/v1/openapi.json` describes the actual routes and JSON schemas. C# clients may use
`Winnow.Api.Client`; other languages need only HTTP, JSON and an SSE reader. JSON uses camel-case
property names. Enum serialization is defined by each schema: do not assume every enum is a
string. Additive fields and routes may appear within v1; incompatible changes require a new
API version. Clients must reject unsupported discovery versions and tolerate unknown fields.

The main capability groups are:

| Capability | Routes |
|---|---|
| Discovery health and protocol | `health`, `capabilities`, `openapi.json`, `events` |
| Library and details | `library`, `library/workspace`, `games/{workId}/details` |
| Lists, manual games and visibility | `lists`, `manual-games`, `hidden-games` |
| Recommendations | `feed`, `feed/supplement`, feedback and impression commands |
| Identity review and correction | `identity/*` |
| Journal, activity and statistics | `sessions/*`, activity/statistics queries |
| Metadata and art | `metadata/*`, `games/{workId}/metadata`, `artwork/*`, `works/{workId}/artwork/{slot}` |
| Settings and setup | Typed `preferences/*` and setup commands |
| Accounts, credentials and plugins | `connections/*` and typed plugin commands |
| Launching and installing | `entries/{ownershipId}/actions` |
| Background operations | Progress and operation status queries |

The small library snapshot is suitable for a new frontend; `library/workspace` supplies the
bulk facts needed for Avalonia's richer tile projection. Backend bucket/grouping results are
authoritative. Do not query SQLite or reimplement identity resolution and recommendation rules
in a frontend. Credentials and storage keys have no generic read/write endpoint; credential
forms submit dedicated commands and read redacted connection state.

`POST entries/{ownershipId}/actions` accepts `Primary` as well as explicit Play, Install,
Uninstall, Manage and OpenStore actions. Primary selects the existing play/install action
from current backend ownership facts; unsupported entries are refused. It shares the same
operation-ID deduplication and cannot accept an executable path or launcher URI from a
client. This lets shell activations launch an entry without reconstructing its action from
a possibly stale library snapshot. Ownership IDs in URL paths retain the positive signed
64-bit range. JavaScript clients must preserve larger IDs as decimal text rather than
rounding them through `Number`.

`GET works/{workId}/backdrop?aspectRatio=...` returns ordered backdrop candidates and a
separate portrait fallback. Each candidate includes its source aspect ratio and whether
an ultrawide surface should fit the whole hero. The backend applies saved artwork choices,
grouped identities and artwork-source preferences through the shared presentation policy;
frontends should try that order rather than rank images themselves. This query reads local
metadata and does not download images. `GET artwork/image` accepts decode widths from 64
through 3840 pixels. Clients should cancel obsolete image requests and release decoded
resources when a view closes.

`GET games/{workId}/igdb/state` includes `available`, which reports whether the backend
has an assignment service. An explicit `false` hides the manual matching control. This
does not report credential readiness: missing credentials can still be configured without
changing the service. Older responses omit the field; clients retain matching support
unless the backend explicitly reports it unavailable.

`GET games/{workId}/metadata` likewise reports `available` for its optional editing
service. When absent, the response retains the work title and pin state with no editable
fields; Details and other reads remain usable. Metadata save, reset and artwork mutations
return `Unavailable` without writes. Clients hide editing only for explicit `false`,
preserving compatibility with older responses that omit the property.

## Live changes

Subscribe to `GET /api/v1/events` before loading snapshots. The response is `text/event-stream`;
each `change` event has `id: <epoch>:<sequence>` and JSON data containing `epoch`, `sequence`,
`kind`, optional `resource`, and `occurredAt`. Comments are heartbeats, not events.

The initial `resync-required` arrives after subscription registration: refetch all observed
state. For subsequent invalidations such as `library.changed`, `preferences.changed`,
`feed.changed`, `identity.changed` and operation progress, refetch the relevant snapshots.
Events describe committed changes rather than speculative local edits. Coalesce bursts, but
retain another refresh when a new event arrives while a read is in flight.

Reconnect with `Last-Event-ID: <last-consumed-id>`. Replay is bounded. An expired cursor,
backend restart or sequence gap means a full resync; never assume missing changes were empty.
Slow consumers receive a resync marker instead of blocking writes. Closing a client does not cancel
backend operations. Unsaved editor drafts remain local; live refresh must not overwrite them.

## Mutations and errors

Manual-game edits, list edits, journal writes, metadata edits, identity review decisions and artwork selections
carry revisions or other explicit concurrency evidence. Preserve the revision from the
snapshot being edited. HTTP 409 means state changed or the operation conflicts: reload and
let the user resolve the draft. Do not silently overwrite another frontend's edit. Identity
separation supplies the link ID the user observed so it cannot remove a replacement link.
IGDB match assignment currently holds a write transaction while fetching uncached metadata;
other writes can wait for that fetch to finish.
Immediate preference toggles and hide/delete intent commands use last-writer-wins semantics;
the full library-preferences PUT replaces that preference object.

Commands are not automatically retried. A dropped response may follow a successful commit.
Launching uses an operation UUID; retry the same logical operation with the same UUID to
avoid dispatching it twice. Operation IDs are bounded, temporary deduplication records,
not an indefinite exactly-once guarantee across backend restarts. Long-running operations
return status identifiers and can continue while a frontend disconnects.
Metadata sync results distinguish completion, missing credentials, partial failure and a
failed library refresh. Once an operation finishes, later progress callbacks cannot replace
its terminal message or result.

An accepted journal PUT also finishes if its frontend disconnects. Graceful backend shutdown
drains the request before disposing its repositories. After a lost response, read the note
again: an unchanged revision permits the same draft to retry; a changed revision must be
reconciled before another write. Closing an editor is not a way to cancel an accepted save.

Typical errors are 400 for invalid input, 401 for a missing/stale token, 403 for a forbidden
origin or host, 404 for missing resources, and 409 for conflicts. Errors use HTTP problem
responses where a diagnostic explanation is available. After an uncertain failure, reload
current state before proposing a retry.

Sign-in starts a client-scoped, expiring challenge. The frontend owns browser presentation;
the backend validates completion and stores credentials. Cancel unused attempts. Launch
commands accept an ownership ID and a supported action, never an arbitrary executable or URI.
Artwork imports upload bounded image bytes; file dialogs and reading the explicitly chosen
file belong to the frontend. Artwork browse results carry expiring offers so clients cannot
substitute provider URLs or forge selection provenance. Images are served through the
authenticated artwork endpoint and may be cached separately by each frontend.

## JavaScript example

This runs in Node.js or an Electron main process. It reads a snapshot; use the event protocol
above for a live interface. The directory is supplied explicitly so the example cannot modify
the user's real library by accident.

```javascript
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

const directory = process.argv[2];
if (!directory) throw new Error('Pass the development data directory');
const connection = JSON.parse(await readFile(join(directory, 'backend', 'endpoint.json'), 'utf8'));
if (connection.apiVersion !== '1') throw new Error('Unsupported backend API');
const origin = new URL(connection.address);
if (origin.protocol !== 'http:' || origin.hostname !== '127.0.0.1'
    || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash)
  throw new Error('Invalid local backend origin');
const response = await fetch(new URL('/api/v1/library', origin), {
  headers: { Authorization: `Bearer ${connection.token}` },
  redirect: 'error'
});
if (!response.ok) throw new Error(`Backend returned ${response.status}`);
const library = await response.json();
console.log(library);
```

## Verification

`Winnow.Backend.Tests` exercises real loopback HTTP, authorization, multi-client commits,
conflicts, restart/replay, artwork and launch deduplication. `Winnow.Application.Tests`
checks use cases against temporary SQLite databases. `Winnow.Api.Client.Tests` checks the
external transport. Frontend composition and headless tests cover desktop and fullscreen;
assembly-reference checks reject backend implementations in Avalonia and Avalonia in the
backend. Existing domain integration tests use `tests/Shared/LegacyTestServices.cs`; that
composition is not shipped or called by the frontend.
