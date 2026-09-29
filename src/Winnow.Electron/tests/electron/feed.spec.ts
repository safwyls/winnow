import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { DatabaseSync } from 'node:sqlite'
import type { ApiRequest } from '../../src/shared/bridge'
import type { FeedSnapshot, FeedVerdict } from '../../src/renderer/api/types'

let application: ElectronApplication, page: Page, directory: string
const failures: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-feed-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60_000,
  })

  page = await application.firstWindow()
  page.on('pageerror', (error) => failures.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} saves a dated receipt, holds Undo, and retains history after navigation`, async () => {
    await application.evaluate(({ BrowserWindow }, mode) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1440, 900)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    }, mode)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    const feed = await api<FeedSnapshot>({ route: 'feed.get' })
    const shelf = feed.shelves.find((shelf) => shelf.supportsFeedback && shelf.items.length > 0)!
    expect(shelf).toBeTruthy()
    if (mode === 'fullscreen') {
      await page.getByRole('button', { name: `Show ${shelf.title}`, exact: true }).click()
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(shelf.title)
    }
    const scope =
      mode === 'desktop'
        ? page
            .locator('.avalon-shelf')
            .filter({ has: page.getByRole('heading', { name: shelf.title, exact: true }) })
        : page.locator('.avalon-home-hero')
    const name =
      mode === 'desktop'
        ? await scope.locator('.avalon-cover').first().getAttribute('aria-label')
        : await page
            .locator('.avalon-retained-row[data-row-active="true"] .avalon-cover[data-selected="true"]')
            .getAttribute('aria-label')
    const cover = page.getByRole('button', { name: name!, exact: true }).first()
    await expect(cover).toBeVisible()
    await scope.getByRole('button', { name: 'Not now', exact: true }).first().click()
    const undo = scope.getByRole('button', { name: 'Undo', exact: true }).first()
    await expect(undo).toBeFocused()
    await expect(scope.getByRole('status').first()).toContainText('Back on')
    const saved = await api<FeedVerdict[]>({ route: 'feedHistory' })
    const active = saved.find((row) => row.kind === 1 && row.status === 0)!
    expect(active.expiresAt).toBeTruthy()
    await expect(scope.locator('time').first()).toHaveAttribute('dateTime', active.expiresAt!)
    await page.getByRole('button', { name: /What you've told the feed/ }).click()
    const dialog = page.getByRole('dialog', { name: "What you've told the feed", exact: true })
    await expect(dialog.getByRole('button', { name: 'Undo', exact: true })).toBeVisible()
    const geometry = await dialog.boundingBox(),
      viewport = page.viewportSize()
    expect(geometry).not.toBeNull()
    expect(geometry!.y).toBeGreaterThanOrEqual(0)
    if (viewport) expect(geometry!.y + geometry!.height).toBeLessThanOrEqual(viewport.height + 1)
    await dialog.getByRole('button', { name: 'Undo', exact: true }).click()
    await expect(dialog.getByText(/Undone on/).first()).toBeVisible()
    await dialog.getByRole('button', { name: 'Back to the feed' }).click()
    await expect(cover).toBeVisible()
    await expect(scope.getByRole('button', { name: 'Not now', exact: true }).first()).toBeVisible()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
    await page.getByRole('button', { name: /What you've told the feed/ }).click()
    await expect(
      page
        .getByRole('dialog')
        .getByText(/Undone on/)
        .first(),
    ).toBeVisible()
    await page.getByRole('button', { name: 'Back to the feed', exact: true }).click()
    expect(failures).toEqual([])
  })

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} records only exposed cards and waits for an opaque modal and scroll clipping`, async () => {
    await page.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => {
        if (document.getElementById('test-feed-cover')) return
        const overlay = document.createElement('div')
        overlay.id = 'test-feed-cover'
        overlay.setAttribute('role', 'dialog')
        overlay.setAttribute('aria-label', 'Exposure fixture cover')
        overlay.style.cssText = 'position:fixed;inset:0;background:black;z-index:999999;pointer-events:none'
        document.body.append(overlay)
      })
    })
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    const database = new DatabaseSync(join(directory, 'winnow.db'))
    try {
      database.exec('PRAGMA busy_timeout=5000')
      await page.reload()
      await expect(page.getByRole('dialog', { name: 'Exposure fixture cover' })).toBeVisible()
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setFullScreen(false)
        window.setContentSize(1024, 720)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
        window.focus()
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'For you', exact: true })
        .click()
      const feed = await api<FeedSnapshot>({ route: 'feed.get' })
      let selectedShelf: FeedSnapshot['shelves'][number] | undefined
      if (mode === 'fullscreen') {
        const shelf = feed.shelves
          .filter((shelf) => shelf.supportsFeedback && shelf.items.length > 0)
          .sort((a, b) => b.items.length + b.reserve.length - (a.items.length + a.reserve.length))[0]!
        selectedShelf = shelf
        await page.getByRole('button', { name: `Show ${shelf.title}`, exact: true }).click()
        await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(shelf.title)
      }
      const impressions = page.locator(
        mode === 'fullscreen' ? '.avalon-retained-row[data-row-active="true"] .impression' : '.impression',
      )
      await expect(impressions.first()).toBeAttached()
      const seen = () =>
        (database.prepare('SELECT release_id FROM feed_surfacings').all() as { release_id: number }[]).map(
          (row) => row.release_id,
        )
      // Clear after the new covered pass has loaded so the previous screen's
      // in-flight surfacing writes are not mistaken for exposures by this pass.
      database.exec('DELETE FROM feed_surfacings')
      // Two compositor frames deliver the observer callback while the cover is present.
      await page.evaluate(
        () =>
          new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))),
      )
      expect(seen()).toEqual([])
      await page.locator('#test-feed-cover').evaluate((element) => element.remove())
      const first = impressions.first()
      await first.scrollIntoViewIfNeeded()
      const firstId = Number(await first.getAttribute('data-feed-release-id'))
      await expect.poll(() => seen()).toContain(firstId)
      if (mode === 'fullscreen') {
        const visible = new Set(
          await impressions.evaluateAll((elements) =>
            elements.map((element) => Number(element.getAttribute('data-feed-release-id'))),
          ),
        )
        const source = [...selectedShelf!.items, ...selectedShelf!.reserve]
        const next = source.find((item) => !visible.has(item.releaseId))!
        expect(next).toBeTruthy()
        expect(seen()).not.toContain(next.releaseId)
        const arriving = page.locator(
          `.avalon-retained-row[data-row-active="true"] .impression[data-feed-release-id="${next.releaseId}"]`,
        )
        expect(await arriving.count()).toBe(0)
        await first.getByRole('button').focus()
        for (let step = 0; step < source.length && !(await arriving.count()); step++)
          await page.keyboard.press('ArrowRight')
        await expect(arriving).toBeVisible()
        await expect.poll(() => seen()).toContain(next.releaseId)
        expect(failures).toEqual([])
        return
      }
      const last = impressions.last()
      const lastId = Number(await last.getAttribute('data-feed-release-id'))
      const clipped = await last.evaluate((element) => {
        const bounds = element.getBoundingClientRect()
        let left = Math.max(0, bounds.left),
          top = Math.max(0, bounds.top)
        let right = Math.min(innerWidth, bounds.right),
          bottom = Math.min(innerHeight, bounds.bottom)
        for (let parent = element.parentElement; parent; parent = parent.parentElement) {
          const clip = parent.getBoundingClientRect(),
            style = getComputedStyle(parent)
          if (/auto|scroll|hidden|clip/.test(style.overflowX)) {
            left = Math.max(left, clip.left)
            right = Math.min(right, clip.right)
          }
          if (/auto|scroll|hidden|clip/.test(style.overflowY)) {
            top = Math.max(top, clip.top)
            bottom = Math.min(bottom, clip.bottom)
          }
        }
        return right <= left || bottom <= top
      })
      expect(clipped).toBe(true)
      expect(seen()).not.toContain(lastId)
      await last.scrollIntoViewIfNeeded()
      await expect.poll(() => seen()).toContain(lastId)
      expect(failures).toEqual([])
    } finally {
      database.close()
    }
  })
