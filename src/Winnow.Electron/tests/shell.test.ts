import { afterEach, describe, expect, it } from 'vitest'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import {
  contentSecurityPolicy,
  containedPath,
  trustedRendererUrl,
  validateExternalUrl,
} from '../src/main/security'
import {
  installThemeDirectory,
  listThemePackages,
  profileDirectory,
  readProfile,
  saveProfile,
  themeFile,
  validateManifest,
} from '../src/main/storage'
import { backendProcessIsRunning, dataDirectoryArgument } from '../src/main/lifecycle'

const temporary: string[] = []
afterEach(async () => {
  for (const path of temporary) await rm(path, { recursive: true, force: true })
  temporary.length = 0
})
async function fixture() {
  const path = await mkdtemp(join(tmpdir(), 'winnow-electron-test-'))
  temporary.push(path)
  const source = join(path, 'source')
  await mkdir(source)
  const manifest = {
    id: 'reading-room',
    name: 'Reading room',
    version: '1.0.0',
    apiVersion: 1,
    entry: 'index.js',
    css: 'theme.css',
  }
  await writeFile(join(source, 'theme.json'), JSON.stringify(manifest))
  await writeFile(join(source, 'index.js'), 'export default () => null')
  await writeFile(join(source, 'theme.css'), '.theme { color: red }')
  return { path, source, manifest, themes: join(path, 'themes') }
}

describe('shell security', () => {
  it('requires explicit development argument values without silent fallback', () => {
    expect(dataDirectoryArgument(['--seed-sample'])).toBeUndefined()
    for (const args of [['--data-dir'], ['--data-dir', '--seed-sample'], ['--data-dir=']])
      expect(() => dataDirectoryArgument(args)).toThrow()
    expect(dataDirectoryArgument(['--data-dir=C:/Temp/isolated'])).toContain('isolated')
  })
  it('recognizes a still-running discovered process before deciding to start another backend', async () => {
    const discover = async () => ({
      address: 'http://127.0.0.1:1/',
      token: 'hidden',
      epoch: 'epoch',
      apiVersion: '1',
      processId: process.pid,
    })
    expect(await backendProcessIsRunning(discover)).toBe(true)
    expect(
      await backendProcessIsRunning(async () => {
        throw new Error('No discovery yet')
      }),
    ).toBe(false)
  })
  it('limits IPC sender origins and external protocols', () => {
    expect(trustedRendererUrl('winnow-app://app/index.html')).toBe(true)
    for (const url of [
      'https://example.com/',
      'file:///index.html',
      'winnow-app://other/index.html',
      'winnow-app://app/evil.html',
    ])
      expect(trustedRendererUrl(url)).toBe(false)
    expect(trustedRendererUrl('http://127.0.0.1:5173/', 'http://127.0.0.1:5173/')).toBe(true)
    expect(trustedRendererUrl('http://127.0.0.1:5174/', 'http://127.0.0.1:5173/')).toBe(false)
    expect(validateExternalUrl('https://store.steampowered.com/app/10/')).toContain('https:')
    expect(validateExternalUrl('steam://store/10')).toBe('steam://store/10')
    expect(validateExternalUrl('steam://nav/games/details/10')).toBe('steam://nav/games/details/10')
    expect(validateExternalUrl('http://example.com/article')).toBe('http://example.com/article')
    for (const url of [
      'file:///etc/passwd',
      'javascript:alert(1)',
      'steam://run/10',
      'steam://install/10',
      'steam://uninstall/10',
      'steam://nav/games/details/10?run=1',
      'steam://nav/games/details/10#run',
      'steam://nav/games/details/10/extra',
      'steam://nav/games/details/not-an-id',
      'steam://nav/games/details/12345678901',
      'steam://nav:123/games/details/10',
      'steam://nav/console',
      'https://user:password@example.com/',
      'https://localhost:443/',
    ])
      expect(() => validateExternalUrl(url)).toThrow()
    const production = contentSecurityPolicy()
    expect(production).toContain("script-src 'self' winnow-theme:;")
    expect(production).not.toContain('unsafe-eval')
    expect(production).toContain("frame-src 'none'")
  })
  it('contains paths and separates preferences for different libraries', () => {
    for (const path of ['../outside', 'folder/../../outside', '/absolute', 'folder\\..\\outside'])
      expect(() => containedPath('C:/Temp/package', path)).toThrow()
    expect(profileDirectory('C:/UserData', 'C:/Library/A')).not.toBe(
      profileDirectory('C:/UserData', 'C:/Library/B'),
    )
  })
})

describe('theme package installation', () => {
  it('installs an independent copy, lists versioned URLs and serves bounded local assets', async () => {
    const f = await fixture()
    const installed = await installThemeDirectory(f.source, f.themes)
    expect(installed.entry).toBe('winnow-theme://reading-room/index.js?v=1.0.0')
    await writeFile(join(f.source, 'index.js'), 'changed after import')
    expect((await themeFile(f.themes, installed.entry)).bytes.toString()).toBe('export default () => null')
    expect(await listThemePackages(f.themes)).toEqual([installed])
    expect((await themeFile(f.themes, installed.css!)).type).toBe('text/css')
    await expect(themeFile(f.themes, 'winnow-theme://reading-room/..%2f..%2fprivate.txt')).rejects.toThrow()
  })
  it('rejects incompatible manifests, path escapes and executable payloads', async () => {
    const f = await fixture()
    for (const change of [
      { apiVersion: 2 },
      { id: '../evil' },
      { id: 'com1' },
      { entry: '../outside.js' },
      { entry: '/absolute.js' },
      { entry: 'https://example.com/code.js' },
      { css: 'style.html' },
    ])
      expect(() => validateManifest({ ...f.manifest, ...change })).toThrow()
    await writeFile(join(f.source, 'run.exe'), 'pretend executable')
    await expect(installThemeDirectory(f.source, f.themes)).rejects.toThrow(/Unsupported/)
  })
  it('persists local profiles atomically and bounds imports', async () => {
    const f = await fixture()
    const path = join(f.path, 'settings', 'preferences.json')
    await saveProfile(path, { schemaVersion: 1, themeId: 'afterglow' })
    await saveProfile(path, { schemaVersion: 1, themeId: 'index' })
    expect(await readProfile(path)).toEqual({ schemaVersion: 1, themeId: 'index' })
    await expect(saveProfile(path, { text: 'x'.repeat(600 * 1024) })).rejects.toThrow()
    expect(JSON.parse(await readFile(path, 'utf8')).themeId).toBe('index')
  })
})
