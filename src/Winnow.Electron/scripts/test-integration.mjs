import { spawn } from 'node:child_process'
import { mkdir, mkdtemp, readFile } from 'node:fs/promises'
import { isAbsolute, join, resolve } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

const executable = process.env.WINNOW_BACKEND_PATH
if (!executable || !isAbsolute(executable))
  throw Error('Set WINNOW_BACKEND_PATH to an absolute Debug backend executable or DLL.')
const temporaryRoot = resolve('../..', '.tmp')
await mkdir(temporaryRoot, { recursive: true })
const directory = await mkdtemp(join(temporaryRoot, 'winnow-electron-integration-'))
const arguments_ = ['--data-dir', directory, '--seed-sample', '--no-sync']
const environment = Object.fromEntries(
  Object.entries(process.env).filter(([key]) => !/^Igdb__(ClientId|ClientSecret)$/i.test(key)),
)
const backend = spawn(
  executable.endsWith('.dll') ? 'dotnet' : executable,
  executable.endsWith('.dll') ? [executable, ...arguments_] : arguments_,
  { windowsHide: true, stdio: 'ignore', env: { ...environment, Igdb__ClientId: '', Igdb__ClientSecret: '' } },
)
let startupError
backend.on('error', (error) => {
  startupError = error
})
let endpoint
let ready = false
try {
  const deadline = Date.now() + 60_000
  while (Date.now() < deadline) {
    if (startupError) throw startupError
    if (backend.exitCode !== null)
      throw Error(`Test backend exited ${backend.exitCode} before becoming ready.`)
    try {
      endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      const address = new URL(endpoint.address)
      if (
        address.protocol !== 'http:' ||
        address.hostname !== '127.0.0.1' ||
        address.pathname !== '/' ||
        address.username ||
        address.password
      )
        throw Error('Unexpected test backend address.')
      const health = await fetch(new URL('/api/v1/health', address), {
        headers: { Authorization: `Bearer ${endpoint.token}` },
        redirect: 'error',
        signal: AbortSignal.timeout(1000),
      })
      if (health.ok) {
        ready = true
        break
      }
    } catch {
      endpoint = undefined
    }
    await delay(100)
  }
  if (!endpoint || !ready) throw Error('Test backend did not become ready.')
  // This database was created above for this test process. Seed a recorded session so
  // journal concurrency runs instead of silently skipping on the artwork-only demo.
  const { DatabaseSync } = await import('node:sqlite')
  const database = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    const ended = new Date(Date.now() - 3600_000),
      started = new Date(ended.valueOf() - 1800_000)
    const result = database
      .prepare(
        'INSERT INTO sessions (ownership_id,started_at,ended_at,duration_s,detection_method) SELECT id,?,?,1800,? FROM ownerships ORDER BY id LIMIT 1',
      )
      .run(
        started.toISOString().replace('T', ' ').slice(0, 19),
        ended.toISOString().replace('T', ' ').slice(0, 19),
        'process_watch',
      )
    if (result.changes !== 1)
      throw Error('Sample ownership is missing. Use a Debug backend for integration tests.')
  } finally {
    database.close()
  }
  const test = spawn(
    process.execPath,
    [resolve('node_modules/vitest/vitest.mjs'), 'run', ...process.argv.slice(2)],
    {
      windowsHide: true,
      stdio: 'inherit',
      env: { ...process.env, WINNOW_TEST_DATA_DIR: directory, WINNOW_ELECTRON_TEST_DATA_DIR: directory },
    },
  )
  process.exitCode = await new Promise((done, reject) => {
    test.once('error', reject)
    test.once('exit', (code) => done(code ?? 1))
  })
} finally {
  if (endpoint) {
    try {
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {}
  }
  if (backend.exitCode === null) {
    await Promise.race([new Promise((done) => backend.once('exit', done)), delay(5000)])
    if (backend.exitCode === null) backend.kill()
  }
}
