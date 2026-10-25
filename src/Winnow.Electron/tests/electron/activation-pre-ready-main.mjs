import { app, BrowserWindow } from 'electron'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const directory = process.argv[process.argv.indexOf('--data-dir') + 1]
globalThis.__preReady = { requestedBeforeFrontendReady: false, platformReady: false, exitCode: null }
globalThis.__holdActivationPrimary = async () => {
  globalThis.__preReady.platformReady = app.isReady()
  globalThis.__preReady.requestedBeforeFrontendReady = BrowserWindow.getAllWindows().length === 0
  const child = spawn(
    process.execPath,
    [fileURLToPath(new URL('../..', import.meta.url)), '--data-dir', directory, '--jump-list-fullscreen'],
    {
      windowsHide: true,
      stdio: 'pipe',
      env: { ...process.env, ELECTRON_RUN_AS_NODE: undefined },
    },
  )
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      child.kill()
      reject(Error('Pre-listener secondary timed out'))
    }, 15000)
    child.once('error', (error) => {
      clearTimeout(timeout)
      reject(error)
    })
    child.once('exit', (code) => {
      clearTimeout(timeout)
      globalThis.__preReady.exitCode = code
      code === 0 ? resolve() : reject(Error(`Secondary exited ${code}`))
    })
  })
}
await import('./activation-main.mjs')
