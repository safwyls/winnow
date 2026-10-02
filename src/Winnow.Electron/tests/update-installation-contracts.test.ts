import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

const queryRegistry = vi.hoisted(() => vi.fn())
vi.mock('node:child_process', async (original) => ({
  ...(await original<typeof import('node:child_process')>()),
  execFileSync: queryRegistry,
}))
import {
  isAppImageInstallation,
  isManagedLinux,
  isPortableInstallation,
  isSupportedUbuntu,
  legacyInstallRegistryKey,
  matchesWindowsInstallation,
  readWindowsInstallDirectory,
  windowsInstallRegistryKey,
} from '../src/main/installation-policy'
import { prepareUpdateResume, restartArguments, restoreUpdateResume } from '../src/main/update-resume'

const ubuntu = 'ID=ubuntu\nVERSION_ID="24.04"'
let directory: string
beforeEach(() => {
  directory = mkdtempSync(join(tmpdir(), 'winnow-installation-contract-'))
  queryRegistry.mockReset()
})
afterEach(() => {
  vi.restoreAllMocks()
  rmSync(directory, { recursive: true, force: true })
})

describe('original update installation eligibility', () => {
  it.each([
    ['ID=ubuntu\nVERSION_ID="24.04"', true],
    ['ID=ubuntu\nVERSION_ID=22.04', false],
    ['ID=debian\nVERSION_ID=24.04', false],
    [null, false],
  ])('LinuxAutomationUsesTheTestedDistribution: %s', (release, supported) => {
    expect(isSupportedUbuntu(release)).toBe(supported)
  })

  it.each([
    ['win-x64', 'Winnow.exe', 'Winnow.Update.Helper.exe'],
    ['linux-x64', 'Winnow', 'Winnow.Update.Helper'],
  ])('PortableRequiresMatchingManifestAppHostAndHelper: %s', (runtime, executable, helper) => {
    mkdirSync(join(directory, 'update-helper'))
    writeFileSync(join(directory, 'release-info.json'), JSON.stringify({ runtime, version: '1.0.0' }))
    const app = join(directory, executable)
    expect(isPortableInstallation(directory, app, runtime, ubuntu)).toBe(false)
    writeFileSync(join(directory, 'update-helper', helper), 'fixture')
    expect(isPortableInstallation(directory, app, runtime, ubuntu)).toBe(true)
    expect(isPortableInstallation(directory, join(directory, 'dotnet.exe'), runtime, ubuntu)).toBe(false)
    writeFileSync(join(directory, 'unins000.exe'), 'fixture')
    expect(isPortableInstallation(directory, app, runtime, ubuntu)).toBe(false)
  })

  it('ManagedLinuxNeverUsesArchiveReplacement', () => {
    expect(isManagedLinux('/opt/winnow/')).toBe(true)
    expect(isManagedLinux('/usr/lib/winnow/')).toBe(true)
    writeFileSync(join(directory, 'package-managed'), 'deb')
    expect(isManagedLinux(directory)).toBe(true)
    mkdirSync(join(directory, 'update-helper'))
    writeFileSync(join(directory, 'update-helper', 'Winnow.Update.Helper'), 'fixture')
    writeFileSync(
      join(directory, 'release-info.json'),
      JSON.stringify({ runtime: 'linux-x64', version: '1.0.0' }),
    )
    expect(isPortableInstallation(directory, join(directory, 'Winnow'), 'linux-x64', ubuntu)).toBe(false)
  })
  it.each([
    ['win-x64', 'file'],
    ['win-x64', 'manifest'],
    ['linux-x64', 'file'],
    ['linux-x64', 'manifest'],
  ])('mirrors the shared helper managed marker refusal for %s in the %s', (runtime, marker) => {
    const windows = runtime === 'win-x64'
    mkdirSync(join(directory, 'update-helper'))
    writeFileSync(
      join(directory, 'update-helper', windows ? 'Winnow.Update.Helper.exe' : 'Winnow.Update.Helper'),
      'fixture',
    )
    writeFileSync(
      join(directory, 'release-info.json'),
      JSON.stringify({
        runtime,
        version: '1.0.0',
        ...(marker === 'manifest' ? { 'package-managed': false } : {}),
      }),
    )
    if (marker === 'file') writeFileSync(join(directory, 'package-managed'), 'managed')
    expect(
      isPortableInstallation(directory, join(directory, windows ? 'Winnow.exe' : 'Winnow'), runtime, ubuntu),
    ).toBe(false)
  })

  it.each(['inno', 'nsis'] as const)('PortableExecutableCannotClaimARegisteredInstallation: %s', (kind) => {
    const uninstaller = join(directory, kind === 'inno' ? 'unins000.exe' : 'Uninstall Winnow.exe')
    writeFileSync(uninstaller, 'fixture')
    expect(matchesWindowsInstallation(join(directory, 'Winnow.exe'), directory, kind)).toBe(true)
    expect(matchesWindowsInstallation(join(directory, 'portable', 'Winnow.exe'), directory, kind)).toBe(false)
    expect(matchesWindowsInstallation(join(directory, 'dotnet.exe'), directory, kind)).toBe(false)
    rmSync(uninstaller)
    expect(matchesWindowsInstallation(join(directory, 'Winnow.exe'), directory, kind)).toBe(false)
  })

  it.each([
    [null, null],
    ['', ''],
    ['Winnow.exe', 'relative'],
  ])('MissingInstallRegistrationIsUnsupported: %s / %s', (executable, registered) => {
    for (const kind of ['inno', 'nsis'] as const)
      expect(matchesWindowsInstallation(executable, registered, kind)).toBe(false)
  })

  it.each(['win-arm64', 'linux-arm64', 'osx-x64', ''])(
    'refuses archive runtime %s even with copied valid helper files',
    (runtime) => {
      mkdirSync(join(directory, 'update-helper'))
      for (const helper of ['Winnow.Update.Helper', 'Winnow.Update.Helper.exe'])
        writeFileSync(join(directory, 'update-helper', helper), 'fixture')
      writeFileSync(join(directory, 'release-info.json'), JSON.stringify({ runtime, version: '1.0.0' }))
      expect(isPortableInstallation(directory, join(directory, 'Winnow.exe'), runtime, ubuntu)).toBe(false)
    },
  )

  it.each([
    '{',
    JSON.stringify({ runtime: 'linux-x64', version: '1.0.0' }),
    JSON.stringify({ runtime: 'win-x64', version: '1.0.0-dev.1' }),
    JSON.stringify({ runtime: 'win-x64', version: '1.0.0-ci.1' }),
    JSON.stringify({ runtime: 'win-x64', version: 'not a release' }),
    JSON.stringify({ runtime: 'win-x64', version: 100 }),
  ])('rejects mismatched or invalid archive manifest %s', (manifest) => {
    mkdirSync(join(directory, 'update-helper'))
    writeFileSync(join(directory, 'update-helper', 'Winnow.Update.Helper.exe'), 'fixture')
    writeFileSync(join(directory, 'release-info.json'), manifest)
    expect(isPortableInstallation(directory, join(directory, 'Winnow.exe'), 'win-x64', ubuntu)).toBe(false)
  })

  it('treats an NSIS uninstaller and a helper directory as ineligible for archive replacement', () => {
    mkdirSync(join(directory, 'update-helper', 'Winnow.Update.Helper.exe'), { recursive: true })
    writeFileSync(
      join(directory, 'release-info.json'),
      JSON.stringify({ runtime: 'win-x64', version: '1.0.0' }),
    )
    expect(isPortableInstallation(directory, join(directory, 'Winnow.exe'), 'win-x64', ubuntu)).toBe(false)
    rmSync(join(directory, 'update-helper', 'Winnow.Update.Helper.exe'), { recursive: true })
    writeFileSync(join(directory, 'update-helper', 'Winnow.Update.Helper.exe'), 'fixture')
    writeFileSync(join(directory, 'Uninstall Winnow.exe'), 'fixture')
    expect(isPortableInstallation(directory, join(directory, 'Winnow.exe'), 'win-x64', ubuntu)).toBe(false)
  })

  it('requires an existing AppImage, the tested distribution and an unmanaged directory', () => {
    const image = join(directory, 'Winnow.AppImage')
    expect(isAppImageInstallation(image, ubuntu)).toBe(false)
    writeFileSync(image, 'fixture')
    expect(isAppImageInstallation(image, ubuntu)).toBe(true)
    expect(isAppImageInstallation('Winnow.AppImage', ubuntu)).toBe(false)
    expect(isAppImageInstallation(image, 'ID=ubuntu\nVERSION_ID=22.04')).toBe(false)
    expect(isAppImageInstallation(image, 'ID=debian\nVERSION_ID=24.04')).toBe(false)
    expect(isAppImageInstallation(image, null)).toBe(false)
    writeFileSync(join(directory, 'package-managed'), 'deb')
    expect(isAppImageInstallation(image, ubuntu)).toBe(false)
  })

  it('refuses ambiguous distribution metadata and reserves only the actual system prefixes', () => {
    expect(isSupportedUbuntu('ID=ubuntu\nID=debian\nVERSION_ID=24.04')).toBe(false)
    expect(isSupportedUbuntu('ID=ubuntu\nVERSION_ID=24.04\nVERSION_ID=22.04')).toBe(false)
    expect(isManagedLinux('/opt')).toBe(true)
    expect(isManagedLinux('/usr')).toBe(true)
    expect(isManagedLinux('/opt-extra/winnow')).toBe(false)
    expect(isManagedLinux('/user/winnow')).toBe(false)
  })
})

