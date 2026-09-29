import type { ElectronApplication } from '@playwright/test'
import { execFile } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { promisify } from 'node:util'
import { setTimeout as delay } from 'node:timers/promises'

async function fixtureEndpoint(directory: string) {
  const deadline = Date.now() + 5000
  for (;;) {
    try {
      return JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    } catch (error) {
      // A test can fail while the separately started backend is still publishing
      // discovery. Quitting the renderer first would orphan that fixture backend.
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT' || Date.now() >= deadline) throw error
      await delay(100)
    }
  }
}

/** Only a fixture's own discovery file and launched process are eligible for cleanup. */
export async function closeFixture(application: ElectronApplication | undefined, directory?: string) {
  let backendError: unknown
  if (directory) {
    try {
      const endpoint = await fixtureEndpoint(directory)
      const address = new URL(endpoint.address)
      if (address.protocol !== 'http:' || address.hostname !== '127.0.0.1' || address.pathname !== '/')
        throw Error('Unexpected fixture backend address')
      const response = await fetch(new URL('/api/v1/lifecycle/shutdown', address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
      if (!response.ok) throw Error(`Fixture backend shutdown returned ${response.status}`)
    } catch (error) {
      // Startup can fail before discovery; an already stopped backend cannot answer.
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT' && !(error instanceof TypeError))
        backendError = error
    }
  }
  if (!application) {
    if (backendError) throw backendError
    return
  }
  const child = application.process(),
    events: string[] = []
  let electronPid: number | undefined
  const record = (chunk: Buffer) => {
    for (const line of chunk.toString().split(/\r?\n/))
      if (line.startsWith('FIXTURE_SHUTDOWN:')) events.push(line)
  }
  child.stderr?.on('data', record)
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    await Promise.race([
      (async () => {
        electronPid = await application.evaluate(({ app, BrowserWindow }) => {
          app.on('before-quit', () => console.error('FIXTURE_SHUTDOWN:before-quit'))
          app.on('will-quit', () =>
            console.error(`FIXTURE_SHUTDOWN:will-quit:${JSON.stringify(process.getActiveResourcesInfo())}`),
          )
          app.on('quit', (_, code) => console.error(`FIXTURE_SHUTDOWN:quit:${code}`))
          for (const window of BrowserWindow.getAllWindows()) {
            window.on('close', () => console.error('FIXTURE_SHUTDOWN:window-close'))
            window.on('closed', () => console.error('FIXTURE_SHUTDOWN:window-closed'))
            window.on('unresponsive', () => console.error('FIXTURE_SHUTDOWN:unresponsive'))
            window.webContents.on('will-prevent-unload', () =>
              console.error('FIXTURE_SHUTDOWN:prevent-unload'),
            )
          }
          // Playwright invokes quit from an inspector evaluation and then detaches.
          // Defer the native call so it runs outside that evaluation without a
          // second debugger request after the application's quit event.
          const quit = app.quit.bind(app)
          app.quit = () => {
            setImmediate(quit)
          }
          return process.pid
        })
        await application.close()
        if (child.exitCode === null && child.signalCode === null)
          await new Promise<void>((done) => child.once('exit', () => done()))
        if (child.exitCode !== 0)
          throw Error(`Electron fixture exited ${child.exitCode}, signal ${child.signalCode}`)
      })(),
      new Promise<never>((_, reject) => {
        timer = setTimeout(
          () =>
            reject(
              Error(
                `Electron fixture close timed out: ${directory}; pid=${child.pid}, exit=${child.exitCode}, signal=${child.signalCode}; ${events.join(', ')}`,
              ),
            ),
          5000,
        )
      }),
    ])
  } catch (error) {
    // Playwright launches cmd.exe on Windows. Killing only that wrapper can leave
    // Electron holding its inherited pipes and stall the worker's final teardown.
    const ownedPid = child.exitCode === null && child.signalCode === null ? child.pid : electronPid
    if (ownedPid && process.platform === 'win32')
      await promisify(execFile)('taskkill.exe', ['/pid', String(ownedPid), '/T', '/F'], {
        windowsHide: true,
        timeout: 5000,
      }).catch(() => {})
    else if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL')
    throw error
  } finally {
    clearTimeout(timer)
    child.stderr?.off('data', record)
  }
  if (backendError) throw backendError
}
