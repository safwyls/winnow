import { spawn, type StdioOptions } from 'node:child_process'
import { existsSync } from 'node:fs'
import { dirname, isAbsolute, join } from 'node:path'
import { AppImageUpdater, NsisUpdater, type AppUpdater } from 'electron-updater'
import { CancellationToken, type UpdateInfo } from 'builder-util-runtime'
import type { UpdateDriver, UpdateRelease } from './application-updater'
import { electronAssetName, newerRelease, updateRepository, validateUpdateFiles } from './update-policy'

export function spawnInstaller(
  command: string,
  args: string[],
  env = process.env,
  stdio: StdioOptions = 'ignore',
): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { env, stdio, detached: true, windowsHide: true })
    child.once('error', reject)
    child.once('spawn', () => {
      child.unref()
      resolve(true)
    })
  })
}
class ConfirmedAppImageUpdater extends AppImageUpdater {
  private launches: Promise<boolean>[] = []
  protected override spawnLog(
    command: string,
    args: string[] = [],
    env?: NodeJS.ProcessEnv,
    stdio: StdioOptions = 'ignore',
  ): Promise<boolean> {
    const launched = spawnInstaller(command, args, env, stdio)
    // The upstream installer does not await its launch. Retain that result before allowing the app to quit.
    void launched.catch(() => {})
    this.launches.push(launched)
    return launched
  }
  async installConfirmed() {
    this.launches = []
    if (!this.install(true, true)) throw new Error('Update installation was refused.')
    await Promise.all(this.launches)
  }
}
export interface NativeUpdateAdapter {
  autoDownload: boolean
  autoInstallOnAppQuit: boolean
  allowPrerelease: boolean
  allowDowngrade: boolean
  channel: string | null
  disableWebInstaller: boolean
  disableDifferentialDownload: boolean
  checkForUpdates(): Promise<{ isUpdateAvailable: boolean; updateInfo: UpdateInfo } | null>
  downloadUpdate(token: CancellationToken): Promise<string[]>
  on(event: string, listener: (...args: any[]) => void): unknown
  removeListener(event: string, listener: (...args: any[]) => void): unknown
}
export function createUpdateDriver(options: {
  native: NativeUpdateAdapter
  version: string
  platform: string
  arch: string
  supported: boolean
  install(paths: string[]): Promise<void>
  quit(): void
  manualCheck?: (includeBeta: boolean) => Promise<UpdateRelease | null>
}): UpdateDriver {
  const native = options.native
  native.autoDownload = false
  native.autoInstallOnAppQuit = false
  native.disableWebInstaller = true
  native.disableDifferentialDownload = true
  native.allowDowngrade = false
  // Errors are reported through the action promise; an unhandled EventEmitter error must not terminate Winnow.
  native.on('error', () => {})
  let release: UpdateRelease | null = null,
    downloaded: string[] = []
  return {
    supported: options.supported,
    async check(includeBeta) {
      if (!options.supported && options.manualCheck) return options.manualCheck(includeBeta)
      const previousRelease = release,
        previousDownload = downloaded
      release = null
      downloaded = []
      native.channel = includeBeta ? 'beta' : 'latest'
      native.allowPrerelease = includeBeta
      native.allowDowngrade = false
      const result = await native.checkForUpdates()
      if (
        !result?.isUpdateAvailable ||
        !newerRelease(result.updateInfo.version, options.version, includeBeta)
      ) {
        release = null
        downloaded = []
        return null
      }
      const info = result.updateInfo
      validateUpdateFiles(info, options.platform, options.arch)
      const name = electronAssetName(info.version, options.platform, options.arch)
      // Keep the native provider's resolved metadata, but remove other architectures before it selects a file.
      const architectureFiles = info.files.filter((file) => file.url.split('/').at(-1) === name)
      info.files.splice(0, info.files.length, ...architectureFiles)
      if (previousRelease?.version === info.version) downloaded = previousDownload
      release = {
        version: info.version,
        releaseUrl: `${updateRepository}/releases/tag/v${info.version}`,
        downloadUrl: `${updateRepository}/releases/download/v${info.version}/${name}`,
      }
      return release
    },
    async download(requested, signal, progress) {
      if (!options.supported || requested.version !== release?.version)
        throw new Error('Check for updates again before downloading.')
      downloaded = []
      const token = new CancellationToken(),
        cancel = () => token.cancel()
      const listener = (value: { percent: number }) => {
        if (Number.isFinite(value.percent)) progress(value.percent)
      }
      signal.addEventListener('abort', cancel, { once: true })
      if (signal.aborted) cancel()
      native.on('download-progress', listener)
      try {
        const paths = await native.downloadUpdate(token)
        if (signal.aborted || !paths.length || paths.some((path) => !isAbsolute(path)))
          throw new Error('Update download was cancelled or invalid.')
        downloaded = paths
      } finally {
        signal.removeEventListener('abort', cancel)
        native.removeListener('download-progress', listener)
      }
    },
    async install() {
      if (!options.supported || !release || !downloaded.length)
        throw new Error('Download and verify the update before installing.')
      await options.install([...downloaded])
      downloaded = []
      options.quit()
    },
  }
}

