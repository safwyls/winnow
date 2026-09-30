import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'

let application: ElectronApplication, page: Page, directory: string, actionProbe: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-modal-accessibility-'))
  const output = join(directory, 'dynamic-actions.js')
  await build({
    entryPoints: [resolve('tests/electron/modal-accessibility-probe.tsx')],
    outfile: output,
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    define: { 'process.env.NODE_ENV': '"production"' },
  })
  actionProbe = await readFile(output, 'utf8')
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/modal-accessibility-main.mjs'),
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
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('modal-failure', { body: await page.screenshot(), contentType: 'image/png' })
  expect(errors).toEqual([])
})
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).modalPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function surface(mode: 'desktop' | 'fullscreen') {
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 800 : 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { modalPad: state })
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
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} combined list prompt keeps accessible names and controller routes through empty busy and error states`, async ({}, info) => {
    await surface(mode)
    await page.locator('.avalon-cover').first().click()
    if (mode === 'fullscreen') await page.getByRole('button', { name: 'More', exact: true }).click()
    await page.getByRole('button', { name: 'Add to list', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: /^Add .* to a list$/ })
    await expect(dialog.getByRole('button', { name: 'Existing list', exact: true })).toBeVisible()
    const create = dialog.getByRole('button', { name: 'New list', exact: true })
    await expect(create).toBeDisabled()
    await assertAccessibleControls(page, dialog)
    await assertDirectionalReachability(page, dialog, tap)
    await dialog.getByRole('textbox', { name: 'New list name' }).fill('New list')
    await create.focus()
    await tap(0)
    await expect(dialog.getByRole('status')).toHaveText('Saving list changes…')
    await expect
      .poll(() => application.evaluate(() => typeof (globalThis as any).__modalAccessibility.releaseList))
      .toBe('function')
    expect(
      await dialog
        .locator('button,input')
        .evaluateAll((nodes) => nodes.every((node) => (node as HTMLButtonElement).disabled)),
    ).toBe(true)
    await assertAccessibleControls(page, dialog)
    await tap(1)
    await expect(dialog).toBeVisible()
    await application.evaluate(() => (globalThis as any).__modalAccessibility.releaseList())
    await expect(dialog.getByRole('alert')).toContainText('Could not save this list.')
    expect(await dialog.ariaSnapshot()).toContain('Could not save this list.')
    await assertAccessibleControls(page, dialog)
    await assertDirectionalReachability(page, dialog, tap)
    await page.screenshot({ path: info.outputPath(`${mode}-list-error.png`) })
    await tap(1)
    await expect(dialog).toHaveCount(0)
  })
  for (const origin of ['activity', 'details'] as const)
    test(`${mode} ${origin} journal editor exposes names validation keyboard action and origin focus`, async ({}, info) => {
      await surface(mode)
      let trigger
      if (origin === 'activity') {
        await page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: /Activity|Journal/, exact: true })
          .click()
        trigger = page.getByRole('button', { name: 'Add selected note', exact: true })
      } else {
        await page.locator('.avalon-cover').first().click()
        if (mode === 'fullscreen') await page.locator('[data-details-reading="History"]').click()
        else await page.getByRole('tab', { name: 'Activity', exact: true }).click()
        trigger = page
          .locator('.timeline-entry')
          .getByRole('button', { name: 'Journal', exact: true })
          .first()
      }
      await trigger.click()
      const dialog = page.getByRole('dialog', { name: 'Remember this session' })
      const note = dialog.getByRole('textbox', { name: 'Your note', exact: true })
      await expect(note).toBeVisible()
      if (mode === 'fullscreen') {
        await expect(note).toHaveCSS('font-size', '28px')
        await expect(note).toHaveCSS('min-height', '240px')
      }
      await assertAccessibleControls(page, dialog)
      await assertDirectionalReachability(page, dialog, tap)
      if (mode === 'fullscreen') await dialog.getByRole('button', { name: 'Edit note', exact: true }).focus()
      else await note.focus()
      await tap(0)
      await expect(page.getByRole('dialog', { name: 'Enter text' })).toBeVisible()
      await tap(1)
      await expect(note).toBeFocused()
      await dialog.getByRole('combobox', { name: 'Your rating', exact: true }).selectOption('0')
      await dialog.getByRole('button', { name: 'Save note', exact: true }).focus()
      await tap(0)
      await expect(dialog.getByRole('alert')).toHaveText('Add a note or rating, or delete this entry.')
      expect(await dialog.ariaSnapshot()).toContain('Add a note or rating, or delete this entry.')
      await assertAccessibleControls(page, dialog)
      await assertDirectionalReachability(page, dialog, tap)
      await page.screenshot({ path: info.outputPath(`${mode}-${origin}-journal-validation.png`) })
      if (mode === 'fullscreen') {
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0]!.setContentSize(1280, 720),
        )
        await page.evaluate(() =>
          document.documentElement.style.setProperty('--fullscreen-text-scale', '1.4'),
        )
        await assertDirectionalReachability(page, dialog, tap)
        for (const control of await dialog.locator('button,textarea,select').all()) {
          await control.focus()
          await control.scrollIntoViewIfNeeded()
          const bounds = (await control.boundingBox())!
          expect(bounds.x).toBeGreaterThanOrEqual(0)
          expect(bounds.y).toBeGreaterThanOrEqual(0)
          expect(bounds.x + bounds.width).toBeLessThanOrEqual(1281)
          expect(bounds.y + bounds.height).toBeLessThanOrEqual(721)
        }
        await page.screenshot({ path: info.outputPath(`fullscreen-${origin}-journal-1280-text140.png`) })
      }
      await tap(1)
      await expect(dialog).toHaveCount(0)
      await expect(trigger).toBeFocused()
    })
}
test('fullscreen Back from a dynamic action modal restores the invoking Controller settings control in the real shell', async ({}, info) => {
  await surface('fullscreen')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Controller', exact: true })
    .click()
  const origin = page
    .getByRole('navigation', { name: 'Settings section' })
    .getByRole('button', { name: 'Controller', exact: true })
  await origin.focus()
  await tap(13)
  await page.evaluate(() => document.activeElement!.setAttribute('data-modal-origin', 'true'))
  await application.evaluate(
    ({ BrowserWindow }, code) => BrowserWindow.getAllWindows()[0]!.webContents.executeJavaScript(code),
    actionProbe,
  )
  const dialog = page.getByRole('dialog', { name: 'Choose an action' })
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
  await expect(dialog.getByRole('button', { name: 'Unavailable', exact: true })).toBeDisabled()
  await assertAccessibleControls(page, dialog)
  await assertDirectionalReachability(page, dialog, tap)
  await page.screenshot({ path: info.outputPath('fullscreen-dynamic-actions.png') })
  await tap(1)
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('[data-modal-origin]')).toBeFocused()
  await expect(
    page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Controller', exact: true }),
  ).toHaveAttribute('aria-pressed', 'true')
})
