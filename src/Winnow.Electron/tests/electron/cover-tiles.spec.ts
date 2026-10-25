import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, relative, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page
const errors: string[] = []
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-cover-tiles-'))
  const source = relative(directory, resolve('tests/electron/cover-tiles-probe-renderer.tsx')).replaceAll(
    '\\',
    '/',
  )
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
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
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
  await page.mouse.move(800, 600)
})
test.afterEach(() => expect(errors).toEqual([]))
const tile = () => page.locator('.avalon-desktop-cover')
const cover = () => page.locator('.avalon-cover')
const primary = () => page.locator('.avalon-tile-primary')
const details = () => page.locator('.avalon-tile-details')
const ring = () => page.locator('.avalon-tile-ring')
const counters = () =>
  page.evaluate(() => ({
    opened: [...(window as any).coverProbe.opened],
    launches: [...(window as any).coverProbe.launches],
  }))
async function configure(value: Record<string, number | boolean | string>) {
  await page.evaluate(async (value) => {
    ;(window as any).coverProbe.configure(value)
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  }, value)
}

for (const fullscreen of [false, true]) {
  test(`unread accessible copy preserves exact counts and eligibility in ${fullscreen ? 'fullscreen' : 'desktop'}`, async () => {
    await configure({ fullscreen })
    const title = 'A deliberately long game title occupying two lines'
    await expect(cover()).toHaveAccessibleName(
      `View ${title}, patched since you played. Owned on Steam, GOG, Epic`,
    )
    for (const count of [1, 3, 1234]) {
      await configure({ unreadCount: count })
      await expect(cover()).toHaveAccessibleName(
        `View ${title}, patched since you played: ${count.toLocaleString()} ${count === 1 ? 'update' : 'updates'}. Owned on Steam, GOG, Epic`,
      )
      await expect(cover().locator('.avalon-unread')).toHaveCount(1)
    }
    await configure({ unreadCount: 0 })
    await expect(cover()).toHaveAccessibleName(`View ${title}. Owned on Steam, GOG, Epic`)
    await expect(cover().locator('.avalon-unread')).toHaveCount(0)
    await configure({ unreadCount: 3, played: false, singleStore: true })
    await expect(cover()).toHaveAccessibleName(`View ${title}`)
    await expect(cover().locator('.avalon-unread')).toHaveCount(0)
    await configure({ reason: 'Bought 3 years ago, never opened.' })
    await cover().focus()
    await expect(cover()).toBeFocused()
    await expect(cover()).toHaveAccessibleDescription('Bought 3 years ago, never opened.')
  })
}
async function hidden() {
  for (const action of [primary(), details()]) {
    await expect(action).toHaveCSS('opacity', '0')
    await expect(action).toHaveCSS('pointer-events', 'none')
    await expect(action).toHaveAttribute('aria-hidden', 'true')
  }
}
async function revealed() {
  for (const action of [primary(), details()]) {
    await expect(action).toHaveCSS('opacity', '1')
    await expect(action).toHaveCSS('pointer-events', 'auto')
    await expect(action).toHaveAttribute('aria-hidden', 'false')
  }
}
async function point(element: Locator) {
  const box = await element.boundingBox()
  return { x: box!.x + box!.width / 2, y: box!.y + box!.height / 2 }
}
async function move(element: Locator) {
  const at = await point(element)
  await page.mouse.move(at.x, at.y)
}
async function geometry() {
  return tile().evaluate((node) => {
    const rect = (element: Element) => {
      const r = element.getBoundingClientRect()
      return {
        x: r.x,
        y: r.y,
        right: r.right,
        bottom: r.bottom,
        width: r.width,
        height: r.height,
      }
    }
    const part = (selector: string) => rect(node.querySelector(selector)!)
    return {
      tile: rect(node),
      primary: part('.avalon-tile-primary'),
      details: part('.avalon-tile-details'),
      scrim: part('.avalon-tile-scrim'),
      stat: part('.avalon-tile-stat'),
      chips: [...node.querySelectorAll('.avalon-store-chips > span')].map(rect),
      badge: node.querySelector('.avalon-unread') ? part('.avalon-unread') : null,
    }
  })
}

