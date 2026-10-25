import { app, ipcMain } from 'electron'
import { fileURLToPath } from 'node:url'
// The wrapper must retain the packaged app's resource root, including its real tray icon.
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const waiting = []
const state = {
  holdFeed: true,
  failFeed: false,
  reduced: false,
  scale: 1,
  activeFeeds: 0,
  reads: [],
  events: [],
}
app.on('web-contents-created', (_event, contents) => {
  const send = contents.send.bind(contents)
  contents.send = (channel, ...args) => {
    if (/event|connection|fullscreen/.test(channel))
      state.events.push({ channel, value: args[0], time: Date.now() })
    return send(channel, ...args)
  }
})
globalThis.__startup = {
  state,
  release() {
    state.holdFeed = false
    waiting.splice(0).forEach((resolve) => resolve())
  },
}
const register = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  register(
    channel,
    channel !== 'winnow:request'
      ? listener
      : async (event, request) => {
          if (
            ['library.get', 'feed.get', 'preferences.presentation.get', 'setup.get'].includes(request.route)
          ) {
            const view = await event.sender.executeJavaScript(
              "({phase: document.querySelector('.startup-presentation')?.dataset.phase ?? null, visibility: document.visibilityState, painted: performance.getEntriesByType('paint').length > 0})",
            )
            state.reads.push({ route: request.route, ...view, held: state.holdFeed, time: Date.now() })
          }
          if (request.route === 'setup.get') return { ok: true, status: 200, data: { step: null } }
          if (request.route === 'feed.get') {
            state.activeFeeds++
            try {
              if (state.holdFeed) await new Promise((resolve) => waiting.push(resolve))
              if (state.failFeed) {
                state.failFeed = false
                return { ok: false, status: 503, message: 'Fixture feed failure' }
              }
              return await listener(event, request)
            } finally {
              state.activeFeeds--
            }
          }
          const response = await listener(event, request)
          if (request.route === 'preferences.presentation.get' && response.ok) {
            response.data = response.data.filter(
              (row) => !['FullscreenReducedMotion', 'FullscreenTextScale'].includes(row.preference),
            )
            response.data.push(
              { preference: 'FullscreenReducedMotion', value: String(state.reduced) },
              { preference: 'FullscreenTextScale', value: String(state.scale) },
            )
          }
          return response
        },
  )
await import('../../out/main/index.js')
