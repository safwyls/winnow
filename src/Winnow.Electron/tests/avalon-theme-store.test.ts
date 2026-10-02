import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonThemeStore, EXAMPLE_AVALON_THEME } from '../src/main/avalon-theme-store'
import { parseAvalonTheme } from '../src/shared/avalonThemeDocument'

let directory: string, store: AvalonThemeStore
const text = (id = 'sample') =>
  JSON.stringify({ ...parseAvalonTheme('example.json', EXAMPLE_AVALON_THEME).document!, id })
beforeEach(async () => {
  directory = await mkdtemp(join(tmpdir(), 'winnow-palette-test-'))
  store = new AvalonThemeStore(async () => directory)
})
afterEach(async () => {
  store.dispose()
  await rm(directory, { recursive: true, force: true })
})

it('prepares a missing themes folder and leaves a working example on first run', async () => {
  const nested = join(directory, 'new', 'themes')
  store = new AvalonThemeStore(async () => nested)
  expect(await store.prepare()).toEqual([])
  const loaded = await store.load()
  expect(loaded.themes).toHaveLength(1)
  expect(loaded.themes[0].document.id).toBe('winnow-copy')
  expect(loaded.diagnostics).toEqual([])
})
it('does not seed or overwrite a folder containing any JSON file including a broken one', async () => {
  await writeFile(join(directory, 'mine.json'), '{')
  expect(await store.prepare()).toEqual([])
  expect(await readdir(directory)).toEqual(['mine.json'])
  expect(await readFile(join(directory, 'mine.json'), 'utf8')).toBe('{')
})
it('reports a broken or oversized file while loading the good top-level files only', async () => {
  await writeFile(join(directory, 'good.json'), text())
  await writeFile(join(directory, 'broken.json'), '{')
  await writeFile(join(directory, 'large.json'), ' '.repeat(256 * 1024 + 1))
  await writeFile(join(directory, 'ignore.txt'), text('not-json'))
  await mkdir(join(directory, 'nested'))
  await writeFile(join(directory, 'nested', 'hidden.json'), text('nested'))
  const loaded = await store.load()
  expect(loaded.themes.map((item) => item.document.id)).toEqual(['sample'])
  expect(loaded.diagnostics.map((item) => item.file)).toEqual(['broken.json', 'large.json'])
})
it('retains the first duplicate by case-insensitive file order and names its owner', async () => {
  await writeFile(join(directory, 'Z-last.json'), text())
  await writeFile(join(directory, 'a-first.json'), text())
  const loaded = await store.load()
  expect(loaded.themes).toHaveLength(1)
  expect(loaded.themes[0].file).toBe('a-first.json')
  expect(loaded.diagnostics[0]).toMatchObject({
    file: 'Z-last.json',
    field: 'id',
    severity: 'error',
    message: expect.stringContaining('a-first.json'),
  })
})
it('bounds the catalogue to 64 files and reports omitted files', async () => {
  await Promise.all(
    Array.from({ length: 67 }, (_, index) =>
      writeFile(join(directory, `${String(index).padStart(2, '0')}.json`), text(`palette-${index}`)),
    ),
  )
  const loaded = await store.load()
  expect(loaded.themes).toHaveLength(64)
  expect(loaded.diagnostics).toEqual([
    expect.objectContaining({
      file: 'themes folder',
      severity: 'warning',
      message: expect.stringContaining('64 of 67'),
    }),
  ])
})
it('exports a valid document and atomically preserves existing files during concurrent exports', async () => {
  const exported = await Promise.all([store.export(text()), store.export(text())])
  expect(exported.map((item) => item.file).sort()).toEqual(['sample-2.json', 'sample.json'])
  const first = await readFile(join(directory, 'sample.json'), 'utf8')
  expect(parseAvalonTheme('sample.json', first).document?.id).toBe('sample')
  await store.export(text())
  expect(await readFile(join(directory, 'sample.json'), 'utf8')).toBe(first)
  expect(await readdir(directory)).toHaveLength(3)
})
it('validates exports and reports inaccessible directories without writing elsewhere', async () => {
  expect((await store.export('null')).file).toBeNull()
  expect((await store.export(text('../escape'))).file).toBeNull()
  expect(await readdir(directory)).toEqual([])
  const blocked = join(directory, 'blocked')
  await writeFile(blocked, 'a file')
  store = new AvalonThemeStore(async () => join(blocked, 'themes'))
  expect((await store.prepare())[0].severity).toBe('warning')
  expect((await store.load()).themes).toEqual([])
  expect((await store.export(text())).file).toBeNull()
})
it('loads an absent folder without a problem and reports unavailable backend directory discovery', async () => {
  store = new AvalonThemeStore(async () => join(directory, 'missing'))
  expect(await store.load()).toEqual({ themes: [], diagnostics: [] })
  store = new AvalonThemeStore(async () => {
    throw Error('Offline')
  })
  expect((await store.prepare())[0].severity).toBe('warning')
  expect((await store.load()).diagnostics[0].severity).toBe('warning')
})
it('debounces a settled theme save and stops notifications after disposal', async () => {
  const changed = vi.fn()
  store = new AvalonThemeStore(async () => directory, changed)
  await store.prepare()
  await writeFile(join(directory, 'edited.json'), text())
  await writeFile(join(directory, 'edited.json'), text('edited'))
  await vi.waitFor(() => expect(changed).toHaveBeenCalledTimes(1), { timeout: 2500, interval: 25 })
  await new Promise((done) => setTimeout(done, 350))
  expect(changed).toHaveBeenCalledTimes(1)
  store.dispose()
  await writeFile(join(directory, 'edited.json'), text('after-disposal'))
  await new Promise((done) => setTimeout(done, 350))
  expect(changed).toHaveBeenCalledTimes(1)
})
