import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'

async function controller(page: Page) {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { setupController: typeof state }).setupController = state
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            touched: false,
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  const buttons = (pressed: number[]) =>
    page.evaluate(async (value) => {
      ;(window as unknown as { setupController: { pressed: number[] } }).setupController.pressed = value
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  return async (button: number) => {
    await buttons([])
    await buttons([button])
    await buttons([])
  }
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} setup keeps navigation visible, traps consent, resumes provider handoff and restores replay focus`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), `winnow-setup-${mode}-`))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [resolve('tests/electron/plugin-install-main.mjs'), '--data-dir', directory, '--no-sync'],
      env: Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>,
      chromiumSandbox: true,
      timeout: 60000,
    })
    const page = await application.firstWindow()
    const errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    try {
      await expect(page.getByRole('heading', { name: 'Welcome to Winnow' })).toBeVisible()
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]
        window.setFullScreen(false)
        window.setContentSize(mode === 'desktop' ? 1000 : 1280, mode === 'desktop' ? 640 : 720)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      const wizard = page.locator(`.setup-dialog.mode-${mode}`)
      await expect(wizard).toBeVisible()
      const tap = await controller(page)
      for (let step = 0; step < 9; step++) {
        await expect(wizard.getByText(`SETUP · ${step + 1} OF 9`)).toBeVisible()
        const next = wizard.getByRole('button', {
          name: step === 0 ? 'Get started' : step === 8 ? 'Open my library' : 'Continue',
          exact: true,
        })
        await expect(next).toBeFocused()
        const bounds = await wizard.evaluate((element) => {
          const targets = [...element.querySelectorAll('.setup-footer button, .setup-header h2')]
          return targets.map((target) => {
            const b = target.getBoundingClientRect()
            return b.top >= 0 && b.left >= 0 && b.bottom <= innerHeight && b.right <= innerWidth
          })
        })
        expect(bounds.every(Boolean)).toBe(true)
        if (step === 1) {
          const secret = wizard.getByLabel('Client secret')
          await expect(secret).toHaveAttribute('type', 'password')
          await secret.fill('unsaved-fixture-secret')
          if (mode === 'fullscreen') {
            await secret.focus()
            await tap(0)
            const keyboard = page.getByRole('dialog', { name: 'Enter text' })
            await expect(keyboard).toBeVisible()
            await keyboard.getByRole('button', { name: 'a', exact: true }).click()
            await expect(secret).toHaveValue('unsaved-fixture-secreta')
            await tap(1)
            await expect(keyboard).toHaveCount(0)
            await expect(secret).toBeFocused()
          }
        }
        if (step === 2) {
          await wizard.getByRole('button', { name: 'Sign in to Steam', exact: true }).click()
          const consent = page.getByRole('dialog', { name: 'Before you sign in' })
          for (let i = 0; i < 10; i++) {
            await page.keyboard.press('Tab')
            expect(await consent.evaluate((element) => element.contains(document.activeElement))).toBe(true)
          }
          if (mode === 'fullscreen') {
            await tap(9)
            await expect(page.getByRole('dialog', { name: 'Quick menu' })).toHaveCount(0)
            await tap(1)
          } else await page.keyboard.press('Escape')
          await expect(consent).toHaveCount(0)
          await expect(wizard.getByRole('heading', { name: 'Your Steam library' })).toBeVisible()
        }
        if (step === 4) {
          await application.evaluate(({ BrowserWindow }) =>
            BrowserWindow.getAllWindows()[0].webContents.send('winnow:activation', {
              kind: 'plugin',
              pluginId: 'xbox',
              releaseTag: 'v1.2.3',
            }),
          )
          const install = page.getByRole('region', { name: 'Plugin installation' })
          await expect(install).toBeVisible()
          await expect(wizard).toHaveCount(0)
          const progress = await page.evaluate(() =>
            window.winnow.request<{ step: number | null }>({ route: 'setup.get' }),
          )
          expect(progress.ok && progress.data?.step).toBe(4)
          await page.getByRole('button', { name: 'Resume setup', exact: true }).click()
          await expect(wizard.getByRole('heading', { name: 'Your GOG library' })).toBeVisible()
        }
        await page.screenshot({ path: info.outputPath(`setup-${mode}-${step}.png`) })
        await next.click()
      }
      await expect(wizard).toHaveCount(0)
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page.getByRole('button', { name: 'Application', exact: true }).click()
      const replay = page.getByRole('button', { name: 'Run setup again', exact: true })
      await replay.click()
      await expect(wizard.getByRole('heading', { name: 'Welcome to Winnow' })).toBeVisible()
      for (let i = 0; i < 12; i++) {
        await page.keyboard.press(i === 11 ? 'Shift+Tab' : 'Tab')
        expect(await wizard.evaluate((element) => element.contains(document.activeElement))).toBe(true)
      }
      await wizard.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await expect(wizard).toHaveCount(0)
      await expect(replay).toBeFocused()
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
}