for (const width of [108, 148, 200])
  test(`fixed action targets and inside ring retain their geometry at ${width}px`, async ({}, info) => {
    await configure({ width, reduced: false })
    await hidden()
    const before = await geometry()
    await cover().hover({ position: { x: width / 2, y: 60 } })
    await revealed()
    const after = await geometry()
    expect(after.primary.width).toBe(32)
    expect(after.primary.height).toBe(32)
    expect(after.details.width).toBe(40)
    expect(after.details.height).toBe(40)
    for (const key of ['primary', 'details'] as const) {
      expect(after[key].x - after.tile.x).toBe(before[key].x - before.tile.x)
      expect(after[key].y - after.tile.y).toBeCloseTo(before[key].y - before.tile.y, 3)
    }
    await expect(primary()).toHaveAttribute('title', 'Launch through Steam')
    await expect(details()).toHaveAttribute('title', 'Full details')
    await expect(ring()).toHaveCSS('border-width', '2px')
    await expect(ring()).toHaveCSS('pointer-events', 'none')
    await expect(cover()).toHaveCSS('transform', 'none')
    const title = page.locator('.avalon-tile-scrim > strong')
    await expect(title).toHaveCSS('font-size', '15px')
    await expect(title).toHaveCSS('line-height', '18px')
    await expect(title).toHaveCSS('font-weight', '500')
    const r = await ring().boundingBox()
    expect(r!.width).toBe(after.tile.width)
    expect(r!.height).toBe(after.tile.height)
    expect(after.badge!.x - after.tile.x).toBe(8)
    expect(after.badge!.y - after.tile.y).toBeCloseTo(8, 3)
    expect(after.badge!.right).toBeLessThan(after.details.x)
    await tile().screenshot({ path: info.outputPath(`tile-${width}.png`) })
  })
for (const [width, singleStore, played] of [
  [108, true, false],
  [108, true, true],
  [108, false, true],
  [148, true, true],
  [200, false, true],
] as const)
  test(`store words and compact stats never overlap at ${width}px single ${singleStore} played ${played}`, async () => {
    await configure({ width, singleStore, played })
    await cover().hover({ position: { x: width / 2, y: 30 } })
    await revealed()
    const g = await geometry()
    expect(g.scrim.height).toBeLessThanOrEqual(g.tile.height)
    expect(g.scrim.y).toBeGreaterThanOrEqual(g.tile.y)
    for (const chip of g.chips) {
      expect(chip.y).toBeGreaterThanOrEqual(g.stat.bottom)
      expect(chip.right).toBeLessThanOrEqual(g.primary.x - 7)
    }
    expect(Math.max(...g.chips.map((chip) => chip.bottom))).toBeCloseTo(g.primary.bottom, 1)
    expect(await page.locator('.avalon-tile-stat').textContent()).toBe(
      played ? '12345h · idle 10y' : 'never opened',
    )
    await expect(page.locator('.avalon-store-chips > span')).toHaveText(
      singleStore ? ['STEAM'] : ['STEAM', 'GOG', 'EPIC'],
    )
  })
