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
