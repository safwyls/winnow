import * as React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { mkdtemp, mkdir, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { tmpdir } from 'node:os'
import { pathToFileURL } from 'node:url'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { installThemeDirectory, listThemePackages, themeFile, validateManifest } from '../src/main/storage'
import { DEFAULT_PROFILE, validateThemeDefinition, type ThemeContext } from '../src/shared/theme'
import { installThemeSDK } from '../src/renderer/theming/runtime'

const manifest = {
  apiVersion: 1,
  id: 'reading-room',
  name: 'Reading room',
  version: '1.0.0',
  entry: 'index.mjs',
  css: 'style.css',
}
const temporary: string[] = []
async function temp(): Promise<string> {
  const directory = await realpath(await mkdtemp(join(tmpdir(), 'winnow-theme-test-')))
  temporary.push(directory)
  return directory
}
afterEach(async () => {
  vi.unstubAllGlobals()
  for (const directory of temporary.splice(0)) {
    // Every path was returned by mkdtemp directly beneath the system temporary directory.
    if (!resolve(directory).startsWith(resolve(tmpdir())))
      throw new Error('Refusing to remove a directory outside the temporary root.')
    await rm(directory, { recursive: true, force: true })
  }
})

describe('installed developer themes', () => {
  it.each([
    { apiVersion: 2 },
    { id: 'afterglow' },
    { id: 'con' },
    { id: '../outside' },
    { version: 'latest' },
    { entry: '../outside.mjs' },
    { entry: '/absolute.mjs' },
    { entry: 'https://example.com/theme.mjs' },
    { entry: 'entry.html' },
    { css: 'nested/../../outside.css' },
  ])('rejects incompatible manifests and invalid asset paths: %j', (patch) => {
    expect(() => validateManifest({ ...manifest, ...patch })).toThrow()
  })

  it('installs and serves the independent example through the public package contract', async () => {
    const themesRoot = join(await temp(), 'themes')
    const example = resolve('examples/themes/reading-room')
    const installed = await installThemeDirectory(example, themesRoot)
    expect(installed.id).toBe('reading-room')
    expect(installed.entry).toBe('winnow-theme://reading-room/index.mjs?v=1.0.0')
    expect(await listThemePackages(themesRoot)).toEqual([installed])
    const moduleFile = await themeFile(themesRoot, installed.entry)
    expect(moduleFile.type).toBe('text/javascript')
    expect(moduleFile.bytes.toString()).toContain('window.WinnowThemeSDK')
    const stylesheet = await themeFile(themesRoot, installed.css!)
    expect(stylesheet.type).toBe('text/css')
    vi.stubGlobal('window', {})
    installThemeSDK()
    const imported = await import(
      /* @vite-ignore */ pathToFileURL(join(themesRoot, 'reading-room', 'index.mjs')).href
    )
    const definition = validateThemeDefinition(imported.default, 'reading-room')
    const context: ThemeContext = {
      mode: 'desktop',
      page: 'discover',
      selectedWorkId: null,
      games: [],
      feed: undefined,
      loading: false,
      profile: structuredClone(DEFAULT_PROFILE),
      children: React.createElement('p', null, 'Host details screen'),
      setPage: () => {},
      openGame: () => {},
      toggleFullscreen: () => {},
      renderScreen: () => null,
      actions: { launch: async () => {} },
      components: { GameCard: () => null, Impression: ({ children }) => children, Artwork: () => null },
    }
    const shell = renderToStaticMarkup(React.createElement(definition.Shell!, context))
    expect(shell).toContain('Reading room')
    expect(shell).toContain('Host details screen')
    const discover = renderToStaticMarkup(React.createElement(definition.Discover!, context))
    expect(discover).toContain('A quiet desk')
    const library = renderToStaticMarkup(React.createElement(definition.Library!, context))
    expect(library).toContain('The index')
    expect(definition.Details).toBeUndefined()
  })

  it('keeps the installed theme intact when a replacement package is incomplete', async () => {
    const root = await temp()
    const themesRoot = join(root, 'themes')
    const existing = await installThemeDirectory(resolve('examples/themes/reading-room'), themesRoot)
    const before = await readFile(join(themesRoot, 'reading-room', 'index.mjs'), 'utf8')
    const incomplete = join(root, 'incomplete')
    await mkdir(incomplete)
    await writeFile(join(incomplete, 'theme.json'), JSON.stringify({ ...manifest, version: '2.0.0' }))
    await expect(installThemeDirectory(incomplete, themesRoot)).rejects.toThrow('missing')
    expect(await readFile(join(themesRoot, 'reading-room', 'index.mjs'), 'utf8')).toBe(before)
    expect(await listThemePackages(themesRoot)).toEqual([existing])
  })
})
