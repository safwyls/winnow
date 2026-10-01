import { app, ipcMain } from 'electron'
import { fileURLToPath } from 'node:url'
import {
  observeActivationHelper,
  sendActivationPipe,
  gamePayload,
  pluginPayload,
} from './activation-observer.mjs'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = { waiting: false, ready: false, forward: false, drained: [], delivered: [], actionPaths: [] }
const fetchBackend = globalThis.fetch
globalThis.fetch = (input, init) => {
  const path = new URL(typeof input === 'string' || input instanceof URL ? input : input.url).pathname
  if (/^\/api\/v1\/entries\/\d+\/actions$/.test(path)) state.actionPaths.push(path)
  return fetchBackend(input, init)
}
let release
const pending = new Promise((resolve) => {
  release = resolve
})
const helper = observeActivationHelper(
  globalThis.__holdActivationPrimary ??
    (process.env.WINNOW_FIXTURE_EARLY_ACTIVATION === '1'
      ? (frame) => sendActivationPipe(frame.pipeName, Buffer.from([1]))
      : undefined),
)
globalThis.__activationFixture = {
  state,
  release,
  helper,
  send: (kind, value) =>
    sendActivationPipe(
      helper.primary.pipeName,
      kind === 'game' ? gamePayload(value) : kind === 'plugin' ? pluginPayload(value) : Buffer.from(value),
    ),
}
app.on('web-contents-created', (_event, contents) => {
  const send = contents.send.bind(contents)
  contents.send = (channel, ...args) => {
    if (channel === 'winnow:activation') {
      state.delivered.push(args[0])
      if (!state.forward) return
    }
    return send(channel, ...args)
  }
})
const register = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  register(
    channel,
    channel === 'winnow:activation:pending'
      ? async (...args) => {
          state.waiting = true
          await pending
          state.drained.push(...(await listener(...args)))
          state.ready = true
          // Inspect native delivery without launching a real game or installing a provider.
          return []
        }
      : listener,
  )
await import('../../out/main/index.js')
