import { execFileSync } from 'node:child_process'
import { readFileSync, realpathSync, statSync } from 'node:fs'
import { dirname, isAbsolute, join, resolve, win32 } from 'node:path'
import { UUID } from 'builder-util-runtime'
import { releaseVersion } from './update-policy'

export type WindowsInstallerKind = 'inno' | 'nsis'
export const legacyInstallRegistryKey =
  'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}_is1'

function isFile(path: string) {
  try {
    return statSync(path).isFile()
  } catch {
    return false
  }
}
function absolute(path: string | null | undefined): path is string {
  return (
    typeof path === 'string' &&
    !!path.trim() &&
    !path.includes('\0') &&
    (process.platform === 'win32'
      ? win32.isAbsolute(path) && win32.parse(path).root.length > 1
      : isAbsolute(path))
  )
}
function matchesAppHost(directory: string, executable: string, name: string, windows: boolean) {
  if (!absolute(directory) || !absolute(executable)) return false
  const expected = resolve(directory, name),
    actual = resolve(executable)
  return windows ? expected.toLowerCase() === actual.toLowerCase() : expected === actual
}

export function isSupportedUbuntu(osRelease: string | null | undefined): boolean {
  const values = new Map<string, string>()
  for (const line of (osRelease ?? '').split('\n')) {
    const match = /^\s*(ID|VERSION_ID)=(?:"([^"\r\n]*)"|([^\s"\r\n]+))\s*$/.exec(line)
    if (!match) continue
    // Ambiguous duplicate identity fields cannot authorize replacement.
    if (values.has(match[1])) return false
    values.set(match[1], match[2] ?? match[3])
  }
  return values.get('ID') === 'ubuntu' && values.get('VERSION_ID') === '24.04'
}

export function isManagedLinux(directory: string): boolean {
  const systemPath = (path: string) => /^\/(?:opt|usr)(?:\/|$)/.test(path)
  if (systemPath(directory) || isFile(join(directory, 'package-managed'))) return true
  try {
    const canonical = realpathSync(directory)
    return systemPath(canonical) || isFile(join(canonical, 'package-managed'))
  } catch {
    return false
  }
}

/** Archive replacement needs the matching release manifest, apphost and bundled helper. */
export function isPortableInstallation(
  directory: string,
  executable: string | null | undefined,
  runtime: string,
  osRelease: string | null | undefined,
): boolean {
  if (!['win-x64', 'linux-x64'].includes(runtime) || !executable) return false
  const windows = runtime === 'win-x64'
  if (!matchesAppHost(directory, executable, windows ? 'Winnow.exe' : 'Winnow', windows)) return false
  if (isFile(join(directory, 'package-managed'))) return false
  if (!windows && (isManagedLinux(directory) || !isSupportedUbuntu(osRelease))) return false
  if (isFile(join(directory, 'unins000.exe')) || isFile(join(directory, 'Uninstall Winnow.exe'))) return false
  try {
    const path = join(directory, 'release-info.json')
    if (statSync(path).size > 8192) return false
    const manifest = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, '')) as {
      runtime?: unknown
      version?: unknown
    }
    const version = typeof manifest.version === 'string' ? releaseVersion(manifest.version) : null
    return (
      manifest.runtime === runtime &&
      !Object.hasOwn(manifest, 'package-managed') &&
      !!version &&
      !/^(dev|ci)$/i.test(version.pre[0] ?? '') &&
      isFile(join(directory, 'update-helper', windows ? 'Winnow.Update.Helper.exe' : 'Winnow.Update.Helper'))
    )
  } catch {
    return false
  }
}

export function matchesWindowsInstallation(
  executable: string | null | undefined,
  registeredDirectory: string | null | undefined,
  kind: WindowsInstallerKind,
  appName = 'Winnow',
): boolean {
  if (!executable || !registeredDirectory || !/^[^\\/\0]+$/.test(appName)) return false
  return (
    matchesAppHost(registeredDirectory, executable, `${appName}.exe`, true) &&
    isFile(join(registeredDirectory, kind === 'inno' ? 'unins000.exe' : `Uninstall ${appName}.exe`))
  )
}

/** The secondary AppImage path obeys the same tested distribution and managed-directory boundary. */
export function isAppImageInstallation(
  appImage: string | null | undefined,
  osRelease: string | null | undefined,
) {
  if (!absolute(appImage) || !isSupportedUbuntu(osRelease) || !isFile(appImage)) return false
  try {
    return !isManagedLinux(dirname(appImage)) && !isManagedLinux(dirname(realpathSync(appImage)))
  } catch {
    return false
  }
}

export function windowsInstallRegistryKey(kind: WindowsInstallerKind, nsisAppId = 'app.winnow.afterglow') {
  if (kind === 'inno') return legacyInstallRegistryKey
  // electron-builder 26.15.3 NsisTarget uses this namespace for its default APP_GUID.
  return `Software\\${UUID.v5(nsisAppId, UUID.parse('50e065bc-3134-11e6-9bab-38c9862bdaf3'))}`
}

export function readWindowsInstallDirectory(
  kind: WindowsInstallerKind,
  nsisAppId = 'app.winnow.afterglow',
): string | null {
  if (process.platform !== 'win32') return null
  const key = windowsInstallRegistryKey(kind, nsisAppId)
  try {
    // Base64 keeps non-ASCII profile paths independent of the console code page.
    const script = `$ErrorActionPreference='Stop'; $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64); try { $key=$base.OpenSubKey('${key}'); if ($null -ne $key) { try { $value=$key.GetValue('InstallLocation',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); if ($value -is [string]) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value)) } } finally { $key.Dispose() } } } finally { $base.Dispose() }`
    const output = execFileSync(
      join(
        process.env.SystemRoot ?? 'C:\\Windows',
        'System32',
        'WindowsPowerShell',
        'v1.0',
        'powershell.exe',
      ),
      ['-NoProfile', '-NonInteractive', '-Command', script],
      { windowsHide: true, timeout: 3000, maxBuffer: 16_384, encoding: 'utf8' },
    ).trim()
    if (!output || !/^[A-Za-z0-9+/]*={0,2}$/.test(output)) return null
    const value = Buffer.from(output, 'base64').toString('utf8')
    return absolute(value) ? value : null
  } catch {
    return null
  }
}
