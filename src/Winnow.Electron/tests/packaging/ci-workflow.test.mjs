import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { load } = require('js-yaml')
const workflow = (name) =>
  load(readFileSync(new URL(`../../../../.github/workflows/${name}.yml`, import.meta.url), 'utf8'))
const ci = workflow('ci'),
  electron = workflow('electron'),
  packages = workflow('electron-packages'),
  release = workflow('release')

test('protected Windows status requires the backend, frontend, platform packages and plugins even after failures', () => {
  assert.equal(ci.jobs.verify.name, 'Windows build, tests and migration integrity')
  assert.deepEqual(ci.jobs.verify.needs, ['dotnet', 'electron', 'packages', 'plugins'])
  assert.equal(ci.jobs.verify.if, 'always()')
  const step = ci.jobs.verify.steps[0]
  for (const dependency of ci.jobs.verify.needs) {
    const entry = Object.entries(step.env).find(([, value]) => value.includes(`needs.${dependency}.result`))
    assert.ok(entry)
    assert.ok(step.run.includes(`test "$${entry[0]}" = success`))
  }
  assert.equal(ci.jobs['linux-sessions'].name, 'Linux native and Proton-environment session smoke tests')
})

test('Electron and package gates always run fresh and have no path-filtered PR substitutes', () => {
  assert.ok('pull_request' in ci.on)
  for (const name of ['electron', 'packages', 'plugins']) assert.equal(ci.jobs[name].if, undefined)
  for (const value of [electron, packages]) {
    assert.ok('workflow_call' in value.on)
    assert.equal(value.on.pull_request, undefined)
    assert.equal(value.on.push, undefined)
  }
  assert.ok(electron.jobs.frontend.steps.some((step) => step.run?.includes('--require-complete')))
})

test('all native shards have isolated machines, one scheduling policy and mandatory accounting', () => {
  assert.deepEqual(electron.jobs.native.strategy.matrix.shard, [1, 2, 3, 4, 5, 6, 7, 8])
  assert.equal(electron.jobs.native.strategy['fail-fast'], false)
  assert.equal(electron.jobs.native['runs-on'], 'windows-2025')
  assert.ok(
    electron.jobs.native.steps.some((step) =>
      step.run?.includes('native-test-ci.mjs run ${{ matrix.shard }}/8'),
    ),
  )
  assert.equal(electron.jobs['native-accounting'].needs, 'native')
  assert.equal(electron.jobs['native-accounting'].if, 'always()')
  assert.ok(
    electron.jobs['native-accounting'].steps.some((step) => step.run?.includes('verify native-evidence 8')),
  )
})

test('both platform package jobs exercise the bundled provider and old-release installation and recovery', () => {
  for (const job of [packages.jobs.windows, packages.jobs.linux]) {
    const commands = job.steps.map((step) => step.run ?? '').join('\n')
    for (const required of [
      'Publish-Electron.ps1',
      'Verify-ElectronPackage.ps1',
      'Test-BundledPlugin.ps1',
      'Test-PortableUpgrade.ps1',
    ])
      assert.ok(commands.includes(required), required)
    assert.ok(
      commands.includes(job === packages.jobs.windows ? 'Test-WindowsInstaller.ps1' : 'smoke-test.sh'),
    )
  }
})

test('release tags and build-only dispatch consume the same validated artifacts before draft creation', () => {
  assert.deepEqual(Object.keys(release.on).sort(), ['push', 'workflow_dispatch'])
  assert.deepEqual(release.on.push, { tags: ['v*'] })
  assert.equal(release.jobs.verify.uses, './.github/workflows/ci.yml')
  assert.equal(release.jobs.verify.with.version, '${{ needs.version.outputs.version }}')
  assert.deepEqual(release.jobs.release.needs, ['version', 'verify'])
  assert.equal(release.jobs.release.if, "startsWith(github.ref, 'refs/tags/v')")
  const patterns = release.jobs.release.steps
    .filter((step) => step.uses?.startsWith('actions/download-artifact'))
    .map((step) => step.with.pattern)
  assert.deepEqual(patterns, ['packages-*', 'electron-packages-*'])
  assert.ok(release.jobs.release.steps.some((step) => step.run?.includes('--draft')))
  assert.equal(release.jobs.build, undefined)
})
