import { app, dialog } from 'electron'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

// Invalid startup arguments are rejected before the ordinary data-directory redirect.
const root = process.env.WINNOW_FIXTURE_PROFILE
if (!root) throw Error('The invalid-launch fixture requires an isolated Chromium profile')
mkdirSync(join(root, 'chromium'), { recursive: true })
app.setPath('userData', root)
app.setPath('sessionData', join(root, 'chromium'))
dialog.showErrorBox = (title, message) => console.error(`${title}: ${message}`)
await import('../../out/main/index.js')
