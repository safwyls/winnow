-- Account identity belongs to the captured fact, never to the currently selected
-- credentials. Legacy facts remain explicitly unknown and are not reassigned.
ALTER TABLE account_transactions ADD COLUMN account_ref TEXT;
ALTER TABLE account_licenses ADD COLUMN account_ref TEXT;

DROP INDEX ux_account_transactions_fact;
CREATE UNIQUE INDEX ux_account_transactions_fact
ON account_transactions(
    source, COALESCE(account_ref, ''), kind, transaction_type_raw,
    COALESCE(occurred_at, ''), item_names_json, COALESCE(note, ''),
    COALESCE(CAST(total_cents AS TEXT), ''),
    COALESCE(CAST(list_price_cents AS TEXT), ''),
    COALESCE(CAST(wallet_change_cents AS TEXT), ''),
    COALESCE(currency_symbol, ''), COALESCE(payment_kind, ''),
    refunded, gift_recipient_present, COALESCE(app_id, ''));

DROP INDEX ux_account_licenses_fact;
CREATE UNIQUE INDEX ux_account_licenses_fact
ON account_licenses(
    source, COALESCE(account_ref, ''), item_name, COALESCE(acquired_at, ''),
    acquisition_method_raw, COALESCE(package_id, ''));

CREATE TABLE ownership_acquisition_observations (
    id               INTEGER PRIMARY KEY,
    ownership_id     INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    account_ref      TEXT,
    acquired_at      TEXT,
    license_type     TEXT,
    price_paid_cents INTEGER,
    price_source     TEXT,
    source           TEXT NOT NULL,
    captured_at      TEXT NOT NULL,
    CHECK (account_ref IS NULL OR length(trim(account_ref)) > 0)
);

CREATE UNIQUE INDEX ux_ownership_acquisition_observation
ON ownership_acquisition_observations(
    ownership_id, COALESCE(account_ref, ''), COALESCE(acquired_at, ''),
    COALESCE(license_type, ''), COALESCE(CAST(price_paid_cents AS TEXT), ''),
    COALESCE(price_source, ''), source);

CREATE INDEX ix_ownership_acquisition_account
ON ownership_acquisition_observations(account_ref, ownership_id);
