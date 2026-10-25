import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, relative, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { FeedSnapshot, LibraryResponse } from '../../src/renderer/api/types'

const environment = () =>
  Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>

test.describe('fullscreen row and shelf primitives', () => {
  let application: ElectronApplication, page: Page
  const errors: string[] = []
  test.beforeAll(async () => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-rows-'))
    const source = relative(
      directory,
      resolve('tests/electron/fullscreen-rows-probe-renderer.tsx'),
    ).replaceAll('\\', '/')
    await writeFile(
      join(directory, 'index.html'),
      `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'"></head><body><div id="root"></div><script type="module" src="${source}"></script></body></html>`,
    )
    await build({
      configFile: false,
      root: directory,
      base: './',
      plugins: [react()],
      logLevel: 'silent',
      build: { outDir: join(directory, 'out'), emptyOutDir: false },
    })
    application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/actions-probe-main.mjs'),
        '--data-dir',
        directory,
        join(directory, 'out/index.html'),
      ],
      env: environment(),
      chromiumSandbox: true,
    })
    page = await application.firstWindow()
    page.on('pageerror', (error) => errors.push(error.message))
  })
  test.afterAll(async () => closeFixture(application))
  test.beforeEach(async () => {
    await page.reload()
    await expect(page.getByRole('button', { name: 'Outside', exact: true })).toBeVisible()
    await page.evaluate(() => document.fonts.ready)
  })
  test.afterEach(() => expect(errors).toEqual([]))

  test('shelf mouse targets preserve controller focus and announce their ordinal and current state', async () => {
    await page.evaluate(() => (window as any).rowProbe.configure({ indicator: true }))
    const indicator = page.getByRole('group', { name: 'Recommendation shelves' })
    const buttons = indicator.getByRole('button')
    await expect(buttons).toHaveCount(5)
    expect(await buttons.evaluateAll((items) => items.every((item) => item.tabIndex === -1))).toBe(true)
    await expect(buttons.nth(2)).toHaveAttribute('aria-current', 'true')
    await expect(buttons.nth(3)).toHaveAccessibleName('Show Unplayed')
    await expect(buttons.nth(3)).toHaveAccessibleDescription('Unplayed, shelf 3 of 3')
    const outside = page.getByRole('button', { name: 'Outside', exact: true })
    await outside.focus()
    for (const index of [0, 3, 4]) {
      const bounds = (await buttons.nth(index).boundingBox())!
      await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2)
      await page.mouse.down()
      await expect(outside).toBeFocused()
      await page.mouse.up()
      await expect(outside).toBeFocused()
    }
    expect(await page.evaluate(() => (window as any).rowProbe.selections)).toEqual([0, 2, 2])
  })

  for (const selected of [0, 5])
    test(`six shelf targets retain intrinsic geometry and disabled endpoint ${selected}`, async ({}, info) => {
      await page.evaluate(
        (selected) =>
          (window as any).rowProbe.configure({
            indicator: true,
            titles: ['One', 'Two', 'Three', 'Four', 'Five', 'Six'],
            selected,
            width: 100,
          }),
        selected,
      )
      const buttons = page.locator('.avalon-shelf-indicator button')
      await expect(buttons).toHaveCount(8)
      if (selected === 0) {
        await expect(buttons.first()).toBeDisabled()
        await expect(buttons.last()).toBeEnabled()
      } else {
        await expect(buttons.first()).toBeEnabled()
        await expect(buttons.last()).toBeDisabled()
      }
      for (const button of await buttons.all()) {
        await expect(button).toBeVisible()
        const bounds = (await button.boundingBox())!
        expect(bounds.height).toBe(44)
        expect(bounds.width).toBe(44)
        expect(bounds.y).toBeGreaterThanOrEqual(0)
        expect(bounds.y + bounds.height).toBeLessThanOrEqual(400)
      }
      expect((await page.locator('.avalon-shelf-indicator').boundingBox())!.height).toBe(380)
      await expect(page.locator('button[aria-current="true"] i')).toHaveCSS('width', '16px')
      await expect(page.locator('button:not([aria-current]) i').first()).toHaveCSS('width', '12px')
      await page.screenshot({ path: info.outputPath(`shelf-endpoint-${selected}.png`) })
    })

  test('scheduled browser render frames finish an eight row transition within three seconds', async () => {
    const result = await page.evaluate(async () => {
      const viewport = document.querySelector<HTMLElement>('.avalon-row-viewport')!
      const start = performance.now()
      ;(window as any).rowProbe.select(1)
      const started = viewport.dataset.animating === 'true'
      let frames = 0
      await new Promise<void>((done) => {
        const sample = () => {
          frames++
          if (!viewport.dataset.animating || performance.now() - start >= 3000) done()
          else requestAnimationFrame(sample)
        }
        requestAnimationFrame(sample)
      })
      return {
        started,
        frames,
        elapsed: performance.now() - start,
        animating: viewport.dataset.animating === 'true',
        first: viewport.dataset.firstRow,
        top:
          document.querySelector('[data-row-id="row-1"]')!.getBoundingClientRect().top -
          viewport.getBoundingClientRect().top,
      }
    })
    expect(result.started).toBe(true)
    expect(result.frames).toBeGreaterThan(1)
    expect(result.elapsed).toBeLessThan(3000)
    expect(result.animating).toBe(false)
    expect(result.first).toBe('1')
    expect(result.top).toBe(0)
  })

  test('forward and reversed navigation preserve arranged geometry until their first frame', async () => {
    const result = await page.evaluate(() => {
      const probe = (window as any).rowProbe
      probe.manual()
      const row = document.querySelector('[data-row-id="row-1"]')!
      const top = () => row.getBoundingClientRect().top
      const initial = top()
      probe.select(1)
      const afterShow = top()
      probe.frame(1000)
      const afterFirstFrame = top()
      probe.frame(1090)
      const beforeReverse = top()
      probe.select(0)
      const afterReverse = top()
      probe.frame(2000)
      return {
        initial,
        afterShow,
        afterFirstFrame,
        beforeReverse,
        afterReverse,
        afterReverseFrame: top(),
        retained: row === document.querySelector('[data-row-id="row-1"]'),
      }
    })
    expect(result.afterShow).toBe(result.initial)
    expect(result.afterFirstFrame).toBe(result.initial)
    expect(result.beforeReverse).toBeGreaterThan(0)
    expect(result.beforeReverse).toBeLessThan(result.initial)
    expect(result.afterReverse).toBe(result.beforeReverse)
    expect(result.afterReverseFrame).toBe(result.beforeReverse)
    expect(result.retained).toBe(true)
  })
})

