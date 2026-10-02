import { app, dialog } from 'electron'
import { basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { observeActivationHelper } from './activation-observer.mjs'
import { appendFileSync } from 'node:fs'

const argument = process.argv.indexOf('--data-dir')
const directory = resolve(process.argv[argument + 1] ?? '')
if (argument < 0 || !basename(directory).startsWith('winnow-electron-native-host-'))
  throw Error('Native host verification requires its isolated data directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
Object.defineProperty(app, 'isPackaged', { get: () => true })
Object.defineProperty(process, 'resourcesPath', {
  value: fileURLToPath(new URL('../../resources', import.meta.url)),
})
const state = (globalThis.__nativeHost = { identities: [], publications: [] })
state.helper = observeActivationHelper()
dialog.showErrorBox = (title, message) => {
  appendFileSync(resolve(directory, 'frontend-error.jsonl'), `${JSON.stringify({ title, message })}\n`)
}
const setIdentity = app.setAppUserModelId.bind(app)
app.setAppUserModelId = (id) => {
  if (!/^Winnow\.Electron\.[a-f0-9]{16}$/.test(id)) throw Error('Unexpected isolated taskbar identity')
  state.identities.push(id)
  return setIdentity(id)
}
const publish = app.setJumpList.bind(app)
app.setJumpList = (items) => {
  if (!state.identities.length) throw Error('Taskbar publication requires an explicit isolated identity')
  const result = publish(items)
  state.publications.push({ items: structuredClone(items), result, at: Date.now() })
  return result
}
app.on('before-quit', () => {
  if (state.identities.length) publish(null)
})
await import('./identity-projections-main.mjs')
