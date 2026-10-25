import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const ports = vi.hoisted(() => ({
  registration: vi.fn<() => string | null>(),
  osRelease: 'ID=ubuntu\nVERSION_ID="24.04"' as string | null,
  osReads: 0,
}))
vi.mock('node:fs', async (original) => {
  const fs = await original<typeof import('node:fs')>()
  return {
    ...fs,
    readFileSync: (
      path: Parameters<typeof fs.readFileSync>[0],
      options?: Parameters<typeof fs.readFileSync>[1],
    ) => {
      if (path !== '/etc/os-release')
        return options === undefined ? fs.readFileSync(path) : fs.readFileSync(path, options)
      ports.osReads++
      if (ports.osRelease === null) throw Error('OS release is unavailable')
      return ports.osRelease
    },
  }
})
vi.mock('../src/main/installation-policy', async (original) => ({
  ...(await original<typeof import('../src/main/installation-policy')>()),
  readWindowsInstallDirectory: ports.registration,
}))
import { primaryDistribution } from '../src/main/distribution-installation'

let root: string
beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'winnow-primary-distribution-'))
  ports.registration.mockReset().mockReturnValue(null)
  ports.osRelease = 'ID=ubuntu\nVERSION_ID="24.04"'
  ports.osReads = 0
})
afterEach(() => rmSync(root, { recursive: true, force: true }))

function fixture(platform: 'win32' | 'linux' = 'win32', bom = false) {
  const runtime = platform === 'win32' ? 'win-x64' : 'linux-x64'
  const executable = join(root, platform === 'win32' ? 'Winnow.exe' : 'Winnow')
  const helper = join(
    root,
    'update-helper',
    platform === 'win32' ? 'Winnow.Update.Helper.exe' : 'Winnow.Update.Helper',
  )
  mkdirSync(join(root, 'update-helper'))
  writeFileSync(helper, 'fixture')
  const manifest = { frontend: 'electron', runtime, version: '1.0.0' }
  writeFileSync(join(root, 'release-info.json'), `${bom ? '\uFEFF' : ''}${JSON.stringify(manifest)}`)
  return {
    manifest,
    helper,
    options: { packaged: true, executable, version: '1.0.0', platform, arch: 'x64' },
  }
}

