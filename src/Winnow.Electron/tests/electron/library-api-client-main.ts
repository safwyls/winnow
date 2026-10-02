import { app, BrowserWindow, ipcMain, nativeTheme, protocol, session } from 'electron'
import { mkdirSync } from 'node:fs'
import { readFile, stat } from 'node:fs/promises'
import { basename, dirname, extname, isAbsolute, join, resolve } from 'node:path'
import { release } from 'node:os'
import { BackendTransport, readDiscovery } from '../../src/main/transport'
import { RequestLifetimes } from '../../src/main/request-lifetimes'
import {
  assertTrustedRendererSender,
  containedPath,
  contentSecurityPolicy,
  trustedRendererUrl,
} from '../../src/main/security'
import { readProfile, saveProfile, listThemePackages } from '../../src/main/storage'
import { AvalonThemeStore } from '../../src/main/avalon-theme-store'
import { FontCatalogue } from '../../src/main/fonts'
import { setPopoutTypography } from '../../src/main/popout-typography'
import { WindowAppearanceController } from '../../src/main/window-appearance'
import { FullscreenFilePickerService } from '../../src/main/file-picker'
import { ActivationQueue } from '../../src/main/activation-queue'
import { initialUpdateSnapshot } from '../../src/main/application-updater'
import { applicationBuildInfo } from '../../src/main/application-build-info'
import type { ApiRequest } from '../../src/shared/bridge'

// An independent API consumer, not a second production frontend owner. It attaches
// to the first app's backend without starting services or taking its activation guard.
const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
const output = process.env.WINNOW_LIBRARY_CLIENT_OUTPUT
if (
  !directory ||
  !isAbsolute(directory) ||
  !basename(directory).startsWith('winnow-library-live-') ||
  !output
)
  throw Error('The independent library client requires its isolated fixture directory and built frontend.')
const profile = join(directory, 'independent-api-client')
mkdirSync(profile, { recursive: true })
app.setPath('userData', profile)
app.setPath('sessionData', profile)
app.enableSandbox()
protocol.registerSchemesAsPrivileged([
  {
    scheme: 'winnow-app',
    privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true },
  },
])
const rendererRoot = resolve(output, 'renderer')
const csp = contentSecurityPolicy()
const state = {
  kind: 'independent-api-client',
  processId: process.pid,
  backendProcessId: 0,
  profile,
  requests: [] as string[],
  events: [] as { kind: string; epoch?: string }[],
  channels: [] as string[],
}
Object.assign(globalThis, { __libraryClient: state })

