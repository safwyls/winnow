-- 0014_account_transaction_facts.sql — what the Steam account pages reported,
-- stored verbatim as facts rather than filtered for the ownership columns they
-- happen to fill.
-- Append-only: never edit this file once shipped; add 0015_*.sql instead.
--
-- ── What went wrong ─────────────────────────────────────────────────────────
--
-- ROADMAP §6 carried "imported acquisition facts are stored but read by
-- nothing yet". The opposite problem is what this fixes. The M5 importer
-- parsed purchases, bundles, gifts, in-game purchases, refunds and wallet
-- top-ups, used three fields from a subset of them to fill ownership columns,
-- and DISCARDED the rest on every pass. The account stats page needs those
-- discarded rows.
--
-- ── Two tables: account_transactions and account_licenses ───────────────────
--
-- They store what the two Steam account pages REPORTED, verbatim as facts,
-- rather than the ownership-shaped subset the M5 importer was fetching them
-- for. Rows are page-capture facts, not entities. `captured_at` is provenance:
-- the capture that FIRST reported the fact. A later capture re-reporting it
-- does not move the column (ON CONFLICT DO NOTHING keeps the existing row).
--
-- ── Identity is the whole fact, not its address ─────────────────────────────
--
-- Following 0013's discipline: the identity is the whole fact, not its
-- address. There is NO usable stable transaction id. The page's `transid`
-- lives only inside an onclick help-wizard URL, the parser does not read it,
-- and the row's position in the table is not stable across captures because
-- new transactions arrive at the top. So the unique index spans every
-- independently-observed column: source, kind, transaction_type_raw,
-- occurred_at, item_names_json, note, total_cents, list_price_cents,
-- wallet_change_cents, currency_symbol, payment_kind, refunded,
-- gift_recipient_present, app_id.
--
-- ── Why COALESCE(..., '') and CAST(... AS TEXT) in the index ────────────────
--
-- Same reason as 0013: SQLite treats every NULL in a UNIQUE index as distinct
-- from every other NULL, so a plain index over the nullable columns would let
-- the commonest rows replay unbounded. Integer columns are coalesced through
-- CAST(... AS TEXT) to '' rather than to a numeric sentinel, because there is
-- no integer that cannot legitimately appear in a cents column (wallet changes
-- are signed).
--
-- ── The honest cost ─────────────────────────────────────────────────────────
--
-- Dates on these pages are day-resolution, so two byte-identical transactions
-- on the SAME DAY — same item, same price, same payment kind — collapse into
-- one row. That undercounts a genuine repeat purchase. It is accepted because
-- the alternative (row position, or a capture timestamp in the identity)
-- would duplicate the entire account on every re-import, which is the far
-- worse failure.
--
-- ── What is NOT in the identity, and why ────────────────────────────────────
--
-- `acquisition_kind` is deliberately NOT part of the licence identity: it is
-- a pure function of `acquisition_method_raw` through the parser's mapping
-- table, so including it could only ever agree.
--
-- ── No CHECK on `kind` ──────────────────────────────────────────────────────
--
-- The vocabulary carries an `other` catch-all for shapes Steam has not shown
-- yet, and a CHECK in a shipped migration would need a table rebuild to widen.
-- `refunded` and `gift_recipient_present` ARE CHECK-constrained to 0/1
-- because those are genuinely binary.
--
-- ── What is deliberately absent ─────────────────────────────────────────────
--
-- This is a rule, not an oversight: no recipient name, persona, community URL
-- or miniprofile id; no card issuer or last-four fragment (only the coarse
-- wallet/card/paypal/other kind); no account id. `gift_recipient_present`
-- records that a gift had a recipient and nothing about who.
--
-- ── Currency ────────────────────────────────────────────────────────────────
--
-- `currency_symbol` is the symbol as the page rendered it. Amounts are
-- as-displayed cents from one locale sample. Nothing converts and nothing
-- should.

CREATE TABLE account_transactions (
    id                     INTEGER PRIMARY KEY,
    source                 TEXT    NOT NULL,
    kind                   TEXT    NOT NULL,
    transaction_type_raw   TEXT    NOT NULL,
    occurred_at            TEXT,
    item_names_json        TEXT    NOT NULL,
    item_count             INTEGER NOT NULL,
    note                   TEXT,
    total_cents            INTEGER,
    list_price_cents       INTEGER,
    discount_percent       INTEGER,
    wallet_change_cents    INTEGER,
    currency_symbol        TEXT,
    payment_kind           TEXT,
    refunded               INTEGER NOT NULL DEFAULT 0 CHECK (refunded IN (0, 1)),
    gift_recipient_present INTEGER NOT NULL DEFAULT 0 CHECK (gift_recipient_present IN (0, 1)),
    app_id                 TEXT,
    captured_at            TEXT    NOT NULL
);

CREATE UNIQUE INDEX ux_account_transactions_fact
ON account_transactions(
    source,
    kind,
    transaction_type_raw,
    COALESCE(occurred_at, ''),
    item_names_json,
    COALESCE(note, ''),
    COALESCE(CAST(total_cents AS TEXT), ''),
    COALESCE(CAST(list_price_cents AS TEXT), ''),
    COALESCE(CAST(wallet_change_cents AS TEXT), ''),
    COALESCE(currency_symbol, ''),
    COALESCE(payment_kind, ''),
    refunded,
    gift_recipient_present,
    COALESCE(app_id, ''));

CREATE INDEX ix_account_transactions_kind ON account_transactions(source, kind, refunded);
CREATE INDEX ix_account_transactions_occurred ON account_transactions(source, occurred_at);

CREATE TABLE account_licenses (
    id                     INTEGER PRIMARY KEY,
    source                 TEXT NOT NULL,
    item_name              TEXT NOT NULL,
    acquired_at            TEXT,
    acquisition_kind       TEXT,
    acquisition_method_raw TEXT NOT NULL,
    package_id             TEXT,
    captured_at            TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_account_licenses_fact
ON account_licenses(
    source,
    item_name,
    COALESCE(acquired_at, ''),
    acquisition_method_raw,
    COALESCE(package_id, ''));

CREATE INDEX ix_account_licenses_acquisition ON account_licenses(source, acquisition_kind);
