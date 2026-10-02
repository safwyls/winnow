import { test, expect, _electron as electron, type ElectronApplication } from '@playwright/test'
import electronPath from 'electron'
import { build } from 'esbuild'
import { createHash } from 'node:crypto'
import { mkdtemp, mkdir } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'

let app: ElectronApplication
let directory: string
const modulePath = resolve('../..', '.tmp/task38124-jump-list-native/helpers.mjs')

test.beforeAll(async () => {
  await mkdir(dirname(modulePath), { recursive: true })
  await build({
    stdin: {
      contents: "export * from './src/main/jump-list'; export * from './src/main/jump-list-icons'",
      resolveDir: process.cwd(),
    },
    outfile: modulePath,
    bundle: true,
    platform: 'node',
    format: 'esm',
    packages: 'external',
  })
})
test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-jump-list-native-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/jump-list-native-main.mjs'),
      '--data-dir',
      directory,
      '--force-color-profile=srgb',
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_JUMP_LIST_MODULE: modulePath,
    },
    chromiumSandbox: true,
  })
  await app.firstWindow()
  await expect.poll(() => app.evaluate(() => Boolean((globalThis as any).__jumpListNative))).toBe(true)
})
test.afterEach(async () => {
  await app?.close()
})

test('source 80 by 160 cover encodes six square PNG ICO frames from its 80 by 80 green center', async () => {
  const result = await app.evaluate(() => (globalThis as any).__jumpListNative.encodeSource())
  expect({ reserved: result.reserved, type: result.type, count: result.count }).toEqual({
    reserved: 0,
    type: 1,
    count: 6,
  })
  for (const [index, size] of [16, 24, 32, 48, 64, 128].entries()) {
    const frame = result.frames[index]
    expect(frame).toMatchObject({
      width: size,
      height: size,
      planes: 1,
      bits: 32,
      decoded: { width: size, height: size },
      centerBGRA: [0, 255, 0, 255],
      pngSignature: [137, 80, 78, 71, 13, 10, 26, 10],
    })
    expect(frame.offset).toBe(
      index === 0 ? 102 : result.frames[index - 1].offset + result.frames[index - 1].length,
    )
  }
  await test.info().attach('source-icon-frame-ledger', {
    body: Buffer.from(JSON.stringify(result)),
    contentType: 'application/json',
  })
})

test('source 64 by 96 blue test game icon is content addressed once under the selected root and missing art returns null', async () => {
  const result = await app.evaluate(() => (globalThis as any).__jumpListNative.cacheSource())
  expect(result.first).not.toBeNull()
  expect(dirname(result.first)).toBe(join(directory, 'jump-list-icons'))
  expect(result.second).toBe(result.first)
  expect(result.files).toEqual([basename(result.first)])
  expect(basename(result.first)).toBe(
    `${createHash('sha256').update(Buffer.from(result.bytes, 'base64')).digest('hex').toUpperCase()}.ico`,
  )
  expect(result.missingKey).toBeNull()
  expect(result.missingArt).toBeNull()
  expect(result.changed).not.toBe(result.first)
  expect(result.changedAgain).toBe(result.changed)
  expect(result.filesAfterChange.sort()).toEqual([basename(result.first), basename(result.changed)].sort())
  delete result.bytes
  await test.info().attach('source-icon-cache-ledger', {
    body: Buffer.from(JSON.stringify(result)),
    contentType: 'application/json',
  })
})

test('actual Windows Jump List publishes then clears only its isolated source smoke identity', async () => {
  test.skip(process.platform !== 'win32', 'Windows shell integration')
  const result = await app.evaluate(() => (globalThis as any).__jumpListNative.publishSource())
  expect(result.directory).toBe(directory)
  expect(basename(directory)).toMatch(/^winnow-electron-jump-list-native-/)
  expect(result.identity).toMatch(/^Winnow\.Electron\.[a-f0-9]{16}$/)
  expect(result.recent.title).toBe('Jump List smoke game')
  expect(result.recent.args).toContain('--jump-list-game 42 --data-dir ')
  expect(result.recent.args).toContain(directory)
  expect(result.recent.args).toContain(resolve('tests/electron/jump-list-native-main.mjs'))
  expect(result.fullscreen.args).toContain('--jump-list-fullscreen --data-dir ')
  expect(result.fullscreen.args).toContain(directory)
  // The source COM implementation permits Windows privacy to reject only custom destinations.
  expect(['ok', 'custom-category-access-denied']).toContain(result.published)
  expect(result.empty).toBe('ok')
  expect(result.cleared).toBe('ok')
  await test.info().attach('source-native-jump-list-ledger', {
    body: Buffer.from(JSON.stringify(result)),
    contentType: 'application/json',
  })
})
