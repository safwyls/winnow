import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'
import { resolvedTypography, type ThemeProfile } from '../../src/shared/theme'

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status}`)
    return result.data
  }, input) as Promise<T>
}
async function launch(width: number, height: number, scale = 1, margin = 5) {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-fullscreen-settings-'))
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--no-sync', '--seed-sample'],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  try {
    const page = await application.firstWindow()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({
      timeout: 45000,
    })
    for (const [preference, value] of [
      ['FullscreenTextScale', String(scale)],
      ['FullscreenSafeMargin', String(margin)],
    ])
      await api(page, { route: 'preferences.presentation.put', params: { preference }, body: { value } })
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
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await page.evaluate(() => {
      const controller = { pressed: [] as number[] }
      Object.assign(window, { settingsController: controller })
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: controller.pressed.includes(i) })),
          },
        ],
      })
    })
    const tap = async (button: number) => {
      await page.waitForTimeout(200)
      for (const pressed of [[], [button], []])
        await page.evaluate(async (pressed) => {
          ;(window as any).settingsController.pressed = pressed
          await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
        }, pressed)
    }
    return { application, page, directory, tap }
  } catch (failure) {
    await closeFixture(application, directory)
    throw failure
  }
}

for (const [width, height, scale] of [
  [1280, 720, 1],
  [1920, 1080, 1.4],
] as const)
  test(`fullscreen Appearance keeps source adjustment switch reset and shared cover behavior at ${width}x${height} text ${scale}`, async ({}, info) => {
    const { application, page, directory, tap } = await launch(width, height, scale)
    try {
      const sections = page.getByRole('navigation', { name: 'Settings section' })
      expect(await sections.getByRole('button').allTextContents()).toEqual([
        'Appearance',
        'Controller',
        'Library',
        'Platforms',
        'Metadata & artwork',
        'Plugins',
        'Application',
      ])
      const appearance = page.getByRole('region', { name: 'Fullscreen appearance', exact: true })
      const text = appearance.getByRole('button', { name: 'Text size', exact: true })
      await expect(text).toBeFocused()
      await expect
        .poll(() =>
          text.evaluate((node) => {
            const style = getComputedStyle(node)
            return style.borderBottomColor === style.color && style.borderBottomStyle === 'solid'
          }),
        )
        .toBe(true)
      const previewHeight = await page
        .locator('.fullscreen-settings-preview-cover')
        .evaluate((node) => node.getBoundingClientRect().height)
      expect(previewHeight).toBeCloseTo(320 * Math.min(1, width / 1920), 0)
      await page.screenshot({ path: info.outputPath(`appearance-initial-${width}-${scale}.png`) })
      await tap(14)
      await expect(text).toContainText(`${Math.round((scale - 0.1) * 100)}%`)
      await expect(text).toBeEnabled()
      await expect(text).toBeFocused()
      await tap(15)
      await expect(text).toContainText(`${Math.round(scale * 100)}%`)
      await expect(text).toBeEnabled()
      const toggle = appearance.getByRole('switch', { name: 'Fit ultrawide displays', exact: true })
      expect(
        await toggle
          .locator('.fullscreen-switch-track')
          .evaluate((node) => node.getBoundingClientRect().width),
      ).toBeCloseTo(80 * Math.min(1, width / 1920), 0)
      await toggle.focus()
      await tap(15)
      await expect(toggle).toBeChecked()
      await expect(toggle).toBeEnabled()
      await expect(toggle).toBeFocused()
      await tap(15)
      await expect(toggle).toBeChecked()
      await expect(toggle).toBeFocused()
      await tap(14)
      await expect(toggle).not.toBeChecked()
      await expect(toggle).toBeEnabled()
      await tap(0)
      await expect(toggle).toBeChecked()
      await expect(toggle).toBeEnabled()
      const dim = appearance.getByRole('switch', { name: 'Dim dormant covers', exact: true })
      await dim.focus()
      await tap(14)
      await expect(dim).not.toBeChecked()
      await expect(dim).toBeEnabled()
      await expect.poll(() => page.evaluate(() => document.documentElement.dataset.dimDormant)).toBe('false')
      const cover = appearance.getByRole('button', { name: 'Cover art', exact: true })
      await cover.focus()
      await tap(15)
      await expect(cover).toContainText('Fill')
      await expect(cover).toBeEnabled()
      await expect(cover).toBeFocused()
      await expect
        .poll(() =>
          page.evaluate(() =>
            getComputedStyle(document.documentElement).getPropertyValue('--cover-art-fit').trim(),
          ),
        )
        .toBe('cover')
      await appearance.getByRole('button', { name: 'Theme', exact: true }).click()
      await page
        .getByRole('dialog', { name: 'Theme', exact: true })
        .getByRole('button', { name: 'Nightshift', exact: true })
        .click()
      const theme = appearance.getByRole('button', { name: 'Theme', exact: true })
      await expect(theme).toContainText('Nightshift')
      await expect(theme).toBeFocused()
      await tap(3)
      const reset = page.getByRole('alertdialog', { name: 'Reset fullscreen appearance?' })
      await expect(reset.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
      const preferences = await api<{ preference: string; value: string }[]>(page, {
        route: 'preferences.presentation.get',
      })
      expect(preferences.find((row) => row.preference === 'FullscreenTextScale')?.value).toBe(String(scale))
      await tap(1)
      await expect(reset).toHaveCount(0)
      await expect(
        appearance.getByRole('button', { name: 'Reset fullscreen appearance…', exact: true }),
      ).toBeFocused()
      await tap(3)
      await reset.getByRole('button', { name: 'Reset fullscreen appearance', exact: true }).click()
      await expect(reset).toHaveCount(0)
      await expect(dim).not.toBeChecked()
      await expect(cover).toContainText('Fill')
      await expect(theme).toContainText('Nightshift')
      await expect(text).toContainText('100%')
      await text.scrollIntoViewIfNeeded()
      await expect(page.getByRole('heading', { name: 'Make yourself comfortable' })).toBeInViewport()
      await page.screenshot({ path: info.outputPath(`appearance-${width}-${scale}.png`) })
      const preview = page.getByRole('complementary', { name: 'Appearance preview' })
      await expect(preview).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true)
    } finally {
      await closeFixture(application, directory)
    }
  })

for (const [width, height] of [
  [1280, 720],
  [1920, 1080],
] as const)
  test(`fullscreen theme typography keeps controller focus and remains reachable at maximum sizes on ${width}x${height}`, async ({}, info) => {
    const { application, page, directory, tap } = await launch(width, height, 1.4)
    try {
      const appearance = page.getByRole('region', { name: 'Fullscreen appearance', exact: true })
      const size = appearance.getByRole('button', { name: 'Theme text size', exact: true })
      await size.focus()
      await size.scrollIntoViewIfNeeded()
      await tap(15)
      await expect(size).toContainText('105%')
      await expect(size).toBeFocused()
      for (let i = 0; i < 4; i++) await tap(15)
      await expect(size).toContainText('120%')
      await expect(size).toBeFocused()
      expect(
        (
          await api<{ preference: string; value: string }[]>(page, { route: 'preferences.presentation.get' })
        ).find((row) => row.preference === 'FullscreenTextScale')?.value,
      ).toBe('1.4')
      const heading = appearance.getByRole('button', { name: 'Heading font', exact: true })
      await heading.focus()
      await heading.scrollIntoViewIfNeeded()
      await tap(0)
      const picker = page.getByRole('dialog', { name: 'Heading font', exact: true })
      await picker.getByRole('button', { name: 'IBM Plex Mono', exact: true }).click()
      await expect(picker).toHaveCount(0)
      await expect(heading).toContainText('IBM Plex Mono')
      await expect(heading).toBeFocused()
      await expect
        .poll(
          async () =>
            resolvedTypography((await page.evaluate(() => window.winnow.loadPreferences())) as ThemeProfile)
              .headingFont,
        )
        .toBe('IBM Plex Mono')
      await tap(0)
      await expect(picker).toBeVisible()
      await tap(1)
      await expect(picker).toHaveCount(0)
      await expect(heading).toBeFocused()
      const reset = appearance.getByRole('button', { name: 'Reset theme typography', exact: true })
      await reset.focus()
      await reset.scrollIntoViewIfNeeded()
      await expect(reset).toBeInViewport()
      await expect(page.getByRole('heading', { name: 'Make yourself comfortable' })).toBeInViewport()
      await page.screenshot({ path: info.outputPath(`typography-maximum-${width}.png`) })
      expect(await appearance.evaluate((node) => node.scrollWidth <= node.clientWidth + 1)).toBe(true)
      await tap(0)
      await expect(size).toContainText('100%')
      await expect(heading).toContainText('Bricolage Grotesque')
      await expect(reset).toBeFocused()
    } finally {
      await closeFixture(application, directory)
    }
  })

for (const [width, height, scale, margin] of [
  [1920, 1080, 1, 5],
  [1280, 720, 0.7, 5],
  [1280, 720, 1.4, 5],
  [1280, 720, 1.4, 10],
] as const)
  test(`fullscreen Controller guide retains ten mappings and fits ${width}x${height} text ${scale} margins ${margin}`, async ({}, info) => {
    const { application, page, directory, tap } = await launch(width, height, scale, margin)
    try {
      await tap(7)
      const guide = page.getByRole('region', { name: 'Controller guide' })
      await expect(guide).toBeVisible()
      await expect(
        page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Controller', exact: true }),
      ).toBeFocused()
      await expect(guide.getByRole('definition')).toHaveCount(10)
      for (const label of ['Move', 'Select', 'Tabs & shelves', 'More / filters / reset'])
        await expect(guide.getByText(label, { exact: true })).toBeVisible()
      const bounds = (await guide.boundingBox())!
      await page.screenshot({ path: info.outputPath(`controller-${width}-${scale}-${margin}.png`) })
      const text = await guide.locator('dd, :scope > p').evaluateAll((nodes) =>
        nodes.map((node) => {
          const box = node.getBoundingClientRect()
          return {
            value: node.textContent,
            tag: node.tagName,
            x: box.x,
            y: box.y,
            right: box.right,
            bottom: box.bottom,
            size: parseFloat(getComputedStyle(node).fontSize),
          }
        }),
      )
      for (const item of text) {
        const sourceSize = item.tag === 'P' ? 24 : 28
        expect(item.size, item.value!).toBeCloseTo(sourceSize * Math.min(1, width / 1920) * scale, 1)
        expect(item.x, item.value!).toBeGreaterThanOrEqual(bounds.x - 1)
        expect(item.y, item.value!).toBeGreaterThanOrEqual(bounds.y - 1)
        expect(item.right, item.value!).toBeLessThanOrEqual(bounds.x + bounds.width + 1)
        expect(item.bottom, item.value!).toBeLessThanOrEqual(bounds.y + bounds.height + 1)
      }
      const keyboard = text.find((item) => item.tag === 'P')!
      for (const item of text.filter((item) => item.tag === 'DD'))
        expect(item.bottom, item.value!).toBeLessThanOrEqual(keyboard.y)
      const layout = await page.locator('.fullscreen-controller-page').evaluate((node) => ({
        height: node.clientHeight,
        content: node.scrollHeight,
        children: [...node.children].map((child) => ({
          tag: child.className,
          height: child.getBoundingClientRect().height,
          content: child.scrollHeight,
        })),
      }))
      expect(layout.content, JSON.stringify(layout)).toBeLessThanOrEqual(layout.height + 1)
      const ratio = await guide
        .locator('.controller-guide-art path')
        .first()
        .evaluate((node) => {
          const box = (node as SVGGraphicsElement).getBBox()
          return box.width / box.height
        })
      expect(ratio).toBeGreaterThan(1.4)
      expect(ratio).toBeLessThan(1.5)
      await page.screenshot({ path: info.outputPath(`controller-${width}-${scale}-${margin}.png`) })
      await tap(7)
      await expect(
        page
          .getByRole('navigation', { name: 'Settings section' })
          .getByRole('button', { name: 'Library', exact: true }),
      ).toHaveAttribute('aria-pressed', 'true')
      await expect(page.getByRole('checkbox', { name: 'Ask for a note after playing' })).toBeVisible()
      await tap(6)
      await expect(guide).toBeVisible()
    } finally {
      await closeFixture(application, directory)
    }
  })