describe('primary distribution route and native eligibility', () => {
  it('leaves development and packages without the primary manifest on their existing route', () => {
    const options = {
      packaged: true,
      executable: join(root, 'Winnow.exe'),
      version: '1.0.0',
      platform: 'win32',
      arch: 'x64',
    }
    expect(primaryDistribution(options)).toBeNull()
    const f = fixture()
    expect(primaryDistribution({ ...f.options, packaged: false })).toBeNull()
    expect(ports.registration).not.toHaveBeenCalled()
    expect(ports.osReads).toBe(0)
  })
  it('does not claim a parent directory manifest for an executable in a portable child directory', () => {
    const f = fixture()
    mkdirSync(join(root, 'portable'))
    expect(primaryDistribution({ ...f.options, executable: join(root, 'portable', 'Winnow.exe') })).toBeNull()
  })
  it.each(['{', 'null', '{}', '{"frontend":"avalonia"}'])(
    'refuses invalid or foreign primary identity instead of falling back to a different installer: %s',
    (text) => {
      const f = fixture()
      writeFileSync(join(root, 'release-info.json'), text)
      expect(() => primaryDistribution(f.options)).toThrow()
      expect(ports.registration).not.toHaveBeenCalled()
    },
  )
  it.each([{ platform: 'darwin' }, { arch: 'arm64' }, { version: '2.0.0' }, { platform: 'linux' }])(
    'refuses an executable identity that differs from the shipped manifest: %j',
    (patch) => {
      const f = fixture()
      expect(() => primaryDistribution({ ...f.options, ...patch })).toThrow('identity')
      expect(ports.registration).not.toHaveBeenCalled()
    },
  )
  it.each(['win32', 'linux'] as const)(
    'selects the exact %s portable host and helper with no registration dependency',
    (platform) => {
      const f = fixture(platform)
      expect(primaryDistribution(f.options)).toEqual({
        installation: root,
        executable: platform === 'win32' ? 'Winnow.exe' : 'Winnow',
        runtime: platform === 'win32' ? 'win-x64' : 'linux-x64',
        kind: 'portable',
        supported: true,
      })
      expect(ports.registration).not.toHaveBeenCalled()
      rmSync(f.helper)
      expect(primaryDistribution(f.options)).toMatchObject({ kind: 'portable', supported: false })
    },
  )
  it.each(['win32', 'linux'] as const)(
    'accepts the same %s UTF-8 BOM manifest in route selection and eligibility',
    (platform) => {
      const f = fixture(platform, true)
      expect(primaryDistribution(f.options)).toMatchObject({ kind: 'portable', supported: true })
    },
  )
  it('accepts the registered primary Inno installation, never the secondary NSIS registration', () => {
    const f = fixture()
    writeFileSync(join(root, 'unins000.exe'), 'fixture')
    ports.registration.mockReturnValue(root)
    expect(primaryDistribution(f.options)).toMatchObject({ kind: 'installed', supported: true })
    expect(ports.registration).toHaveBeenCalledWith('inno')
    ports.registration.mockReturnValue(join(root, 'portable'))
    expect(primaryDistribution(f.options)).toMatchObject({ kind: 'installed', supported: false })
    ports.registration.mockReturnValue(null)
    expect(primaryDistribution(f.options)).toMatchObject({ kind: 'installed', supported: false })
  })
  it('does not let another apphost next to the manifest claim the registered installation', () => {
    const f = fixture()
    writeFileSync(join(root, 'unins000.exe'), 'fixture')
    ports.registration.mockReturnValue(root)
    expect(primaryDistribution({ ...f.options, executable: join(root, 'dotnet.exe') })).toMatchObject({
      kind: 'installed',
      supported: false,
    })
  })
  it('keeps an installed package without its handoff helper on manual updates', () => {
    const f = fixture()
    writeFileSync(join(root, 'unins000.exe'), 'fixture')
    ports.registration.mockReturnValue(root)
    rmSync(f.helper)
    expect(primaryDistribution(f.options)).toMatchObject({ kind: 'installed', supported: false })
  })
  it.each(['ID=ubuntu\nVERSION_ID=22.04', 'ID=debian\nVERSION_ID=24.04', null])(
    'keeps an unsupported Linux distribution readable but cannot replace its archive: %s',
    (release) => {
      const f = fixture('linux')
      ports.osRelease = release
      expect(primaryDistribution(f.options)).toMatchObject({ kind: 'portable', supported: false })
      expect(ports.registration).not.toHaveBeenCalled()
    },
  )
  it('routes a managed Linux installation to its package artifact without enabling replacement', () => {
    const f = fixture('linux')
    writeFileSync(join(root, 'package-managed'), 'deb')
    expect(primaryDistribution(f.options)).toMatchObject({ kind: 'managed', supported: false })
    expect(ports.registration).not.toHaveBeenCalled()
  })
  it.each([
    ['win32', 'file'],
    ['win32', 'manifest'],
    ['linux', 'file'],
    ['linux', 'manifest'],
  ] as const)('keeps %s %s-managed copies ineligible before any helper replacement', (platform, marker) => {
    const f = fixture(platform)
    if (marker === 'file') writeFileSync(join(root, 'package-managed'), 'managed')
    else
      writeFileSync(
        join(root, 'release-info.json'),
        JSON.stringify({ ...f.manifest, 'package-managed': false }),
      )
    expect(primaryDistribution(f.options)).toMatchObject({
      kind: platform === 'linux' && marker === 'file' ? 'managed' : 'portable',
      supported: false,
    })
  })
})
