import { readdir, readFile, writeFile, mkdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { resolve, relative } from 'node:path'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const evidenceDirectory = resolve(root, 'src/Winnow.Electron/tests')
const evidence = {}
for (const file of (await readdir(evidenceDirectory))
  .filter((name) => /^migration-.+\.json$/.test(name))
  .sort()) {
  const entries = JSON.parse(await readFile(resolve(evidenceDirectory, file), 'utf8'))
  for (const [id, entry] of Object.entries(entries)) {
    if (evidence[id]) throw new Error(`Duplicate migration evidence for ${id} in ${file}`)
    evidence[id] = entry
  }
}
const records = []
const reviewedBackendFiles = new Set([
  'src/Winnow.Diagnostics/DiagnosticLogging.cs',
  'src/Winnow.Enrich.Stores/StorefrontClient.cs',
  'src/Winnow.Enrich.Stores/StorefrontCache.cs',
  'src/Winnow.Enrich.Stores/ServiceCollectionExtensions.cs',
  'src/Winnow.Ingest.Gog/GogInstalledGameRegistry.cs',
  'src/Winnow.Monitor/LaunchIntents.cs',
])
// Freeze the original contract list: deleting an old source test must never make
// the migration gate pass without an equivalent test or an explicit classification.
const baseline = JSON.parse(
  await readFile(resolve(root, 'docs/spikes/2026-09-28-electron-parity/source-contracts.json'), 'utf8'),
)
for (const { id, source } of baseline.tests) {
  const entry = evidence[id]
  const status = entry?.status ?? 'pending'
  if (!['pending', 'partial', 'ported', 'retained-backend', 'framework-specific'].includes(status))
    throw new Error(`Invalid status for ${id}`)
  if (status === 'ported') {
    if (!entry.tests?.length) throw new Error(`Missing tests for ${id}`)
    for (const test of entry.tests) {
      const path = resolve(root, 'src/Winnow.Electron', test.file)
      if (
        !relative(resolve(root, 'src/Winnow.Electron/tests'), path).startsWith('..') &&
        test.case &&
        (await readFile(path, 'utf8')).includes(test.case)
      )
        continue
      throw new Error(`Missing test evidence ${test.file}: ${test.case} for ${id}`)
    }
  }
  if (status === 'framework-specific' && !entry.reason)
    throw new Error(`Missing framework classification rationale for ${id}`)
  if (status === 'retained-backend') {
    if (!entry.reason || !entry.implementation?.length || !source.startsWith('tests/Winnow.Tests/'))
      throw new Error(`Missing retained backend evidence for ${id}`)
    for (const file of entry.implementation) {
      if ((!/^src\/Winnow\.(Application|Data|Core|Plugins|Recommend|Resolve|Api\.Contracts|Enrich\.SteamWeb|Ingest\.Epic)\//.test(file) && !reviewedBackendFiles.has(file)) || file.includes('..'))
        throw new Error(`Not a backend implementation: ${file}`)
      await readFile(resolve(root, file), 'utf8')
    }
  }
  records.push({ id, source, status, ...(entry ?? {}) })
}
if (new Set(records.map((row) => row.id)).size !== records.length)
  throw new Error('Duplicate original test method ID; qualify its class before recording evidence.')
const known = new Set(records.map((row) => row.id))
for (const id of Object.keys(evidence))
  if (!known.has(id)) throw new Error(`Unknown Avalonia test in evidence: ${id}`)
const counts = Object.fromEntries(
  ['pending', 'partial', 'ported', 'retained-backend', 'framework-specific'].map((status) => [
    status,
    records.filter((row) => row.status === status).length,
  ]),
)
console.log(
  JSON.stringify(
    { methods: records.length, files: new Set(records.map((row) => row.source)).size, ...counts },
    null,
    2,
  ),
)
if (process.argv.includes('--write-report')) {
  const directory = resolve(root, 'docs/spikes/2026-09-28-electron-parity')
  await mkdir(directory, { recursive: true })
  await writeFile(
    resolve(directory, 'test-inventory.json'),
    JSON.stringify({ sourceRevision: baseline.sourceRevision, counts, tests: records }, null, 2) + '\n',
  )
}
if (process.argv.includes('--require-complete') && (counts.pending || counts.partial)) process.exitCode = 1