describe('registered installer identity', () => {
  it('keeps the primary legacy Inno key separate from the actual NSIS builder identity', () => {
    expect(windowsInstallRegistryKey('inno')).toBe(legacyInstallRegistryKey)
    expect(legacyInstallRegistryKey).toBe(
      'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}_is1',
    )
    expect(windowsInstallRegistryKey('nsis')).toMatch(/^Software\\[0-9a-f-]{36}$/)
    expect(windowsInstallRegistryKey('nsis', 'different.app')).not.toBe(windowsInstallRegistryKey('nsis'))
  })
  it('reads a bounded per-user registration without losing non-ASCII path characters', () => {
    vi.spyOn(process, 'platform', 'get').mockReturnValue('win32')
    const selected = 'C:\\Users\\Winnow café 日本'
    queryRegistry.mockReturnValue(Buffer.from(selected, 'utf8').toString('base64') + '\r\n')
    expect(readWindowsInstallDirectory('inno')).toBe(selected)
    expect(queryRegistry).toHaveBeenCalledWith(
      expect.stringMatching(/powershell\.exe$/),
      ['-NoProfile', '-NonInteractive', '-Command', expect.stringContaining(legacyInstallRegistryKey)],
      expect.objectContaining({ windowsHide: true, timeout: 3000, maxBuffer: 16384 }),
    )
    expect(queryRegistry.mock.calls[0][1][3]).toContain('RegistryHive]::CurrentUser')
    expect(queryRegistry.mock.calls[0][1][3]).toContain('RegistryView]::Registry64')
    expect(queryRegistry.mock.calls[0][1][3]).not.toContain('SetValue')
  })
  it.each(['', 'not base64!', Buffer.from('relative').toString('base64')])(
    'refuses missing or malformed registration %s',
    (output) => {
      vi.spyOn(process, 'platform', 'get').mockReturnValue('win32')
      queryRegistry.mockReturnValue(output)
      expect(readWindowsInstallDirectory('inno')).toBeNull()
    },
  )
  it('fails closed on inaccessible registration and does not query it on other systems', () => {
    vi.spyOn(process, 'platform', 'get').mockReturnValue('win32')
    queryRegistry.mockImplementation(() => {
      throw Error('Access denied')
    })
    expect(readWindowsInstallDirectory('nsis')).toBeNull()
    queryRegistry.mockClear()
    vi.spyOn(process, 'platform', 'get').mockReturnValue('linux')
    expect(readWindowsInstallDirectory('nsis')).toBeNull()
    expect(queryRegistry).not.toHaveBeenCalled()
  })
})

