import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  Menu,
  protocol,
  session,
  shell,
  Tray,
  Notification,
  nativeTheme,
  type IpcMainInvokeEvent,
} from 'electron'
import { mkdirSync } from 'node:fs'
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises'
import { dirname, extname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'
import { release as osRelease } from 'node:os'
import { WindowAppearanceController } from './window-appearance'
import { appearanceSession } from '../shared/appearance-session'
import { SessionAppearance } from './appearance-session'
import { AvalonThemeStore } from './avalon-theme-store'
import type { LibraryResponse, Workspace } from '../renderer/api/types'
import { recentGames } from './jump-list'
import { SnapshotRefresh } from '../shared/snapshot-refresh'
import { FontCatalogue } from './fonts'
import { inspectExecutable } from './executable-facts'
import { frontendDataLocation } from './frontend-data-location'
import { createBackendServiceLifecycle } from './backend-service'
import { ApplicationUpdater } from './application-updater'
import { electronUpdateDriver } from './electron-update-driver'
import {
  clearUpdateResume,
  prepareUpdateResume,
  restoreUpdateResume,
  updateResumeFile,
} from './update-resume'
import type { ApplicationUpdateAction, ApplicationUpdateSnapshot } from '../shared/bridge'
import type { ApiRequest, ApplicationActivation, BackendEvent, ConnectionState } from '../shared/bridge'
import { quoteArgument, readActivation, validateActivationArguments, validatedActivation } from './activation'
import { BackendTransport } from './transport'
import { importArtworkFile } from './artwork-import'
import { cancelSteamWindow, captureSteamPages, signInToSteam } from './steam-auth'
import { writeSteamDiagnostic } from './steam-diagnostics'
import { EpicSignInController } from './epic-auth'
import type { EpicSignInOptions } from '../shared/epic'
import { RequestLifetimes } from './request-lifetimes'
import { dataDirectoryRefusalCode, reportStartupFailure } from './startup-failure'
import { openLinkBrowser } from './link-browser'
import { routeLink } from './link-routing'
import { openInstallFolder, type InstallationWorkspace } from './install-folder'
import { deliverNotification } from './notifications'
import type { SteamSignInOptions } from '../shared/bridge'
import {
  backendProcessIsRunning,
  backendResponds,
  dataDirectoryArgument,
  defaultDataRoot,
  discoverBackend,
  startBackend,
} from './lifecycle'
import { containedPath, contentSecurityPolicy, trustedRendererUrl, validateExternalUrl } from './security'
import {
  installThemeDirectory,
  listThemePackages,
  profileDirectory,
  readManifest,
  readProfile,
  saveProfile,
  themeFile,
} from './storage'

const here = fileURLToPath(new URL('.', import.meta.url))
const rendererRoot = resolve(here, '../renderer')
let dataDirectory: string | undefined
let startupArgumentError: Error | undefined
let startupArgs = [...process.argv]
const resumeFile = updateResumeFile(app.getPath('appData'), app.getName())
try {
  validateActivationArguments(process.argv.slice(app.isPackaged ? 1 : 2))
  if (app.isPackaged)
    startupArgs = restoreUpdateResume(resumeFile, {
      version: app.getVersion(),
      args: process.argv,
      appImageUpdated: process.env.WINNOW_APPIMAGE_UPDATED === '1',
    }).args
  delete process.env.WINNOW_APPIMAGE_UPDATED
  dataDirectory = dataDirectoryArgument(startupArgs)
  if (!app.isPackaged && !dataDirectory)
    throw new Error('Pass --data-dir <throwaway directory> to run the frontend in development.')
  if (dataDirectory) {
    const isolatedUserData = join(dataDirectory, 'electron-userdata')
    const isolatedSessionData = join(isolatedUserData, 'chromium')
    mkdirSync(isolatedSessionData, { recursive: true })
    // Chromium creates caches at readiness, so redirects must happen before app.whenReady().
    app.setPath('userData', isolatedUserData)
    app.setPath('sessionData', isolatedSessionData)
  } else {
    const current = app.getPath('userData')
    const preserved = frontendDataLocation(app.getPath('appData'), current)
    if (preserved !== current) {
      app.setPath('userData', preserved)
      app.setPath('sessionData', preserved)
    }
  }
} catch (error) {
  startupArgumentError = error instanceof Error ? error : new Error('Invalid data directory')
}
protocol.registerSchemesAsPrivileged([
  {
    scheme: 'winnow-app',
    privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true },
  },
  {
    scheme: 'winnow-theme',
    privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true },
  },
])
app.enableSandbox()
let window: BrowserWindow | undefined
let transport: BackendTransport | undefined
let updater: ApplicationUpdater | undefined
let safeTheme = process.argv.includes('--safe-theme')
const captureAppearance = appearanceSession(startupArgs, app.isPackaged)
const sessionAppearance = captureAppearance ? new SessionAppearance(captureAppearance) : null
let quitting = false
let tray: Tray | undefined
let rendererAcceptsActivation = false
const pendingActivations: ApplicationActivation[] = [
  readActivation(process.argv.slice(app.isPackaged ? 1 : 2)),
].filter((value) => value.kind !== 'show')
let showPrimary = () => {}
function activatePrimary(activation: ApplicationActivation) {
  showPrimary()
  if (rendererAcceptsActivation && window && !window.isDestroyed())
    window.webContents.send('winnow:activation', activation)
  else if (pendingActivations.length < 64) pendingActivations.push(activation)
}
const ownsInstance = startupArgumentError
  ? true
  : app.requestSingleInstanceLock({ activation: readActivation(process.argv.slice(app.isPackaged ? 1 : 2)) })
