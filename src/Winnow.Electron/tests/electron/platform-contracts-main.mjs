import { app, dialog, ipcMain, shell } from 'electron'
import { basename, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const index = process.argv.indexOf('--data-dir')
const root = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(root).startsWith('winnow-electron-platform-context-'))
  throw Error('Platform verification requires its own isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
app.setPath('documents', join(root, 'documents'))
const state = (globalThis.__platformContracts = {
  dispatches: [],
  requests: [],
  challenges: [],
  saveDialogs: [],
  openDialogs: [],
  nextSave: null,
  forbidden: [],
  savedPages: [],
})
const originalHandle = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  originalHandle(channel, async (event, ...args) => {
    if (!channel.startsWith('winnow:steam:saved-pages:')) return listener(event, ...args)
    const call = { channel, completed: false }
    state.savedPages.push(call)
    const result = await listener(event, ...args)
    call.completed = true
    if (channel.endsWith(':choose')) call.selection = result
    return result
  })
shell.openExternal = async (url) => {
  state.dispatches.push(url)
}
shell.openPath = async () => {
  throw Error('Unexpected executable or folder dispatch')
}
shell.showItemInFolder = () => {
  throw Error('Unexpected folder reveal')
}
dialog.showSaveDialog = async (_owner, options) => {
  state.saveDialogs.push(options)
  const filePath = state.nextSave
  state.nextSave = null
  return { canceled: !filePath, ...(filePath ? { filePath } : {}) }
}
dialog.showOpenDialog = async (_owner, options) => {
  state.openDialogs.push(options)
  return { canceled: true, filePaths: [] }
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1') state.requests.push({ path: url.pathname, method: init?.method ?? 'GET' })
  const response = await originalFetch(input, init)
  if (
    url.hostname === '127.0.0.1' &&
    url.pathname === '/api/v1/connections/stores/epic/sign-in' &&
    response.ok
  )
    state.challenges.push(await response.clone().json())
  return response
}
// Only the external provider's browser document is substituted. The actual challenge,
// completion, cancellation and connection reads still cross the authenticated backend.
app.on('session-created', (profile) => {
  if (basename(profile.getStoragePath() ?? '') !== 'epic') return
  const handle = async (request) => {
    const url = new URL(request.url)
    if (url.origin !== 'https://www.epicgames.com') {
      state.forbidden.push(url.origin + url.pathname)
      return new Response('Blocked offline provider request', { status: 403 })
    }
    const challenge = state.challenges.at(-1)
    if (!challenge) return new Response('No actual challenge', { status: 409 })
    if (url.pathname === '/id/api/redirect')
      return new Response('{}', { headers: { 'content-type': 'application/json' } })
    const callback = new URL(challenge.request.redirectUrl ?? 'https://localhost/launcher/authorized')
    callback.searchParams.set(challenge.request.redirectCodeParameter ?? 'code', 'fixture-platform-code')
    if (challenge.request.expectedState)
      callback.searchParams.set(challenge.request.stateParameter ?? 'state', challenge.request.expectedState)
    return new Response(
      `<!doctype html><h1>Offline Epic provider fixture</h1><a href="${callback.href.replaceAll('&', '&amp;')}">Complete fixture sign-in</a>`,
      {
        headers: { 'content-type': 'text/html' },
      },
    )
  }
  profile.protocol.handle('https', handle)
  profile.protocol.handle('http', handle)
})
await import('../../out/main/index.js')
