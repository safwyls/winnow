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
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-merge-surface-'))
  const source = relative(directory, resolve('tests/electron/merge-surface-probe-renderer.tsx')).replaceAll(
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
  await expect(page.locator('.merge-card')).toHaveCount(2)
  await page.evaluate(() => document.fonts.ready)
  await page.mouse.move(5, 5)
})
test.afterEach(() => expect(errors).toEqual([]))

for (const zoom of [0.8, 1, 1.4])
  test(`shared desktop sort menu keeps original rows and selected dot inside a lower window corner at scale ${zoom}`, async ({}, info) => {
    await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0].setContentSize(700, 420),
    )
    await page.evaluate((zoom) => {
      document.body.style.zoom = String(zoom)
    }, zoom)
    await page.addStyleTag({
      content: '.shared-sort-trigger { position: fixed; right: 12px; bottom: 12px; z-index: 10; }',
    })
    const trigger = page.getByRole('button', { name: 'Sort · Strongest match', exact: true })
    await trigger.click()
    const menu = page.getByRole('menu', { name: 'Sort order', exact: true })
    await expect(menu.getByRole('menuitemradio')).toHaveText([
      'Strongest match',
      'Playtime at stake',
      'Title',
    ])
    await expect(menu.getByRole('menuitemradio', { name: 'Strongest match', exact: true })).toHaveAttribute(
      'aria-checked',
      'true',
    )
    const geometry = await menu.evaluate((node) => {
      const rect = node.getBoundingClientRect(),
        row = node.querySelector('button')!,
        dot = node.querySelector('.shared-sort-dot')!
      return {
        left: rect.left,
        right: rect.right,
        top: rect.top,
        bottom: rect.bottom,
        width: rect.width,
        rowHeight: row.getBoundingClientRect().height,
        dot: dot.getBoundingClientRect().width,
        border: getComputedStyle(node).borderTopWidth,
        inset: getComputedStyle(node).paddingTop,
        radius: getComputedStyle(node).borderTopLeftRadius,
      }
    })
    expect(geometry.left).toBeGreaterThanOrEqual(8 * zoom - 1)
    expect(geometry.right).toBeLessThanOrEqual(700 - 8 * zoom + 1)
    expect(geometry.top).toBeGreaterThanOrEqual(8 * zoom - 1)
    expect(geometry.bottom).toBeLessThanOrEqual((await trigger.boundingBox())!.y)
    expect(geometry.width).toBeGreaterThanOrEqual(176 * zoom - 1)
    expect(geometry.width).toBeLessThanOrEqual(320 * zoom + 1)
    expect(geometry.rowHeight).toBeCloseTo(30 * zoom, 0)
    expect(geometry.dot).toBeCloseTo(6 * zoom, 0)
    // Chromium snaps this hairline to one device pixel at each tested zoom.
    expect(parseFloat(geometry.border) * zoom).toBeCloseTo(1, 5)
    expect([geometry.inset, geometry.radius]).toEqual(['4px', '4px'])
    await menu.getByRole('menuitemradio', { name: 'Title', exact: true }).click()
    await expect(menu).toHaveCount(0)
    await expect(trigger).toHaveCount(0)
    const selected = page.getByRole('button', { name: 'Sort · Title', exact: true })
    await expect(selected).toBeFocused()
    await selected.click()
    await expect(menu.getByRole('menuitemradio', { name: 'Title', exact: true })).toHaveAttribute(
      'aria-checked',
      'true',
    )
    await expect(menu.getByRole('menuitemradio', { checked: true })).toHaveCount(1)
    await page.screenshot({ path: info.outputPath(`sort-menu-${zoom}.png`) })
  })

