import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-gallery-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/gallery-main.mjs'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
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
      if (new URL(endpoint.address).hostname !== '127.0.0.1') throw Error('Unexpected fixture address')
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {
      /* Preserve the isolated fixture for diagnosis. */
    }
})

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} gallery keeps wheel input, full image geometry, keyboard focus and origin return`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(false)
      window.setContentSize(1280, 720)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
    await page.locator('.avalon-cover').first().click()
    const origin = page.getByRole('button', { name: 'Open screenshot 2 of 10', exact: true })
    await expect(origin).toBeVisible()
    await origin.scrollIntoViewIfNeeded()
    const strip = page.locator('.screenshot-strip')
    await strip.evaluate((node) => {
      node.scrollLeft = 0
    })
    await strip.hover()
    const before = await strip.evaluate((node) => {
      let parent = node.parentElement
      while (parent && !/auto|scroll/.test(getComputedStyle(parent).overflowY)) parent = parent.parentElement
      return parent?.scrollTop ?? 0
    })
    await page.mouse.wheel(0, 180)
    await expect.poll(() => strip.evaluate((node) => node.scrollLeft)).toBeGreaterThan(0)
    const bodyScroll = () =>
      strip.evaluate((node) => {
        let parent = node.parentElement
        while (parent && !/auto|scroll/.test(getComputedStyle(parent).overflowY))
          parent = parent.parentElement
        return parent?.scrollTop ?? 0
      })
    expect(await bodyScroll()).toBe(before)
    await strip.evaluate((node) => {
      node.scrollLeft = node.scrollWidth
    })
    await page.mouse.wheel(0, 180)
    expect(await bodyScroll()).toBe(before)
    await origin.click()
    const dialog = page.getByRole('dialog', { name: 'Screenshot 2 of 10', exact: true })
    await expect(dialog).toBeVisible()
    const close = page.getByRole('button', { name: 'Close screenshots', exact: true })
    await expect(close).toBeFocused()
    await expect(dialog.locator('img')).toBeVisible()
    for (const [width, height] of [
      [1280, 720],
      [1920, 1080],
    ]) {
      await application.evaluate(
        ({ BrowserWindow }, size) => BrowserWindow.getAllWindows()[0].setContentSize(size[0], size[1]),
        [width, height],
      )
      await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
      const metrics = await dialog.locator('img').evaluate((image) => {
        const bounds = image.getBoundingClientRect()
        return {
          x: bounds.x,
          y: bounds.y,
          width: bounds.width,
          height: bounds.height,
          naturalWidth: (image as HTMLImageElement).naturalWidth,
          naturalHeight: (image as HTMLImageElement).naturalHeight,
          fit: getComputedStyle(image).objectFit,
        }
      })
      expect(metrics.naturalWidth).toBe(1280)
      expect(metrics.naturalHeight).toBe(720)
      expect(metrics.width).toBeLessThanOrEqual(1280)
      expect(metrics.height).toBeLessThanOrEqual(720)
      expect(metrics.width / metrics.height).toBeCloseTo(1280 / 720, 2)
      expect(metrics.fit).toBe('contain')
      expect(metrics.x).toBeGreaterThanOrEqual(23)
      expect(metrics.y).toBeGreaterThanOrEqual(23)
      expect(metrics.x + metrics.width).toBeLessThanOrEqual(width - 23)
      expect(metrics.y + metrics.height).toBeLessThanOrEqual(height - 23)
    }
    for (let i = 0; i < 5; i++) {
      await page.keyboard.press('Tab')
      expect(await page.evaluate(() => Boolean(document.activeElement?.closest('.screenshot-dialog')))).toBe(
        true,
      )
    }
    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('dialog', { name: 'Screenshot 3 of 10', exact: true })).toBeVisible()
    await page.screenshot({ path: join(directory, `${mode}-gallery.png`) })
    await page.keyboard.press('Escape')
    await expect(page.locator('.screenshot-dialog')).toHaveCount(0)
    await expect(origin).toBeFocused()
    await expect(page.getByRole('button', { name: 'Open screenshot 3 of 10', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await expect(page.locator('.details-page')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('.details-page')).toHaveCount(0)
  })
}
test('gallery emits no uncaught renderer errors', () => expect(errors).toEqual([]))
