-- Choices remain attached to their original works. Confirmed identity groups share
-- them on reads; no provider observation or identity link is rewritten.
CREATE TABLE artwork_choices (
    revision      INTEGER PRIMARY KEY AUTOINCREMENT,
    work_id       INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    slot          INTEGER NOT NULL CHECK (slot IN (0, 1, 2)),
    kind          INTEGER NOT NULL CHECK (kind IN (0, 1)),
    asset_key     TEXT NOT NULL,
    source_id     TEXT NOT NULL,
    asset_id      TEXT NOT NULL,
    source_url    TEXT,
    creator       TEXT,
    page_url      TEXT,
    collection_id TEXT,
    UNIQUE (work_id, slot, kind)
);

-- Imported images already live in the durable user-art store. Preserve their
-- references without fetching assets or changing the legacy metadata fields.
INSERT INTO artwork_choices (work_id, slot, kind, asset_key, source_id, asset_id)
SELECT w.id, 1, 0, w.cover_url, 'user', w.cover_url
FROM works w
JOIN work_field_sources s ON s.work_id = w.id AND s.field = 'cover_url' AND s.source = 'user'
WHERE w.cover_url IS NOT NULL AND length(trim(w.cover_url)) > 0;

INSERT INTO artwork_choices (work_id, slot, kind, asset_key, source_id, asset_id)
SELECT w.id, 0, 0, w.background_url, 'user', w.background_url
FROM works w
JOIN work_field_sources s ON s.work_id = w.id AND s.field = 'background_url' AND s.source = 'user'
WHERE w.background_url IS NOT NULL AND length(trim(w.background_url)) > 0;
