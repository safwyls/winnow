-- Individual membership/play observations are positive evidence only.
-- No legacy row can prove that a whole account inventory completed.
CREATE TABLE account_inventory_observations (
    store           TEXT NOT NULL,
    account_ref     TEXT NOT NULL,
    source          TEXT NOT NULL,
    revision        INTEGER NOT NULL,
    attempted_at    TEXT NOT NULL,
    is_complete     INTEGER NOT NULL DEFAULT 0 CHECK (is_complete IN (0, 1)),
    observed_at     TEXT,
    item_count      INTEGER CHECK (item_count >= 0),
    PRIMARY KEY (store, account_ref, source),
    CHECK (is_complete = 0 OR (observed_at IS NOT NULL AND item_count IS NOT NULL))
);
