---
id: m-1
title: "M6-EXPORT"
---

## Description

Export the user's Winnow database as JSON and CSV. The export must round-trip through the importer without loss. This is the first milestone that consumes the acquisition-fact columns (acquired_at, license_type, price_paid_cents) stored by M5's account-page importer.

Exit criteria: JSON and CSV exports exist; re-importing a JSON export into an empty database produces identical query results; acquisition-fact columns appear in the export.
