import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { prebuiltBackend, prebuiltActivationHelper } from './prebuilt-backend'

for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const) {
  test(`${mode} Epic embedded failure continues in a masked manual form at text ${scale} and cancels without reopening a browser`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-epic-workflow-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/epic-workflow-main.mjs'),
        '--data-dir',
        directory,
        '--no-sync',
        '--seed-sample',
      ],
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
      timeout: 60000,
    })
    try {
      const page = await application.firstWindow()
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      if (await page.getByRole('dialog', { name: 'Winnow setup', exact: true }).count())
        await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
      await page.evaluate(() =>
        Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
      )
      await application.evaluate(({ BrowserWindow }, mode) => {
        const owner = BrowserWindow.getAllWindows()[0]
        owner.setFullScreen(mode === 'fullscreen')
        if (mode === 'desktop') owner.setContentSize(1200, 900)
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      if (mode === 'fullscreen')
        await page.evaluate(async (scale) => {
          const response = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!response.ok) throw Error('Could not save fixture text scale')
        }, scale)
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Platforms', exact: true })
        .click()
      await page.getByRole('button', { name: mode === 'fullscreen' ? 'Epic' : 'EPIC', exact: true }).click()
      if (mode === 'fullscreen')
        await expect(page.getByRole('heading', { name: 'Epic', exact: true })).toHaveCSS(
          'font-size',
          `${64 * scale}px`,
        )
      const readState = () => application.evaluate(() => (globalThis as any).__epicWorkflow)
      for (const cancel of [true, false]) {
        await page.getByRole('button', { name: 'Connect Epic Games', exact: true }).click()
        const agree = page.getByLabel('I agree to connect this account')
        await expect(agree).toBeVisible()
        await expect(page.locator('.epic-consent')).toHaveCSS('white-space', 'pre-line')
        if (mode === 'fullscreen')
          await expect(page.locator('.epic-consent')).toHaveCSS(
            'font-size',
            `${Number((28 * scale).toFixed(1))}px`,
          )
        await expect(page.getByText(/Winnow is a 3rd party service/)).toBeVisible()
        await expect(
          page.getByRole('button', { name: 'Open Epic sign-in window', exact: true }),
        ).toBeDisabled()
        await agree.check()
        await page.getByRole('button', { name: 'Open Epic sign-in window', exact: true }).click()
        await expect
          .poll(() =>
            application
              .context()
              .pages()
              .some((candidate) => candidate.url().startsWith('https://www.epicgames.com/id/authorize')),
          )
          .toBe(true)
        const provider = application
          .context()
          .pages()
          .find((candidate) => candidate.url().startsWith('https://www.epicgames.com/id/authorize'))!
        await expect(provider.getByRole('heading', { name: 'Epic workflow fixture' })).toBeVisible()
        await provider
          .getByRole('link', { name: 'Finish without a usable response' })
          .click({ noWaitAfter: true })
        const input = page.getByLabel('Final sign-in address', { exact: true })
        await expect(input).toBeVisible()
        await expect(input).toHaveAttribute('type', 'password')
        await expect(page.getByText(/without handing back a usable code/)).toBeVisible()
        expect((await readState()).opened).toHaveLength(cancel ? 0 : 1)
        await page.getByRole('button', { name: 'Continue in your browser', exact: true }).click()
        await expect(input).toBeEnabled()
        const state = await readState()
        const challenge = state.challenges.at(-1)
        expect(state.opened.at(-1)).toBe(challenge.request.startUrl)
        expect(state.completions).toHaveLength(0)
        const callback = `https://localhost/launcher/authorized?code=PRIVATE-FIXTURE-MANUAL-CODE&state=${encodeURIComponent(challenge.request.expectedState)}`
        await input.fill(callback)
        await input.scrollIntoViewIfNeeded()
        const inputBox = await input.boundingBox()
        expect(inputBox).not.toBeNull()
        expect(inputBox!.y).toBeGreaterThanOrEqual(0)
        expect(inputBox!.y + inputBox!.height).toBeLessThanOrEqual(await page.evaluate(() => innerHeight))
        if (mode === 'fullscreen') {
          expect(inputBox!.height).toBeGreaterThanOrEqual(72)
          await expect(input).toHaveCSS('font-size', `${Number((28 * scale).toFixed(1))}px`)
          await expect(page.getByRole('button', { name: 'Finish connecting', exact: true })).toHaveCSS(
            'min-height',
            '64px',
          )
        }
        if (cancel) {
          await input.press('Escape')
          await expect(page.getByText('Sign-in cancelled. Nothing was changed.')).toBeVisible()
          await expect(input).toHaveCount(0)
          expect((await readState()).opened).toHaveLength(1)
          expect((await readState()).completions).toHaveLength(0)
        } else {
          const finish = page.getByRole('button', { name: 'Finish connecting', exact: true })
          await finish.focus()
          await finish.scrollIntoViewIfNeeded()
          expect(
            await finish.evaluate((element) => {
              const box = element.getBoundingClientRect()
              let top = 0,
                bottom = innerHeight
              for (let parent = element.parentElement; parent; parent = parent.parentElement) {
                if (!/auto|scroll|hidden|clip/.test(getComputedStyle(parent).overflowY)) continue
                const bounds = parent.getBoundingClientRect()
                top = Math.max(top, bounds.top)
                bottom = Math.min(bottom, bounds.bottom)
              }
              return box.top >= top && box.bottom <= bottom
            }),
          ).toBe(true)
          await page.screenshot({ path: info.outputPath(`${mode}-epic-manual.png`) })
          await page.keyboard.press('Enter')
          await expect(page.getByText('Connected as Fixture Epic account.', { exact: true })).toBeVisible()
          await expect(page.getByText(/lasts for this run only/)).toBeVisible()
          await expect(input).toHaveCount(0)
          const completed = await readState()
          expect(completed.challenges).toHaveLength(2)
          expect(completed.challenges[0].request.expectedState).not.toBe(challenge.request.expectedState)
          expect(completed.completions).toHaveLength(1)
          expect(completed.completions[0]).toMatchObject({
            attemptId: challenge.attemptId,
            kind: 0,
            state: challenge.request.expectedState,
            code: 'PRIVATE-FIXTURE-MANUAL-CODE',
          })
          expect(completed.forbidden).toEqual([])
          expect(await page.locator('body').innerText()).not.toContain('PRIVATE-FIXTURE-MANUAL-CODE')
        }
      }
    } finally {
      await closeFixture(application, directory)
    }
  })
}
