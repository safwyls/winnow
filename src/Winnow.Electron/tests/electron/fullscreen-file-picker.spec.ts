import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'

let application: ElectronApplication, page: Page, directory: string, documents: string
const errors: string[] = []
test.beforeEach(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-picker-'))
  documents = join(directory, 'documents')
  await mkdir(documents)
  await writeFile(join(documents, 'report.csv'), 'keep this until export succeeds')
  await writeFile(join(documents, 'cover.PNG'), 'read-only selection fixture')
  await writeFile(join(documents, 'unrelated.exe'), 'never execute')
  errors.length = 0
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-file-picker-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async () => {
  await closeFixture(application, directory)
  expect(errors).toEqual([])
})
async function fullscreen() {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setContentSize(1920, 1080)
    window.isFullScreen = () => true
    window.webContents.send('winnow:fullscreen:changed', true)
    window.focus()
  })
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { pickerPad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
        },
      ],
    })
  })
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as unknown as { pickerPad: { pressed: number[] } }).pickerPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function start(
  operation: 'acquisitions' | 'artwork' | 'profile-import' | 'profile-export' | 'executable' | 'theme',
) {
  await page.evaluate((operation) => {
    const bridge = window.winnow
    const testWindow = window as unknown as { pickerResult: unknown; pickerSettled: boolean }
    testWindow.pickerSettled = false
    const result =
      operation === 'acquisitions'
        ? bridge.exportAcquisitions!()
        : operation === 'artwork'
          ? bridge.importArtwork!({ workId: 1, slot: 'Cover', revision: 'A'.repeat(64) })
          : operation === 'profile-import'
            ? bridge.importProfile()
            : operation === 'profile-export'
              ? bridge.exportProfile({})
              : operation === 'executable'
                ? bridge.chooseManualExecutable!()
                : bridge.installTheme()
    void result.then((value) => {
      testWindow.pickerResult = value
      testWindow.pickerSettled = true
    })
  }, operation)
}
const settled = () => page.evaluate(() => (window as unknown as { pickerSettled: boolean }).pickerSettled)

async function assertPickerHints(view: 'open' | 'save' | 'replace') {
  const hints = page.getByRole('group', { name: 'File chooser controls' })
  await expect(hints).toContainText('Browse')
  await expect(hints).toContainText('Select')
  await expect(hints).toContainText(view === 'replace' ? 'Back' : 'Cancel')
  for (const glyph of ['D-pad', 'A', 'B'])
    await expect(hints.locator(`[data-picker-glyph="${glyph}"] svg`)).toBeVisible()
  if (view === 'save') {
    await expect(hints).toContainText('Keyboard')
    await expect(hints.locator('[data-picker-glyph="Y"] svg')).toBeVisible()
  } else await expect(hints.locator('[data-picker-glyph="Y"]')).toHaveCount(0)
  expect(
    await hints.evaluate((element) => {
      const bounds = element.getBoundingClientRect()
      return bounds.left >= 0 && bounds.top >= 0 && bounds.right <= innerWidth && bounds.bottom <= innerHeight
    }),
  ).toBe(true)
}

