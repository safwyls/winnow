import { app, BrowserWindow, session, ipcMain } from 'electron'
import { mkdirSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { EpicSignInController } from '../../src/main/epic-auth'
import type { EpicAuthChallenge, EpicSignInResult } from '../../src/shared/epic'
import type { BackendTransport } from '../../src/main/transport'
const argument = (name: string) => process.argv[process.argv.indexOf(name) + 1]
const directory = argument('--data-dir'),
  preload = argument('--epic-preload')
if (
  !directory ||
  !isAbsolute(directory) ||
  !directory.includes('winnow-electron-epic-fixture-') ||
  !isAbsolute(preload)
)
  throw new Error('An isolated Epic fixture directory and preload are required.')
mkdirSync(directory, { recursive: true })
app.setPath('userData', directory)
app.setPath('sessionData', join(directory, 'chromium'))
app.whenReady().then(async () => {
  const state = {
    preloadErrors: [] as string[],
    bootstraps: [] as { main: boolean; origin: string }[],
    results: [] as EpicSignInResult[],
    completions: [] as { kind: number; state: string; code: string }[],
    forbidden: [] as string[],
    authenticated: false,
    mode: 'bridge',
    pressed: [] as number[],
  }
  app.on('web-contents-created', (_event, contents) =>
    contents.on('preload-error', (_event, _path, error) => state.preloadErrors.push(error.message)),
  )
  ipcMain.on('winnow:epic:document', (event) =>
    state.bootstraps.push({
      main: event.senderFrame === event.sender.mainFrame,
      origin: event.senderFrame?.url ?? '',
    }),
  )
  const owner = new BrowserWindow({
    show: false,
    fullscreen: argument('--mode') === 'fullscreen',
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
  })
  await owner.loadURL('data:text/html,<h1>Epic fixture owner</h1>')
  owner.webContents.executeJavaScriptInIsolatedWorld = async () => ({
    index: 0,
    buttons: Array.from({ length: 18 }, (_, i) => state.pressed.includes(i)),
    axes: [0, 0],
  })
  const root = join(directory, 'profiles'),
    profile = session.fromPath(join(root, 'epic'), { cache: true })
  for (const scheme of ['https', 'http'])
    profile.protocol.handle(scheme, (request) => {
      const url = new URL(request.url)
      if (url.protocol !== 'https:' || !['epic.example.test', 'social.example.test'].includes(url.hostname)) {
        state.forbidden.push(`${url.protocol}//${url.host}${url.pathname}`)
        return new Response('Blocked fixture network', { status: 403 })
      }
      if (url.pathname === '/id/api/redirect')
        return new Response(
          JSON.stringify({
            authorizationCode: state.authenticated ? 'fixture-harvest-code' : null,
            exchangeCode: null,
          }),
          { headers: { 'Content-Type': 'application/json' } },
        )
      if (url.pathname === '/code')
        return new Response('{"authorizationCode":"fixture-json-code","exchangeCode":null}', {
          headers: { 'Content-Type': 'application/json' },
        })
      if (url.pathname === '/signed-in') {
        state.authenticated = true
        return new Response('ok')
      }
      if (url.pathname === '/iframe')
        return new Response(
          '<!doctype html><script>window.firstBridge=typeof window.ue;window.appBridge=typeof window.winnow</script><h2>Fixture frame</h2>',
          { headers: { 'Content-Type': 'text/html' } },
        )
      if (url.hostname === 'social.example.test')
        return new Response(
          '<!doctype html><script>window.firstBridge=typeof window.ue;window.appBridge=typeof window.winnow</script><h1>Social fixture</h1><a href="https://epic.example.test/code">Complete social sign-in</a>',
          { headers: { 'Content-Type': 'text/html' } },
        )
      return new Response(
        `<!doctype html><html><head><script>window.firstBridge=typeof window.ue;window.appBridge=typeof window.winnow</script></head><body style="font:24px system-ui;padding:30px"><h1>Epic sign-in fixture</h1>
      <label>Password<input type="password" aria-label="Password"></label>
      <button onclick="window.ue.signinprompt.requestexchangecodesignin('fixture-exchange-code')">Complete bridge</button>
      <a href="https://localhost/launcher/authorized?code=fixture-redirect-code&state=fixture-state">Complete redirect</a>
      <a href="https://epic.example.test/code">Complete JSON</a>
      <a href="https://social.example.test/login">Use social provider</a>
      <button onclick="fetch('/signed-in').then(()=>document.querySelector('output').textContent='Authenticated')">Authenticate for harvest</button><output>Waiting</output>
      <iframe title="Social frame" src="https://social.example.test/iframe"></iframe><iframe title="Provider frame" src="https://epic.example.test/iframe"></iframe>
      </body></html>`,
        { headers: { 'Content-Type': 'text/html' } },
      )
    })
  const controller = new EpicSignInController(
    {
      request: async ({ route, body }: { route: string; body?: any }) => {
        if (route === 'connections.epic.signin') {
          const data: EpicAuthChallenge = {
            attemptId: 'a'.repeat(32),
            expiresAt: new Date(Date.now() + 600000).toISOString(),
            request: {
              providerName: 'Epic Games',
              startUrl: 'https://epic.example.test/id/login?state=fixture-state',
              consentNotice: 'Fixture consent.',
              redirectUrl: 'https://localhost/launcher/authorized',
              redirectCodeParameter: 'code',
              expectedState: 'fixture-state',
              stateParameter: 'state',
              additionalNavigableOrigins: ['https://social.example.test'],
              harvestUrl: 'https://epic.example.test/id/api/redirect',
              jsonCodeFields: [
                { fieldName: 'authorizationCode', kind: 0 },
                { fieldName: 'exchangeCode', kind: 1 },
              ],
              strategies: 15,
              profileKey: 'epic',
              timeout: '00:10:00',
            },
          }
          return { ok: true, status: 200, data }
        }
        if (route === 'connections.epic.complete') {
          state.completions.push({ kind: body.kind, state: body.state, code: body.code })
          return {
            ok: true,
            status: 200,
            data: { succeeded: true, persisted: true, failure: 0, displayName: 'Fixture Epic account' },
          }
        }
        return { ok: true, status: 200 }
      },
    } as unknown as BackendTransport,
    root,
    preload,
  )
  Object.assign(globalThis, {
    epicTest: {
      state,
      controller,
      owner,
      async start() {
        state.authenticated = false
        await controller.prepare(owner)
        void controller
          .signIn(owner, { attemptId: 'a'.repeat(32), consentGranted: true })
          .then((result) => state.results.push(result))
      },
    },
  })
})
app.on('window-all-closed', () => app.quit())
