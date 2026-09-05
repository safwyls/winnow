---
id: TASK-78
title: Encrypt the Steam Web API key and IGDB client secret at rest
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-03 00:52'
updated_date: '2026-09-04 00:34'
labels: []
dependencies: []
priority: medium
ordinal: 105000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Epic refresh token and the Steam session secrets are stored as one DPAPI-encrypted blob under CurrentUser scope. The Steam Web API key and the IGDB client secret are still plaintext rows in the local database.

game-library-design.md section 4.7 condition 2 states the standard: a host that cannot encrypt refuses to store rather than degrading to plaintext. That condition binds the two Steam session secrets today, and the section records that the same standard is intended for every secret Winnow keeps. These two do not meet it.

README previously claimed this was already tracked as future work when no task existed. This task is that record.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The Steam Web API key is written through the same DPAPI-protected credential store the Steam session secrets use
- [x] #2 The IGDB client secret is written through that same store
- [x] #3 A host that cannot encrypt refuses to store either secret rather than falling back to a plaintext row
- [x] #4 Existing plaintext rows are migrated on first run and the plaintext columns are left empty
- [x] #5 README's statement about where credentials live matches the shipped behaviour
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Steam API key (Winnow.Enrich.SteamWeb): add ISteamApiKeyProtector + DpapiSteamApiKeyProtector (entropy Winnow.Steam.WebApiKey.v1) + UnavailableSteamApiKeyProtector (refuses); add SettingsSteamApiKeyStore (ISteamApiKeyStore) owning one encrypted settings row steam.api_key.v1 plus lazy migration of the legacy plaintext steam.api_key row (migrate on first read, leave the legacy row empty; on a host that cannot encrypt, refuse to consume and leave the user-typed row intact); SaveAsync refuses when it cannot encrypt. SettingsTableApiKeySource reads through the store; StoreConnections writes through it and returns Stored/Refused so the panel can say why.

2. IGDB (Winnow.Enrich.Igdb): add IIgdbSecretProtector + DpapiIgdbSecretProtector (entropy Winnow.Igdb.Secret.v1) + UnavailableIgdbSecretProtector; SettingsTableCredentialSource reads the client secret from encrypted igdb.client_secret.v1 with lazy migration of the legacy plaintext row; the cached Twitch access token (TwitchTokenProvider) moves from three plaintext rows to one DPAPI-encrypted blob igdb.token.v1, migrating and emptying the three legacy rows on first load (machine-minted token rows are cleared even on a host that cannot encrypt; the user-typed client secret is never destroyed). This token half is a small scope addition beyond the two secrets named in the acceptance criteria; it is named in the review finding N01 evidence as plaintext and is the same class of bearer credential.

3. UI: StoresViewModel maps the save outcome to copy (new ApiKeySaveRefused sentence); the input is only cleared on success.

4. Tests: SteamApiKeyStoreTests (round-trip, refusal, migration, entropy separation, DI selection); update SteamWebCredentialTests/SteamWebTestHost/SteamConnectionSeamTests/StoresViewModelTests; Igdb credential-source tests updated plus new migration/refusal tests; TwitchTokenProvider blob persistence and migration tests. Test hosts register reversible protectors so no test depends on real DPAPI.

5. Docs: README reworded to say precisely which credentials are DPAPI-protected (the 'uneven today' paragraph is replaced); game-library-design.md 4.7 condition 2 updated to say the key and secret now meet the standard; ROADMAP debt row removed; superseded sentences appended to docs/decisions.md.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Scope note: the cached Twitch access token (igdb.token.access_token) was included beyond the two named secrets. The review finding N01 lists it as plaintext evidence and it is the same class of bearer credential; it is now one DPAPI-protected blob under igdb.token.v1, migrated and emptied from its three legacy rows. One line was drawn and recorded in design §4.7: refusing never destroys what a user typed (legacy user-supplied rows are left as-is on a host that cannot encrypt), while machine-minted rows — the token — are emptied even there, because a mint is free.

Validation: full dotnet test green (70 Covers + 145 Recommend + 2796 Winnow.Tests, BaseOutputPath scratch per AGENTS.md). New tests: SteamApiKeyStoreTests (round-trip, no plaintext at rest, refusal, legacy migration, refusal-leaves-user-data, entropy separation Steam session vs key, DPAPI round-trip on Windows, DI selection); SteamConnectionSeamTests refusal case; StoresViewModelTests save/refusal notices; IgdbCredentialTests migration/refusal; IgdbAuthTests token blob, migration, refuse-to-store, plaintext-rows-emptied. App smoke-started with --data-dir throwaway: composition resolves ISteamApiKeyStore and IIgdbSecretProtector, no startup failures.

Docs: README 'Where your data lives' now lists precisely which credentials are DPAPI-protected; design §4.7 condition 2 updated to say the key/secret/token meet the standard; ROADMAP §5 debt row removed; superseded sentences appended verbatim to docs/decisions.md. Also added docs/code-review-2026-09-03.md to the Hoard-hygiene test's existing review-document exemption list — the untracked review file quotes legacy text and was failing the suite before this change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Closed N01: the Steam Web API key and the IGDB client secret are no longer plaintext rows in winnow.db. The key is stored DPAPI-encrypted under its own entropy (Winnow.Steam.WebApiKey.v1) through a new SettingsSteamApiKeyStore that owns the key at rest — the panel writes through it, the key chain reads through it, and StoreConnections surfaces Stored/Refused so the Platforms screen says when a key was not stored and keeps the input. The IGDB client secret is read protected (igdb.client_secret.v1) and the cached Twitch token became one protected blob (igdb.token.v1). Plaintext rows from pre-protection installs are migrated on first read and left empty; a host that cannot encrypt refuses each credential rather than saving a readable row (user-typed rows are never destroyed, machine-minted ones are emptied). README, design §4.7 and ROADMAP updated, superseded text appended to docs/decisions.md. Verified by the full test suite (3011 passing, including new store/migration/refusal/entropy tests and UI refusal tests) and a --data-dir smoke start of the app.
<!-- SECTION:FINAL_SUMMARY:END -->
