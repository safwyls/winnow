#!/usr/bin/env python3
"""Start a real released baseline on a disposable desktop and clean up only its own processes."""
import json
import os
from pathlib import Path
import signal
import re
import sqlite3
import subprocess
import sys
import time
import urllib.request
from contextlib import closing


def identity(pid):
    fields = (Path('/proc') / str(pid) / 'stat').read_text().rsplit(')', 1)[1].split()
    return fields[0], fields[19]


def backend_request(connection, route, method='GET'):
    if not re.fullmatch(r'http://127\.0\.0\.1:[1-9][0-9]{0,4}/?', connection['address']):
        raise RuntimeError('Invalid baseline loopback endpoint')
    request = urllib.request.Request(connection['address'].rstrip('/') + route,
        headers={'Authorization': 'Bearer ' + connection['token']}, method=method)
    return urllib.request.urlopen(request, timeout=5)

executable, directory, logfile = map(Path, sys.argv[1:])
directory.mkdir(parents=True, exist_ok=True)
connection = None
backend_pid = None
backend_started = None
with logfile.open('w', encoding='utf-8') as log:
    process = subprocess.Popen([str(executable), '--data-dir', str(directory), '--no-sync'], stdout=log, stderr=log)
    try:
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f'Baseline exited before startup: {process.returncode}')
            if (directory / 'winnow.db').is_file():
                # Older releases may host internally; independent backend releases publish discovery.
                for candidate in [directory / 'backend/endpoint.json']:
                    try:
                        value = json.loads(candidate.read_text(encoding='utf-8-sig'))
                        if {'address', 'token', 'processId', 'epoch'} <= value.keys():
                            connection = value
                            backend_pid = value['processId']
                            _, backend_started = identity(backend_pid)
                    except (OSError, ValueError, AttributeError):
                        pass
                try:
                    with closing(sqlite3.connect((directory / 'winnow.db').resolve().as_uri() + '?mode=ro', uri=True, timeout=.5)) as db:
                        tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
                        ready = {'works', 'releases', 'ownerships'} <= tables
                    if (executable.parent / 'backend/Winnow.Backend').exists():
                        ready = ready and connection is not None
                        if ready:
                            with backend_request(connection, '/api/v1/health') as response:
                                health = json.load(response)
                                ready = health.get('epoch') == connection['epoch'] and health.get('apiVersion') == '1'
                    if ready and process.poll() is None:
                        break
                except (OSError, ValueError, sqlite3.Error):
                    pass
            time.sleep(.25)
        else:
            raise RuntimeError('Baseline did not initialize its isolated library')
    finally:
        if connection:
            try:
                backend_request(connection, '/api/v1/lifecycle/shutdown', 'POST').close()
            except Exception:
                pass
        if process.poll() is None:
            process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
        if backend_pid and backend_pid != process.pid:
            expected = executable.parent / 'backend/Winnow.Backend'
            proc = Path('/proc') / str(backend_pid)
            try:
                state, started = identity(backend_pid)
                if state in ('Z', 'X') or started != backend_started:
                    raise FileNotFoundError()
                if (proc / 'exe').resolve(strict=True) != expected.resolve():
                    raise RuntimeError('Refusing to stop a different backend process')
                args = (proc / 'cmdline').read_bytes().split(b'\0')
                if os.fsencode(directory) not in args:
                    raise RuntimeError('Refusing to stop a backend with a different data directory')
                os.kill(backend_pid, signal.SIGTERM)
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    state, started = identity(backend_pid)
                    if state in ('Z', 'X') or started != backend_started:
                        break
                    time.sleep(.1)
                else:
                    raise RuntimeError('Baseline backend did not exit after termination')
            except FileNotFoundError:
                pass
