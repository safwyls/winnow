import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

interface Fixture {
  hold: boolean
  starts: { operationId: string; request: { pluginId: string; releaseTag: string } }[]
  refreshes: string[]
  operations: Record<string, { state: string }>
  finish(id: string, outcome: number): void
}
async function launch(mode: 'desktop' | 'fullscreen') {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-plugin-installation-'))
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/plugin-install-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
    timeout: 60000,
  })
  try {
    const page = await application.firstWindow()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    if (await page.getByRole('dialog', { name: 'Winnow setup', exact: true }).count())
      await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(mode === 'desktop' ? 1200 : 1920, mode === 'desktop' ? 900 : 1080)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      ;(globalThis as unknown as { __pluginInstallFixture: Fixture }).__pluginInstallFixture.hold = true
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    return { application, page, directory }
  } catch (error) {
    await closeFixture(application, directory)
    throw error
  }
}
async function activate(application: ElectronApplication, pluginId = 'psn') {
  await application.evaluate(({ BrowserWindow }, pluginId) => {
    BrowserWindow.getAllWindows()[0].webContents.send('winnow:activation', {
      kind: 'plugin',
      pluginId,
      releaseTag: 'v0.2.0',
    })
  }, pluginId)
}
async function state(application: ElectronApplication) {
  return application.evaluate(() => {
    const value = (globalThis as unknown as { __pluginInstallFixture: Fixture }).__pluginInstallFixture
    return { starts: value.starts, refreshes: value.refreshes, operations: value.operations }
  })
}
async function finish(application: ElectronApplication, outcome: number) {
  await application.evaluate((_, outcome) => {
    const fixture = (globalThis as unknown as { __pluginInstallFixture: Fixture }).__pluginInstallFixture
    fixture.finish(fixture.starts.at(-1)!.operationId, outcome)
  }, outcome)
}
async function tap(page: Page, button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            mapping: 'standard',
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, index) => ({
              pressed: pressed.includes(index),
              value: pressed.includes(index) ? 1 : 0,
            })),
          },
        ],
      })
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} a browser request waits for a manual retry and then installs its own provider`, async () => {
    const { application, page, directory } = await launch(mode)
    try {
      await activate(application)
      const status = page.getByRole('region', { name: 'Plugin installation', exact: true })
      await expect(status.getByRole('progressbar')).toBeVisible()
      await finish(application, 2)
      await status.getByRole('button', { name: 'Retry plugin installation' }).click()
      await expect.poll(async () => (await state(application)).starts.length).toBe(2)
      await activate(application, 'xbox')
      await page.evaluate(
        () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
      )
      expect((await state(application)).starts.map((start) => start.request.pluginId)).toEqual(['psn', 'psn'])
      await finish(application, 0)
      await expect
        .poll(async () => (await state(application)).starts.map((start) => start.request.pluginId))
        .toEqual(['psn', 'psn', 'xbox'])
      await expect(status.getByRole('status')).toHaveText('Downloading the plugin…')
      await finish(application, 1)
      await expect(page.getByRole('region', { name: 'Xbox settings' })).toBeVisible()
      expect((await state(application)).refreshes).toEqual(['psn'])
    } finally {
      await closeFixture(application, directory)
    }
  })
  for (const outcome of [0, 1])
    test(`${mode} website installation reports progress, retries from native input and opens settings for outcome ${outcome}`, async ({}, info) => {
      const { application, page, directory } = await launch(mode)
      try {
        await activate(application)
        const status = page.getByRole('region', { name: 'Plugin installation', exact: true })
        await expect(status.getByRole('status')).toHaveText('Downloading the plugin…')
        await expect(status.getByRole('status')).toHaveAttribute('aria-live', 'polite')
        await expect(status.getByRole('progressbar', { name: 'Installing plugin' })).toBeVisible()
        expect((await state(application)).starts).toHaveLength(1)
        await expect(page.getByRole('dialog', { name: 'Review provider installation' })).toHaveCount(0)
        await page.screenshot({ path: info.outputPath(`${mode}-install-progress.png`) })
        await finish(application, 2)
        await expect(status.getByRole('status')).toHaveText('The download failed. Try again.')
        const retry = status.getByRole('button', { name: 'Retry plugin installation' })
        await retry.focus()
        if (mode === 'fullscreen') await tap(page, 0)
        else await page.keyboard.press('Enter')
        await expect(status.getByRole('progressbar')).toBeVisible()
        await expect.poll(async () => (await state(application)).starts.length).toBe(2)
        await finish(application, outcome)
        await expect(page.getByRole('region', { name: 'PlayStation settings' })).toBeVisible()
        await expect(page.getByText('Plugin settings are ready.', { exact: true })).toBeVisible()
        if (mode === 'desktop')
          await expect(page.getByRole('tab', { name: 'PlayStation', exact: true })).toHaveAttribute(
            'aria-selected',
            'true',
          )
        else {
          await expect(page.getByLabel('PlayStation Account label', { exact: true })).toBeInViewport()
          await expect(page.getByRole('button', { name: 'Back', exact: true })).toBeVisible()
          await expect(page.getByRole('heading', { name: 'Make yourself at home.' })).toBeHidden()
        }
        await expect(page.getByRole('button', { name: 'Retry plugin installation' })).toHaveCount(0)
        expect((await state(application)).refreshes).toEqual(outcome === 0 ? ['psn'] : [])
        await page.screenshot({ path: info.outputPath(`${mode}-installed-settings.png`) })
      } finally {
        await closeFixture(application, directory)
      }
    })

  test(`${mode} installation survives departure without taking navigation back and its result stays available`, async () => {
    const { application, page, directory } = await launch(mode)
    try {
      await activate(application)
      await expect(page.getByRole('progressbar', { name: 'Installing plugin' })).toBeVisible()
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true })
        .click()
      await finish(application, 0)
      await expect.poll(async () => (await state(application)).refreshes).toEqual(['psn'])
      await expect(
        page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Library', exact: true }),
      ).toHaveAttribute('aria-current', 'page')
      await expect(page.getByRole('region', { name: 'PlayStation settings' })).toHaveCount(0)
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await expect(page.getByRole('tab', { name: 'Manage plugins' })).toHaveAttribute('aria-selected', 'true')
      if (mode === 'fullscreen')
        await page.getByRole('button', { name: 'Plugin installation', exact: true }).click()
      await expect(page.getByRole('region', { name: 'Plugin installation' }).getByRole('status')).toHaveText(
        'Plugin settings are ready.',
      )
      expect((await state(application)).starts).toHaveLength(1)
      if (mode === 'fullscreen') {
        await tap(page, 1)
        await expect(page.getByRole('tab', { name: 'Manage plugins' })).toBeVisible()
        await expect(page.getByRole('dialog', { name: 'Quick menu' })).toHaveCount(0)
      }
    } finally {
      await closeFixture(application, directory)
    }
  })

  test(`${mode} cancellation clears progress, offers retry and unlocks backend restart`, async () => {
    const { application, page, directory } = await launch(mode)
    try {
      await activate(application)
      const status = page.getByRole('region', { name: 'Plugin installation' })
      await expect(status.getByRole('progressbar')).toBeVisible()
      if (mode === 'desktop')
        await expect(
          page.getByRole('button', { name: 'Restart library service', exact: true }),
        ).toBeDisabled()
      await status.getByRole('button', { name: 'Cancel installation' }).click()
      await expect(status.getByRole('status')).toHaveText(
        'Plugin installation cancelled. Try again when you’re ready.',
      )
      await expect(status.getByRole('progressbar')).toHaveCount(0)
      await expect(status.getByRole('button', { name: 'Retry plugin installation' })).toBeEnabled()
      if (mode === 'fullscreen') await status.getByRole('button', { name: 'Back', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Restart library service', exact: true })).toBeEnabled()
      expect((await state(application)).refreshes).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
}
