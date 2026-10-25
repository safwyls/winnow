import { app } from 'electron'
import { writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

app.setAppPath(fileURLToPath(new URL('../..', import.meta.url)))
const state = { pid: process.pid, windows: 0, exitCode: null }
const save = () => writeFileSync(process.env.WINNOW_TERMINAL_REPORT, JSON.stringify(state))
app.on('browser-window-created', () => {
  state.windows++
  save()
})
app.on('quit', (_event, code) => {
  state.exitCode = code
  save()
})
save()
await import('../../out/main/index.js')
