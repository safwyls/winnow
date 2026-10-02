import { BrowserWindow, ipcMain } from 'electron'

const state = {
  snapshot: {
    automatic: false,
    includeBeta: false,
    busy: false,
    canDownload: false,
    canRestart: false,
    canCancel: false,
    progress: 0,
    status: 'Updates have not been checked yet.',
  },
  actions: [],
  downloads: 0,
  restarts: 0,
  links: [],
  snapshots: [],
}
let finishDownload
const publish = (patch) => {
  Object.assign(state.snapshot, patch)
  state.snapshots.push(structuredClone(state.snapshot))
  for (const window of BrowserWindow.getAllWindows())
    window.webContents.send('winnow:update:changed', structuredClone(state.snapshot))
}
const finish = (success) => {
  const verified = success === true
  publish({
    busy: false,
    canDownload: !verified,
    canCancel: false,
    canRestart: verified,
    progress: verified ? 100 : 0,
    status: verified
      ? 'Update ready. Restart when you are ready.'
      : success === 'failure'
        ? 'The update could not be downloaded or verified. Try again.'
        : 'Update download cancelled.',
  })
  finishDownload?.()
  finishDownload = undefined
}
async function action(action, value) {
  state.actions.push(action)
  if (action === 'automatic' || action === 'beta')
    publish({ [action === 'automatic' ? 'automatic' : 'includeBeta']: value })
  if (action === 'cancel') finish(false)
  if (action === 'release-notes') state.links.push(state.snapshot.releaseUrl)
  if (action === 'manual-download') state.links.push(state.snapshot.downloadUrl)
  if ((action === 'download' || action === 'update-and-restart') && state.snapshot.canDownload) {
    state.downloads++
    publish({ busy: true, canCancel: true, progress: 34, status: 'Downloading and verifying update…' })
    await new Promise((resolve) => {
      finishDownload = resolve
    })
  }
  if (
    (action === 'restart' || action === 'update-and-restart') &&
    state.snapshot.canRestart &&
    !state.snapshot.busy
  ) {
    state.restarts++
    publish({
      canRestart: false,
      canDownload: true,
      status: 'The update could not start. Try downloading it again.',
      recoveryStatus:
        'The previous update did not start. Your library is unchanged. Download the update again when you are ready.',
    })
  }
  return structuredClone(state.snapshot)
}
globalThis.__updates = { state, publish, finish }
const register = ipcMain.handle.bind(ipcMain)
ipcMain.handle = (channel, listener) =>
  register(
    channel,
    channel === 'winnow:update:snapshot'
      ? () => structuredClone(state.snapshot)
      : channel === 'winnow:update:action'
        ? (_event, name, value) => action(name, value)
        : listener,
  )
await import('../../out/main/index.js')