test('hover ring preserves selection, keyboard Tab reveals actions and pointer exit does not pin them', async () => {
  await expect(ring()).toHaveCSS('visibility', 'hidden')
  await cover().hover()
  await revealed()
  await expect(ring()).toHaveCSS('visibility', 'visible')
  await page.mouse.move(800, 600)
  await hidden()
  await expect(ring()).toHaveCSS('visibility', 'hidden')
  await configure({ selected: true })
  await hidden()
  await expect(ring()).toHaveCSS('visibility', 'visible')
  await configure({ selected: false })
  await page.getByRole('button', { name: 'Outside' }).focus()
  await page.keyboard.press('Tab')
  await expect(cover()).toBeFocused()
  await revealed()
  await page.keyboard.press('Tab')
  await expect(primary()).toBeFocused()
  await revealed()
  await page.keyboard.press('Tab')
  await expect(details()).toBeFocused()
  await revealed()
  await page.keyboard.press('Tab')
  await hidden()
  await move(primary())
  await primary().click()
  await revealed()
  await page.mouse.move(800, 600)
  await hidden()
  expect(await counters()).toEqual({ opened: [], launches: [10] })
})
test('the first pointer move exposes a live action target on all twelve immediate presses', async () => {
  const at = await point(primary())
  for (let attempt = 1; attempt <= 12; attempt++) {
    await page.mouse.move(800, 600)
    await hidden()
    await page.mouse.move(at.x, at.y)
    await page.mouse.down()
    await page.mouse.up()
    await expect.poll(async () => (await counters()).launches.length).toBe(attempt)
    expect((await counters()).opened).toEqual([])
  }
})
test('release outside cancels both cover and action activation and never restores stale hover', async () => {
  for (const element of [cover(), primary(), details()]) {
    await cover().hover()
    await revealed()
    const at = await point(element)
    await page.mouse.move(at.x, at.y)
    await page.mouse.down()
    await page.mouse.move(800, 600)
    await page.mouse.up()
    await hidden()
  }
  expect(await counters()).toEqual({ opened: [], launches: [] })
})
test('recycled presses and keyboard action focus cannot transfer to an incoming identity', async () => {
  for (const selector of ['.avalon-cover', '.avalon-tile-primary', '.avalon-tile-details']) {
    await configure({ workId: 1 })
    await cover().hover()
    await revealed()
    const at = await point(page.locator(selector))
    await page.mouse.move(at.x, at.y)
    await page.mouse.down()
    await configure({ workId: 2 })
    await page.mouse.up()
    await hidden()
    expect(await counters()).toEqual({ opened: [], launches: [] })
  }
  await cover().focus()
  await page.keyboard.press('Tab')
  await page.keyboard.press('Tab')
  await expect(details()).toBeFocused()
  await revealed()
  await configure({ workId: 3 })
  await hidden()
  await expect(details()).not.toBeFocused()
  await page.mouse.move(800, 600)
  await move(details())
  await revealed()
  await details().click()
  expect((await counters()).opened).toEqual([3])
})
test('detachment clears outgoing controls and a stationary pointer can reveal the reattached tile', async () => {
  await cover().hover()
  await revealed()
  await configure({ attached: false })
  await expect(tile()).toHaveCount(0)
  await configure({ attached: true })
  await hidden()
  const at = await point(primary())
  await page.mouse.move(at.x, at.y)
  await page.mouse.down()
  await page.mouse.up()
  expect(await counters()).toEqual({ opened: [], launches: [10] })
})
for (const width of [108, 148])
  test(`the entire Details hit area owns repeated opens and cover activation at ${width}px`, async () => {
    await configure({ width })
    for (let attempt = 1; attempt <= 3; attempt++) {
      await page.mouse.move(800, 600)
      await move(details())
      await revealed()
      const owned = await details().evaluate((node) => {
        const r = node.getBoundingClientRect(),
          hits = []
        for (let y = 3; y < r.height - 3; y += 4)
          for (let x = 3; x < r.width - 3; x += 4)
            hits.push(document.elementFromPoint(r.x + x, r.y + y)?.closest('button') === node)
        return hits
      })
      expect(owned.every(Boolean)).toBe(true)
      await details().click({ position: { x: 9, y: 9 } })
      expect((await counters()).opened).toHaveLength(attempt)
    }
    await cover().click({ position: { x: width / 2, y: 70 } })
    expect((await counters()).opened).toEqual([1, 1, 1, 1])
    expect((await counters()).launches).toEqual([])
  })
test('repeated primary clicks and Space belong to the button, while an off-disk copy offers Install', async () => {
  await move(primary())
  await primary().click()
  await primary().click()
  await primary().focus()
  await page.keyboard.press('Space')
  expect(await counters()).toEqual({ opened: [], launches: [10, 10, 10] })
  await configure({ installed: false })
  await expect(primary()).toHaveAccessibleName('Install')
  await expect(primary().locator('[data-glyph="install"]')).toHaveCount(1)
  await primary().press('Enter')
  expect(await counters()).toEqual({ opened: [], launches: [10, 10, 10, 10] })
})
test('expansion marks yield to Play and reduced motion snaps all cover transitions', async () => {
  await configure({ width: 108, expansions: 2, reduced: false })
  await expect(page.locator('.avalon-expansion-mark')).toHaveCSS('opacity', '1')
  await cover().hover()
  await revealed()
  await expect(page.locator('.avalon-expansion-mark')).toHaveCSS('opacity', '0')
  await configure({ reduced: true })
  const durations = await tile().evaluate((node) =>
    [node, ...node.querySelectorAll('*')].map((element) => getComputedStyle(element).transitionDuration),
  )
  expect(durations.every((duration) => duration === '0s')).toBe(true)
  await page.mouse.move(800, 600)
  await hidden()
  await expect(page.locator('.avalon-expansion-mark')).toHaveCSS('opacity', '1')
})
test('fullscreen preserves its separate cover and directional collection path', async () => {
  await configure({ fullscreen: true })
  await expect(tile()).toHaveCount(0)
  await expect(cover()).toHaveCount(1)
  await expect(primary()).toHaveCount(0)
  await expect(details()).toHaveCount(0)
  await cover().focus()
  await page.keyboard.press('ArrowRight')
  await page.keyboard.press('Enter')
  expect(await page.evaluate(() => (window as any).coverProbe.keys)).toContain('ArrowRight')
  expect(await counters()).toEqual({ opened: [1], launches: [] })
})
