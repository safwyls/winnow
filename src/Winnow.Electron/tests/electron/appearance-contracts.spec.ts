import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { access, mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'
import { closeFixture } from './fixture-cleanup'
import type { ThemeProfile } from '../../src/shared/theme'

let app: ElectronApplication | undefined
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  await Promise.all([access(prebuiltBackend), access(prebuiltActivationHelper)])
})
test.beforeEach(async () => {
  app = undefined
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-appearance-contracts-'))
  await expect(access(join(directory, 'themes'))).rejects.toThrow()
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync', '--force-color-profile=srgb'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: prebuiltBackend,
      WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
    },
    chromiumSandbox: true,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible({ timeout: 30000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async () => {
  try {
    if (page && !page.isClosed() && test.info().status !== test.info().expectedStatus)
      await page.screenshot({ path: test.info().outputPath('before-teardown.png') })
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})
async function mode(fullscreen: boolean) {
  await app!.evaluate(({ BrowserWindow }, fullscreen) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setMinimumSize(0, 0)
    window.setContentSize(fullscreen ? 1920 : 1000, fullscreen ? 1080 : 640)
    window.isFullScreen = () => fullscreen
    window.webContents.send('winnow:fullscreen:changed', fullscreen)
    window.focus()
  }, fullscreen)
  await expect(page.locator(`.avalon-shell.${fullscreen ? 'fullscreen' : 'desktop'}`)).toBeVisible()
}
async function saved() {
  return page.evaluate(async () => {
    const presentation = await window.winnow.request({ route: 'preferences.presentation.get' })
    if (!presentation.ok) throw Error('Actual presentation preference read failed')
    return {
      profile: (await window.winnow.loadPreferences()) as ThemeProfile,
      presentation: presentation.data,
    }
  })
}
for (const surface of ['desktop', 'fullscreen'] as const)
  test(`${surface} fresh appearance keeps first choices collapsed warnings authored selection and restored scroll across reload`, async () => {
    await mode(surface === 'fullscreen')
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    const studio = page.locator('.theme-studio')
    await expect(studio).toBeVisible()
    await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue(
      process.platform === 'win32' ? '30' : '0',
    )
    await expect(page.getByRole('combobox', { name: 'Backdrop', exact: true })).toHaveValue('acrylic')
    await expect(page.getByRole('combobox', { name: 'Pane layout', exact: true })).toHaveValue('floating')
    await expect(page.getByRole('checkbox', { name: /Include content panes/ })).toBeChecked()
    const warnings = studio.locator('summary').filter({ hasText: /^Some themes may affect legibility\.$/ })
    await expect(warnings).toBeVisible()
    await expect(warnings.locator('..')).not.toHaveAttribute('open')
    await warnings.click()
    await expect(warnings.locator('..')).toHaveAttribute('open', '')
    await warnings.click()
    await expect(warnings.locator('..')).not.toHaveAttribute('open')
    const choice = page.getByRole('combobox', { name: /^Avalon palette/ })
    await expect(choice.locator('option[value="rose-pine-dawn"]')).toHaveCount(1)
    await expect(choice.locator('option[value="silkcircuit-dawn"]')).toHaveCount(1)
    await choice.selectOption('rose-pine-dawn')
    await expect(page.locator('html')).toHaveCSS('color-scheme', 'light')
    await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')
    await expect(warnings.locator('..')).not.toHaveAttribute('open')
    await warnings.scrollIntoViewIfNeeded()
    await page.screenshot({ path: test.info().outputPath(`${surface}-dawn-default-collapsed-notes.png`) })

    // Keep every authored color/default from the bundled source; only its identity names this local copy.
    const authored = JSON.parse(
      await readFile(resolve('src/renderer/themes/avalon/assets/rose-pine-dawn.json'), 'utf8'),
    )
    authored.id = 'native-authored'
    authored.name = 'Native authored Dawn'
    await mkdir(join(directory, 'themes'), { recursive: true })
    await writeFile(join(directory, 'themes/native-authored.json'), JSON.stringify(authored))
    await page.getByRole('button', { name: 'Reload authored palettes', exact: true }).click()
    await expect(choice.locator('option[value="native-authored"]')).toHaveCount(1)
    await choice.selectOption('native-authored')
    await expect.poll(async () => (await saved()).profile?.settings?.avalon?.palette).toBe('native-authored')
    await page.reload()
    await expect(page.locator('.avalon-shell')).toBeVisible()
    await mode(surface === 'fullscreen')
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await expect(choice).toHaveValue('native-authored')
    await expect(page.locator('html')).toHaveCSS('color-scheme', 'light')
    await expect(page.getByRole('combobox', { name: 'Backdrop', exact: true })).toHaveValue('acrylic')
    await expect(page.getByRole('combobox', { name: 'Pane layout', exact: true })).toHaveValue('floating')
    await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')

    const firstTheme = studio.locator('.studio-theme-option').first()
    await firstTheme.click()
    await expect(firstTheme).toBeFocused()
    const scroller = await firstTheme.evaluateHandle((node) => {
      for (let parent = node.parentElement; parent; parent = parent.parentElement)
        if (
          /(auto|scroll)/.test(getComputedStyle(parent).overflowY) &&
          parent.scrollHeight > parent.clientHeight
        )
          return parent
      throw Error('Appearance control has no real scrolling ancestor')
    })
    // Playwright enables document focus emulation. This proof needs native window focus instead.
    const nativeFocus = await page.context().newCDPSession(page)
    await nativeFocus.send('Emulation.setFocusEmulationEnabled', { enabled: false })
    const focusWindows = await app!.evaluate(async ({ BrowserWindow }) => {
      const primary = BrowserWindow.getAllWindows()[0]!
      const recipient = new BrowserWindow({
        width: 240,
        height: 160,
        title: 'Appearance focus recipient',
        webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false },
      })
      await recipient.loadURL(
        'data:text/html,<title>Appearance focus recipient</title><button>Focus recipient</button>',
      )
      recipient.focus()
      return { primary: primary.id, recipient: recipient.id }
    })
    await expect
      .poll(() =>
        app!.evaluate(
          ({ BrowserWindow }, ids) => ({
            primary: BrowserWindow.fromId(ids.primary)!.isFocused(),
            recipient: BrowserWindow.fromId(ids.recipient)!.isFocused(),
          }),
          focusWindows,
        ),
      )
      .toEqual({ primary: false, recipient: true })
    await expect.poll(() => page.evaluate(() => document.hasFocus())).toBe(false)
    const scrolled = await scroller.evaluate((node) => {
      node.scrollTop = 400
      return node.scrollTop
    })
    expect(scrolled).toBeGreaterThan(0)
    await app!.evaluate(({ BrowserWindow }, ids) => BrowserWindow.fromId(ids.primary)!.focus(), focusWindows)
    await expect.poll(() => page.evaluate(() => document.hasFocus())).toBe(true)
    await expect
      .poll(() =>
        app!.evaluate(
          ({ BrowserWindow }, ids) => BrowserWindow.fromId(ids.primary)!.isFocused(),
          focusWindows,
        ),
      )
      .toBe(true)
    await expect(firstTheme).toBeFocused()
    expect(await scroller.evaluate((node) => node.scrollTop)).toBe(scrolled)
    await app!.evaluate(
      ({ BrowserWindow }, ids) => BrowserWindow.fromId(ids.recipient)!.destroy(),
      focusWindows,
    )
    // Move to the preceding real control, restore the source offset, then enter with an actual Tab.
    await page.keyboard.press('Shift+Tab')
    await scroller.evaluate((node, offset) => {
      node.scrollTop = offset
    }, scrolled)
    await page.keyboard.press('Tab')
    await expect(firstTheme).toBeFocused()
    const revealed = await scroller.evaluate((node) => node.scrollTop)
    expect(revealed).toBeLessThan(scrolled)
    await expect(firstTheme).toBeInViewport()
    await test.info().attach('appearance-persistence-and-scroll', {
      body: JSON.stringify({ surface, scrolled, restored: scrolled, revealed, saved: await saved() }),
      contentType: 'application/json',
    })
    await page.screenshot({ path: test.info().outputPath(`${surface}-appearance-keyboard-revealed.png`) })
    await scroller.dispose()
    await nativeFocus.detach()
  })