async function manualRelease(version: string, includeBeta: boolean): Promise<UpdateRelease | null> {
  const response = await fetch('https://api.github.com/repos/safwyls/winnow/releases?per_page=100', {
    headers: { Accept: 'application/vnd.github+json' },
    redirect: 'error',
    signal: AbortSignal.timeout(30_000),
  })
  if (!response.ok) throw new Error('The releases could not be read.')
  const text = await response.text()
  if (text.length > 4 * 1024 ** 2) throw new Error('The release list is too large.')
  const releases = JSON.parse(text) as Array<{
    tag_name?: string
    draft?: boolean
    prerelease?: boolean
    assets?: Array<{ name?: string }>
  }>
  if (!Array.isArray(releases)) throw new Error('Invalid releases.')
  let selected = version
  for (const release of releases) {
    if (
      release.draft ||
      (!includeBeta && release.prerelease) ||
      typeof release.tag_name !== 'string' ||
      !release.tag_name.startsWith('v') ||
      !release.assets?.some((asset) => asset.name?.startsWith('Winnow-Electron-'))
    )
      continue
    const candidate = release.tag_name.slice(1)
    if (newerRelease(candidate, selected, includeBeta)) selected = candidate
  }
  return selected === version
    ? null
    : {
        version: selected,
        releaseUrl: `${updateRepository}/releases/tag/v${selected}`,
        downloadUrl: `${updateRepository}/releases/tag/v${selected}`,
      }
}
export function electronUpdateDriver(options: {
  version: string
  packaged: boolean
  appName: string
  quit(): void
}): UpdateDriver {
  const linux = process.platform === 'linux'
  const native: AppUpdater = linux ? new ConfirmedAppImageUpdater() : new NsisUpdater()
  native.setFeedURL({ provider: 'github', owner: 'safwyls', repo: 'winnow' })
  native.logger = null
  const supported =
    options.packaged &&
    (process.platform === 'win32'
      ? !process.env.PORTABLE_EXECUTABLE_DIR &&
        existsSync(join(dirname(process.execPath), `Uninstall ${options.appName}.exe`))
      : linux &&
        !!process.env.APPIMAGE &&
        isAbsolute(process.env.APPIMAGE) &&
        existsSync(process.env.APPIMAGE))
  return createUpdateDriver({
    native,
    version: options.version,
    platform: process.platform,
    arch: process.arch,
    supported,
    manualCheck: (includeBeta) => manualRelease(options.version, includeBeta),
    async install(paths) {
      if (linux) {
        process.env.WINNOW_APPIMAGE_UPDATED = '1'
        await (native as ConfirmedAppImageUpdater).installConfirmed()
      } else {
        await spawnInstaller(paths[0], ['--updated', '/S', '--force-run'])
      }
    },
    quit: options.quit,
  })
}
