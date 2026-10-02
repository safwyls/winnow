import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeEach(async ({}, info) => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-information-'))
  errors.length = 0
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-information-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_LONG_NOTE: info.title.includes('reading') ? '1' : '0',
    },
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
})
test.afterEach(async () => {
  await closeFixture(application, directory)
  expect(errors).toEqual([])
})
async function surface(mode: 'desktop' | 'fullscreen', width: number, height: number) {
  await application.evaluate(
    ({ BrowserWindow }, { mode, width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setContentSize(width, height)
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Activity', exact: true })
    .click()
  await expect(page.locator('.activity-history .timeline-entry')).toHaveCount(1)
}

for (const [width, height] of [
  [1920, 1080],
  [1280, 720],
])
  test(`fullscreen reading keeps the full note bounded and Back stationary at ${width}x${height}`, async ({}, info) => {
    await surface('fullscreen', width, height)
    await page.evaluate(async () => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenSafeMargin' },
        body: { value: '10' },
      })
      if (!result.ok) throw Error(result.message)
    })
    await expect(page.locator('html')).toHaveCSS('--fullscreen-safe-ratio', '0.1')
    const origin = page.getByRole('button', { name: 'Read note', exact: true })
    await origin.click()
    const reader = page.locator('.fullscreen-note-reading')
    const back = reader.getByRole('button', { name: 'Back', exact: true })
    const scroll = reader.getByRole('region', { name: 'Session note' })
    await expect(back).toBeFocused()
    const bounds = (await back.boundingBox())!
    expect(Math.abs(bounds.x - width * 0.1)).toBeLessThan(1)
    expect(Math.abs(bounds.y - height * 0.1)).toBeLessThan(1)
    const metrics = await scroll.evaluate((node) => ({
      width: node.querySelector('p')!.getBoundingClientRect().width,
      logicalWidth: (node.querySelector('p') as HTMLElement).offsetWidth,
      scrolls: node.scrollHeight > node.clientHeight,
      paragraphs: node.textContent!.split('\n\n').length,
      backOutside: !node.contains(document.querySelector('.fullscreen-note-reading button')),
    }))
    expect(metrics.logicalWidth).toBeLessThanOrEqual(1200)
    expect(metrics.logicalWidth / Math.min(1, width / 1920)).toBeLessThanOrEqual(1201)
    expect(metrics.width).toBeLessThan(width)
    expect(metrics).toMatchObject({ scrolls: true, paragraphs: 24, backOutside: true })
    await page.keyboard.press('ArrowDown')
    await expect.poll(() => scroll.evaluate((node) => node.scrollTop)).toBeGreaterThan(0)
    await expect(back).toBeFocused()
    expect(await back.boundingBox()).toEqual(bounds)
    await page.keyboard.press('ArrowUp')
    await expect.poll(() => scroll.evaluate((node) => node.scrollTop)).toBe(0)
    await expect(back).toBeFocused()
    await page.screenshot({ path: info.outputPath(`reading-${width}.png`) })
    await page.keyboard.press('Escape')
    await expect(reader).toHaveCount(0)
    await expect(origin).toBeFocused()
  })

test('fullscreen Activity separates the selected session title date and note, then shows the empty update week', async ({}, info) => {
  await surface('fullscreen', 1920, 1080)
  const row = page.locator('.activity-history .timeline-entry')
  await expect(row).toHaveAttribute('aria-current', 'true')
  const title = row.locator('.text-button')
  await expect(title).toHaveCSS('font-size', '32px')
  await expect(title).toHaveCSS('font-weight', '700')
  await expect(row.locator('time')).toHaveCSS('font-size', '22px')
  await expect(page.getByRole('heading', { name: 'Your note', exact: true })).toHaveCSS(
    'text-transform',
    'uppercase',
  )
  await expect(page.getByRole('heading', { name: 'Your note', exact: true })).toHaveCSS('font-weight', '400')
  await expect(page.getByRole('heading', { name: 'Your note', exact: true })).toHaveCSS('font-size', '18px')
  const bodyFont = await title.evaluate((node) => getComputedStyle(node).fontFamily)
  await expect(page.getByRole('heading', { name: 'Your note', exact: true })).toHaveCSS(
    'font-family',
    bodyFont,
  )
  await expect(page.getByRole('complementary', { name: 'Selected activity' })).toContainText(
    'Found a new path through the old ruins.',
  )
  await row.focus()
  await page.screenshot({ path: info.outputPath('fullscreen-activity.png') })
  await page
    .getByRole('navigation', { name: 'Activity type' })
    .getByRole('button', { name: 'Updates', exact: true })
    .click()
  await expect(page.getByText('No updates this week', { exact: true })).toBeVisible()
})

