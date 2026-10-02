#!/usr/bin/env python3
"""Offline mutation tests; synthetic payloads are never executed or installed."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
import shutil
import sqlite3
import subprocess
import sys
from contextlib import closing

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('package', Path(__file__).with_name('verify-package.py'))
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackageContracts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        for name in package.REQUIRED:
            self.write(name, b'payload')
        self.write_json('release-info.json', {'frontend': 'electron', 'runtime': 'linux-x64', 'version': '1.2.3', 'commit': 'a' * 40})
        self.write_json('plugins/steamgriddb/plugin.json', {'id': 'steamgriddb', 'entryAssembly': 'Winnow.Plugin.SteamGridDb.dll'})
        for directory, name in [('backend', 'Winnow.Backend'), ('update-helper', 'Winnow.Update.Helper')]:
            for suffix in ['', '.dll', '.deps.json']:
                self.write(f'{directory}/{name}{suffix}', b'payload')
            for library in ['libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so']:
                self.write(f'{directory}/{library}', b'payload')
            frameworks = ['Microsoft.NETCore.App'] + (['Microsoft.AspNetCore.App'] if directory == 'backend' else [])
            self.write_json(f'{directory}/{name}.runtimeconfig.json', {'runtimeOptions': {'includedFrameworks': [{'name': f} for f in frameworks]}})
        elf = b'\x7fELF\x02\x01' + bytes(12) + b'\x3e\x00'
        for name in package.APPHOSTS:
            self.write(name, elf)
            (self.root / name).chmod(0o755)
        self.verify(refresh=True)

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)

    def write_json(self, name, value):
        self.write(name, json.dumps(value).encode())

    def verify(self, **kwargs):
        return package.verify(self.root, '1.2.3', **kwargs)

    def test_intact_complete_inventory(self):
        self.assertGreater(self.verify(), 25)

    def test_changed_payload_refused(self):
        self.write('resources/app.asar', b'changed')
        with self.assertRaisesRegex(ValueError, 'checksum'):
            self.verify()

    def test_unlisted_file_refused(self):
        self.write('unlisted', b'data')
        with self.assertRaisesRegex(ValueError, 'checksum'):
            self.verify()

    def test_missing_checksum_file_refused(self):
        (self.root / 'PACKAGE-SHA256SUMS').unlink()
        with self.assertRaises(FileNotFoundError):
            self.verify()

    def test_missing_runtime_and_notices_refused(self):
        for name in ['backend/libhostfxr.so', 'resources/DOTNET-NOTICES.json', 'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.dll']:
            with self.subTest(name=name):
                path = self.root / name
                original = path.read_bytes()
                path.unlink()
                with self.assertRaisesRegex(ValueError, 'Missing'):
                    self.verify()
                path.write_bytes(original)

    def test_recursive_private_data_and_legacy_ui_refused_even_if_rehashed(self):
        for name in ['backend/appsettings.local.json', 'data/winnow.db-wal', 'resources/Avalonia.Base.dll', 'backend/Winnow.dll', 'test.secrets.json']:
            with self.subTest(name=name):
                self.write(name, b'private')
                with self.assertRaisesRegex(ValueError, 'Forbidden'):
                    self.verify(refresh=True)
                (self.root / name).unlink()

    def test_wrong_frontend_runtime_version_commit_refused(self):
        original = json.loads((self.root / 'release-info.json').read_text())
        for key, value in [('frontend', 'avalonia'), ('runtime', 'osx-x64'), ('version', '1.2.4'), ('commit', 'not-a-sha')]:
            with self.subTest(key=key):
                self.write_json('release-info.json', {**original, key: value})
                with self.assertRaisesRegex(ValueError, 'identity'):
                    self.verify(refresh=True)

    def test_incorrect_provider_refused(self):
        self.write_json('plugins/steamgriddb/plugin.json', {'id': 'different', 'entryAssembly': 'Other.dll'})
        with self.assertRaisesRegex(ValueError, 'provider'):
            self.verify(refresh=True)

    def test_missing_self_contained_aspnet_refused(self):
        self.write_json('backend/Winnow.Backend.runtimeconfig.json', {'runtimeOptions': {'includedFrameworks': [{'name': 'Microsoft.NETCore.App'}]}})
        with self.assertRaisesRegex(ValueError, 'frameworks'):
            self.verify(refresh=True)

    def test_wrong_architecture_executable_refused(self):
        self.write('Winnow', b'MZ' + bytes(30))
        with self.assertRaisesRegex(ValueError, 'ELF x64'):
            self.verify(refresh=True)

    def test_duplicate_or_invalid_checksum_entry_refused(self):
        ledger = self.root / 'PACKAGE-SHA256SUMS'
        original = ledger.read_text()
        for line in [original.splitlines()[0], 'bad-hash  Winnow']:
            ledger.write_text(original + line + '\n')
            with self.assertRaisesRegex(ValueError, 'checksum'):
                self.verify()

    def test_managed_marker_required_and_portable_marker_refused(self):
        with self.assertRaisesRegex(ValueError, 'Missing'):
            self.verify(kind='managed', refresh=True)
        self.write('package-managed', b'deb\n')
        self.verify(kind='managed', refresh=True)
        self.verify(kind='managed')
        with self.assertRaisesRegex(ValueError, 'marker'):
            self.verify()

    def test_manifest_managed_marker_presence_refuses_portable_even_when_false(self):
        original = json.loads((self.root / 'release-info.json').read_text())
        for value in [True, False, None, 'deb']:
            with self.subTest(value=value):
                self.write_json('release-info.json', {**original, 'package-managed': value})
                with self.assertRaisesRegex(ValueError, 'marker'):
                    self.verify(refresh=True)

    @unittest.skipIf(os.name == 'nt', 'The Linux Winnow/winnow pair requires a case-sensitive filesystem')
    def test_portable_integration_files_included_in_ledger(self):
        for name in ['winnow', 'winnow.desktop', 'dragon.svg', 'setup-sandbox.sh', 'LINUX-README.md']:
            self.write(name, b'integration')
            (self.root / name).chmod(0o755 if name in ['winnow', 'setup-sandbox.sh'] else 0o644)
        self.verify(kind='portable', refresh=True)
        self.verify(kind='portable')
        self.write('setup-sandbox.sh', b'changed')
        with self.assertRaisesRegex(ValueError, 'checksum'):
            self.verify(kind='portable')

    @unittest.skipIf(os.name == 'nt', 'Unix executable and privileged modes are checked on the Linux packaging runner')
    def test_unix_executable_and_privileged_modes(self):
        for mode in [0o644, 0o4755, 0o2755, 0o777]:
            with self.subTest(mode=oct(mode)):
                (self.root / 'Winnow').chmod(mode)
                with self.assertRaises(ValueError):
                    self.verify()
        (self.root / 'Winnow').chmod(0o755)
        self.verify()

    @unittest.skipIf(os.name == 'nt', 'Symlink creation is verified on the Linux packaging runner')
    def test_symlinks_refused(self):
        (self.root / 'linked').symlink_to(self.root / 'Winnow')
        with self.assertRaisesRegex(ValueError, 'links'):
            self.verify(refresh=True)

    def test_native_dependency_closure_includes_transitive_alternatives_not_unrelated_runner_packages(self):
        installed = {'libgtk': 'libglib (>= 1), libc', 'libglib': 'libc', 'unrelated': 'secret'}
        actual = package.dependency_closure('libgtk (>= 2), libgcc-s1 | libgcc1', installed)
        self.assertEqual(actual, {'libgtk', 'libglib', 'libc', 'libgcc-s1', 'libgcc1'})
        self.assertNotIn('unrelated', actual)
        self.assertNotIn('secret', actual)


class SandboxAndPreservationContracts(unittest.TestCase):
    def test_desktop_entry_matches_measured_electron_class_and_preserves_legacy_integration(self):
        source = Path(__file__).with_name('build.sh').read_text(encoding='utf-8')
        start = source.index('write_desktop_entry() {')
        writer = source[start:source.index('\n}\n', start) + 3]
        bash = shutil.which('bash') or 'C:/Program Files/Git/bin/bash.exe'
        for frontend, expected in [('electron', 'winnow'), ('avalonia', 'Winnow')]:
            with self.subTest(frontend=frontend), tempfile.TemporaryDirectory() as directory:
                result = subprocess.run([bash, '-s', '--', frontend],
                                        input=('PATH=/usr/bin:/bin:$PATH\n' + writer + '\nfrontend=$1\nwrite_desktop_entry winnow.desktop\n').encode(),
                                        cwd=directory, capture_output=True)
                self.assertEqual(result.returncode, 0, result.stderr.decode())
                entry = (Path(directory) / 'winnow.desktop').read_text(encoding='utf-8').splitlines()
                self.assertIn('StartupWMClass=' + expected, entry)
                self.assertIn('Name=Winnow', entry)
                self.assertIn('Exec=winnow %u', entry)
                self.assertIn('MimeType=x-scheme-handler/winnow;', entry)
                self.assertIn('Icon=winnow', entry)
                self.assertEqual(sum(line.startswith('StartupWMClass=') for line in entry), 1)

    def test_actual_setup_path_guard_accepts_literal_paths_and_rejects_policy_syntax(self):
        source = Path(__file__).with_name('setup-sandbox.sh').read_text(encoding='utf-8')
        guard = next(line for line in source.splitlines() if line.startswith('case $executable in '))
        bash = shutil.which('bash') or 'C:/Program Files/Git/bin/bash.exe'
        for path, accepted in [('/home/user/My Games/Winnow', True), ('/home/é/Winnow', True),
                               ('/home/*/Winnow', False), ('/home/{a,b}/Winnow', False),
                               ('/home/a[0]/Winnow', False), ('/home/a?/Winnow', False),
                               ('/home/a"/Winnow', False), ('/home/a\\/Winnow', False),
                               ('/home/a\n/Winnow', False), ('/home/a\t/Winnow', False)]:
            with self.subTest(path=path):
                # NUL-delimited stdin preserves control/pattern characters through Windows/MSYS,
                # whose native command-line conversion otherwise expands braces before Bash sees them.
                result = subprocess.run([bash, '-c', 'fail() { exit 1; }; IFS= read -r -d "" executable; ' + guard],
                                        input=path.encode() + b'\0', capture_output=True)
                self.assertEqual(result.returncode == 0, accepted, result.stderr)

    def test_library_snapshot_requires_unchanged_real_records_and_sentinel(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with closing(sqlite3.connect(root / 'winnow.db')) as db, db:
                db.executescript('''
                    CREATE TABLE works(id INTEGER PRIMARY KEY,name TEXT);
                    CREATE TABLE releases(id INTEGER PRIMARY KEY,work_id INTEGER REFERENCES works(id),name TEXT);
                    CREATE TABLE ownerships(id INTEGER PRIMARY KEY,release_id INTEGER REFERENCES releases(id),
                        store TEXT,account_ref TEXT,acquired_at TEXT,license_type TEXT,price_paid_cents INTEGER);
                ''')
            command = [sys.executable, str(Path(__file__).with_name('library-evidence.py')), str(root)]
            initial = subprocess.check_output([*command, '--seed'], text=True)
            evidence = root / 'before.json'
            evidence.write_text(initial, encoding='utf-8')
            self.assertEqual(json.loads(initial)['ownerships'][0][-1], 500)
            subprocess.run([*command, '--expect', str(evidence)], check=True, capture_output=True)
            with closing(sqlite3.connect(root / 'winnow.db')) as db, db:
                db.execute('UPDATE works SET name=?', ('unexpected overwrite',))
            result = subprocess.run([*command, '--expect', str(evidence)], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b'changed the released library records', result.stderr)

    def test_library_snapshot_does_not_create_a_missing_database(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('library-evidence.py')), directory], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((Path(directory) / 'winnow.db').exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)
