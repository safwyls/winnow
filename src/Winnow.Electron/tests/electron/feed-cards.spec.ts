import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, relative, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page
const errors: string[] = []
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-feed-cards-'))
  const source = relative(directory, resolve('tests/electron/feed-cards-probe-renderer.tsx')).replaceAll(
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
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].setContentSize(1400, 800),
  )
  await page.reload()
  await expect(page.locator('.avalon-feed-card')).toHaveCount(1)
  await page.evaluate(() => document.fonts.ready)
  await page.mouse.move(1200, 700)
})
test.afterEach(() => expect(errors).toEqual([]))
const cover = () => page.locator('.avalon-cover').first()
const frame = () => page.locator('.avalon-desktop-cover').first()
const card = () => page.locator('.avalon-feed-card').first()
const actions = () => page.locator('.avalon-feed-card-actions').first()
async function configure(value: Record<string, number | boolean>) {
  await page.evaluate(async (patch) => {
    ;(window as any).feedCardProbe.configure(patch)
    await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  }, value)
}
async function counters() {
  return page.evaluate(() => {
    const p = (window as any).feedCardProbe
    return {
      opened: p.opened,
      launches: p.launches,
      feedback: p.feedback,
      requests: p.requests,
      cancelled: p.cancelled,
      heroes: p.heroes,
    }
  })
}
async function geometry() {
  return card().evaluate((node) => {
    const rect = (element: Element) => {
      const r = element.getBoundingClientRect()
      return { x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom }
    }
    const part = (selector: string) => rect(node.querySelector(selector)!)
    return {
      card: rect(node),
      frame: part('.avalon-desktop-cover'),
      face: part('.avalon-cover'),
      primary: part('.avalon-tile-primary'),
      details: part('.avalon-tile-details'),
      ring: part('.avalon-tile-ring'),
      scrim: part('.avalon-tile-scrim'),
      actions: part('.avalon-feed-card-actions'),
      caption: part('.avalon-feed-card-caption'),
    }
  })
}
test('hover lifts the complete feed cover once and keeps its chrome aligned', async () => {
  const before = await geometry()
  expect(before.frame.y - before.card.y).toBe(2)
  await cover().hover({ position: { x: 50, y: 50 } })
  const after = await geometry()
  expect(after.frame.y - before.frame.y).toBe(-2)
  expect(after.face.y).toBe(after.ring.y)
  expect(after.details.y).toBeGreaterThan(after.ring.y)
  expect(after.scrim.bottom).toBe(after.actions.y)
  expect(after.card).toEqual(before.card)
  await expect(cover()).toHaveCSS('transform', 'none')
})
for (const [width, installed] of [
  [180, false],
  [240, true],
] as const) {
  test(`shared feed cover and independent launch details caption targets scale at ${width}`, async ({}, info) => {
    await configure({ width, installed })
    await cover().focus()
    let boxes = await geometry()
    expect(boxes.frame.height).toBe(width * 1.5)
    expect(boxes.primary.bottom).toBeLessThanOrEqual(boxes.actions.y)
    await page.getByRole('button', { name: installed ? 'Play' : 'Install', exact: true }).click()
    expect((await counters()).launches).toEqual([10])
    expect((await counters()).opened).toEqual([])
    await page.mouse.move(1200, 700)
    await page.getByRole('button', { name: 'Outside' }).focus()
    await expect(actions()).toHaveCSS('opacity', '0')
    await expect(actions()).toHaveCSS('pointer-events', 'none')
    await cover().hover({ position: { x: 50, y: 50 } })
    await page.getByRole('button', { name: 'Details', exact: true }).click()
    await cover().click({ position: { x: width / 2, y: 70 } })
    await page.locator('.avalon-feed-card-caption').click({ position: { x: width / 2, y: 12 } })
    expect((await counters()).opened).toEqual([1, 1, 1])
    await configure({ width: width + 20 })
    expect((await geometry()).frame.height).toBe((width + 20) * 1.5)
    await cover().hover({ position: { x: 50, y: 50 } })
    await card().screenshot({ path: info.outputPath(`feed-card-${width}.png`) })
  })
  test(`portrait actions stay measured named and keyboard reachable at ${width}`, async () => {
    await configure({ width, installed })
    const before = await geometry()
    await cover().focus()
    expect((await geometry()).card).toEqual(before.card)
    expect((await geometry()).frame.y).toBe(before.frame.y)
    await expect(page.locator('.avalon-feed-card-caption > strong')).toHaveCSS('height', '19px')
    await expect(page.locator('.avalon-feed-card-caption > span')).toHaveCSS('height', '36px')
    await expect(page.locator('.avalon-feed-card-caption > strong')).toHaveCSS('font-weight', '700')
    const titleBox = (await page.locator('.avalon-feed-card-caption > strong').boundingBox())!
    expect(titleBox.y).toBe(before.frame.bottom + 8)
    const buttons = actions().getByRole('button')
    expect(await buttons.count()).toBe(3)
    let right = 0,
      top = -1
    for (const [index, name] of ['Add to list', 'Not now', 'Not interested'].entries()) {
      const button = buttons.nth(index),
        box = (await button.boundingBox())!
      await expect(button).toHaveAccessibleName(name)
      await expect(button).toHaveAttribute('title', name)
      expect(box.width).toBe(36)
      expect(box.height).toBe(36)
      expect(box.x).toBeGreaterThanOrEqual(Math.max(right, before.card.x))
      expect(box.x + box.width).toBeLessThanOrEqual(before.card.right)
      if (top >= 0) expect(box.y).toBe(top)
      right = box.x + box.width
      top = box.y
      await button.focus()
      await expect(button).toBeFocused()
    }
    const inks = await buttons.evaluateAll((nodes) =>
      nodes.map((node, i) => {
        const sample = document.createElement('span')
        sample.style.color = ['var(--cool-foreground)', 'var(--avalon-amber-foreground)', 'var(--muted)'][i]
        node.append(sample)
        const expected = getComputedStyle(sample).color,
          actual = getComputedStyle(node.querySelector('svg')!).color
        sample.remove()
        return { expected, actual }
      }),
    )
    for (const ink of inks) expect(ink.actual).toBe(ink.expected)
    expect((await geometry()).card).toEqual(before.card)
  })
}
test('feed action pointer press has no border while keyboard focus uses Volt', async () => {
  await cover().hover({ position: { x: 40, y: 40 } })
  const add = actions().locator('button').first()
  await expect(add).toHaveAccessibleName('Add to list')
  await add.hover()
  await page.mouse.down()
  await expect(add).toHaveCSS('border-color', 'rgba(0, 0, 0, 0)')
  await page.mouse.up()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(add).toHaveCSS('border-color', 'rgba(0, 0, 0, 0)')
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(add).toBeFocused()
  await page.mouse.move(1200, 700)
  await cover().focus()
  await page.keyboard.press('Tab')
  await add.focus()
  await expect(add).toBeFocused()
  const border = await add.evaluate((node) => {
    const span = document.createElement('span')
    span.style.color = 'var(--accent-foreground)'
    node.append(span)
    const value = getComputedStyle(span).color
    span.remove()
    return value
  })
  await expect(add).toHaveCSS('border-color', border)
})
for (const [x, y] of [
  [20, 340],
  [650, 340],
  [650, -100],
]) {
  test(`preview remains within the client area for edge tile at ${x},${y}`, async () => {
    await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0].setContentSize(900, 400),
    )
    await configure({ x, y })
    await page.mouse.move(x + 40, Math.max(40, y + 20))
    const bubble = page.getByRole('tooltip')
    await expect(bubble).toBeVisible()
    const box = (await bubble.boundingBox())!
    expect(box.x).toBeGreaterThanOrEqual(8)
    expect(box.y).toBeGreaterThanOrEqual(8)
    expect(box.x + box.width).toBeLessThanOrEqual(892)
    expect(box.y + box.height).toBeLessThanOrEqual(392)
  })
}
for (const action of ['Add to list', 'Not now', 'Not interested']) {
  test(`${action} leaves Details closed and retains fixed geometry through receipt and Undo`, async ({}, info) => {
    await cover().hover({ position: { x: 40, y: 40 } })
    await expect(page.getByRole('tooltip')).toBeVisible()
    const before = (await geometry()).card
    await page.getByRole('button', { name: action, exact: true }).click()
    expect((await counters()).opened).toEqual([])
    await expect(page.getByRole('tooltip')).toHaveCount(0)
    if (action === 'Add to list') {
      await expect(page.getByRole('dialog')).toBeVisible()
      expect((await counters()).feedback).toEqual([])
      await page.getByRole('button', { name: 'Cancel', exact: true }).click()
    } else {
      await expect(page.getByRole('status')).toContainText(action === 'Not now' ? 'Back on' : 'Off the feed.')
      await expect(page.getByRole('progressbar')).toHaveCount(1)
      if (action === 'Not now')
        await expect(page.locator('.avalon-feed-card-receipt time')).toHaveCSS('display', 'block')
      if (action === 'Not now') await card().screenshot({ path: info.outputPath('feed-receipt.png') })
      expect(await card().boundingBox()).toEqual({
        x: before.x,
        y: before.y,
        width: before.width,
        height: before.height,
      })
      await page.getByRole('button', { name: 'Undo', exact: true }).click()
      await expect(page.getByRole('status')).toHaveCount(0)
      expect((await counters()).feedback).toEqual([
        { releaseId: 10, kind: action === 'Not now' ? 1 : 0, undo: false },
        { releaseId: 10, kind: action === 'Not now' ? 1 : 0, undo: true },
      ])
    }
    expect((await geometry()).card).toEqual(before)
    expect((await counters()).opened).toEqual([])
  })
}
for (const key of ['pointer', 'Enter', 'Space']) {
  test(`card ${key} activation opens Details directly and closes the preview`, async () => {
    await cover().hover({ position: { x: 40, y: 40 } })
    if (key === 'pointer') await cover().click({ position: { x: 40, y: 40 } })
    else {
      await cover().focus()
      await page.keyboard.press(key)
    }
    expect((await counters()).opened).toEqual([1])
    await expect(page.getByRole('tooltip')).toHaveCount(0)
  })
}
for (const change of ['same-work rebind', 'other-work rebind', 'detach']) {
  test(`${change} releases the open preview and its in-flight request`, async () => {
    await configure({ deferDetails: true })
    await cover().hover({ position: { x: 40, y: 40 } })
    await expect(page.getByRole('tooltip')).toBeVisible()
    expect((await counters()).heroes).toBe(1)
    await configure(
      change === 'detach'
        ? { attached: false }
        : change === 'same-work rebind'
          ? { version: 1 }
          : { workId: 2 },
    )
    await expect(page.getByRole('tooltip')).toHaveCount(0)
    const state = await counters()
    expect(state.heroes).toBe(0)
    expect(state.cancelled).toEqual(state.requests)
    await page.evaluate(() => (window as any).feedCardProbe.resolveDetails())
    await expect(page.getByRole('tooltip')).toHaveCount(0)
  })
}
test('Escape closes the preview while card focus stays put', async () => {
  await cover().focus()
  await cover().hover({ position: { x: 40, y: 40 } })
  await expect(page.getByRole('tooltip')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('tooltip')).toHaveCount(0)
  await cover().hover({ position: { x: 42, y: 42 } })
  await expect(page.getByRole('tooltip')).toHaveCount(0)
  await expect(cover()).toBeFocused()
  await page.getByRole('button', { name: 'Outside' }).focus()
  await expect(cover()).not.toBeFocused()
})
for (const overPreview of [false, true]) {
  test(`preview opens without delay or focus and exits even over bubble ${overPreview}`, async () => {
    await configure({ deferDetails: true })
    expect((await counters()).requests).toEqual([])
    await cover().hover({ position: { x: 40, y: 40 } })
    const bubble = page.getByRole('tooltip')
    await expect(bubble).toBeVisible()
    await expect(cover()).toHaveAccessibleDescription(
      /Last played on 7 Sep 2026\. A reason kept beneath the cover\./,
    )
    await expect(cover()).not.toBeFocused()
    expect((await counters()).requests).toHaveLength(1)
    await expect(bubble.getByRole('button')).toHaveCount(0)
    await expect(bubble).not.toContainText('WHY THIS GAME')
    await expect(bubble).not.toContainText('A reason kept beneath the cover')
    if (overPreview) {
      const box = (await bubble.boundingBox())!
      await page.mouse.move(box.x + 40, box.y + 40)
    } else await page.mouse.move(1200, 700)
    await expect(bubble).toHaveCount(0)
    await expect(cover()).toHaveAccessibleDescription(
      'Last played on 7 Sep 2026. A reason kept beneath the cover.',
    )
    const state = await counters()
    expect(state.cancelled).toEqual(state.requests)
    expect(state.heroes).toBe(0)
    expect(state.opened).toEqual([])
  })
}
test('hovering another card replaces the preview without a click', async () => {
  await configure({ second: true })
  await cover().hover({ position: { x: 40, y: 40 } })
  await expect(page.getByRole('tooltip')).toContainText('Aloft')
  await page
    .locator('.avalon-cover')
    .nth(1)
    .hover({ position: { x: 40, y: 40 } })
  await expect(page.getByRole('tooltip')).toHaveCount(1)
  await expect(page.getByRole('tooltip')).toContainText('Second game 2')
  expect((await counters()).opened).toEqual([])
})
test('recently played retains only Add to list and the shared compact launch', async () => {
  await configure({ feedback: false })
  await cover().focus()
  await expect(actions().getByRole('button')).toHaveCount(1)
  await expect(actions().getByRole('button')).toHaveAccessibleName('Add to list')
  await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Not now', exact: true })).toHaveCount(0)
})