app.on('second-instance', (_event, argv, _cwd, additionalData) =>
  activatePrimary(
    additionalData && typeof additionalData === 'object' && 'activation' in additionalData
      ? validatedActivation(additionalData.activation)
      : readActivation(argv.slice(app.isPackaged ? 1 : 2)),
  ),
)
app.on('open-url', (event, url) => {
  event.preventDefault()
  activatePrimary(readActivation([url]))
})

function emit(
  channel: string,
  payload: BackendEvent | ConnectionState | ApplicationUpdateSnapshot | boolean | undefined,
): void {
  if (window && !window.isDestroyed()) window.webContents.send(channel, payload)
}

async function initialize(): Promise<void> {
  const profileRoot = profileDirectory(
    app.getPath('userData'),
    dataDirectory ?? join(defaultDataRoot(), 'Winnow'),
  )
  const preferencesFile = join(profileRoot, 'preferences.json')
  const themesRoot = join(profileRoot, 'themes')
  const removedJumpListFile = join(profileRoot, 'jump-list-removed.json')
  const excludedJumpItems = new Set<string>()
  try {
    for (const value of JSON.parse(await readFile(removedJumpListFile, 'utf8')))
      if (typeof value === 'string') excludedJumpItems.add(value)
  } catch {}
  const jumpListRefresh = new SnapshotRefresh(async () => {
    if (!app.isPackaged || process.platform !== 'win32') return
    try {
      const [library, workspace] = await Promise.all([
        transport!.request<LibraryResponse>({ route: 'library.get' }),
        transport!.request<Workspace>({ route: 'library.workspace' }),
      ])
      if (!library.ok || !library.data || !workspace.ok || !workspace.data) return
      for (const item of app.getJumpListSettings().removedItems)
        if (item.args) excludedJumpItems.add(item.args)
      await mkdir(profileRoot, { recursive: true })
      await writeFile(removedJumpListFile, JSON.stringify([...excludedJumpItems]), 'utf8')
      const suffix = dataDirectory ? ` --data-dir ${quoteArgument(dataDirectory)}` : ''
      const items = recentGames(library.data.games, workspace.data).flatMap((game) => {
        const args = `--jump-list-game ${game.ownershipId}${suffix}`
        return excludedJumpItems.has(args)
          ? []
          : [
              {
                type: 'task' as const,
                title: game.title,
                program: process.execPath,
                args,
                iconPath: join(process.resourcesPath, 'icon.ico'),
                iconIndex: 0,
              },
            ]
      })
      app.setJumpList([
        ...(items.length ? [{ type: 'custom' as const, name: 'Recently Played', items }] : []),
        {
          type: 'tasks',
          items: [
            {
              type: 'task',
              title: 'Switch to Fullscreen Mode',
              program: process.execPath,
              args: `--jump-list-fullscreen${suffix}`,
              iconPath: join(process.resourcesPath, 'icon.ico'),
              iconIndex: 0,
            },
          ],
        },
      ])
    } catch {
      /* Jump List privacy settings can disallow custom destinations. The app remains usable. */
    }
  })
  if (app.isPackaged && process.platform === 'win32') {
    const scope = createHash('sha256').update(profileRoot.toLowerCase()).digest('hex').slice(0, 16)
    app.setAppUserModelId(`Winnow.Electron.${scope}`)
    app.setAsDefaultProtocolClient('winnow', process.execPath, [
      ...(dataDirectory ? ['--data-dir', dataDirectory] : []),
      '--uri',
    ])
  }
  const developmentOrigin = !app.isPackaged ? process.env.ELECTRON_RENDERER_URL : undefined
  if (developmentOrigin) {
    const url = new URL(developmentOrigin)
    if (
      url.protocol !== 'http:' ||
      !['localhost', '127.0.0.1'].includes(url.hostname) ||
      url.username ||
      url.password ||
      url.pathname !== '/'
    )
      throw new Error('Invalid development renderer URL')
  }
  const rendererUrl = developmentOrigin ?? 'winnow-app://app/index.html'
  const rendererOrigin = developmentOrigin ? new URL(developmentOrigin).origin : 'winnow-app://app'
  const csp = contentSecurityPolicy(developmentOrigin)
  const fonts = new FontCatalogue((url) => trustedRendererUrl(url, developmentOrigin))
  session.defaultSession.setPermissionRequestHandler((contents, permission, callback, details) =>
    callback(
      contents === window?.webContents &&
        fonts.allows(contents, permission, details.requestingUrl ?? '', details.isMainFrame),
    ),
  )
  session.defaultSession.setPermissionCheckHandler(
    (contents, permission, requestingOrigin, details) =>
      contents === window?.webContents &&
      fonts.allows(contents, permission, requestingOrigin, details.isMainFrame),
  )
  session.defaultSession.webRequest.onHeadersReceived((details, callback) =>
    callback({ responseHeaders: { ...details.responseHeaders, 'Content-Security-Policy': [csp] } }),
  )
  protocol.handle('winnow-app', async (request) => {
    try {
      const url = new URL(request.url)
      if (url.host !== 'app' || url.username || url.password || request.method !== 'GET')
        return new Response(null, { status: 403 })
      const path = containedPath(rendererRoot, decodeURIComponent(url.pathname.slice(1)))
      // The application bundle is read-only; this protocol never serves arbitrary local files.
      if (!(await stat(path)).isFile()) return new Response(null, { status: 404 })
      const types: Record<string, string> = {
        '.html': 'text/html',
        '.js': 'text/javascript',
        '.css': 'text/css',
        '.json': 'application/json',
        '.svg': 'image/svg+xml',
        '.png': 'image/png',
        '.jpg': 'image/jpeg',
        '.webp': 'image/webp',
        '.woff': 'font/woff',
        '.woff2': 'font/woff2',
      }
      const bytes = await readFile(path)
      return new Response(new Uint8Array(bytes), {
        headers: {
          'Content-Type': types[extname(path)] ?? 'application/octet-stream',
          'Content-Security-Policy': csp,
          'X-Content-Type-Options': 'nosniff',
        },
      })
    } catch {
      return new Response(null, { status: 404 })
    }
  })
  protocol.handle('winnow-theme', async (request) => {
    try {
      if (request.method !== 'GET') return new Response(null, { status: 405 })
      const origin = request.headers.get('Origin')
      if (origin && origin !== rendererOrigin && !origin.startsWith('winnow-theme://'))
        return new Response(null, { status: 403 })
      const file = await themeFile(themesRoot, request.url)
      return new Response(new Uint8Array(file.bytes), {
        headers: {
          'Content-Type': file.type,
          'Access-Control-Allow-Origin': origin ?? rendererOrigin,
          'Cache-Control': 'no-store',
          'X-Content-Type-Options': 'nosniff',
        },
      })
    } catch {
      return new Response(null, { status: 404 })
    }
  })
  let preferences: Record<string, string | null> = {}
  let startupPreferencesApplied = false
  const preferencesRefresh = new SnapshotRefresh(async () => {
    const result = await transport!.request<Array<{ preference: string; value: string | null }>>({
      route: 'preferences.presentation.get',
    })
    if (!result.ok || !Array.isArray(result.data)) return
    preferences = Object.fromEntries(result.data.map((entry) => [entry.preference, entry.value]))
    void updater?.refreshPreferences().catch(() => {})
    if (!startupPreferencesApplied && window) {
      startupPreferencesApplied = true
      if (preferences.StartInFullscreen === 'true' && !process.argv.includes('--background'))
        window.setFullScreen(true)
    }
  })
  transport = new BackendTransport({
    discover: () => discoverBackend(dataDirectory),
    onEvent: (event) => {
      emit('winnow:event', event)
      if (event.kind === 'preferences.changed' || event.kind === 'resync-required')
        void preferencesRefresh.request()
      if (event.kind === 'library.changed' || event.kind === 'resync-required') void jumpListRefresh.request()
    },
    onConnection: (state) => {
      emit('winnow:connection', state)
      if (state.connected) {
        void preferencesRefresh.request()
        void jumpListRefresh.request()
      }
    },
  })
  const backendService = createBackendServiceLifecycle({
    appPath: app.getAppPath(),
    resourcesPath: process.resourcesPath,
    packaged: app.isPackaged,
    dataDirectory,
    args: startupArgs,
  })
  updater = new ApplicationUpdater({
    version: app.getVersion(),
    packaged: app.isPackaged,
    driver: electronUpdateDriver({
      version: app.getVersion(),
      packaged: app.isPackaged,
      appName: app.getName(),
      quit: () => {
        quitting = true
        app.quit()
      },
    }),
    async preferences() {
      const result = await transport!.request<Array<{ preference: string; value: string | null }>>({
        route: 'preferences.presentation.get',
      })
      if (!result.ok || !Array.isArray(result.data)) throw new Error('Update preferences could not be read.')
      const values = Object.fromEntries(result.data.map((row) => [row.preference, row.value?.toLowerCase()]))
      return {
        automatic: values.AutomaticUpdates !== 'false',
        includeBeta: values.IncludeBetaUpdates === 'true',
      }
    },
    async savePreference(preference, value) {
      const result = await transport!.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value: String(value) },
      })
      if (!result.ok) throw new Error('The update preference could not be saved.')
    },
    stopBackend: () => backendService.stopForUpdate(),
    recoverBackend: () => backendService.recover(),
    async prepareRestart(version) {
      prepareUpdateResume(resumeFile, {
        version,
        dataDirectory,
        noSync: startupArgs.includes('--no-sync') || startupArgs.includes('--seed-sample'),
      })
    },
    async clearRestart() {
      clearUpdateResume(resumeFile)
    },
    openDownload: (url) => shell.openExternal(url),
  })
  updater.subscribe((snapshot) => emit('winnow:update:changed', snapshot))
  function validateSender(event: IpcMainInvokeEvent): void {
    if (
      !window ||
      event.sender !== window.webContents ||
      event.senderFrame !== window.webContents.mainFrame ||
      !trustedRendererUrl(event.senderFrame.url, developmentOrigin)
    )
      throw new Error('Untrusted renderer')
  }
  function handle(channel: string, handler: (...args: any[]) => unknown): void {
    ipcMain.handle(channel, (event, ...args) => {
      validateSender(event)
      return handler(...args)
    })
  }
  const requestLifetimes = new RequestLifetimes()
  const requestOwners = new WeakSet<object>()
  const observeRequestOwner = (owner: Electron.WebContents) => {
    if (!requestOwners.has(owner)) {
      requestOwners.add(owner)
      owner.once('destroyed', () => requestLifetimes.close(owner))
      owner.on('render-process-gone', () => requestLifetimes.close(owner))
    }
  }
  ipcMain.handle('winnow:request', (event, request: ApiRequest) => {
    validateSender(event)
    observeRequestOwner(event.sender)
    return requestLifetimes.run(event.sender, request, (signal) =>
      sessionAppearance
        ? sessionAppearance.request(request, () => transport!.request(request, signal))
        : transport!.request(request, signal),
    )
  })
  ipcMain.handle('winnow:request:cancel', (event, requestId: unknown) => {
    validateSender(event)
    return requestLifetimes.cancel(event.sender, requestId)
  })
  handle('winnow:connection', () => transport!.connection())
  handle('winnow:fonts', () => fonts.read(window!.webContents))
  handle('winnow:window:appearance', (value) =>
    new WindowAppearanceController(window!, () => ({
      platform: process.platform,
      release: osRelease(),
      highContrast: nativeTheme.shouldUseHighContrastColors || nativeTheme.inForcedColorsMode,
      reducedTransparency: nativeTheme.prefersReducedTransparency,
      remoteSession: process.env.SESSIONNAME?.toUpperCase().startsWith('RDP-') ?? false,
    })).apply(value),
  )
  nativeTheme.on('updated', () => emit('winnow:window:appearance:invalidated', undefined))
  handle('winnow:backend:restart', () => backendService.restart())
  handle('winnow:update:snapshot', () => updater!.snapshot)
  handle('winnow:update:action', (action: ApplicationUpdateAction, value?: boolean) =>
    updater!.action(action, value),
  )
  handle('winnow:activation:pending', () => {
    rendererAcceptsActivation = true
    return pendingActivations.splice(0)
  })
  ipcMain.handle(
    'winnow:artwork',
    async (event, provider: string, id: string, width?: number, requestId?: string) => {
      validateSender(event)
      observeRequestOwner(event.sender)
      const result = await requestLifetimes.run(
        event.sender,
        { route: 'artwork.image', requestId },
        async (signal) => ({
          ok: true,
          status: 200,
          data: await transport!.artwork(provider, id, width, signal),
        }),
      )
      return result.ok ? (result.data ?? null) : null
    },
  )
  handle('winnow:artwork:import', (input: unknown) =>
    importArtworkFile(input, {
      choose: async () => {
        const choice = await dialog.showOpenDialog(window!, {
          title: 'Choose artwork',
          properties: ['openFile'],
          filters: [{ name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif', 'bmp'] }],
        })
        return choice.canceled ? null : (choice.filePaths[0] ?? null)
      },
      upload: (target, bytes) => transport!.importArtwork(target, bytes),
    }),
  )
  handle('winnow:preferences:load', async () => {
    if (sessionAppearance) return sessionAppearance.loadProfile()
    if (safeTheme) return null
    try {
      return await readProfile(preferencesFile)
    } catch {
      return null
    }
  })
  handle('winnow:appearance:session', () => captureAppearance)
  handle('winnow:preferences:save', (value: unknown) =>
    sessionAppearance ? sessionAppearance.saveProfile(value) : saveProfile(preferencesFile, value),
  )
  handle('winnow:profile:import', async () => {
    const choice = await dialog.showOpenDialog(window!, {
      title: 'Import appearance profile',
      filters: [{ name: 'Winnow appearance profile', extensions: ['json'] }],
      properties: ['openFile'],
    })
    return choice.canceled || !choice.filePaths[0] ? null : readProfile(choice.filePaths[0])
  })
  handle('winnow:profile:export', async (value: unknown) => {
    const choice = await dialog.showSaveDialog(window!, {
      title: 'Export appearance profile',
      defaultPath: 'winnow-appearance.json',
      filters: [{ name: 'JSON', extensions: ['json'] }],
    })
    if (choice.canceled || !choice.filePath) return false
    await saveProfile(choice.filePath, value)
    return true
  })
  handle('winnow:themes:list', () => listThemePackages(themesRoot))
  const avalonThemes = new AvalonThemeStore(
    async () => {
      const result = await transport!.request<{ directory: string }>({ route: 'plugins.directory' })
      if (!result.ok || !result.data?.directory)
        throw new Error('Connect to your library before reading its themes.')
      return join(dirname(result.data.directory), 'themes')
    },
    () => emit('winnow:avalon-themes:changed', undefined),
  )
  handle('winnow:avalon-themes:list', async () => {
    const preparation = await avalonThemes.prepare()
    const catalogue = await avalonThemes.load()
    return { ...catalogue, diagnostics: [...preparation, ...catalogue.diagnostics] }
  })
  handle('winnow:avalon-themes:export', (text: unknown) => avalonThemes.export(text))
  app.once('will-quit', () => avalonThemes.dispose())
  let installing = false
  handle('winnow:themes:install', async () => {
    if (installing) return null
    installing = true
    try {
      const choice = await dialog.showOpenDialog(window!, {
        title: 'Choose a Winnow theme folder',
        properties: ['openDirectory'],
      })
      if (choice.canceled || !choice.filePaths[0]) return null
      const source = choice.filePaths[0]
      const manifest = await readManifest(source)
      const trust = await dialog.showMessageBox(window!, {
        type: 'warning',
        title: 'Install developer theme',
        message: `Trust “${manifest.name}”?`,
        detail:
          'Developer themes run JavaScript inside this frontend. They can read your library and use its supported commands, including editing lists and launching games. Install only themes from authors you trust. Themes cannot access your backend token or arbitrary local files. Installing replaces an existing theme with the same ID.',
        buttons: ['Cancel', 'Trust and install'],
        defaultId: 0,
        cancelId: 0,
        noLink: true,
      })
      if (trust.response !== 1) return null
      return await installThemeDirectory(source, themesRoot)
    } finally {
      installing = false
    }
  })
  handle('winnow:fullscreen', (value: boolean) => {
    if (typeof value !== 'boolean') throw new Error('Invalid fullscreen setting')
    window!.setFullScreen(value)
  })
  handle('winnow:fullscreen:get', () => window?.isFullScreen() ?? false)
  handle('winnow:quit', () => {
    quitting = true
    app.quit()
  })
  handle('winnow:external', (url: string) => {
    return routeLink(url, preferences.LinkDestination, {
      inApp: (address) => openLinkBrowser(window!, address),
      external: (address) => shell.openExternal(address),
      hasSteam: () => Boolean(app.getApplicationNameForProtocol('steam://')),
    })
  })
  handle('winnow:folder', async (folder: string) => {
    if (!['logs', 'plugins', 'themes'].includes(folder)) throw new Error('Unknown Winnow folder')
    // Ask the backend for its active directory, including legacy fallback installs.
    const result = await transport!.request<{ directory: string }>({ route: 'plugins.directory' })
    if (!result.ok || !result.data?.directory)
      throw new Error('Connect to your library before opening its folder.')
    const path = join(dirname(result.data.directory), folder)
    await mkdir(path, { recursive: true })
    const problem = await shell.openPath(path)
    if (problem) throw new Error('The folder could not be opened.')
  })
  handle('winnow:install-folder', (ownershipId: unknown) =>
    openInstallFolder(ownershipId, {
      workspace: async () => {
        const result = await transport!.request<InstallationWorkspace>({ route: 'library.workspace' })
        if (!result.ok || !result.data) throw new Error('Connect to your library before opening this folder.')
        return result.data
      },
      isDirectory: async (path) => (await stat(path)).isDirectory(),
      openPath: (path) => shell.openPath(path),
    }),
  )
  const chooseManualExecutable = async () => {
    const result = await dialog.showOpenDialog(window!, {
      title: 'Choose game executable',
      properties: ['openFile'],
      ...(process.platform === 'win32' ? { filters: [{ name: 'Executable', extensions: ['exe'] }] } : {}),
    })
    return result.canceled ? null : (result.filePaths[0] ?? null)
  }
  handle('winnow:manual-executable', chooseManualExecutable)
  handle('winnow:manual-executable-facts', async () => {
    const path = await chooseManualExecutable()
    return path ? inspectExecutable(path) : null
  })
  handle('winnow:acquisitions:export', async () => {
    const result = await transport!.request<{ content: string; ownershipCount: number }>({
      route: 'acquisitions.export',
    })
    if (!result.ok || !result.data) throw new Error('Acquisitions could not be exported. Try again.')
    const choice = await dialog.showSaveDialog(window!, {
      title: 'Export acquisitions',
      defaultPath: 'winnow-acquisitions.csv',
      filters: [{ name: 'CSV', extensions: ['csv'] }],
    })
    if (choice.canceled || !choice.filePath) return false
    await writeFile(choice.filePath, result.data.content, 'utf8')
    return true
  })
  handle('winnow:steam:signin', (options: SteamSignInOptions) =>
    signInToSteam(window!, transport!, options, (message) => {
      // Resolve the active backend location so legacy-directory fallback installs share their logs.
      void transport!
        .request<{ directory: string }>({ route: 'plugins.directory' })
        .then((result) => {
          if (result.ok && result.data?.directory)
            writeSteamDiagnostic(dirname(result.data.directory), message)
        })
        .catch(() => {})
    }),
  )
  const epicSignIn = new EpicSignInController(
    transport!,
    join(app.getPath('userData'), 'account-profiles'),
    join(here, '../preload/epic.cjs'),
  )
  app.once('will-quit', () => epicSignIn.dispose())
  handle('winnow:epic:prepare', () => epicSignIn.prepare(window!))
  handle('winnow:epic:signin', (options: EpicSignInOptions) => epicSignIn.signIn(window!, options))
  handle('winnow:epic:browser', (options: EpicSignInOptions) => epicSignIn.openManual(window!, options))
  handle('winnow:epic:complete', (options: EpicSignInOptions & { callback: string }) =>
    epicSignIn.completeManual(window!, options),
  )
  handle('winnow:epic:cancel', () => epicSignIn.cancel(window!))
  handle('winnow:steam:cancel', () => cancelSteamWindow(window!))
  handle('winnow:steam:capture', (options: { consentGranted: boolean }) =>
    captureSteamPages(window!, options),
  )
  handle('winnow:application:info', () => ({
    version: app.getVersion(),
    platform: process.platform,
    packaged: app.isPackaged,
    steamStoreAvailable: Boolean(app.getApplicationNameForProtocol('steam://')),
    autostartSupported: app.isPackaged && process.platform === 'win32',
    openAtLogin: process.platform === 'win32' ? app.getLoginItemSettings().openAtLogin : false,
  }))
  handle('winnow:application:autostart', (enabled: boolean) => {
    if (typeof enabled !== 'boolean' || !app.isPackaged || process.platform !== 'win32')
      throw new Error('Start at sign-in is available in packaged Windows builds.')
    app.setLoginItemSettings({
      openAtLogin: enabled,
      args: ['--background', ...(dataDirectory ? ['--data-dir', dataDirectory] : [])],
    })
  })
  const journalNotifications = new Map<number, Notification>()
  handle('winnow:journal:notify', (value: { sessionId: number; title: string }) => {
    if (
      !value ||
      !Number.isSafeInteger(value.sessionId) ||
      value.sessionId <= 0 ||
      typeof value.title !== 'string' ||
      value.title.length > 1024
    )
      throw new Error('Invalid session notification')
    if (window?.isFocused() || !Notification.isSupported()) return false
    journalNotifications.get(value.sessionId)?.close()
    const notification = new Notification({ title: 'Remember this session', body: value.title, silent: true })
    journalNotifications.set(value.sessionId, notification)
    notification.once('click', () => {
      window?.show()
      window?.restore()
      window?.focus()
      window?.webContents.send('winnow:journal:activated', value.sessionId)
      journalNotifications.delete(value.sessionId)
    })
    notification.once('close', () => journalNotifications.delete(value.sessionId))
    return deliverNotification(notification)
  })
  handle('winnow:journal:clear', (sessionId: number) => {
    journalNotifications.get(sessionId)?.close()
    journalNotifications.delete(sessionId)
  })
  function createWindow(): void {
    rendererAcceptsActivation = false
    window = new BrowserWindow({
      width: 1440,
      height: 980,
      minWidth: 760,
      minHeight: 560,
      backgroundColor: '#18191b',
      title: 'Winnow',
      show: false,
      autoHideMenuBar: true,
      icon: app.isPackaged
        ? join(process.resourcesPath, 'icon.ico')
        : join(app.getAppPath(), 'resources', 'icon.ico'),
      webPreferences: {
        preload: join(here, '../preload/index.cjs'),
        contextIsolation: true,
        sandbox: true,
        nodeIntegration: false,
        nodeIntegrationInWorker: false,
        webSecurity: true,
        webviewTag: false,
        spellcheck: false,
      },
    })
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
    window.webContents.on('will-navigate', (event) => event.preventDefault())
    window.webContents.on('will-attach-webview', (event) => event.preventDefault())
    window.webContents.on('did-start-loading', () => {
      rendererAcceptsActivation = false
    })
    window.on('enter-full-screen', () => emit('winnow:fullscreen:changed', true))
    window.on('leave-full-screen', () => emit('winnow:fullscreen:changed', false))
    window.once('ready-to-show', () => {
      if (!process.argv.includes('--background') || !tray) window?.show()
    })
    window.on('minimize', () => {
      if (tray && preferences.MinimizeToTray === 'true') window?.hide()
    })
    window.on('close', (event) => {
      if (!quitting && tray && preferences.CloseToTray === 'true') {
        event.preventDefault()
        window?.hide()
      }
    })
    window.on('closed', () => {
      window = undefined
    })
    void window.loadURL(rendererUrl)
  }
  Menu.setApplicationMenu(
    Menu.buildFromTemplate([
      { label: 'Winnow', submenu: [{ role: 'about' }, { type: 'separator' }, { role: 'quit' }] },
      {
        label: 'Edit',
        submenu: [
          { role: 'undo' },
          { role: 'redo' },
          { type: 'separator' },
          { role: 'cut' },
          { role: 'copy' },
          { role: 'paste' },
          { role: 'selectAll' },
        ],
      },
      {
        label: 'View',
        submenu: [
          { role: 'reload' },
          { role: 'togglefullscreen' },
          { role: 'resetZoom' },
          { role: 'zoomIn' },
          { role: 'zoomOut' },
          { type: 'separator' },
          {
            label: 'Recover bundled appearance',
            accelerator: 'CmdOrCtrl+Shift+R',
            click: () => {
              safeTheme = true
              window?.webContents.reload()
            },
          },
          ...(!app.isPackaged ? [{ role: 'toggleDevTools' as const }] : []),
        ],
      },
    ]),
  )
  createWindow()
  showPrimary = () => {
    if (!window) createWindow()
    window?.show()
    window?.restore()
    window?.focus()
  }
  try {
    tray = new Tray(
      app.isPackaged
        ? join(process.resourcesPath, 'icon.ico')
        : join(app.getAppPath(), 'resources', 'icon.ico'),
    )
    const show = showPrimary
    tray.setToolTip('Winnow')
    tray.setContextMenu(
      Menu.buildFromTemplate([
        { label: 'Open Winnow', click: show },
        { label: 'Quit Winnow', click: () => app.quit() },
      ]),
    )
    tray.on('double-click', show)
  } catch {
    /* A desktop without a notification area keeps ordinary window close behavior. */
  }
  app.on('activate', () => {
    showPrimary()
  })
  transport.start()
  // Starting a companion is independent of renderer readiness, so connection failures remain visible.
  if (!(await backendResponds(() => discoverBackend(dataDirectory)))) {
    // A slow startup or an in-progress restart can have a published endpoint before health responds.
    // Let that backend finish instead of racing it with another companion process.
    if (!(await backendProcessIsRunning(() => discoverBackend(dataDirectory)))) {
      try {
        const started = await startBackend({
          appPath: app.getAppPath(),
          resourcesPath: process.resourcesPath,
          packaged: app.isPackaged,
          dataDirectory,
          args: startupArgs,
        })
        void started.exited.then(async (code) => {
          if (
            code !== 0 &&
            !transport!.connection().connected &&
            !(await backendResponds(() => discoverBackend(dataDirectory)))
          )
            transport?.setStartupProblem(
              `The Winnow backend stopped before connecting (exit ${code ?? 'unknown'}). Check the backend logs, then start the backend again. This window will reconnect automatically.`,
            )
        })
      } catch (error) {
        transport.setStartupProblem(
          error instanceof Error
            ? error.message
            : 'Could not start the backend. Start it separately; this window will reconnect automatically.',
        )
      }
    }
  }
  void updater.initialize()
}

if (!ownsInstance) app.quit()
else if (startupArgumentError) {
  app.exit(
    reportStartupFailure(startupArgumentError, {
      exitCode: dataDirectoryRefusalCode,
      surface: dialog.showErrorBox,
    }),
  )
} else
  app
    .whenReady()
    .then(initialize)
    .catch((error) => {
      app.exit(reportStartupFailure(error, { directory: dataDirectory, surface: dialog.showErrorBox }))
    })
app.on('before-quit', () => {
  quitting = true
  updater?.dispose()
  tray?.destroy()
  tray = undefined
  transport?.stop()
})
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
