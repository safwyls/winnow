import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-steam-import-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env,
    chromiumSandbox: true,
    timeout: 60_000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('button', { name: 'Skip setup', exact: true }).isVisible())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => {
  if (application) {
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        application.close(),
        new Promise<void>((done) => {
          timer = setTimeout(() => {
            application.process().kill()
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(timer)
    }
  }
  if (directory)
    try {
      const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      if (new URL(endpoint.address).hostname !== '127.0.0.1')
        throw Error('Unexpected fixture backend address')
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {
      /* Preserve the isolated fixture for diagnostics. */
    }
})

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} imports multiple selected saved licence pages through the real loader and importer with detailed results`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(1280, 720)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Settings', exact: true })
      .click()
    await page
      .getByRole('navigation', { name: 'Settings section' })
      .getByRole('button', { name: 'Platforms', exact: true })
      .click()
    await page.getByRole('button', { name: 'Import purchase history', exact: true }).click()
    const imported = page.getByRole('region', { name: 'Steam purchase and licence import' })
    await imported
      .getByLabel('Saved Steam pages')
      .setInputFiles([
        resolve('../../tests/fixtures/steam-account-pages/licenses-page1.html'),
        resolve('../../tests/fixtures/steam-account-pages/licenses-final-page.html'),
        resolve('../../tests/fixtures/steam-account-pages/purchase-history.html'),
      ])
    await expect(imported.getByText('licenses-page1.html: Licence page ready')).toBeVisible()
    await expect(imported.getByText('licenses-final-page.html: Licence page ready')).toBeVisible()
    await expect(imported.getByText('purchase-history.html: Purchase history ready')).toBeVisible()
    const result = imported.getByRole('status', { name: 'Steam import results' })
    await expect(result).toBeVisible()
    // The final-page fixture repeats three first-page facts; the retained parser unions them.
    await expect(result.getByText('Licences found').locator('..')).toHaveText('Licences found13')
    await expect(result.getByText('Licences reported').locator('..')).toHaveText('Licences reported979')
    await expect(result.getByText(/Steam paginates licences/)).toBeVisible()
    await expect(result.getByText(/Steam only saves the purchase history/)).toBeVisible()
    await expect(result.getByRole('region', { name: 'Rows not applied' })).toBeVisible()
    await expect(imported.getByRole('button', { name: 'Import captured pages' })).toHaveCount(0)
    const layout = await result.evaluate((node) => ({
      width: node.clientWidth,
      scroll: node.scrollWidth,
      font: getComputedStyle(node.querySelector('dd')!).fontFamily,
      tabular: getComputedStyle(node.querySelector('dd')!).fontVariantNumeric,
    }))
    expect(layout.scroll).toBeLessThanOrEqual(layout.width + 1)
    expect(layout.font).toContain('Avalon Data')
    expect(layout.tabular).toContain('tabular-nums')
    expect(errors).toEqual([])
    await page.getByRole('dialog', { name: 'Import Steam purchase history' }).getByRole('button', { name: 'Close', exact: true }).click()
  })
