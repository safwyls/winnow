-- 0025_manual_entries.sql — origin marker for hand-added games: the one
-- table that distinguishes a manual entry from everything ingest writes.
-- Append-only: never edit this file once shipped; add 0026_*.sql instead.
--
-- ── What makes a game hand-added ───────────────────────────────────────
--
-- A hand-added game is an ordinary work + release + ownership; what
-- makes it hand-added is that the ownership's store is 'manual' and a
-- manual_entries row exists for it. The presence of this row IS the
-- origin marker — one mechanism, not two, and a table no ingest path
-- writes.
--
-- ── Why ingest never reaches it ────────────────────────────────────────
--
-- The guarantee rests on facts that were already true rather than on new
-- machinery:
--
--   (1) OwnershipRepository.UpsertAsync conflicts on (release_id, store)
--       and no ingest reader emits the store 'manual', so no resolve
--       pass can reach the row however it resolves.
--
--   (2) The work is created with name_is_provisional = 0, and
--       ExternalIdResolver.PromoteProvisionalNameAsync renames a work
--       only while that flag is set while EnrichmentSyncService's patch
--       is fill-only, so a hand-typed title is never a candidate for
--       replacement.
--
--   (3) Nothing in the runtime deletes a works, releases or ownerships
--       row at all — sync is strictly additive.
--
-- ── Session monitoring ─────────────────────────────────────────────────
--
-- No change is needed in Winnow.Monitor. GameExecutableIndexBuilder
-- reads ownerships.installed and ownerships.install_path and walks that
-- directory for executables, so naming an executable stores its
-- directory as the install path and sets installed = 1, and the game is
-- watched by the existing code path. The stored executable_path is the
-- fact a later exact-match change in the Monitor will read; today it is
-- evidence, not an index.
--
-- ── Deletion ───────────────────────────────────────────────────────────
--
-- Deleting a hand-added entry deletes the ownership (this row follows
-- by ON DELETE CASCADE), then the release only when no other ownership
-- hangs off it, then the work only when it has no releases left. A
-- Steam entry that later attached to the same release — because the
-- user supplied a Steam appid by hand — keeps its game.

CREATE TABLE manual_entries (
    ownership_id     INTEGER PRIMARY KEY REFERENCES ownerships(id) ON DELETE CASCADE,
    executable_path  TEXT,
    platform_label   TEXT,
    added_at         TEXT NOT NULL,
    updated_at       TEXT NOT NULL
);
