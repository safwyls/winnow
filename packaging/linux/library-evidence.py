#!/usr/bin/env python3
"""Seed/read only the disposable installed-release library, using schema in the released baseline."""
import argparse
import json
from pathlib import Path
import sqlite3

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
parser.add_argument('--seed', action='store_true')
parser.add_argument('--expect', type=Path)
args = parser.parse_args()
database = (args.directory / 'winnow.db').resolve()
with sqlite3.connect(database.as_uri() + ('?mode=rw' if args.seed else '?mode=ro'), uri=True) as db:
    db.execute('PRAGMA foreign_keys=ON')
    if args.seed:
        db.execute('INSERT INTO works(id,name) VALUES (?,?)', (-159, 'Installed upgrade fixture'))
        db.execute('INSERT INTO releases(id,work_id,name) VALUES (?,?,?)', (-159, -159, 'Installed upgrade edition'))
        db.execute('INSERT INTO ownerships(id,release_id,store,account_ref,acquired_at,license_type,price_paid_cents) VALUES (?,?,?,?,?,?,?)',
                   (-159, -159, 'steam', '11111', '2020-01-02 00:00:00', 'retail', 500))
        (args.directory / 'caller-owned-sentinel').write_text('do not remove\n', encoding='utf-8')
    assert db.execute('PRAGMA integrity_check').fetchone() == ('ok',)
    assert db.execute('PRAGMA foreign_key_check').fetchall() == []
    evidence = {
        'works': db.execute('SELECT id,name FROM works ORDER BY id').fetchall(),
        'releases': db.execute('SELECT id,work_id,name FROM releases ORDER BY id').fetchall(),
        'ownerships': db.execute('SELECT id,release_id,store,account_ref,acquired_at,license_type,price_paid_cents FROM ownerships ORDER BY id').fetchall(),
    }
    assert all(evidence.values()), 'The released library must contain actual linked game records'
assert (args.directory / 'caller-owned-sentinel').read_text(encoding='utf-8') == 'do not remove\n'
output = json.dumps(evidence, sort_keys=True)
if args.expect:
    assert json.loads(output) == json.loads(args.expect.read_text(encoding='utf-8')), 'Upgrade/removal changed the released library records'
print(output)
