import { app, ipcMain } from 'electron'
import { fileURLToPath } from 'node:url'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = (globalThis.__setupContracts = {
  requests: [],
  firstSetup: null,
  windows: [],
  preferences: { held: false, released: false, status: null },
  heldWindowCount: null,
})
const nativeFetch = globalThis.fetch
let firstPreferences = true
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  const hold =
    process.env.WINNOW_SETUP_HOLD_PREFERENCES === '1' &&
    firstPreferences &&
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/preferences/presentation'
  if (hold) firstPreferences = false
  const response = await nativeFetch(input, init)
  if (hold) {
    state.preferences.status = response.status
    state.preferences.held = true
    await new Promise((resolve) => {
      globalThis.__releaseSetupPreferences = () => {
        state.preferences.released = true
        resolve()
      }
    })
  }
  return response
}
app.on('browser-window-created', (_event, window) => {
  state.windows.push({ id: window.id, createdAt: Date.now() })
})
app.on('web-contents-created', (_event, contents) => {
  contents.once('dom-ready', () => {
    void contents
      .executeJavaScript(
        `new Promise(resolve => {
      const scan = () => {
        const setup = document.querySelector('.setup-dialog');
        if (!setup) return;
        observer.disconnect();
        resolve({ className: setup.className, mode: document.documentElement.dataset.mode,
          startupPresent: !!document.querySelector('.startup-presentation'),
          elapsed: performance.now() });
      };
      const observer = new MutationObserver(scan);
      observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true });
      scan();
    })`,
      )
      .then((value) => {
        state.firstSetup = value
      })
      .catch(() => {})
  })
})
const register = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  register(
    channel,
    channel !== 'winnow:request'
      ? listener
      : async (event, request) => {
          // Names/status only: unsaved credential drafts never enter the evidence ledger.
          const record = { route: request.route, started: Date.now(), completed: false, status: null }
          state.requests.push(record)
          const result = await listener(event, request)
          record.completed = true
          record.status = result.status
          return result
        },
  )
await import('./identity-projections-main.mjs')