async function initialize() {
  await app.whenReady()
  // Require an existing live endpoint; this client has no start/restart/shutdown capability.
  state.backendProcessId = (await readDiscovery(directory)).processId
  const window = new BrowserWindow({
    width: 1440,
    height: 960,
    show: false,
    webPreferences: {
      preload: resolve(output!, 'preload/index.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })
  const emit = (channel: string, value: unknown) => {
    if (!window.isDestroyed()) window.webContents.send(channel, value)
  }
  const transport = new BackendTransport({
    discover: () => readDiscovery(directory),
    onEvent: (event) => {
      state.events.push({ kind: event.kind, epoch: event.epoch })
      emit('winnow:event', event)
    },
    onConnection: (value) => emit('winnow:connection', value),
  })
  const requests = new RequestLifetimes()
  const owner = window.webContents
  const handle = (channel: string, handler: (...args: any[]) => unknown) => {
    ipcMain.handle(channel, (event, ...args) => {
      assertTrustedRendererSender(event, owner)
      state.channels.push(channel)
      return handler(...args)
    })
  }
  handle('winnow:request', (request: ApiRequest) =>
    requests.run(owner, request, (signal) => {
      state.requests.push(request.route)
      return transport.request(request, signal)
    }),
  )
  handle('winnow:request:cancel', (id) => requests.cancel(owner, id))
  handle('winnow:connection', () => transport.connection())
  handle('winnow:artwork', async (provider: string, id: string, width?: number, requestId?: string) => {
    const result = await requests.run(owner, { route: 'artwork.image', requestId }, async (signal) => ({
      ok: true,
      status: 200,
      data: await transport.artwork(provider, id, width, signal),
    }))
    return result.ok ? (result.data ?? null) : null
  })
  const fonts = new FontCatalogue(trustedRendererUrl)
  session.defaultSession.setPermissionRequestHandler((contents, permission, callback, details) =>
    callback(
      contents === owner &&
        fonts.allows(contents, permission, details.requestingUrl ?? '', details.isMainFrame),
    ),
  )
  session.defaultSession.setPermissionCheckHandler(
    (contents, permission, origin, details) =>
      contents === owner && fonts.allows(contents, permission, origin, details.isMainFrame),
  )
  session.defaultSession.webRequest.onHeadersReceived((details, callback) =>
    callback({
      responseHeaders: { ...details.responseHeaders, 'Content-Security-Policy': [csp] },
    }),
  )
  handle('winnow:fonts', () => fonts.read(owner))
  handle('winnow:typography:popouts', (value) => setPopoutTypography(owner, value))
  handle('winnow:preferences:load', () =>
    readProfile(join(profile, 'profile.json')).catch((error) => {
      if (error.code === 'ENOENT') return null
      throw error
    }),
  )
  handle('winnow:preferences:save', (value) => saveProfile(join(profile, 'profile.json'), value))
  handle('winnow:themes:list', () => listThemePackages(join(profile, 'themes')))
  const themes = new AvalonThemeStore(
    async () => {
      const result = await transport.request<{ directory: string }>({ route: 'plugins.directory' })
      if (!result.ok || !result.data?.directory)
        throw Error('The shared backend did not supply its themes directory.')
      return join(dirname(result.data.directory), 'themes')
    },
    () => emit('winnow:avalon-themes:changed', undefined),
  )
  handle('winnow:avalon-themes:list', async () => {
    const preparation = await themes.prepare(),
      catalogue = await themes.load()
    return { ...catalogue, diagnostics: [...preparation, ...catalogue.diagnostics] }
  })
  handle('winnow:appearance:session', () => null)
  handle('winnow:window:appearance', (value) =>
    new WindowAppearanceController(window, () => ({
      platform: process.platform,
      release: release(),
      highContrast: nativeTheme.shouldUseHighContrastColors || nativeTheme.inForcedColorsMode,
      reducedTransparency: nativeTheme.prefersReducedTransparency,
      remoteSession: process.env.SESSIONNAME?.toUpperCase().startsWith('RDP-') ?? false,
    })).apply(value),
  )
  const picker = new FullscreenFilePickerService((value) => emit('winnow:file-picker:changed', value))
  handle('winnow:file-picker:snapshot', () => picker.snapshot)
  handle('winnow:file-picker:action', (value) => picker.action(value))
  const activations = new ActivationQueue()
  handle('winnow:activation:pending', () => activations.drain())
  // This deliberately narrow consumer has no updater, provider browser or OS action
  // owner. Only its idle caption is exposed; invoking unsupported IPC fails normally.
  handle('winnow:update:snapshot', initialUpdateSnapshot)
  handle('winnow:controller:battery', (value) => {
    if (value !== null) throw Error('The independent API client has no native controller probe.')
    return null
  })
  handle('winnow:application:info', () => ({
    ...applicationBuildInfo(false),
    platform: process.platform,
    packaged: false,
    steamStoreAvailable: Boolean(app.getApplicationNameForProtocol('steam://')),
    autostartSupported: false,
    openAtLogin: false,
  }))
  handle('winnow:fullscreen:get', () => window.isFullScreen())
  handle('winnow:fullscreen', (value) => {
    if (typeof value !== 'boolean') throw Error('Invalid fullscreen setting')
    window.setFullScreen(value)
  })
  handle('winnow:window:visible', () => window.isVisible() && !window.isMinimized())
  handle('winnow:quit', () => app.quit())
  const visibility = () => emit('winnow:window:visibility', window.isVisible() && !window.isMinimized())
  window.on('show', visibility)
  window.on('hide', visibility)
  window.on('minimize', visibility)
  window.on('restore', visibility)
  window.on('enter-full-screen', () => emit('winnow:fullscreen:changed', true))
  window.on('leave-full-screen', () => emit('winnow:fullscreen:changed', false))
  owner.setWindowOpenHandler(() => ({ action: 'deny' }))
  owner.on('will-navigate', (event, url) => {
    if (!trustedRendererUrl(url)) event.preventDefault()
  })
  owner.once('destroyed', () => requests.close(owner))
  owner.on('render-process-gone', () => requests.close(owner))
  app.once('before-quit', () => {
    requests.close(owner)
    transport.stop()
    themes.dispose()
    picker.cancel()
  })
  app.on('window-all-closed', () => app.quit())
  protocol.handle('winnow-app', async (request) => {
    const url = new URL(request.url)
    if (request.method !== 'GET' || url.host !== 'app' || url.username || url.password)
      return new Response(null, { status: 403 })
    try {
      const path = containedPath(rendererRoot, decodeURIComponent(url.pathname.slice(1)))
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
      return new Response(new Uint8Array(await readFile(path)), {
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
  transport.start()
  await window.loadURL('winnow-app://app/index.html')
  window.show()
}
void initialize().catch((error) => {
  console.error(error)
  app.exit(3)
})
