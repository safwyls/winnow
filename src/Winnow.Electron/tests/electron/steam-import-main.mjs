import { app } from 'electron'
import { basename, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
const index = process.argv.indexOf('--data-dir')
const directory = resolve(process.argv[index + 1] ?? '')
if (index < 0 || !basename(directory).startsWith('winnow-electron-steam-import-'))
  throw Error('Saved-page verification requires its own isolated directory.')
app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
app.setPath('documents', join(directory, 'documents'))
await import('../../out/main/index.js')
