---
id: doc-1
title: Artwork browser and library collection design
type: specification
created_date: '2026-09-17 16:09'
updated_date: '2026-09-17 16:10'
---
# Artwork browser and library collections

Proposed product design, 2026-09-17. This describes future behavior, not shipped capabilities. Implementation tasks own execution plans; the current domain documents remain authoritative for existing behavior.

## Purpose and scope

Let a person choose how their games look in Winnow. The browser belongs to the app. Steam and IGDB are built-in sources; enabled artwork plugins contribute to the same browser. SteamGridDB supplies community alternatives rather than requiring a separate editor.

Provide three independent slots: Hero (the existing background), Cover and Icon. Logos and animation playback are outside the initial scope. Preserve local-file and URL imports. This changes Winnow artwork only.

## Per-game flow

Open Change artwork from game details or an artwork row in Edit details. Show the game title and Hero, Cover and Icon tabs. Each tab retains its current choice, source filter and browsing position during the visit.

The source filter offers All, Steam, IGDB and active browser-capable plugins. All is the default. Built-in sources remain visible with actionable setup or unavailable states where necessary. Sources only offer real assets appropriate to the selected slot; IGDB must not fabricate icon choices from unrelated portraits. Steam and IGDB may offer few official choices; their presence does not imply a community-sized gallery.

Show the current image first, then source-grouped candidate thumbnails. Label source, dimensions and creator where supplied. Indicate Current and Selected with text as well as color. Load thumbnails progressively, support paging where a provider offers it, and keep usable sources available when another fails.

Selecting a thumbnail previews it without saving. Hero previews offer desktop and fullscreen crops; cover previews honor the existing Fit/Fill preference. Icon previews show actual small-size use and a larger inspection view, with transparency legible. Use hero/background consistently in explanatory copy to connect the existing editor field.

Use artwork saves the selected slot and refreshes visible uses on both surfaces. Back or Escape leaves saved art unchanged. Use automatic explicitly removes that slot's saved choice and restores the normal source policy. Download and validate the chosen full-size image before committing it; a failure leaves the old choice intact.

## Desktop and fullscreen

Desktop uses a focused body in the existing details visual tree, with slot tabs and source controls above a thumbnail grid and a larger preview beside it. Narrow layouts place the preview above the grid. Keep the apply action reachable without scrolling through all results. Closing returns focus to the invoking control.

Fullscreen uses a dedicated artwork page with larger candidates, a persistent preview and the normal safe margins and text scaling. D-pad navigates candidates, A previews or activates controls, and B returns one level without saving. The visible Use artwork action commits. Source selection and file import remain controller reachable. Preserve focus by candidate identity as pages arrive; do not jump focus when results reorder.

Use existing theme tokens and typography. Full-color artwork stays untinted; Volt marks selection. Provide named controls, readable source/error states, keyboard focus, no required hover interactions and reduced-motion behavior. Verification covers each presentation independently.

## Saved choices and source capabilities

A manual slot choice wins over collection choices; a collection choice wins over automatic sources. Store enough provenance to show the source, asset and creator/page link where available, while retaining downloaded selected images offline. Refreshes, key removal and plugin disablement must not erase selected images.

Apply a choice to the displayed game and its confirmed linked copies without changing game identity. Implementation must define and test how choices survive unlinking; it must not copy a choice onto an unrelated work. Provider title search, if needed, requires explicit artwork-match selection and never performs a library identity merge.

The current SDK exposes an unpaged artwork list with Background, Cover and Screenshot kinds. It lacks Icon and dedicated browsing/collection capabilities. Extend capabilities compatibly so existing providers continue working, and keep paging, setup state, attribution and supported slots explicit. The app owns presentation, caching and saved choices; plugins own provider-specific discovery.

Icon is a new saved slot, not just another browser tab. Its initial visible consumer is the Windows game jump list, which currently derives icons from covers; fallback remains available. Both app surfaces expose the same icon picker and preview. Do not add decorative game icons to cover-led layouts solely to demonstrate the setting.

## Collections

Add Apply artwork collection under Metadata & artwork on both surfaces when a capable provider is available. Start with a pasted SteamGridDB collection URL or ID; an in-app collection directory is optional future work.

Resolve the collection into a review showing games matched to the library, before/after images, supported slots, unmatched entries and games without collection art. Match exact external identifiers or an explicit user-confirmed artwork match. Duplicate candidates for the same game and slot require a choice; never silently pick a title match.

Default to preserving individual choices. Let the user explicitly include selected manually customized slots in the review. Apply only selected, successfully downloaded assignments and report applied, skipped and failed counts. Missing assets leave current artwork in place. Cancellation stops further work and retains a clear record of completed assignments.

Treat collection application as a one-time operation initially. Do not subscribe to collection changes or silently style games imported later. A later reapply is another review. Offer Undo for each application; restore the preceding value only where the slot still has the value written by that operation, preserving later manual edits.

## Feasibility and delivery

Collection enumeration is not yet verified against a supported SteamGridDB interface. Public collection pages and a third-party collection downloader exist, but neither establishes a stable API contract. A separate feasibility task must verify pagination, game identifiers, artwork types, access requirements, terms and rate limits. If only unsupported page scraping is available, return that limitation for a product decision before implementing it.

Deliver the shared choice model and provider capabilities, then the Steam/IGDB browser, then SteamGridDB browsing. Investigate collections independently; bulk application depends on both verified access and the shared choice model.

Acceptance includes restart/offline persistence, existing plugin compatibility, linked games, source failures, concurrent refreshes, desktop keyboard and fullscreen controller navigation, scaling and focus return. Collection tests additionally cover ambiguous matches, partial failures, cancellation and undo after later edits. Use throwaway data directories for interactive verification.

## Evidence consulted

- src/Winnow.PluginSdk/PluginContracts.cs: current artwork kinds and provider method.
- src/Winnow.Core/Queries/WorkFields.cs: editable cover and background slots, no icon.
- src/Winnow.App/Services/WorkMetadataEditService.cs: local/URL imports and field reset.
- src/Winnow.App/Services/BackdropSelection.cs: automatic source order and saved-background priority.
- design-system.md sections 2, 3, 8 and 10.10: tokens, typography, accessibility and existing editor.
- https://www.steamgriddb.com/api/v2
- https://docs.rs/crate/steamgriddb-dl/1.0.1 (third-party collection downloader; not proof of official API support)