test('desktop reading preserves its note dialog and restores the invoking action', async ({}, info) => {
  await surface('desktop', 1280, 820)
  await expect(page.locator('.avalon-ambient-backdrop')).toHaveCount(0)
  const origin = page.getByRole('button', { name: 'Read note', exact: true })
  await origin.click()
  await expect(page.locator('.fullscreen-note-reading')).toHaveCount(0)
  const dialog = page.getByRole('dialog')
  await expect(dialog.locator('blockquote')).toContainText('A journal entry about returning')
  await page.screenshot({ path: info.outputPath('desktop-note.png') })
  await dialog.getByRole('button', { name: 'Close note', exact: true }).click()
  await expect(origin).toBeFocused()
})

for (const [width, fit] of [
  [1920, false],
  [2560, true],
] as const)
  test(`every root backdrop fills the canvas beyond ten percent safe margins at ${width}`, async ({}, info) => {
    await surface('fullscreen', width, 1080)
    await page.evaluate(async (fit) => {
      for (const [preference, value] of [
        ['FullscreenSafeMargin', '10'],
        ['FullscreenFitUltrawide', String(fit)],
      ]) {
        const result = await window.winnow.request({
          route: 'preferences.presentation.put',
          params: { preference },
          body: { value },
        })
        if (!result.ok) throw Error(result.message)
      }
    }, fit)
    await expect(page.locator('html')).toHaveCSS('--fullscreen-safe-margin', '10%')
    for (const name of ['For you', 'Library', 'Activity', 'Settings']) {
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name, exact: true })
        .click()
      const metrics = await page.locator('.avalon-shell').evaluate((node) => {
        const shell = node.getBoundingClientRect(),
          content = node.querySelector('#main-content')!.getBoundingClientRect()
        const backdrop = node
          .querySelector('.avalon-home-backdrop,.avalon-library-backdrop,.avalon-ambient-backdrop')
          ?.getBoundingClientRect()
        return {
          shell: { x: shell.x, y: shell.y, width: shell.width, height: shell.height },
          content: { x: content.x, y: content.y },
          color: getComputedStyle(node).backgroundColor,
          backdrop: backdrop
            ? { x: backdrop.x, y: backdrop.y, width: backdrop.width, height: backdrop.height }
            : null,
        }
      })
      for (const [key, value] of Object.entries({ x: 0, y: 0, width, height: 1080 }))
        expect(Math.abs(metrics.shell[key as keyof typeof metrics.shell] - value)).toBeLessThan(1)
      expect(metrics.content.x).toBeGreaterThanOrEqual(width * 0.1 - 1)
      expect(metrics.content.y).toBeGreaterThan(108)
      expect(metrics.color).not.toBe('rgba(0, 0, 0, 0)')
      expect(metrics.backdrop).not.toBeNull()
      expect(metrics.backdrop).toEqual(metrics.shell)
      if (name === 'Activity' || name === 'Settings') {
        const decoration = page.locator(
          `.avalon-ambient-backdrop[data-ambient-page="${name === 'Activity' ? 'journal' : 'settings'}"]`,
        )
        await expect(decoration.locator('svg')).toBeVisible()
        expect(await decoration.locator('path').count()).toBeGreaterThan(0)
        await expect(decoration).toHaveAttribute('aria-hidden', 'true')
      }
      await page.screenshot({ path: info.outputPath(`backdrop-${name}.png`) })
    }
  })