describe('restart recovery source contract', () => {
  it('RestartPreservesSelectedLibraryAndNoSyncWithoutRepeatingSeedOrLogin', () => {
    const selected = join(tmpdir(), 'Winnow selected library')
    expect(
      restartArguments(selected, [
        '--seed-sample',
        '--epic-login',
        '--data-dir',
        'wrong',
        '--no-sync',
        '--unknown',
      ]),
    ).toEqual(['--data-dir', resolve(selected), '--no-sync'])
    expect(
      restartArguments(selected, ['--seed-sample', '--epic-login', 'winnow://launch/1', '--unknown']),
    ).toEqual(['--data-dir', resolve(selected)])
  })
  it.each([false, true])(
    'restores a matching one-use restart without inherited commands: AppImage=%s',
    (appImageUpdated) => {
      const path = join(directory, 'resume.json'),
        selected = join(directory, 'Winnow selected library')
      prepareUpdateResume(path, { version: '2.0.0', dataDirectory: selected, noSync: true }, 100)
      expect(
        restoreUpdateResume(path, {
          version: '2.0.0',
          args: [
            'Winnow.exe',
            ...(appImageUpdated ? [] : ['--updated']),
            '--seed-sample',
            '--epic-login',
            '--unknown',
          ],
          appImageUpdated,
          now: 101,
        }).args,
      ).toEqual([
        'Winnow.exe',
        ...(appImageUpdated ? [] : ['--updated']),
        '--data-dir',
        selected,
        '--no-sync',
      ])
      expect(existsSync(path)).toBe(false)
    },
  )
  it('writes only the known restart fields and leaves a normal launch context unconsumed', () => {
    const path = join(directory, 'resume.json')
    prepareUpdateResume(
      path,
      {
        version: '2.0.0',
        dataDirectory: directory,
        noSync: false,
        args: ['--seed-sample'],
        secret: 'no',
      } as Parameters<typeof prepareUpdateResume>[1],
      100,
    )
    expect(JSON.parse(readFileSync(path, 'utf8'))).toEqual({
      version: '2.0.0',
      dataDirectory: directory,
      noSync: false,
      created: 100,
    })
    const args = ['Winnow.exe', '--data-dir', directory, '--seed-sample']
    expect(restoreUpdateResume(path, { version: '2.0.0', args, appImageUpdated: false }).args).toEqual(args)
    expect(existsSync(path)).toBe(true)
  })
  it.each([
    { version: '1.0.0', created: 100, noSync: true },
    { version: '2.0.0', created: 100, noSync: 'true' },
    { version: '2.0.0', created: '100', noSync: true },
    { version: '2.0.0', created: -8_000_000, noSync: true },
    { version: '2.0.0', created: 102, noSync: true },
    { version: '2.0.0', created: 100, noSync: true, dataDirectory: 'relative' },
    { version: '2.0.0', created: 100, noSync: true, dataDirectory: 'bad\0directory' },
  ])('refuses invalid restart context and deletes it instead of selecting another library: %j', (value) => {
    const path = join(directory, 'resume.json')
    writeFileSync(path, JSON.stringify(value))
    expect(() =>
      restoreUpdateResume(path, {
        version: '2.0.0',
        args: ['Winnow.exe', '--updated'],
        appImageUpdated: false,
        now: 101,
      }),
    ).toThrow('could not be restored')
    expect(existsSync(path)).toBe(false)
  })
  it('refuses conflicting directory and URI arguments before any restored launch', () => {
    for (const args of [
      ['--data-dir', 'wrong'],
      ['--data-dir=wrong'],
      ['winnow://launch/1'],
      ['--uri=winnow://launch/1'],
    ]) {
      const path = join(directory, 'resume.json')
      prepareUpdateResume(path, { version: '2.0.0', dataDirectory: directory, noSync: true }, 100)
      expect(() =>
        restoreUpdateResume(path, {
          version: '2.0.0',
          args: ['Winnow.exe', '--updated', ...args],
          appImageUpdated: false,
          now: 101,
        }),
      ).toThrow('could not be restored')
      expect(existsSync(path)).toBe(false)
    }
  })
})
