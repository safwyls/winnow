import { existsSync, readFileSync, statSync } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import {
  isManagedLinux,
  isPortableInstallation,
  matchesWindowsInstallation,
  readWindowsInstallDirectory,
} from './installation-policy'
import type { DistributionKind } from './distribution-release'

export function primaryDistribution(options: {
  packaged: boolean
  executable: string
  version: string
  platform?: string
  arch?: string
}): {
  installation: string
  executable: string
  runtime: 'win-x64' | 'linux-x64'
  kind: DistributionKind
  supported: boolean
} | null {
  if (!options.packaged) return null
  const installation = dirname(options.executable)
  const manifestPath = join(installation, 'release-info.json')
  if (!existsSync(manifestPath)) return null
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8').replace(/^\uFEFF/, ''))
  if (manifest.frontend !== 'electron') throw Error('This package does not identify the Electron frontend.')
  const platform = options.platform ?? process.platform
  const runtime = platform === 'win32' ? 'win-x64' : 'linux-x64'
  if (
    !['win32', 'linux'].includes(platform) ||
    (options.arch ?? process.arch) !== 'x64' ||
    manifest.runtime !== runtime ||
    manifest.version !== options.version
  )
    throw Error('The installed package identity does not match Winnow.')
  let release: string | null = null
  if (platform === 'linux') {
    try {
      release = readFileSync('/etc/os-release', 'utf8')
    } catch {}
  }
  const kind: DistributionKind =
    platform === 'linux' && isManagedLinux(installation)
      ? 'managed'
      : platform === 'win32' && existsSync(join(installation, 'unins000.exe'))
        ? 'installed'
        : 'portable'
  let helperAvailable = false
  try {
    helperAvailable = statSync(
      join(
        installation,
        'update-helper',
        platform === 'win32' ? 'Winnow.Update.Helper.exe' : 'Winnow.Update.Helper',
      ),
    ).isFile()
  } catch {}
  const supported =
    kind === 'installed'
      ? helperAvailable &&
        matchesWindowsInstallation(options.executable, readWindowsInstallDirectory('inno'), 'inno')
      : kind === 'portable' && isPortableInstallation(installation, options.executable, runtime, release)
  return { installation, executable: basename(options.executable), runtime, kind, supported }
}
