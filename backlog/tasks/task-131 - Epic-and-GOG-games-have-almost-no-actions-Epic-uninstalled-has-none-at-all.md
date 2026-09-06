---
id: TASK-131
title: Epic and GOG games have almost no actions; Epic uninstalled has none at all
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-06 02:28'
updated_date: '2026-09-06 03:55'
labels:
  - ui
dependencies: []
priority: medium
type: feature
ordinal: 158000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Confirmed by the user from a screenshot of the running app: "we have almost zero options for epic games, I have not looked at GOG".

Read out of StoreActions.cs, the current matrix is:

| store | installed | not installed | links |
|---|---|---|---|
| Steam | Play (steam://run) | Install (steam://install) | Store page, All patch notes |
| GOG | launchGame | installationScreen | Show in GOG Galaxy |
| Epic | launch, and only with an EpicLaunchKey | NOTHING | NOTHING |

So an uninstalled Epic game presents an action band containing only the More trigger. The band exists to get the user into the game (design-system.md section 10.3) and for that game it offers no way in at all.

Two different causes, and the task should not confuse them:

1. MEASURED ABSENCE. StoreActions.cs records that its URIs "were verified by measurement against the installed launchers, not from documentation", and that no install action exists in the Epic binary. That is a finding, not an oversight, and any replacement must be verified the same way rather than taken from a forum post.

2. SIMPLY NOT BUILT. Epic has no store-page link and no launcher shortcut, and GOG has no store page and no patch notes. Epic ingest already stores CatalogItemId, CatalogNamespace and AppName (EpicCatalogEntry, EpicManifest), which is the raw material a store URL would need; whether a usable public URL can be built from them without a slug lookup is the open question. GOG stores a numeric product id and Galaxy already accepts goggalaxy://openGameView.

Establish what each launcher and storefront genuinely supports before designing the band, verify every URI against the installed launcher as the existing ones were, and record the findings in docs/spikes/ so the next person does not re-derive them. Where a store genuinely cannot support an action, that is an acceptable answer — but it should be a recorded answer rather than an empty band.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 What each of Epic and GOG can support for launch, install, store page and patch notes is established by measurement and recorded in docs/spikes/
- [x] #2 An uninstalled Epic game offers at least one honest way to reach the game, or the band states why it cannot
- [x] #3 Every new URI is verified against the installed launcher, not taken from documentation, matching how the existing ones were established
- [ ] #4 GOG gains whatever of store page and patch notes proves reachable
- [x] #5 No button promises something it cannot do — an action that cannot be honestly offered is not drawn, per section 10.3
- [x] #6 design-system.md section 10.3 records the per-store matrix so the asymmetry is visible rather than discovered
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read Epic's official protocol-activation documentation (dev.epicgames.com/docs/epic-games-store/protocol-activation). Done: it documents action=launch|updatecheck|installer, a com.epicgames.launcher://store/product/<slug> PDP route with add-on sub-paths, and a URL-encoded install-directory alternative to the Sandbox:Catalog:Artifact triple.
2. Delegate launcher verification by EXECUTION to the steam-ingest agent (it owns the Epic reader): fire action=installer at a real owned-but-uninstalled Epic game and read the LogUriHandler oracle; contrast against the undocumented action=install the spike used; probe action=updatecheck; probe the install-path route's dispatch; probe ://store/product/fortnite (the documentation's own example) to settle whether the spike's ://store/product/fez was a wrong-slug miss. Measure how many Epic rows lack a complete launch-key triple and how many of those have an install_path, to decide whether the install-path route earns its place.
3. Correct docs/spikes/store-actions-per-launcher.md via docs-writer: fix every claim the documentation contradicts, add verified-by-vendor-documentation as a provenance category citing the page, and — the part worth more than the corrected table — record HOW the error happened: a real URI tested with an invalid slug, generalised into a negative, with binary evidence (the NavigationUriHandler location table) that looked corroborating but described a different dispatch table.
4. Implement the Epic Install action with action=installer in StoreActions.EpicPrimary, only if step 2 verifies it behaves as documented. Ship nothing if it does not.
5. Assess action=updatecheck and the install-path launch route against the measured numbers; build only what earns its place.
6. Leave the Epic store route unbuilt (TASK-132 owns it), but correct the spike and design-system.md 10.3 to say 'no slug' rather than 'no route' — those imply different follow-on work. Update TASK-132's description if the finding changes what it must do.
7. Update design-system.md 10.3's per-store matrix, its prose and its 10.4 copy row via docs-writer; append every superseded sentence to docs/decisions.md.
8. Update tests: Epic off-disk now installs. Re-point TileActionsTests and any sibling assertions.
9. Wait for every docs-writer child, scan for TODO(docs-writer) and PLACEHOLDER_*, check CRLF, then dotnet build -p:BaseOutputPath=C:\Temp\winnow-epic\ -m:1 and dotnet test per project --no-build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Launcher investigation delegated to the steam-ingest agent (it owns the Epic and GOG readers). Findings recorded in docs/spikes/store-actions-per-launcher.md, every claim tagged verified-by-inspection, verified-by-execution or needs-execution-by-the-user.

Two prior findings changed. (1) 'No install action exists in the Epic binary' is stale against launcher build 20.2.9: FAppInstallUriHandler is present, found by taking the complete UriHandler symbol inventory. It was NOT executed, so it is not verified, so no Install button was shipped -- the exact one-line probe command is in the spike. (2) GOG's bare-id/release-key asymmetry (installationScreen takes the numeric id while launchGame and openGameView take gog_<id>) was suspected to be a bug and is verified correct by inspection of GalaxyClient.exe; StoreActions.GogPrimary now carries that note.

No new URI was added. Every URI already shipped was re-verified and recorded. Built instead: Band 3 states why it cannot get the user in when there is no primary action and no link. New NoWayIn enum and StoreActions.WhyNoWayIn, surfaced through TileEntry, GameTileViewModel and GameDetailsViewModel as NoWayInSentence, drawn by a new TextBlock in GameDetailsView.axaml. Three causes, three sentences, all authored by docs-writer.

AC4 NOT met and left unchecked. GOG's store page and patch notes both proved reachable (verified-by-execution against api.gog.com, anonymous, no key) but only through a network call Winnow does not make and a slug it does not store, so neither is buildable in the action band today. The same is true of the Epic store page (namespace->slug map at store-content.ak.epicgames.com, 84% coverage). That work is ingest and enrichment, not UI.

Validation: dotnet build Winnow.slnx clean, 0 warnings 0 errors. Tests 3385 / 152 / 82 passing (baseline 3373 / 152 / 82; the 12 new tests cover the reason classification, the sentence per cause, that a reachable store never draws one, and a guard against TODO/PLACEHOLDER text shipping).
<!-- SECTION:NOTES:END -->