test('fullscreen acquisitions export keeps existing CSV untouched until Cancel-first replacement is explicitly accepted', async ({}, info) => {
  await fullscreen()
  await start('acquisitions')
  const picker = page.getByRole('dialog', { name: 'Export acquisitions', exact: true })
  const filename = picker.getByRole('textbox', { name: 'File name' })
  await expect(filename).toBeFocused()
  await assertPickerHints('save')
  await page.screenshot({ path: info.outputPath('fullscreen-save-file.png'), animations: 'disabled' })
  await assertAccessibleControls(page, picker)
  await assertDirectionalReachability(page, picker, tap)
  await filename.focus()
  await filename.fill('report.csv')
  await tap(13)
  await expect(picker.getByRole('button', { name: 'Save here' })).toBeFocused()
  await tap(0)
  const confirmation = page.getByRole('dialog', { name: 'Replace report.csv?' })
  await expect(confirmation.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  await assertPickerHints('replace')
  await assertAccessibleControls(page, confirmation)
  await assertDirectionalReachability(page, confirmation, tap)
  await confirmation.getByRole('button', { name: 'Cancel', exact: true }).focus()
  expect(await settled()).toBe(false)
  expect(await readFile(join(documents, 'report.csv'), 'utf8')).toBe('keep this until export succeeds')
  await page.screenshot({ path: info.outputPath('fullscreen-replace-file.png'), animations: 'disabled' })
  await tap(0)
  await expect(filename).toBeFocused()
  expect(await settled()).toBe(false)
  expect(await readFile(join(documents, 'report.csv'), 'utf8')).toBe('keep this until export succeeds')
  await tap(13)
  await tap(0)
  await expect(confirmation.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  await tap(13)
  await expect(confirmation.getByRole('button', { name: 'Replace file' })).toBeFocused()
  await tap(0)
  await expect.poll(settled).toBe(true)
  await expect(page.locator('.fullscreen-file-picker')).toHaveCount(0)
  expect(await readFile(join(documents, 'report.csv'), 'utf8')).not.toBe('keep this until export succeeds')
  expect(
    await application.evaluate(() => (globalThis as unknown as { pickerDialogs: unknown[] }).pickerDialogs),
  ).toEqual([])
})

test('fullscreen artwork filters extensions and Back cancels all shared chooser routes while preserving selected files', async ({}, info) => {
  await fullscreen()
  await start('artwork')
  const picker = page.locator('.fullscreen-file-picker')
  await expect(picker.getByRole('button', { name: 'File cover.PNG' })).toBeVisible()
  await assertPickerHints('open')
  await expect(picker.getByRole('button', { name: /unrelated.exe/ })).toHaveCount(0)
  await expect(picker.getByRole('button', { name: 'Parent folder' })).toBeFocused()
  await assertAccessibleControls(page, picker)
  await assertDirectionalReachability(page, picker, tap)
  await picker.getByRole('button', { name: 'Parent folder' }).focus()
  await tap(13)
  await expect(picker.getByRole('button', { name: 'File cover.PNG' })).toBeFocused()
  await page.screenshot({
    path: info.outputPath('fullscreen-artwork-file-picker.png'),
    animations: 'disabled',
  })
  await tap(0)
  await expect.poll(settled).toBe(true)
  await expect(picker).toHaveCount(0)
  expect(await readFile(join(documents, 'cover.PNG'), 'utf8')).toBe('read-only selection fixture')
  expect(await readFile(join(documents, 'unrelated.exe'), 'utf8')).toBe('never execute')
  for (const operation of ['artwork', 'profile-import', 'profile-export', 'executable', 'theme'] as const) {
    await start(operation)
    await expect(picker).toBeVisible()
    await tap(1)
    await expect.poll(settled).toBe(true)
    await expect(picker).toHaveCount(0)
  }
  expect(
    await application.evaluate(() => (globalThis as unknown as { pickerDialogs: unknown[] }).pickerDialogs),
  ).toEqual([])
})

test('desktop file routes retain native chooser filters and cancelling leaves fixture files unchanged', async () => {
  for (const operation of [
    'artwork',
    'profile-import',
    'profile-export',
    'executable',
    'theme',
    'acquisitions',
  ] as const) {
    await start(operation)
    await expect.poll(settled).toBe(true)
    await expect(page.locator('.fullscreen-file-picker')).toHaveCount(0)
  }
  const dialogs = await application.evaluate(
    () => (globalThis as unknown as { pickerDialogs: Record<string, unknown>[] }).pickerDialogs,
  )
  expect(dialogs).toEqual([
    expect.objectContaining({
      title: 'Choose artwork',
      filters: [{ name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif', 'bmp'] }],
    }),
    expect.objectContaining({
      title: 'Import appearance profile',
      filters: [{ name: 'Winnow appearance profile', extensions: ['json'] }],
    }),
    expect.objectContaining({
      title: 'Export appearance profile',
      defaultPath: 'winnow-appearance.json',
      filters: [{ name: 'JSON', extensions: ['json'] }],
    }),
    expect.objectContaining({
      title: 'Choose game executable',
      filters: [{ name: 'Executable', extensions: ['exe'] }],
    }),
    expect.objectContaining({ title: 'Choose a Winnow theme folder', properties: ['openDirectory'] }),
    expect.objectContaining({
      title: 'Export acquisitions',
      defaultPath: 'winnow-acquisitions.csv',
      filters: [{ name: 'CSV', extensions: ['csv'] }],
    }),
  ])
  expect(await readFile(join(documents, 'report.csv'), 'utf8')).toBe('keep this until export succeeds')
  expect(await readFile(join(documents, 'cover.PNG'), 'utf8')).toBe('read-only selection fixture')
})

test('Back from the fullscreen artwork chooser restores its actual temporarily disabled Choose file opener', async () => {
  await fullscreen()
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.locator('.avalon-cover').first().click()
  await page.locator('.avalon-details').getByRole('button', { name: 'More', exact: true }).click()
  await page
    .getByRole('dialog', { name: 'More game actions', exact: true })
    .getByRole('button', { name: 'Artwork…', exact: true })
    .click()
  const artwork = page.locator('.artwork-browser-dialog')
  const opener = artwork.getByRole('button', { name: 'Choose file', exact: true, includeHidden: true })
  await expect(opener).toBeEnabled()
  await opener.focus()
  await tap(0)
  await expect(page.locator('.fullscreen-file-picker')).toBeVisible()
  await expect(opener).toBeDisabled()
  await tap(1)
  await expect(page.locator('.fullscreen-file-picker')).toHaveCount(0)
  await expect(opener).toBeEnabled()
  await expect(opener).toBeFocused()
  expect(await readFile(join(documents, 'cover.PNG'), 'utf8')).toBe('read-only selection fixture')
})
