import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createEvidence, verifyEvidence } from '../../scripts/native-test-evidence.mjs'

const provenance = {
  commit: 'a'.repeat(40),
  tree: 'b'.repeat(40),
  lockfile: 'c'.repeat(64),
  node: 'v24.19.0',
  electron: '44.4.5',
  sdk: '10.0.400',
  platform: 'win32',
  imageOS: 'win25',
  imageVersion: '1',
}
const spec = (id) => ({
  id,
  file: `${id}.spec.ts`,
  title: `test ${id}`,
  line: 1,
  column: 1,
  ok: true,
  tests: [
    {
      projectId: '',
      expectedStatus: 'passed',
      status: 'expected',
      results: [{ status: 'passed', retry: 0, duration: 20, errors: [] }],
    },
  ],
})
const report = (ids, shard) => ({
  config: {
    workers: 1,
    fullyParallel: false,
    forbidOnly: true,
    projects: [{ retries: 0, repeatEach: 1 }],
    ...(shard ? { shard: { ...shard } } : {}),
  },
  errors: [],
  suites: [{ specs: ids.map(spec) }],
})
const inputs = (current = 1) => {
  const shard = { current, total: 2 },
    ids = current === 1 ? ['a', 'b'] : ['c']
  return [report(['a', 'b', 'c']), report(ids, shard), report(ids, shard), structuredClone(provenance), shard]
}
test('all unique, successful shards account for every collected test', () => {
  assert.equal(verifyEvidence([createEvidence(...inputs()), createEvidence(...inputs(2))], 2).tests, 3)
})
for (const [name, change] of Object.entries({
  'collection error': (x) => x[0].errors.push({ message: 'collection failed' }),
  'missing native body': (x) => x[2].suites[0].specs.pop(),
  'duplicate native body': (x) => x[2].suites[0].specs.push(x[2].suites[0].specs[0]),
  'unexpected native body': (x) => (x[2].suites[0].specs[0].id = 'unknown'),
  skip: (x) => (x[2].suites[0].specs[0].tests[0].results[0].status = 'skipped'),
  failure: (x) => (x[2].suites[0].specs[0].tests[0].results[0].status = 'failed'),
  interruption: (x) => (x[2].suites[0].specs[0].tests[0].results[0].status = 'interrupted'),
  'expected failure': (x) => (x[2].suites[0].specs[0].tests[0].expectedStatus = 'failed'),
  retry: (x) => (x[2].suites[0].specs[0].tests[0].results[0].retry = 1),
  'flaky status': (x) => (x[2].suites[0].specs[0].tests[0].status = 'flaky'),
  'second result': (x) => x[2].suites[0].specs[0].tests[0].results.push({ status: 'passed' }),
  'test error': (x) => x[2].suites[0].specs[0].tests[0].results[0].errors.push({ message: 'oops' }),
  'wrong shard': (x) => (x[2].config.shard.current = 2),
  'parallel worker': (x) => (x[2].config.workers = 2),
  'file splitting': (x) => (x[2].config.fullyParallel = true),
  'exclusive tests allowed': (x) => (x[2].config.forbidOnly = false),
  'retries configured': (x) => (x[2].config.projects[0].retries = 1),
  'repeat configured': (x) => (x[2].config.projects[0].repeatEach = 2),
  'missing SDK': (x) => delete x[3].sdk,
  'missing checkout': (x) => delete x[3].commit,
  'missing lockfile': (x) => delete x[3].lockfile,
}))
  test(`refuses ${name}`, () => {
    const value = inputs()
    change(value)
    assert.throws(() => createEvidence(...value))
  })
for (const [name, change] of Object.entries({
  'missing shard': (x) => x.pop(),
  'duplicate shard': (x) => (x[1] = x[0]),
  'different source': (x) => (x[1].provenance.commit = 'd'.repeat(40)),
  'different dependency': (x) => (x[1].provenance.lockfile = 'd'.repeat(64)),
  'different runtime': (x) => (x[1].provenance.electron = '99.0.0'),
  'different inventory': (x) => x[1].full.pop(),
  'missing provenance on every shard': (x) => {
    for (const record of x) delete record.provenance.sdk
  },
  'changed planned identity': (x) => (x[1].expected[0].title = 'a different test'),
  'missing result': (x) => x[1].results.pop(),
  'duplicate result': (x) => (x[0].results[1] = x[0].results[0]),
  'unpassed result': (x) => (x[1].results[0].status = 'skipped'),
  'uncovered inventory entry': (x) => {
    for (const r of x) r.full.push({ key: 'uncovered' })
  },
}))
  test(`aggregate refuses ${name}`, () => {
    const records = [createEvidence(...inputs()), createEvidence(...inputs(2))]
    change(records)
    assert.throws(() => verifyEvidence(records, 2))
  })
