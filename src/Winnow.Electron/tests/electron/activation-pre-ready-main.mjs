import { app } from 'electron'
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
const marker = join(directory, 'secondary-requesting.json')
globalThis.__preReady = { requestedBeforeReady: false, exitCode: null }
const acquire = app.requestSingleInstanceLock.bind(app)
app.requestSingleInstanceLock = (data) => {
  const owns = acquire(data)
  if (owns) {
    const child = spawn(
      process.execPath,
      [
        fileURLToPath(new URL('./activation-pre-ready-child.mjs', import.meta.url)),
        '--data-dir',
        directory,
        '--jump-list-fullscreen',
      ],
      {
        windowsHide: true,
        stdio: 'ignore',
        env: { ...process.env, ELECTRON_RUN_AS_NODE: undefined },
      },
    )
    child.once('error', (error) => {
      globalThis.__preReady.error = error.message
    })
    child.once('exit', (code) => {
      globalThis.__preReady.exitCode = code
    })
    const deadline = Date.now() + 5000
    const wait = new Int32Array(new SharedArrayBuffer(4))
    // Hold the owner before its listener registration and before app readiness.
    while (!existsSync(marker) && Date.now() < deadline) Atomics.wait(wait, 0, 0, 20)
    globalThis.__preReady.requestedBeforeReady = existsSync(marker) && !app.isReady()
    Atomics.wait(wait, 0, 0, 200)
  }
  return owns
}
await import('./activation-main.mjs')
