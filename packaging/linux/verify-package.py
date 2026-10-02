#!/usr/bin/env python3
"""Verify trusted primary Linux payload bytes, Unix modes and dynamic-library closure."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess

REQUIRED = (
    'Winnow', 'resources/app.asar', 'resources/icon.ico', 'resources/THIRD-PARTY-NOTICES.md',
    'resources/DOTNET-NOTICES.md', 'resources/DOTNET-NOTICES.json', 'LICENSE.electron.txt',
    'LICENSES.chromium.html', 'icudtl.dat', 'resources.pak', 'v8_context_snapshot.bin',
    'chrome-sandbox', 'chrome_crashpad_handler', 'release-info.json',
    'plugins/steamgriddb/plugin.json', 'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.dll',
    'plugins/steamgriddb/Winnow.Plugin.SteamGridDb.deps.json', 'plugins/steamgriddb/README.md',
    'backend/Microsoft.AspNetCore.dll',
)
APPHOSTS = ('Winnow', 'chrome_crashpad_handler', 'chrome-sandbox',
            'backend/Winnow.Backend', 'update-helper/Winnow.Update.Helper')


def payload(root):
    files = {}
    for path in root.rglob('*'):
        relative = path.relative_to(root).as_posix()
        if path.is_symlink():
            raise ValueError(f'Package links are not allowed: {relative}')
        if not path.is_file():
            continue
        if '\n' in relative or '\r' in relative or '\\' in relative:
            raise ValueError(f'Invalid package path: {relative!r}')
        if (path.name.startswith('Avalonia') and path.suffix == '.dll' or path.name in
            ('Winnow.dll', 'Winnow.Auth.WebView.dll', 'Winnow.Covers.Avalonia.dll', 'appsettings.local.json')
            or path.name.endswith('.secrets.json') or re.search(r'\.db(?:-(?:wal|shm))?$', path.name)):
            raise ValueError(f'Forbidden UI assembly or local data: {relative}')
        if os.name != 'nt' and path.stat().st_mode & (stat.S_ISUID | stat.S_ISGID | stat.S_IWOTH):
            raise ValueError(f'Privileged or world-writable package file: {relative}')
        if relative != 'PACKAGE-SHA256SUMS':
            files[relative] = path
    return files


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def dependency_names(declaration):
    return {part.strip().split()[0].split(':')[0] for part in re.split(r'[,|]', declaration) if part.strip()}


def dependency_closure(declaration, installed):
    pending = list(dependency_names(declaration))
    closure = set()
    while pending:
        name = pending.pop()
        if name in closure:
            continue
        closure.add(name)
        pending.extend(dependency_names(installed.get(name, '')) - closure)
    return closure


def declared_packages(deb):
    declared = subprocess.check_output(['dpkg-deb', '-f', str(deb), 'Depends'], text=True)
    rows = subprocess.check_output(['dpkg-query', '-W', '-f=${Package}\t${Depends},${Pre-Depends}\n'], text=True)
    return dependency_closure(declared, dict(line.split('\t', 1) for line in rows.splitlines()))


def verify(root, version, kind='published', refresh=False, dependencies=False, deb=None):
    root = Path(root).resolve(strict=True)
    manifest = json.loads((root / 'release-info.json').read_text(encoding='utf-8-sig'))
    if (manifest.get('frontend') != 'electron' or manifest.get('runtime') != 'linux-x64'
        or manifest.get('version') != version or not re.fullmatch('[0-9a-f]{40}', manifest.get('commit', ''))):
        raise ValueError('Incorrect Electron Linux release identity')
    files = payload(root)
    required = list(REQUIRED)
    for directory, name in (('backend', 'Winnow.Backend'), ('update-helper', 'Winnow.Update.Helper')):
        required.extend(f'{directory}/{name}{suffix}' for suffix in ('', '.dll', '.deps.json', '.runtimeconfig.json'))
        required.extend(f'{directory}/{name}' for name in ('libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so'))
        config = json.loads((root / directory / f'{name}.runtimeconfig.json').read_text(encoding='utf-8-sig'))
        frameworks = {row['name'] for row in config.get('runtimeOptions', {}).get('includedFrameworks', [])}
        expected = {'Microsoft.NETCore.App'} | ({'Microsoft.AspNetCore.App'} if directory == 'backend' else set())
        if not expected <= frameworks:
            raise ValueError(f'Missing self-contained frameworks: {directory}')
    if kind == 'portable':
        required += ['winnow', 'winnow.desktop', 'dragon.svg', 'setup-sandbox.sh', 'LINUX-README.md']
    if kind == 'managed':
        required += ['package-managed']
    elif (root / 'package-managed').exists() or 'package-managed' in manifest:
        raise ValueError('Portable/published directory has a package-manager marker')
    for name in required:
        if name not in files or not files[name].stat().st_size:
            raise ValueError(f'Missing or empty package file: {name}')
    plugin = json.loads(files['plugins/steamgriddb/plugin.json'].read_text(encoding='utf-8-sig'))
    if plugin.get('id') != 'steamgriddb' or plugin.get('entryAssembly') != 'Winnow.Plugin.SteamGridDb.dll':
        raise ValueError('Incorrect bundled provider identity')
    for name in APPHOSTS:
        data = files[name].read_bytes()[:20]
        if data[:6] != b'\x7fELF\x02\x01' or data[18:20] != b'\x3e\x00':
            raise ValueError(f'Expected ELF x64 executable: {name}')
    if os.name != 'nt':
        for name in APPHOSTS + (('winnow', 'setup-sandbox.sh') if kind == 'portable' else ()):
            if files[name].stat().st_mode & 0o111 != 0o111:
                raise ValueError(f'Executable mode missing: {name}')
    checksum_file = root / 'PACKAGE-SHA256SUMS'
    actual = {name: digest(path) for name, path in files.items()}
    if refresh:
        checksum_file.write_text(''.join(f'{digest}  {name}\n' for name, digest in sorted(actual.items())), encoding='utf-8')
    else:
        declared = {}
        for line in checksum_file.read_text(encoding='utf-8-sig').splitlines():
            match = re.fullmatch(r'([0-9a-f]{64})  (.+)', line)
            if not match or match[2] in declared:
                raise ValueError('Invalid or duplicate package checksum entry')
            declared[match[2]] = match[1]
        if declared != actual:
            raise ValueError('Package checksum inventory or bytes changed')
    if dependencies:
        if os.name == 'nt':
            raise ValueError('Native dependency verification requires Linux')
        allowed = declared_packages(deb) if deb else None
        for name, path in files.items():
            with path.open('rb') as stream:
                if stream.read(4) != b'\x7fELF':
                    continue
            env = {**os.environ, 'LD_LIBRARY_PATH': f'{path.parent}:{root}'}
            result = subprocess.run(['ldd', str(path)], env=env, text=True, capture_output=True, check=False)
            output = result.stdout + result.stderr
            if 'not found' in output or result.returncode and 'not a dynamic executable' not in output and 'statically linked' not in output:
                raise ValueError(f'Unresolved native dependencies: {name}\n{output}')
            if allowed is not None:
                for dependency in re.findall(r'(?:=>\s*)?(/[^\s]+)\s+\(', output):
                    library = Path(dependency).resolve()
                    if library.is_relative_to(root):
                        continue
                    owners = set()
                    for candidate in {str(library), dependency}:
                        result = subprocess.run(['dpkg-query', '-S', candidate], capture_output=True, text=True)
                        if result.returncode == 0:
                            owners.update(line.split(':', 1)[0] for line in result.stdout.splitlines() if ': ' in line)
                    if not owners & allowed:
                        raise ValueError(f'Native dependency is not covered by package Depends: {name}: {library} ({owners})')
    return len(files)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory')
    parser.add_argument('version')
    parser.add_argument('--kind', choices=('published', 'portable', 'managed'), default='published')
    parser.add_argument('--refresh-hashes', action='store_true')
    parser.add_argument('--dependencies', action='store_true')
    parser.add_argument('--deb', help='Require each system ELF dependency to belong to this package\'s declared dependency closure')
    args = parser.parse_args()
    try:
        count = verify(args.directory, args.version, args.kind, args.refresh_hashes, args.dependencies, args.deb)
        print(f'Verified {count} Electron Linux files ({args.kind}).')
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, f'error: {error}\n')
