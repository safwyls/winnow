import { test, expect, _electron as electron, type ElectronApplication } from '@playwright/test'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { FeedSnapshot, LibraryResponse } from '../../src/renderer/api/types'

type Host = {
  __winnowLayoutFixture: { library: LibraryResponse; feed: FeedSnapshot }
  __feedExposures: { releaseId: number; shelfId: string }[]
}

for (const [width, height] of [
  [1920, 1080],
  [1280, 720],
])
  test(`fullscreen exposes only the attached shelf and RT accumulates both shelves at ${width}×${height}`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-feed-exposure-'))
    let application: ElectronApplication | undefined
    try {
      const games = ['Stardew Valley', 'Hollow Knight', 'Disco Elysium', 'Slay the Spire'].map(
        (title, index) => ({
          workId: index + 1,
          title,
          bucket: 'never_played',
          playtimeMinutes: 0,
          entries: [
            {
              ownershipId: index + 1,
              releaseId: index + 1,
              workId: index + 1,
              title,
              store: 'steam',
              installed: true,
              playtimeMinutes: 0,
            },
          ],
        }),
      )
      const feed: FeedSnapshot = {
        candidateCount: 8,
        confidence: 1,
        failed: false,
        shelves: [
          ['patched', 'Patched while you were away'],
          ['untouched', 'Never opened'],
        ].map(([id, title], index) => ({
          id,
          title,
          blurb: '',
          supportsFeedback: true,
          reserve: [],
          items: games.slice(index * 2, index * 2 + 2).map((game) => ({
            ownershipId: game.workId,
            releaseId: game.workId,
            title: game.title,
            reason: 'Something worth coming back to.',
          })),
        })),
      }
      const path = join(directory, 'fixture.json')
      await writeFile(path, JSON.stringify({ library: { games, lists: [] }, feed: { ...feed, shelves: [] } }))
      const env = Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>
      application = await electron.launch({
        executablePath: electronPath as unknown as string,
        args: [
          resolve('tests/electron/feed-exposure-main.mjs'),
          '--data-dir',
          directory,
          '--seed-sample',
          '--no-sync',
        ],
        env: { ...env, WINNOW_LAYOUT_FIXTURE: path },
        chromiumSandbox: true,
      })
      const page = await application.firstWindow()
      const failures: string[] = []
      page.on('pageerror', (error) => failures.push(error.message))
      await expect(page.locator('.avalon-shell')).toBeVisible()
      await expect
        .poll(() => page.evaluate(() => window.winnow.connection()), { timeout: 45_000 })
        .toMatchObject({ connected: true })
      await application.evaluate(
        ({ BrowserWindow }, { width, height }) => {
          const window = BrowserWindow.getAllWindows()[0]!
          window.setFullScreen(false)
          window.setContentSize(width, height)
          window.webContents.send('winnow:fullscreen:changed', true)
          window.focus()
        },
        { width, height },
      )
      await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
      await page.waitForFunction(({ width, height }) => innerWidth === width && innerHeight === height, {
        width,
        height,
      })
      const navigation = page.getByRole('navigation', { name: 'Main navigation' })
      await navigation.getByRole('button', { name: 'Library', exact: true }).click()
      await expect(page.locator('.avalon-library')).toBeVisible()
      await application.evaluate(({ BrowserWindow }, feed) => {
        ;(globalThis as unknown as Host).__winnowLayoutFixture.feed = feed
        BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
          kind: 'library.changed',
          resource: 'feed',
        })
      }, feed)
      const published = await page.evaluate(() => window.winnow.request<FeedSnapshot>({ route: 'feed.get' }))
      expect(published).toMatchObject({ ok: true })
      expect(published.data?.shelves).toHaveLength(2)
      const exposures = () => application!.evaluate(() => (globalThis as unknown as Host).__feedExposures)
      expect(await exposures()).toEqual([])
      await navigation.getByRole('button', { name: 'For you', exact: true }).click()
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(feed.shelves[0].title)
      await expect.poll(async () => (await exposures()).map((item) => item.releaseId).sort()).toEqual([1, 2])
      expect((await exposures()).every((item) => item.shelfId === 'patched')).toBe(true)
      await page.evaluate(() => {
        const state = { pressed: [] as number[] }
        Object.assign(window, { exposurePad: state })
        Object.defineProperty(navigator, 'getGamepads', {
          configurable: true,
          value: () => [
            {
              index: 0,
              connected: true,
              axes: [0, 0, 0, 0],
              buttons: Array.from({ length: 17 }, (_, index) => ({ pressed: state.pressed.includes(index) })),
            },
          ],
        })
      })
      async function tap(button: number) {
        for (const pressed of [[], [button], []])
          await page.evaluate(async (pressed) => {
            ;(window as unknown as { exposurePad: { pressed: number[] } }).exposurePad.pressed = pressed
            await new Promise<void>((done) =>
              requestAnimationFrame(() => requestAnimationFrame(() => done())),
            )
          }, pressed)
      }
      await tap(7)
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(feed.shelves[1].title)
      await expect
        .poll(async () => (await exposures()).map((item) => item.releaseId).sort())
        .toEqual([1, 2, 3, 4])
      expect(
        (await exposures())
          .filter((item) => item.shelfId === 'untouched')
          .map((item) => item.releaseId)
          .sort(),
      ).toEqual([3, 4])
      await expect(page.locator('.avalon-footer').getByText('LT / RT  Shelf', { exact: true })).toBeVisible()
      await expect(page.locator('.avalon-row-viewport[data-animating]')).toHaveCount(0)
      await page.screenshot({ path: info.outputPath('second-shelf.png') })
      await tap(6)
      await expect(page.locator('.avalon-home-shelf').getByRole('heading')).toHaveText(feed.shelves[0].title)
      expect(await exposures()).toHaveLength(4)
      expect(failures).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })
