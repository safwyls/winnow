import { createHash } from 'node:crypto'

const fail = (message) => {
  throw new Error(message)
}
const digest = (value) => createHash('sha256').update(JSON.stringify(value)).digest('hex')

export function inventory(report) {
  if (report.errors?.length) fail('Playwright reported collection errors.')
  const records = []
  const visit = (suite) => {
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        const key = `${spec.id}:${test.projectId}`
        if (!spec.id || typeof test.projectId !== 'string') fail('Missing test identity.')
        records.push({
          key,
          file: spec.file.replaceAll('\\', '/'),
          title: spec.title,
          line: spec.line,
          column: spec.column,
          project: test.projectId,
        })
      }
    }
    for (const child of suite.suites ?? []) visit(child)
  }
  visit(report)
  records.sort((a, b) => a.key.localeCompare(b.key, 'en'))
  if (!records.length || new Set(records.map((test) => test.key)).size !== records.length)
    fail('Empty or duplicate native test inventory.')
  return records
}

export function createEvidence(full, expected, actual, provenance, shard) {
  if (
    !Number.isInteger(shard.current) ||
    !Number.isInteger(shard.total) ||
    shard.current < 1 ||
    shard.current > shard.total
  )
    fail('Invalid shard identity.')
  const fullTests = inventory(full),
    expectedTests = inventory(expected),
    actualTests = inventory(actual)
  if (digest(actualTests) !== digest(expectedTests)) fail('Executed tests differ from the planned shard.')
  if (
    full.config?.shard ||
    expected.config?.shard?.current !== shard.current ||
    expected.config?.shard?.total !== shard.total ||
    actual.config?.shard?.current !== shard.current ||
    actual.config?.shard?.total !== shard.total
  )
    fail('Playwright shard configuration differs from plan.')
  for (const report of [full, expected, actual]) {
    if (
      report.config?.workers !== 1 ||
      report.config?.fullyParallel !== false ||
      report.config?.forbidOnly !== true
    )
      fail('Native tests require one worker, file-preserving scheduling and forbidOnly.')
    if (
      !report.config.projects?.length ||
      report.config.projects.some((p) => p.retries !== 0 || p.repeatEach !== 1)
    )
      fail('Retries or repeated native tests are not accepted.')
  }
  const fullByKey = new Map(fullTests.map((test) => [test.key, test]))
  for (const test of expectedTests)
    if (digest(fullByKey.get(test.key)) !== digest(test))
      fail('Shard contains a test outside the full inventory.')
  const results = []
  const visit = (suite) => {
    for (const spec of suite.specs ?? [])
      for (const test of spec.tests ?? []) {
        const result = test.results?.[0]
        if (
          spec.ok !== true ||
          test.expectedStatus !== 'passed' ||
          test.status !== 'expected' ||
          test.results?.length !== 1 ||
          result.status !== 'passed' ||
          result.retry !== 0 ||
          result.errors?.length ||
          result.error
        )
          fail(`Native test did not pass once: ${spec.file}: ${spec.title}`)
        results.push({ key: `${spec.id}:${test.projectId}`, duration: result.duration, status: 'passed' })
      }
    for (const child of suite.suites ?? []) visit(child)
  }
  visit(actual)
  validateProvenance(provenance)
  return { schema: 1, provenance, shard, full: fullTests, expected: expectedTests, results }
}

function validateProvenance(provenance) {
  if (
    !provenance ||
    !/^[a-f0-9]{40}$/.test(provenance.commit ?? '') ||
    !/^[a-f0-9]{40}$/.test(provenance.tree ?? '') ||
    !/^[a-f0-9]{64}$/.test(provenance.lockfile ?? '') ||
    ['node', 'electron', 'sdk', 'platform', 'imageOS', 'imageVersion'].some((key) => !provenance[key])
  )
    fail('Native evidence has incomplete checkout or runtime provenance.')
}

export function verifyEvidence(records, total) {
  if (!Number.isInteger(total) || total < 1 || records.length !== total)
    fail('Native shard evidence is missing.')
  const first = records[0],
    seenShards = new Set(),
    seenTests = new Set(),
    fileOwners = new Map()
  for (const record of records) {
    validateProvenance(record.provenance)
    if (
      record.schema !== 1 ||
      record.shard?.total !== total ||
      !Number.isInteger(record.shard.current) ||
      record.shard.current < 1 ||
      record.shard.current > total ||
      seenShards.has(record.shard.current)
    )
      fail('Invalid or duplicate native shard evidence.')
    seenShards.add(record.shard.current)
    if (digest(record.provenance) !== digest(first.provenance) || digest(record.full) !== digest(first.full))
      fail('Native shards have different source, dependencies, runtimes or inventories.')
    if (!record.expected?.length || record.results?.length !== record.expected.length)
      fail('Native shard is empty or incomplete.')
    const expected = new Set(record.expected.map((test) => test.key))
    if (expected.size !== record.expected.length) fail('Duplicate test in a shard plan.')
    const fullByKey = new Map(record.full.map((test) => [test.key, test]))
    for (const test of record.expected) {
      if (digest(fullByKey.get(test.key)) !== digest(test))
        fail('Shard plan differs from the full inventory.')
      const owner = fileOwners.get(test.file)
      if (owner && owner !== record.shard.current) fail('A native test file was split across runners.')
      fileOwners.set(test.file, record.shard.current)
    }
    for (const result of record.results) {
      if (result.status !== 'passed' || !expected.has(result.key) || seenTests.has(result.key))
        fail('Failed, unplanned or duplicate native result.')
      seenTests.add(result.key)
    }
  }
  const full = first.full
  if (
    !full?.length ||
    new Set(full.map((test) => test.key)).size !== full.length ||
    full.length !== seenTests.size ||
    full.some((test) => !seenTests.has(test.key))
  )
    fail('Native shard union does not cover the complete test inventory.')
  return { tests: full.length, shards: total, provenance: first.provenance }
}
