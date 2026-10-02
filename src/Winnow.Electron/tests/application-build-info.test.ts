import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it, vi } from 'vitest'
import { applicationBuildInfo, fromInformationalVersion } from '../src/main/application-build-info'
import { buildInformationalVersion } from '../build/application-metadata'
import packageMetadata from '../package.json'

afterEach(() => vi.unstubAllGlobals())

it.each([
  ['0.1.0-beta.1+abc123', '0.1.0-beta.1', 'abc123'],
  ['0.1.0-ci.42+abc123', '0.1.0-ci.42', 'abc123'],
  ['0.1.0-dev', '0.1.0-dev', 'Unavailable'],
  ['1.2.3', '1.2.3', 'Unavailable'],
  [null, 'Unknown', 'Unavailable'],
])('Preserves_release_identity_and_handles_builds_without_git (%s)', (input, version, commit) => {
  expect(fromInformationalVersion(input)).toEqual({ version, commit })
})

it('uses embedded frontend identity in development and the installed package version after packaging', () => {
  vi.stubGlobal('__WINNOW_BUILD_INFORMATIONAL_VERSION__', '0.1.0-ci.42+abc123')
  expect(applicationBuildInfo(false, '44.4.5')).toEqual({ version: '0.1.0-ci.42', commit: 'abc123' })
  expect(applicationBuildInfo(true, '0.1.0-beta.1')).toEqual({ version: '0.1.0-beta.1', commit: 'abc123' })
})

it('keeps the real frontend package version when imported without bundler metadata', () => {
  expect(applicationBuildInfo(false, '44.4.5')).toEqual({
    version: packageMetadata.version,
    commit: 'Unavailable',
  })
  expect(applicationBuildInfo(false).version).not.toBe('Unknown')
})

it('builds source archives without Git using their package version and an unavailable commit', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-build-info-'))
  try {
    await writeFile(join(directory, 'package.json'), JSON.stringify({ version: '0.1.0-dev' }))
    expect(fromInformationalVersion(buildInformationalVersion(directory))).toEqual({
      version: '0.1.0-dev',
      commit: 'Unavailable',
    })
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})

it('embeds an explicit release identity without changing package metadata or requiring Git', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-release-info-'))
  try {
    const original = JSON.stringify({ version: '0.1.0' })
    await writeFile(join(directory, 'package.json'), original)
    expect(
      buildInformationalVersion(directory, {
        WINNOW_BUILD_VERSION: '0.2.0-beta.2',
        WINNOW_BUILD_COMMIT: 'ABCDEF0123456789ABCDEF0123456789ABCDEF01',
      }),
    ).toBe('0.2.0-beta.2+abcdef0123456789abcdef0123456789abcdef01')
    const { readFile } = await import('node:fs/promises')
    expect(await readFile(join(directory, 'package.json'), 'utf8')).toBe(original)
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})

it.each([
  { WINNOW_BUILD_VERSION: '0.2.0' },
  { WINNOW_BUILD_COMMIT: 'a'.repeat(40) },
  { WINNOW_BUILD_VERSION: '' },
  ...['v0.2.0', '0.2.0+source', '0.02.0', '0.2.0-beta.01', '65536.2.0', '0.2.0\n'].map((version) => ({
    WINNOW_BUILD_VERSION: version,
    WINNOW_BUILD_COMMIT: 'a'.repeat(40),
  })),
  { WINNOW_BUILD_VERSION: '0.2.0', WINNOW_BUILD_COMMIT: 'short' },
  { WINNOW_BUILD_VERSION: '0.2.0', WINNOW_BUILD_COMMIT: 'a'.repeat(64) },
])('refuses malformed or incomplete release overrides before building (%j)', (environment) => {
  expect(() => buildInformationalVersion('does-not-need-a-checkout', environment)).toThrow('valid version')
})
