# Steam local-file fixtures

Captured 2026-08-23 from a live Windows Steam install and SANITIZED:
account ids replaced (steam3id → `12345678`, steamid64 `LastOwner` →
`76561197972611406`), app tickets replaced with junk hex, contentids rounded,
collection membership trimmed. Structure, key names, casing, and value formats
are otherwise verbatim from disk. See `docs/spikes/steam-local-files.md` for
the findings these encode.

| File | Simulates | Quirks deliberately preserved |
|---|---|---|
| `libraryfolders.vdf` | `<steam>\steamapps\libraryfolders.vdf` | two library roots; `totalsize "0"` on primary; app with size `"0"` |
| `appmanifest_1244090.acf` | never-played install | `LastPlayed "0"`; lowercase `lastupdated` |
| `appmanifest_2686630.acf` | recently played install | `LastPlayed` epoch seconds matching localconfig ±1 s |
| `localconfig.vdf` | `userdata\12345678\config\localconfig.vdf` (trimmed) | `apptickets` false-match trap; `86400` LastPlayed sentinel; cloud-only app blocks with no playtime; unstable key order (app 4588700); `Playtime2wks`; `autocloud`/`BadgeData`/`_eula_` noise keys |
| `cloud-storage-namespace-1.json` | collections store (trimmed) | array-of-pairs top level; string-encoded JSON values; `is_deleted` tombstone; dynamic collection with `filterSpec`; `partner-ea-access` with no `added`/`removed`; `uc-` ids containing `+ / *`; `srm-` base64 id; non-collection keys to be skipped |
| `cloud-storage-namespaces.json` | namespace → version map | |

Parser limit fixtures `appmanifest-oversize.acf` and `appmanifest-deep.acf` derive from
`appmanifest_1244090.acf`, retaining its field spelling and replacing LastOwner with
fake id `76561197972611406`. The first adds a 4 KiB comment (tests lower the byte limit);
the second adds 70 nested blocks to cross the default depth limit. These mutations are
adversarial inputs, not observed Steam output.
