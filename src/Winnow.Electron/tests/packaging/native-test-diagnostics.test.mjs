import { test } from 'node:test'
import assert from 'node:assert/strict'
import { link, mkdtemp, mkdir, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { inventory } from '../../scripts/native-test-evidence.mjs'
import {
  retainNativeDiagnostics,
  sanitizeDiagnostic,
  withRetainedDiagnostics,
} from '../../scripts/native-test-diagnostics.mjs'

const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  'base64',
)
const json = (value) => JSON.stringify(value, null, 2)
const attachment = (value) => ({
  name: 'fixture-ledger',
  contentType: 'application/json',
  body: Buffer.from(json(value)).toString('base64'),
})
const report = (result = {}) => ({
  config: { metadata: { gitDiff: 'large source diff', runner: 'Windows' } },
  errors: [],
  suites: [
    {
      specs: [
        {
          id: 'source-id',
          file: 'scope.spec.ts',
          title: 'source behavior',
          line: 12,
          column: 1,
          tests: [{ projectId: '', expectedStatus: 'passed', results: [result] }],
        },
      ],
    },
  ],
})
const resultOf = (value) => value.suites[0].specs[0].tests[0].results[0]
async function workspace(t) {
  const root = await mkdtemp(join(tmpdir(), 'winnow-native-diagnostics-test-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const raw = join(root, 'raw'),
    retained = join(root, 'retained')
  await mkdir(raw)
  await mkdir(retained)
  const put = async (name, contents) => {
    const path = join(raw, name)
    await mkdir(join(path, '..'), { recursive: true })
    await writeFile(path, contents)
    return path
  }
  const read = async (name) => JSON.parse(await readFile(join(retained, name), 'utf8'))
  return { root, raw, retained, put, read }
}

test('sanitizes credential fields and named settings while preserving error identity and numeric evidence', () => {
  const source = {
    token: 'discovery-fixture-token',
    clientSecret: 'client-fixture-secret',
    settings: [{ key: 'npsso', value: 'sony-fixture-secret' }],
    request: { code: 'callback-fixture-code', expectedState: 'callback-fixture-state' },
    error: { code: 'ECONNREFUSED', message: 'connection refused', stack: 'at source.ts:12:3' },
    storedSecret: true,
    tokenCount: 2,
    count: 15,
    metadata: { gitDiff: 'not retained', build: 'abc' },
  }
  const clean = sanitizeDiagnostic(source)
  for (const secret of [
    'discovery-fixture-token',
    'client-fixture-secret',
    'sony-fixture-secret',
    'callback-fixture-code',
    'callback-fixture-state',
  ])
    assert.ok(!json(clean).includes(secret))
  assert.deepEqual(clean.error, source.error)
  assert.equal(clean.storedSecret, true)
  assert.equal(clean.tokenCount, 2)
  assert.equal(clean.count, 15)
  assert.deepEqual(clean.metadata, { build: 'abc' })
  assert.equal(source.token, 'discovery-fixture-token')
})

test('scrubs bearer, cookies and callback query values without removing the failure message', () => {
  const clean = sanitizeDiagnostic({
    message: [
      'HTTP 401: Bearer fixture-bearer-value',
      'Cookie: session=fixture-cookie-value',
      'GET https://fixture.invalid/callback?code=fixture-code&state=fixture-state&other=keep',
      'client_secret="fixture-secret"',
    ].join('\n'),
  })
  assert.match(clean.message, /HTTP 401/)
  assert.match(clean.message, /other=keep/)
  for (const value of [
    'fixture-bearer-value',
    'fixture-cookie-value',
    'fixture-code',
    'fixture-state',
    'fixture-secret',
  ])
    assert.ok(!clean.message.includes(value), value)
})

test('preserves a 32 KiB noncredential diagnostic beside real credential assignments', () => {
  const identifier = 'source.component.'.repeat(2048)
  const clean = sanitizeDiagnostic({ message: `${identifier}: unchanged\nclient_secret="fixture-secret"` })
  assert.ok(clean.message.startsWith(`${identifier}: unchanged\n`))
  assert.ok(!clean.message.includes('fixture-secret'))
  assert.ok(clean.message.endsWith('client_secret="[REDACTED]"'))
})

test('retains only immediate test PNG, sanitized JSON and error context, never profile/database/archive files', async (t) => {
  const w = await workspace(t)
  await w.put(
    'results.json',
    json(report({ status: 'failed', errors: [{ message: 'expected a visible card' }] })),
  )
  await w.put('artifacts/scope-geometry/card.png', png)
  await w.put('artifacts/scope-geometry/metrics.json', json({ width: 320, token: 'fixture-secret' }))
  await w.put('artifacts/scope-geometry/error-context.md', '# Failure\nBearer fixture-bearer')
  await w.put('artifacts/scope-geometry/attachments/details.json', json({ count: 3 }))
  for (const name of [
    'library.db',
    'library.db-wal',
    'trace.zip',
    'archive.tar.gz',
    'video.webm',
    'startup.log',
    'note.txt',
  ])
    await w.put(`artifacts/scope-geometry/${name}`, 'must not retain')
  await w.put(
    'artifacts/scope-geometry/electron-userdata/Local Storage/state.json',
    json({ token: 'must not retain' }),
  )
  await w.put('artifacts/scope-geometry/attachments/nested/state.json', json({ token: 'must not retain' }))
  await w.put('artifacts/profile/preferences.json', json({ token: 'must not retain' }))
  await w.put('artifacts/backend.json', json({ token: 'must not retain' }))
  const summary = await retainNativeDiagnostics(w.raw, w.retained)
  assert.deepEqual(summary.artifacts.sort(), [
    'artifacts/scope-geometry/attachments/details.json',
    'artifacts/scope-geometry/card.png',
    'artifacts/scope-geometry/error-context.md',
    'artifacts/scope-geometry/metrics.json',
  ])
  assert.deepEqual(await readFile(join(w.retained, 'artifacts/scope-geometry/card.png')), png)
  assert.deepEqual(await w.read('artifacts/scope-geometry/metrics.json'), { width: 320, token: '[REDACTED]' })
  assert.equal(
    await readFile(join(w.retained, 'artifacts/scope-geometry/error-context.md'), 'utf8'),
    '# Failure\nBearer [REDACTED]',
  )
  assert.ok(summary.excludedFiles >= 10)
})

test('decodes and sanitizes inline JSON and buffered stderr, preserving PNG attachments and test inventory', async (t) => {
  const w = await workspace(t)
  const source = report({
    status: 'failed',
    retry: 0,
    duration: 5,
    errors: [{ message: 'request for inline-secret failed' }],
    stderr: [{ buffer: Buffer.from('inline-secret: connection refused').toString('base64') }],
    attachments: [
      attachment({ clientSecret: 'inline-secret', count: 20 }),
      { name: 'screenshot', contentType: 'image/png', body: png.toString('base64') },
    ],
  })
  await w.put('results.json', json(source))
  await retainNativeDiagnostics(w.raw, w.retained)
  const clean = await w.read('results.json'),
    result = resultOf(clean)
  assert.deepEqual(inventory(clean), inventory(source))
  assert.equal(clean.config.metadata.gitDiff, undefined)
  assert.match(result.errors[0].message, /request for \[REDACTED\] failed/)
  assert.deepEqual(result.stderr, [{ text: '[REDACTED]: connection refused' }])
  assert.deepEqual(JSON.parse(Buffer.from(result.attachments[0].body, 'base64')), {
    clientSecret: '[REDACTED]',
    count: 20,
  })
  assert.deepEqual(Buffer.from(result.attachments[1].body, 'base64'), png)
})

test('only retained local attachment paths survive; unknown binary and arbitrary text bodies are omitted', async (t) => {
  const w = await workspace(t)
  const path = await w.put('artifacts/scope-attachments/details.json', json({ count: 3 }))
  await w.put(
    'results.json',
    json(
      report({
        attachments: [
          { name: 'local', contentType: 'application/json', path },
          { name: 'escape', contentType: 'application/json', path: resolve(w.raw, '..', 'outside.json') },
          {
            name: 'raw text',
            contentType: 'text/plain',
            body: Buffer.from('unstructured secret').toString('base64'),
          },
          {
            name: 'archive',
            contentType: 'application/zip',
            body: Buffer.from('archive bytes').toString('base64'),
          },
          {
            name: 'broken json',
            contentType: 'application/json',
            body: Buffer.from('password plain').toString('base64'),
          },
        ],
      }),
    ),
  )
  await retainNativeDiagnostics(w.raw, w.retained)
  assert.deepEqual(resultOf(await w.read('results.json')).attachments, [
    { name: 'local', contentType: 'application/json', path: 'artifacts/scope-attachments/details.json' },
  ])
})

test('never traverses linked output trees or follows linked JSON files', async (t) => {
  const w = await workspace(t),
    outside = join(w.root, 'outside')
  await mkdir(outside)
  await writeFile(join(outside, 'private.json'), json({ token: 'outside-secret' }))
  await w.put('results.json', json(report()))
  await mkdir(join(w.raw, 'artifacts/scope-local'), { recursive: true })
  await symlink(
    outside,
    join(w.raw, 'artifacts/scope-linked'),
    process.platform === 'win32' ? 'junction' : 'dir',
  )
  // Windows directory junctions and hardlinks need no symlink privilege; Unix
  // covers a file symlink as well as the linked directory.
  if (process.platform === 'win32')
    await link(join(outside, 'private.json'), join(w.raw, 'artifacts/scope-local/private.json'))
  else await symlink(join(outside, 'private.json'), join(w.raw, 'artifacts/scope-local/private.json'), 'file')
  const summary = await retainNativeDiagnostics(w.raw, w.retained)
  assert.deepEqual(summary.artifacts, [])
  assert.deepEqual(await readdir(w.retained), ['results.json', 'retention.json'])
})

test('failed and interrupted runs retain available errors in finally and never produce a passing receipt', async (t) => {
  const w = await workspace(t),
    failure = Error('Playwright exited 1')
  await assert.rejects(
    withRetainedDiagnostics(w.raw, w.retained, async () => {
      await w.put(
        'results.json',
        json(
          report({
            status: 'interrupted',
            retry: 0,
            errors: [{ message: 'renderer exited unexpectedly', stack: 'at test.ts:7' }],
          }),
        ),
      )
      throw failure
    }),
    (error) => error === failure,
  )
  assert.equal(resultOf(await w.read('results.json')).status, 'interrupted')
  assert.match(resultOf(await w.read('results.json')).errors[0].stack, /test.ts:7/)
  assert.deepEqual((await w.read('retention.json')).reports, ['results.json'])
  assert.ok(!(await readdir(w.retained)).includes('native-evidence.json'))
})

test('unparseable JSON and disguised image data are recorded but never copied raw', async (t) => {
  const w = await workspace(t)
  await w.put('full-inventory.json', 'unparseable private text')
  await w.put('results.json', json(report({ status: 'failed' })))
  await w.put('artifacts/scope-failed/data.json', 'unparseable private text')
  await w.put('artifacts/scope-failed/private.png', 'not a screenshot')
  const summary = await retainNativeDiagnostics(w.raw, w.retained)
  assert.deepEqual(summary.artifacts, [])
  assert.equal(summary.invalidFiles.length, 3)
  assert.deepEqual(await readdir(w.retained), ['results.json', 'retention.json'])
})
