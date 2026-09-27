import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  Menu,
  protocol,
  session,
  shell,
  type IpcMainInvokeEvent,
} from 'electron'
import { mkdirSync } from 'node:fs'
import { readFile, stat } from 'node:fs/promises'
import { extname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { ApiRequest, BackendEvent, ConnectionState } from '../shared/bridge'
import { BackendTransport } from './transport'
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
try {
  dataDirectory = dataDirectoryArgument(process.argv)
  if (!app.isPackaged && !dataDirectory)
    throw new Error('Pass --data-dir <throwaway directory> to run the frontend in development.')
  if (dataDirectory) {
    const isolatedUserData = join(dataDirectory, 'electron-userdata')
    const isolatedSessionData = join(isolatedUserData, 'chromium')
    mkdirSync(isolatedSessionData, { recursive: true })
    // Chromium creates caches at readiness, so redirects must happen before app.whenReady().
    app.setPath('userData', isolatedUserData)
    app.setPath('sessionData', isolatedSessionData)
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
let safeTheme = process.argv.includes('--safe-theme')

function emit(channel: string, payload: BackendEvent | ConnectionState | boolean): void {
  if (window && !window.isDestroyed()) window.webContents.send(channel, payload)
}

async function initialize(): Promise<void> {
  const profileRoot = profileDirectory(
    app.getPath('userData'),
    dataDirectory ?? join(defaultDataRoot(), 'Winnow'),
  )
  const preferencesFile = join(profileRoot, 'preferences.json')
  const themesRoot = join(profileRoot, 'themes')
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
  session.defaultSession.setPermissionRequestHandler((_contents, _permission, callback) => callback(false))
  session.defaultSession.setPermissionCheckHandler(() => false)
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
  transport = new BackendTransport({
    discover: () => discoverBackend(dataDirectory),
    onEvent: (event) => emit('winnow:event', event),
    onConnection: (state) => emit('winnow:connection', state),
  })
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
  handle('winnow:request', (request: ApiRequest) => transport!.request(request))
  handle('winnow:connection', () => transport!.connection())
  handle('winnow:artwork', (provider: string, id: string, width?: number) =>
    transport!.artwork(provider, id, width),
  )
  handle('winnow:preferences:load', async () => {
    if (safeTheme) return null
    try {
      return await readProfile(preferencesFile)
    } catch {
      return null
    }
  })
  handle('winnow:preferences:save', (value: unknown) => saveProfile(preferencesFile, value))
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
  handle('winnow:external', (url: string) => shell.openExternal(validateExternalUrl(url)))
  function createWindow(): void {
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
    window.on('enter-full-screen', () => emit('winnow:fullscreen:changed', true))
    window.on('leave-full-screen', () => emit('winnow:fullscreen:changed', false))
    window.once('ready-to-show', () => window?.show())
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
  app.on('activate', () => {
    if (!window) createWindow()
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
          args: process.argv,
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
}

if (startupArgumentError) {
  dialog.showErrorBox('Winnow could not start', startupArgumentError.message)
  app.exit(2)
} else
  app
    .whenReady()
    .then(initialize)
    .catch((error) => {
      dialog.showErrorBox(
        'Winnow could not start',
        error instanceof Error ? error.message : 'Startup failed.',
      )
      app.exit(2)
    })
app.on('before-quit', () => transport?.stop())
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
