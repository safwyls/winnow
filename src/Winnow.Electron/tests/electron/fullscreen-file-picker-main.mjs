import { app, dialog } from 'electron'
import { join } from 'node:path'

const dataDirectory = process.argv[process.argv.indexOf('--data-dir') + 1]
app.setPath('documents', join(dataDirectory, 'documents'))
globalThis.pickerDialogs = []
dialog.showOpenDialog = async (_window, options) => {
  globalThis.pickerDialogs.push(options)
  return { canceled: true, filePaths: [] }
}
dialog.showSaveDialog = async (_window, options) => {
  globalThis.pickerDialogs.push(options)
  return { canceled: true }
}
const originalFetch = globalThis.fetch
globalThis.fetch = async (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input)
  if (url.hostname === '127.0.0.1') {
    if (url.pathname === '/api/v1/artwork/sources') return Response.json([])
    if (
      /^\/api\/v1\/works\/\d+\/artwork\/(Hero|Cover|Icon)$/.test(url.pathname) &&
      (!init?.method || init.method === 'GET')
    )
      return Response.json({ current: null, revision: 'A'.repeat(64) })
  }
  return originalFetch(input, init)
}
await import('../../out/main/index.js')