test.describe('fullscreen Home shelf diagnostics', () => {
  let application: ElectronApplication, page: Page, directory: string
  const errors: string[] = []
  test.beforeAll(async () => {
    directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-home-rows-'))
    const games = Array.from({ length: 100 }, (_, index) => ({
      workId: index + 1,
      title: `Shelf game ${index + 1}`,
      bucket: 'never_played',
      playtimeMinutes: 0,
      entries: [
        {
          ownershipId: index + 1,
          releaseId: index + 1,
          workId: index + 1,
          title: `Shelf game ${index + 1}`,
          store: 'Steam',
          installed: true,
          playtimeMinutes: 0,
        },
      ],
    }))
    const fixture: { library: LibraryResponse; feed: FeedSnapshot } = {
      library: { games, lists: [] } as unknown as LibraryResponse,
      feed: {
        candidateCount: 100,
        confidence: 2,
        failed: false,
        shelves: Array.from({ length: 10 }, (_, index) => ({
          id: `diagnostic-${index}`,
          title: `Shelf ${index}`,
          blurb: '',
          supportsFeedback: false,
          reserve: [],
          items: games.slice(index * 10, index * 10 + 10).map((game) => ({
            ownershipId: game.workId,
            releaseId: game.workId,
            title: game.title,
            reason: 'Ready to play.',
          })),
        })),
      },
    }
    const fixturePath = join(directory, 'layout-fixture.json')
    await writeFile(fixturePath, JSON.stringify(fixture))
    application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('tests/electron/layout-main.mjs'),
        '--data-dir',
        directory,
        '--seed-sample',
        '--no-sync',
      ],
      env: { ...environment(), WINNOW_LAYOUT_FIXTURE: fixturePath },
      chromiumSandbox: true,
      timeout: 60000,
    })
    page = await application.firstWindow()
    page.on('pageerror', (error) => errors.push(error.message))
    await expect(page.locator('.avalon-cover').first()).toBeVisible()
    await application.evaluate(({ BrowserWindow }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(1920, 1080)
      window.webContents.send('winnow:fullscreen:changed', true)
      window.focus()
    })
    await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'For you', exact: true })
      .click()
  })
  test.afterAll(async () => closeFixture(application, directory))
  test.afterEach(() => expect(errors).toEqual([]))
  for (const scale of [0.7, 1, 1.4])
    test(`ten Home shelves retain viewport, footer and one hero update per move at text ${scale}`, async ({}, info) => {
      await page.evaluate(
        (scale) => document.documentElement.style.setProperty('--fullscreen-text-scale', String(scale)),
        scale,
      )
      await page.getByRole('button', { name: 'Show Shelf 0', exact: true }).click()
      await expect(page.locator('.avalon-row-viewport[data-animating]')).toHaveCount(0)
      await page.evaluate(async () => {
        await document.fonts.ready
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      })
      await page.locator('[data-row-active="true"] .avalon-cover').first().focus()
      await page.evaluate(() => {
        const viewport = document.querySelector('.avalon-row-viewport')!
        const footer = document.querySelector('.avalon-footer')!
        const hero = document.querySelector('.avalon-home-hero h1')!
        const state = {
          viewport,
          footer,
          footerChildren: [...footer.childNodes],
          footerText: footer.textContent,
          hero,
          base: viewport.getBoundingClientRect().toJSON(),
          changes: 0,
          frames: [] as string[],
          running: true,
        }
        const observer = new MutationObserver((records) => {
          state.changes += records.length
        })
        observer.observe(hero, { subtree: true, childList: true, characterData: true })
        const sample = () => {
          if (!state.running) return
          state.frames.push(JSON.stringify(viewport.getBoundingClientRect().toJSON()))
          requestAnimationFrame(sample)
        }
        requestAnimationFrame(sample)
        Object.assign(window, { homeRows: state, homeRowsObserver: observer })
      })
      for (let step = 0; step < 8; step++) {
        await page.evaluate(() => {
          ;(window as any).homeRows.changes = 0
          ;(window as any).homeRows.frames = []
        })
        await page.keyboard.press('ArrowDown')
        await expect(page.locator('.avalon-row-viewport')).toHaveAttribute('data-first-row', String(step + 1))
        await expect(page.locator('.avalon-row-viewport[data-animating]')).toHaveCount(0)
        const state = await page.evaluate(() => {
          const state = (window as any).homeRows
          return {
            sameViewport: state.viewport === document.querySelector('.avalon-row-viewport'),
            sameFooter:
              state.footer === document.querySelector('.avalon-footer') &&
              state.footerChildren.every(
                (child: Node, index: number) => child === state.footer.childNodes[index],
              ),
            footer: state.footer.textContent === state.footerText,
            sameHero: state.hero === document.querySelector('.avalon-home-hero h1'),
            changes: state.changes,
            bounds: state.viewport.getBoundingClientRect().toJSON(),
            base: state.base,
            frames: state.frames,
          }
        })
        expect(state.sameViewport).toBe(true)
        expect(state.sameFooter).toBe(true)
        expect(state.footer).toBe(true)
        expect(state.sameHero).toBe(true)
        expect(state.changes).toBe(1)
        expect(state.bounds).toEqual(state.base)
        expect(state.frames.length).toBeGreaterThan(0)
        expect(state.frames.every((bounds: string) => bounds === JSON.stringify(state.base))).toBe(true)
      }
      await page.evaluate(() => {
        ;(window as any).homeRows.running = false
        ;(window as any).homeRowsObserver.disconnect()
      })
      await page.screenshot({ path: info.outputPath(`home-ten-shelves-text-${scale}.png`) })
    })
})