test('shared sort menu keyboard and outside dismissal restore the correct focus without writing a merge answer', async () => {
  const trigger = page.getByRole('button', { name: 'Sort · Strongest match', exact: true })
  await trigger.focus()
  await page.keyboard.press('ArrowDown')
  const menu = page.getByRole('menu', { name: 'Sort order', exact: true })
  await expect(menu.getByRole('menuitemradio', { name: 'Strongest match', exact: true })).toBeFocused()
  await page.keyboard.press('End')
  await expect(menu.getByRole('menuitemradio', { name: 'Title', exact: true })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(menu).toHaveCount(0)
  await expect(trigger).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await expect(menu.getByRole('menuitemradio', { name: 'Title', exact: true })).toBeFocused()
  await page.keyboard.press('Home')
  await page.keyboard.press('p')
  await expect(menu.getByRole('menuitemradio', { name: 'Playtime at stake', exact: true })).toBeFocused()
  await page.keyboard.press('Enter')
  const sorted = page.getByRole('button', { name: 'Sort · Playtime at stake', exact: true })
  await expect(sorted).toBeFocused()
  await sorted.click()
  await page.keyboard.press('Tab')
  await expect(menu).toHaveCount(0)
  await expect(page.getByRole('combobox', { name: 'Preferred main platform', exact: true })).toBeFocused()
  await sorted.click()
  await page.getByRole('button', { name: 'Outside the queue', exact: true }).click()
  await expect(menu).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Outside the queue', exact: true })).toBeFocused()
  expect(await page.evaluate(() => (window as any).mergeSurfaceProbe.writes)).toEqual([])
})

test('desktop row hover cross-fades reason ink in 120ms and restores row fill in 140ms without moving cards', async ({}, info) => {
  const card = page.getByRole('article', { name: 'Bastion proposal', exact: true })
  const reason = card.locator('.merge-reason'),
    row = card.locator('.merge-row').nth(1)
  const before = await card.boundingBox()
  const originalText = await reason.textContent(),
    originalColor = await reason.evaluate((node) => getComputedStyle(node).color)
  await page.evaluate(() => {
    ;(window as any).mergeSurfaceProbe.pauseTransitions = true
  })
  await row.locator('.merge-cover').hover()
  await expect(reason).toHaveAttribute('data-row-detail', 'true')
  await expect(reason).not.toHaveText(originalText!)
  await expect.poll(() => page.evaluate(() => (window as any).mergeSurfaceProbe.transitions.length)).toBe(2)
  const transitions = await page.evaluate(() => (window as any).mergeSurfaceProbe.transitions)
  expect(transitions).toEqual(
    expect.arrayContaining([
      { property: 'color', duration: 120, target: 'merge-reason' },
      { property: 'background-color', duration: 140, target: 'merge-row' },
    ]),
  )
  const middleColor = await reason.evaluate((node) => getComputedStyle(node).color)
  expect(middleColor).not.toBe(originalColor)
  await reason.evaluate((node) => node.getAnimations().forEach((animation) => animation.finish()))
  const finalColor = await reason.evaluate((node) => getComputedStyle(node).color)
  expect(middleColor).not.toBe(finalColor)
  expect(await card.boundingBox()).toEqual(before)
  await row.evaluate((node) => node.getAnimations().forEach((animation) => animation.finish()))
  await page.evaluate(() => {
    ;(window as any).mergeSurfaceProbe.pauseTransitions = false
  })
  await page.mouse.move(5, 5)
  await expect(reason).toHaveText(originalText!)
  await expect(reason).toHaveCSS('color', originalColor)
  await expect(row).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  expect(await card.boundingBox()).toEqual(before)
  await page.screenshot({ path: info.outputPath('merge-desktop-surface.png') })
})

test('queue transitions affect only row fill and reason ink and reduced motion removes both', async () => {
  const transitions = await page.locator('.merge-queue').evaluate((queue) =>
    [queue, ...queue.querySelectorAll('*')].flatMap((node) => {
      const css = getComputedStyle(node)
      return css.transitionDuration.split(',').some((duration) => parseFloat(duration) > 0)
        ? [{ className: node.className, property: css.transitionProperty, duration: css.transitionDuration }]
        : []
    }),
  )
  expect(transitions).toHaveLength(6)
  for (const transition of transitions) {
    expect(transition.className).toMatch(/^merge-(row|reason)/)
    const reason = transition.className.split(/\s+/).includes('merge-reason')
    expect(transition.property).toBe(reason ? 'color' : 'background-color')
    expect(transition.duration).toBe(reason ? '0.12s' : '0.14s')
  }
  await page.evaluate(() => (window as any).mergeSurfaceProbe.configure({ reduced: true }))
  const card = page.getByRole('article', { name: 'Bastion proposal', exact: true }),
    reason = card.locator('.merge-reason')
  await card.locator('.merge-row').nth(1).hover()
  await expect(reason).toHaveAttribute('data-row-detail', 'true')
  await expect(reason).toHaveCSS('transition-duration', '0s')
  await expect(card.locator('.merge-row').nth(1)).toHaveCSS('transition-duration', '0s')
  expect(await page.evaluate(() => document.getAnimations().length)).toBe(0)
})

test('row pointer and keyboard cursor keep promotion details and inclusion independent', async () => {
  const card = page.getByRole('article', { name: 'Bastion proposal', exact: true }),
    rows = card.locator('.merge-row')
  await rows.nth(1).locator('.merge-cover').click()
  await expect(rows.nth(1).getByRole('radio')).toBeChecked()
  await rows.first().getByRole('checkbox').uncheck()
  await expect(
    card.getByRole('button', { name: 'Same game: Bastion (GOG), under Bastion', exact: true }),
  ).toBeDisabled()
  await rows.first().getByRole('button', { name: 'Details for Bastion (Steam)', exact: true }).click()
  expect(await page.evaluate(() => (window as any).mergeSurfaceProbe.opened)).toEqual([1])
  await expect(rows.nth(1).getByRole('radio')).toBeChecked()
  await expect(rows.first().locator('[data-merge-row]')).toBeFocused()
  await page.keyboard.press('Space')
  await expect(rows.first().getByRole('radio')).toBeChecked()
  await page.keyboard.press('ArrowDown')
  await expect(rows.nth(1).locator('[data-merge-row]')).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await expect(rows.first().locator('[data-merge-row]')).toBeFocused()
  await expect(
    card.getByRole('button', {
      name: 'Same game: Bastion (Steam), Bastion (GOG), under Bastion',
      exact: true,
    }),
  ).toBeEnabled()
  expect(await page.evaluate(() => (window as any).mergeSurfaceProbe.writes)).toEqual([])
})

test('Flare marks unread rows and their group only and disappears from the group when its patched member is excluded', async () => {
  const patched = page.getByRole('article', { name: 'Prey 2006 proposal', exact: true })
  await expect(page.locator('.merge-unread')).toHaveCount(2)
  const flare = await patched
    .locator('.merge-unread')
    .first()
    .evaluate((node) => getComputedStyle(node).backgroundColor)
  for (const dot of await patched.locator('.merge-unread').all()) {
    await expect(dot).toHaveAttribute('title', 'Patched since you played')
    await expect(dot).toHaveAttribute('aria-label', 'Patched since you played')
  }
  const uses = await page.locator('.merge-queue').evaluate(
    (queue, flare) =>
      [...queue.querySelectorAll('*')]
        .filter((node) => {
          const css = getComputedStyle(node)
          return [css.color, css.backgroundColor, css.borderTopColor].includes(flare)
        })
        .map((node) => node.className),
    flare,
  )
  expect(uses).toEqual(['merge-unread', 'merge-unread'])
  await patched.getByRole('radio', { name: 'Make Prey 2017 the main game', exact: true }).check()
  const promoted = page.getByRole('article', { name: 'Prey 2017 proposal', exact: true })
  await promoted.getByRole('checkbox', { name: 'Include Prey 2006', exact: true }).uncheck()
  await expect(promoted.locator('header .merge-unread')).toHaveCount(0)
  await expect(promoted.locator('.merge-row .merge-unread')).toHaveCount(1)
})

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} named answers identify the exact group and one Undo restores it`, async ({}, info) => {
    await page.evaluate((mode) => (window as any).mergeSurfaceProbe.configure({ mode }), mode)
    const card = page.getByRole('article', { name: 'Bastion proposal', exact: true })
    if (mode === 'fullscreen') await card.getByRole('button').click()
    const scope = mode === 'fullscreen' ? page.getByRole('dialog') : card
    const same = scope.getByRole('button', {
      name: 'Same game: Bastion (Steam), Bastion (GOG), under Bastion',
      exact: true,
    })
    await expect(same).toHaveText('Same game')
    await expect(same).toHaveAttribute('title', 'Nest the other rows under the header (S)')
    await expect(
      scope.getByRole('button', { name: 'Different games: Bastion (Steam), Bastion (GOG)', exact: true }),
    ).toHaveAttribute('title', 'Leave them separate, not asked again (D)')
    await page.screenshot({ path: info.outputPath(`merge-${mode}-answers.png`) })
    await same.click()
    if (mode === 'fullscreen') await page.getByRole('button', { name: 'Continue', exact: true }).click()
    const saved = page.getByRole('article', { name: 'Bastion saved group', exact: true })
    await expect(saved).toBeVisible()
    if (mode === 'fullscreen') await saved.getByRole('button').click()
    await expect(page.getByRole('button', { name: 'Separate again: Bastion', exact: true })).toHaveAttribute(
      'title',
      'Undo this roll-up. Nothing was deleted.',
    )
    if (mode === 'fullscreen')
      await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
    const dock = page.getByRole('status', { name: 'Review undo', exact: true })
    await expect(dock.getByRole('button')).toHaveCount(2)
    await expect(dock.locator('strong')).toHaveText('Rolled up under Bastion.')
    await expect(dock.locator('.merge-dock-note')).toContainText('nothing was deleted')
    await expect(dock.getByRole('button', { name: 'Dismiss review undo', exact: true })).toHaveAttribute(
      'title',
      'Dismiss',
    )
    const undo = dock.getByRole('button', { name: 'Undo review decisions', exact: true })
    await expect(undo).toHaveAttribute('title', 'Put it back the way it was')
    await undo.click()
    await expect(card).toBeVisible()
    expect(
      await page.evaluate(() =>
        (window as any).mergeSurfaceProbe.writes.map((input: { route: string }) => input.route),
      ),
    ).toEqual(['identity.link', 'identity.undo'])
  })
}
