import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const)
  test(`${mode} journal prompt preserves opt-in retractable rating draft retry and pending-save controls at text ${scale}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-journal-prompt-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/journal-prompt-main.mjs'),
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
    try {
      const page = await application.firstWindow(),
        errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
      await application.evaluate(
        ({ BrowserWindow }, { mode, scale }) => {
          const window = BrowserWindow.getAllWindows()[0]!
          window.setFullScreen(false)
          window.setContentSize(
            mode === 'desktop' ? 1200 : scale === 1 ? 1280 : 1920,
            mode === 'desktop' ? 800 : scale === 1 ? 720 : 1080,
          )
          window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
          window.focus()
        },
        { mode, scale },
      )
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      if (mode === 'fullscreen') {
        await page.evaluate(async (scale) => {
          const result = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!result.ok) throw Error(result.message)
        }, scale)
        await expect
          .poll(() =>
            page.evaluate(() =>
              getComputedStyle(document.documentElement).getPropertyValue('--fullscreen-text-scale').trim(),
            ),
          )
          .toBe(String(scale))
      }
      const publish = async (id: number) => {
        await page.evaluate((id) => {
          Object.assign(window, {
            journalEventReceipt: new Promise<void>((resolve) => {
              const unsubscribe = window.winnow.onEvent((event) => {
                if (event.kind === 'session.ended' && event.resource === `sessions/${id}`) {
                  unsubscribe()
                  resolve()
                }
              })
            }),
          })
        }, id)
        await application.evaluate(
          ({ BrowserWindow }, id) =>
            BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
              kind: 'session.ended',
              resource: `sessions/${id}`,
            }),
          id,
        )
        await page.evaluate(() => (window as any).journalEventReceipt)
      }
      const state = () =>
        application.evaluate(() => {
          const value = (globalThis as any).__journalPromptFixture
          return {
            title: value.title,
            calls: value.calls,
            preferenceReads: value.preferenceReads,
            waiting: typeof value.release === 'function',
          }
        })
      await expect.poll(state).toMatchObject({ calls: [] })
      const readsBefore = (await state()).preferenceReads.length
      await publish(9000)
      await expect.poll(async () => (await state()).preferenceReads.length).toBeGreaterThan(readsBefore)
      expect((await state()).preferenceReads.at(-1)).toBe(false)
      await expect(page.locator('.session-prompt')).toHaveCount(0)
      await application.evaluate(() => {
        ;(globalThis as any).__journalPromptFixture.enabled = true
      })
      await publish(9001)
      const prompt = page.locator('.session-prompt'),
        note = prompt.getByLabel('Your note', { exact: true })
      await expect(note).toBeVisible()
      await expect(prompt.getByText('47m', { exact: true })).toBeVisible()
      if (mode === 'desktop') {
        const bounds = await prompt.boundingBox()
        expect(bounds!.width).toBe(352)
        expect(bounds!.x).toBe(18)
        expect(bounds!.height).toBeLessThan(200)
        expect(await note.evaluate((node) => node.tagName)).toBe('INPUT')
        await expect(prompt.locator('.journal-rating button span')).toHaveCount(5)
      } else {
        await expect(prompt).toHaveAttribute('role', 'dialog')
        expect(await note.evaluate((node) => node.tagName)).toBe('TEXTAREA')
        const bounds = await prompt.boundingBox()
        expect(bounds!.x).toBe(0)
        expect(bounds!.y).toBe(0)
        await page.evaluate(() => {
          const pad = { pressed: [] as number[] }
          Object.assign(window, { journalPad: pad })
          Object.defineProperty(navigator, 'getGamepads', {
            configurable: true,
            value: () => [
              {
                index: 0,
                connected: true,
                mapping: 'standard',
                axes: [0, 0, 0, 0],
                buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: pad.pressed.includes(index) })),
              },
            ],
          })
        })
        // The offer left focus on the previous page. A must enter the prompt's Edit note action.
        for (const pressed of [[], [0], []])
          await page.evaluate(async (pressed) => {
            ;(window as any).journalPad.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
        await expect(page.getByRole('dialog', { name: 'Enter text', exact: true })).toBeVisible()
        await page.keyboard.press('Escape')
        await expect(prompt).toBeVisible()
      }
      const rating = prompt.getByRole('button', { name: '3 out of 5', exact: true })
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-ready.png`) })
      await rating.click()
      await expect(rating).toHaveAttribute('aria-pressed', 'true')
      await rating.click()
      await expect(rating).toHaveAttribute('aria-pressed', 'false')
      await note.fill('  Keep this exact sitting.  ')
      await prompt.getByRole('button', { name: '5 out of 5', exact: true }).click()
      await publish(9002)
      await expect(note).toHaveValue('  Keep this exact sitting.  ')
      await prompt.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(prompt.getByRole('alert')).toContainText('Could not save your note.')
      await expect(prompt.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()
      await expect(note).toHaveValue('  Keep this exact sitting.  ')
      await expect(prompt.getByRole('button', { name: '5 out of 5', exact: true })).toHaveAttribute(
        'aria-pressed',
        'true',
      )
      await expect(prompt.getByRole('button', { name: 'Keep my draft for the next save' })).toHaveCount(0)
      const saveButton = prompt.getByRole('button', { name: 'Save', exact: true })
      await saveButton.scrollIntoViewIfNeeded()
      if (mode === 'fullscreen') {
        expect(
          await prompt.getByRole('alert').evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(20 * scale)
        expect(
          await prompt
            .locator('.journal-current-rating')
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(20 * scale)
        const action = await saveButton.boundingBox(),
          hints = await prompt.locator('.journal-controller-hints').boundingBox()
        expect(action!.y + action!.height).toBeLessThanOrEqual(hints!.y)
      }
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-retry.png`) })
      await application.evaluate(() => {
        const value = (globalThis as any).__journalPromptFixture
        value.fail = false
        value.hold = true
      })
      await prompt.getByRole('button', { name: 'Save', exact: true }).click()
      await expect.poll(state).toMatchObject({ waiting: true })
      await expect(note).toBeDisabled()
      await expect(prompt.getByRole('button', { name: 'Dismiss journal prompt' })).toBeDisabled()
      await expect(prompt.getByRole('button', { name: '1 out of 5', exact: true })).toBeDisabled()
      await page.keyboard.press('Escape')
      await expect(prompt).toBeVisible()
      await publish(9003)
      await application.evaluate(() => (globalThis as any).__journalPromptFixture.release())
      await expect(prompt).toHaveCount(0)
      expect((await state()).calls).toEqual(
        [1, 2].map(() => ({
          id: 9001,
          body: { note: 'Keep this exact sitting.', rating: 5, expectedRevision: 'original' },
        })),
      )
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
