import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { IgdbConnection } from '../../src/renderer/api/types'

async function snapshot(page: Page): Promise<IgdbConnection> {
  return page.evaluate(async () => {
    const response = await window.winnow.request({ route: 'connections.igdb.get' })
    if (!response.ok) throw Error(`IGDB read failed: ${response.status}`)
    return response.data as IgdbConnection
  })
}
for (const [mode, scale] of [
  ['desktop', 1],
  ['fullscreen', 1],
  ['fullscreen', 1.4],
] as const)
  test(`${mode} IGDB settings preserve protected credentials masked drafts controller input and save focus at text ${scale}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-igdb-settings-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/igdb-settings-main.mjs'),
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
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({
        timeout: 45000,
      })
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
          const response = await window.winnow.request({
            route: 'preferences.presentation.put',
            params: { preference: 'FullscreenTextScale' },
            body: { value: String(scale) },
          })
          if (!response.ok) throw Error('Could not set text scale')
          const controller = { pressed: [] as number[] }
          Object.assign(window, { igdbController: controller })
          Object.defineProperty(navigator, 'getGamepads', {
            configurable: true,
            value: () => [
              {
                index: 0,
                connected: true,
                axes: [0, 0, 0, 0],
                buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: controller.pressed.includes(i) })),
              },
            ],
          })
        }, scale)
      }
      const tap = async (button: number) => {
        await page.waitForTimeout(200)
        for (const pressed of [[], [button], []])
          await page.evaluate(async (pressed) => {
            ;(window as any).igdbController.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
      }
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Metadata & artwork', exact: true })
        .click()
      const form = page.getByRole('form', { name: 'IGDB credentials' })
      const client = form.getByLabel('Client ID', { exact: true }),
        secret = form.getByLabel('Client secret', { exact: true })
      const save = form.getByRole('button', { name: 'Save credentials', exact: true })
      await expect(secret).toHaveAttribute('type', 'password')
      await expect(secret).toHaveValue('')
      await client.fill(' client-id ')
      await secret.fill('entered-secret')
      await save.click()
      await expect(form.getByRole('alert')).toContainText('nothing was saved')
      await expect(secret).toHaveValue('entered-secret')
      expect((await snapshot(page)).hasSavedCredentials).toBe(false)
      expect(await form.textContent()).not.toContain('entered-secret')
      await application.evaluate(() => {
        ;(globalThis as any).__igdbFixture.refuseSave = false
      })
      if (mode === 'fullscreen') {
        await secret.fill('')
        await secret.focus()
        await tap(0)
        const keyboard = page.getByRole('dialog', { name: 'Enter text' })
        await expect(keyboard).toBeVisible()
        for (const letter of 'test') await keyboard.getByRole('button', { name: letter, exact: true }).click()
        await expect(keyboard.getByRole('status', { name: 'Current text' })).toHaveText('••••')
        await keyboard.getByRole('button', { name: 'Done', exact: true }).click()
        await expect(secret).toBeFocused()
        await expect(secret).toHaveValue('test')
        expect(await secret.evaluate((node) => parseFloat(getComputedStyle(node).fontSize))).toBeCloseTo(
          24 * scale,
        )
        expect((await secret.boundingBox())!.height).toBeGreaterThanOrEqual(72)
        expect(
          await form
            .getByText('The secret is stored securely on this device. Changes take effect immediately.')
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(22 * scale)
      }
      await save.focus()
      if (mode === 'fullscreen') await tap(0)
      else await page.keyboard.press('Enter')
      await expect(form.getByRole('status')).toHaveText(
        'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
      )
      await expect(secret).toHaveValue('')
      await expect(client).toHaveValue('client-id')
      await expect(save).toBeFocused()
      const content = await page.getByRole('main').boundingBox()
      const feedback = await form.getByRole('status').boundingBox()
      expect(feedback!.y).toBeGreaterThanOrEqual(content!.y)
      expect(feedback!.y + feedback!.height).toBeLessThanOrEqual(content!.y + content!.height + 1)
      expect(await snapshot(page)).toMatchObject({
        clientId: 'client-id',
        hasSavedCredentials: true,
        isReadable: true,
      })
      await page.screenshot({ path: info.outputPath(`${mode}-${scale}-igdb-saved.png`) })
      await form.getByRole('button', { name: 'Get IGDB credentials' }).click()
      await expect(form.getByRole('alert')).toHaveText(
        'Could not open the page. Visit dev.twitch.tv/console/apps to create a Twitch application.',
      )
      expect(await application.evaluate(() => (globalThis as any).__igdbFixture.links)).toEqual([
        'https://dev.twitch.tv/console/apps',
      ])
      await secret.fill('unsaved-secret')
      await form.getByRole('button', { name: 'Remove saved credentials' }).click()
      await expect(form.getByRole('status')).toContainText(
        'Saved credentials removed. The change is active now.',
      )
      await expect(client).toHaveValue('')
      await expect(secret).toHaveValue('')
      expect(await snapshot(page)).toMatchObject({
        clientId: '',
        hasSavedCredentials: false,
        isReadable: false,
      })
      await secret.fill('departed-secret')
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Application', exact: true })
        .click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Metadata & artwork', exact: true })
        .click()
      await expect(secret).toHaveValue('')
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
