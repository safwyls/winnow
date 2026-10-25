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
        window.setMinimumSize(0, 0)
        window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 604 : 1080)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      const wizard = page.locator(`.setup-dialog.mode-${mode}`)
      await expect(wizard).toBeVisible()
      const tap = await controller(page)
      await expect
        .poll(() => page.evaluate(() => [innerWidth, innerHeight]))
        .toEqual(mode === 'desktop' ? [1200, 604] : [1920, 1080])
      for (let step = 0; step < 9; step++) {
        await expect(wizard.getByText(`SETUP · ${step + 1} OF 9`)).toBeVisible()
        const next = wizard.getByRole('button', {
          name: step === 0 ? 'Get started' : step === 8 ? 'Open my library' : 'Continue',
          exact: true,
        })
        const providerLabels = [
          '',
          'Set up IGDB metadata',
          'Set up Steam',
          'Set up Epic',
          'Check GOG Galaxy',
          'Choose theme and appearance',
          'Choose app settings',
          'Choose library settings',
        ]
        if (mode === 'fullscreen' && step > 0 && step < 8)
          await expect(wizard.getByRole('button', { name: providerLabels[step], exact: true })).toBeFocused()
        else await expect(next).toBeFocused()
        const bounds = await wizard.evaluate((element) => {
          const targets = [...element.querySelectorAll('.setup-footer button, .setup-header h2')]
          return targets.map((target) => {
            const b = target.getBoundingClientRect()
            return b.top >= 0 && b.left >= 0 && b.bottom <= innerHeight && b.right <= innerWidth
          })
        })
        expect(bounds.every(Boolean)).toBe(true)
        if (step === 1) {
          if (mode === 'fullscreen') await tap(0)
          const secret = wizard.getByLabel('Client secret')
          await expect(secret).toHaveAttribute('type', 'password')
          if (mode === 'desktop') {
            const actions = wizard.getByRole('button', {
              name: /^(Save credentials|Remove saved credentials)$/,
            })
            await expect(actions).toHaveCount(2)
            for (const action of await actions.all()) {
              const measured = await action.evaluate((node) => {
                const box = node.getBoundingClientRect()
                const scrollParents = []
                for (let parent = node.parentElement; parent; parent = parent.parentElement)
                  if (/auto|scroll/.test(getComputedStyle(parent).overflowY))
                    scrollParents.push(parent.className)
                return {
                  inside:
                    box.width > 0 &&
                    box.height > 0 &&
                    box.x >= 0 &&
                    box.y >= 0 &&
                    box.right <= innerWidth + 1 &&
                    box.bottom <= innerHeight + 1,
                  scrollParents,
                }
              })
              expect(measured).toEqual({ inside: true, scrollParents: [] })
            }
          }
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
            await tap(1)
            await expect(
              wizard.getByRole('heading', { name: 'Fill in the details', exact: true }),
            ).toBeVisible()
            await expect(
              wizard.getByRole('button', { name: providerLabels[step], exact: true }),
            ).toBeFocused()
          }
        }
        if ([2, 3, 4].includes(step))
          await expect(wizard.getByRole('heading', { name: 'Platforms', exact: true })).toHaveCount(0)
        if (step === 6) {
          await expect(wizard.getByRole('button', { name: 'Run setup again', exact: true })).toHaveCount(0)
          await expect(wizard.locator('.igdb-connection-panel')).toHaveCount(0)
        }
        if (step === 2) {
          if (mode === 'fullscreen') await tap(0)
          await wizard.getByRole('button', { name: 'Sign in to Steam', exact: true }).click()
          const consent = page.getByRole('dialog', { name: 'Before you sign in' })
          await expect(consent).toBeVisible()
          for (const action of await wizard.locator('.setup-footer button').all())
            await expect(action).toBeDisabled()
          for (let i = 0; i < 10; i++) {
            await page.keyboard.press('Tab')
            expect(await consent.evaluate((element) => element.contains(document.activeElement))).toBe(true)
          }
          for (let cycle = 0; cycle < 4; cycle++)
            for (const direction of [5, 13, 15, 4]) {
              await tap(direction)
              expect(await consent.evaluate((element) => element.contains(document.activeElement))).toBe(true)
            }
          if (mode === 'fullscreen') {
            await tap(9)
            await expect(page.getByRole('dialog', { name: 'Quick menu' })).toHaveCount(0)
            await tap(1)
          } else await tap(1)
          await expect(consent).toHaveCount(0)
          for (const action of await wizard.locator('.setup-footer button').all())
            await expect(action).toBeEnabled()
          await expect(
            wizard.locator('.setup-header').getByRole('heading', {
              name: mode === 'fullscreen' ? 'Steam' : 'Your Steam library',
              exact: true,
            }),
          ).toBeVisible()
          const progress = await page.evaluate(() =>
            window.winnow.request<{ step: number }>({ route: 'setup.get' }),
          )
          expect(progress.ok && progress.data?.step).toBe(2)
          if (mode === 'fullscreen') {
            await tap(1)
            await expect(wizard.getByRole('heading', { name: 'Your Steam library' })).toBeVisible()
            await expect(
              wizard.getByRole('button', { name: providerLabels[step], exact: true }),
            ).toBeFocused()
          }
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
