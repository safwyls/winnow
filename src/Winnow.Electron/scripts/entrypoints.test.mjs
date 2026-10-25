import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { packagePlan, executePackagePlan } from './package-primary.mjs'
import { assertRepositoryVersions, checkRepositoryVersion, repositoryRoot } from './check-version.mjs'

const context = {
  root: repositoryRoot,
  cwd: resolve(repositoryRoot, 'src/Winnow.Electron'),
  platform: 'win32',
  arch: 'x64',
  version: '0.2.0-dev',
  commit: 'a'.repeat(40),
}

test('package builds only the verified primary directory with the complete source identity', () => {
  const [command, ...rest] = packagePlan(['package'], context)
  assert.deepEqual(rest, [])
  assert.equal(command.executable, 'pwsh')
  assert.deepEqual(command.args, [
    '-NoProfile',
    '-File',
    resolve(repositoryRoot, 'packaging/Publish.ps1'),
    '-Runtime',
    'win-x64',
    '-Version',
    context.version,
    '-Commit',
    context.commit,
    '-OutputDirectory',
    resolve(repositoryRoot, 'artifacts/electron-publish/win-x64'),
  ])
})

for (const platform of ['win32', 'linux']) {
  test(`${platform} dist uses the established packager after successful primary publishing`, () => {
    const commands = packagePlan(
      [
        'dist',
        '--version',
        '0.2.0-beta.8',
        '--commit',
        'b'.repeat(40),
        '--output',
        '../../artifacts/quoted output',
        '--publish-directory',
        '../../artifacts/source directory',
      ],
      { ...context, platform },
    )
    assert.equal(commands.length, 2)
    assert.ok(commands[0].args.includes('0.2.0-beta.8'))
    assert.ok(commands[0].args.includes('b'.repeat(40)))
    assert.equal(commands[1].executable, platform === 'win32' ? 'pwsh' : 'bash')
    assert.ok(commands[1].args.includes(resolve(repositoryRoot, 'artifacts/quoted output')))
    assert.ok(commands[1].args.includes(resolve(repositoryRoot, 'artifacts/source directory')))
    assert.ok(
      commands[1].args.includes(
        resolve(
          repositoryRoot,
          platform === 'win32' ? 'packaging/windows/New-WindowsPackage.ps1' : 'packaging/linux/build.sh',
        ),
      ),
    )
    const calls = []
    executePackagePlan(commands, (file, args, options) => {
      calls.push({ file, args, options })
      return { status: 0 }
    })
    assert.equal(calls.length, 2)
    assert.equal(calls[0].options.shell, undefined)
  })
}

test('package preserves a literal output argument instead of invoking a shell', () => {
  const output = 'path with spaces; $x & literal'
  const [command] = packagePlan(['package', '--output', output], context)
  assert.equal(command.args.at(-1), resolve(context.cwd, output))
})

for (const args of [
  ['dist', '--publish', 'always'],
  ['package', 'extra'],
  ['package', '--commit', 'short'],
  ['package', '--version', '1;bad'],
  ['package', '--publish-directory', 'x'],
  ['dist', '--output', 'x', '--publish-directory', 'x/child'],
]) {
  test(`invalid package request is refused: ${args.join(' ')}`, () =>
    assert.throws(() => packagePlan(args, context)))
}
for (const host of [{ platform: 'darwin' }, { arch: 'arm64' }, { platform: 'linux' }]) {
  test(`mismatched or unsupported primary host is refused: ${JSON.stringify(host)}`, () =>
    assert.throws(() => packagePlan(['package', '--runtime', 'win-x64'], { ...context, ...host })))
}

test('failed publishing cannot continue into distribution or turn a missing process into success', () => {
  const commands = packagePlan(['dist'], context)
  for (const failure of [
    { status: 7 },
    { status: null, signal: 'SIGTERM' },
    { error: Error('missing pwsh') },
  ]) {
    let calls = 0
    assert.throws(() =>
      executePackagePlan(commands, () => {
        calls++
        return failure
      }),
    )
    assert.equal(calls, 1)
  }
})

test('development package identities match Version.props and both lockfile identity fields', () => {
  assert.match(checkRepositoryVersion(), /^\d+\.\d+\.\d+-dev$/)
  const props =
    '<Project><VersionPrefix>1.2.3</VersionPrefix><VersionSuffix Condition="example">dev</VersionSuffix></Project>'
  for (const versions of [
    ['old', '1.2.3-dev', '1.2.3-dev'],
    ['1.2.3-dev', 'old', '1.2.3-dev'],
    ['1.2.3-dev', '1.2.3-dev', 'old'],
  ])
    assert.throws(() =>
      assertRepositoryVersions(
        props,
        { version: versions[0] },
        { version: versions[1], packages: { '': { version: versions[2] } } },
      ),
    )
})

test('ordinary npm commands stay frontend commands and secondary targets require explicit names', () => {
  const manifest = JSON.parse(readFileSync(resolve(context.cwd, 'package.json'), 'utf8'))
  assert.equal(manifest.scripts.dev, 'electron-vite dev --')
  assert.equal(manifest.scripts.preview, 'electron-vite preview --skipBuild --')
  assert.equal(manifest.scripts.package, 'node scripts/package-primary.mjs package')
  assert.equal(manifest.scripts.dist, 'node scripts/package-primary.mjs dist')
  for (const name of ['package:secondary', 'dist:secondary'])
    assert.match(manifest.scripts[name], /--publish never$/)
  assert.equal(
    readFileSync(resolve(context.cwd, 'src/renderer/index.html'), 'utf8').match(/<title>([^<]+)<\/title>/)[1],
    'Winnow',
  )
})
