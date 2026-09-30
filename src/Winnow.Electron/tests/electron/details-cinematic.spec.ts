import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
type Request = { provider: string; id: string; width: number }
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-details-cinematic-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/details-cinematic-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus) await page.screenshot({ path: info.outputPath('failure.png') })
  expect(errors).toEqual([])
})
async function size(width: number, height: number, enterFullscreen = false) {
  await application.evaluate(
    ({ BrowserWindow }, { width, height, enterFullscreen }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      if (enterFullscreen) window.setFullScreen(false)
      window.setContentSize(width, height)
      window.focus()
      if (enterFullscreen) window.webContents.send('winnow:fullscreen:changed', true)
    },
    { width, height, enterFullscreen },
  )
  await expect.poll(() => page.evaluate(() => [innerWidth, innerHeight])).toEqual([width, height])
}
async function scales(text: number, ui: number) {
  await page.evaluate(
    async ({ text, ui }) => {
      for (const [preference, value] of [
        ['FullscreenTextScale', text],
        ['FullscreenInterfaceScale', ui],
        ['FullscreenSafeMargin', 3],
      ] as const) {
        const result = await window.winnow.request({
          route: 'preferences.presentation.put',
          params: { preference },
          body: { value: String(value) },
        })
        if (!result.ok) throw Error(result.message)
      }
    },
    { text, ui },
  )
  await expect
    .poll(() =>
      page.evaluate(() => [
        document.documentElement.style.getPropertyValue('--fullscreen-text-scale'),
        document.documentElement.style.getPropertyValue('--fullscreen-interface-scale'),
        document.documentElement.style.getPropertyValue('--fullscreen-safe-margin'),
      ]),
    )
    .toEqual([String(text), String(ui), '3%'])
}
const requests = () => application.evaluate(() => (globalThis as any).cinematicFixture.requests as Request[])
async function withinReading(control: Locator, reading: Locator) {
  const [box, viewport] = await Promise.all([control.boundingBox(), reading.boundingBox()])
  expect(box!.y).toBeGreaterThanOrEqual(viewport!.y - 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport!.y + viewport!.height + 1)
}
async function previewEdges(image: Locator) {
  const screenshot = await image.screenshot()
  const samples = await application.evaluate(({ nativeImage }, base64) => {
    const bitmap = nativeImage.createFromBuffer(Buffer.from(base64, 'base64'))
    const { width, height } = bitmap.getSize(),
      pixels = bitmap.toBitmap()
    return [
      [0.5, 0.01],
      [0.5, 0.99],
      [0.005, 0.5],
      [0.995, 0.5],
    ].map(([x, y]) => {
      const offset = (Math.floor(y! * height) * width + Math.floor(x! * width)) * 4
      return [pixels[offset + 2], pixels[offset + 1], pixels[offset]]
    })
  }, screenshot.toString('base64'))
  for (const [index, expected] of [
    [255, 0, 0],
    [0, 255, 0],
    [0, 0, 255],
    [255, 255, 0],
  ].entries())
    expected.forEach((channel, i) => expect(Math.abs(samples[index]![i]! - channel)).toBeLessThanOrEqual(8))
}

for (const [userBackground, longTitle, hasJournal, uiScale] of [
  [true, false, false, 1],
  [false, false, false, 1],
  [true, true, false, 1],
  [true, false, true, 1],
  [true, false, false, 0.8],
  [true, false, true, 0.8],
] as const) {
  test(`cinematic Details preserves ${userBackground ? 'saved' : 'automatic'} landscape, ${longTitle ? 'long' : 'short'} title, journal ${hasJournal} and interface ${uiScale}`, async ({}, info) => {
    await application.evaluate(
      ({}, options) =>
        Object.assign((globalThis as any).cinematicFixture, options, { requests: [], reads: 0 }),
      { userBackground, longTitle, hasJournal },
    )
    await page.reload()
    await expect(page.locator('.avalon-cover').first()).toBeVisible()
    await size(2560, 1440, true)
    await scales(1, uiScale)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await page.evaluate(() => {
      const live = new Set<string>(),
        create = URL.createObjectURL.bind(URL),
        revoke = URL.revokeObjectURL.bind(URL)
      ;(window as any).cinematicUrls = live
      URL.createObjectURL = (value) => {
        const url = create(value)
        live.add(url)
        return url
      }
      URL.revokeObjectURL = (url) => {
        live.delete(url)
        revoke(url)
      }
    })
    await page.locator('.avalon-cover').first().click()
    const details = page.locator('.avalon-details'),
      reading = details.locator('.avalon-details-reading')
    const backdrop = details.locator('.avalon-backdrop'),
      previews = details.locator('.screenshot-previews > button')
    const key = userBackground ? 'user:landscape' : 'steam-hero:42'
    await expect(backdrop.locator(`[data-key="${key}"] img`)).toBeVisible()
    await expect(previews.locator('img')).toHaveCount(2)
    await expect
      .poll(() =>
        previews
          .locator('img')
          .evaluateAll((nodes) => nodes.every((node) => (node as HTMLImageElement).naturalWidth === 1600)),
      )
      .toBe(true)
    await expect(
      details.locator('.avalon-details-actions').getByRole('button', { name: 'Play', exact: true }),
    ).toBeFocused()
    const geometry = await details.evaluate((node) => {
      const rect = (selector: string) => node.querySelector(selector)!.getBoundingClientRect().toJSON()
      const style = (selector: string) => getComputedStyle(node.querySelector(selector)!)
      return {
        backdrop: rect('.avalon-backdrop'),
        personal: rect('.avalon-personal-history'),
        about: rect('.avalon-about'),
        tabsRule: style('.avalon-details-tabs').borderBottomWidth,
        columnRule: style('.avalon-personal-history').borderRightWidth,
        journalRule: style('.avalon-latest-note').borderTopWidth,
        journal: rect('.avalon-latest-note'),
        history: rect('[data-details-reading="History"]'),
        primary: [style('.primary-button').backgroundColor, style('.primary-button').color],
      }
    })
    expect(geometry.backdrop.x).toBeCloseTo(0, 0)
    expect(geometry.backdrop.y).toBeCloseTo(0, 0)
    expect(geometry.backdrop.width).toBeCloseTo(2560, 0)
    expect(geometry.backdrop.height).toBeCloseTo(1440, 0)
    expect(geometry.about.width).toBeGreaterThan(geometry.personal.width * 2)
    for (const rule of [geometry.tabsRule, geometry.columnRule, geometry.journalRule])
      expect(parseFloat(rule) * uiScale).toBeGreaterThanOrEqual(0.99)
    expect(geometry.journal.width / uiScale).toBeGreaterThan(250)
    expect(geometry.journal.y).toBeGreaterThan(geometry.history.y)
    expect(geometry.primary[0]).not.toBe('rgba(0, 0, 0, 0)')
    expect(geometry.primary[0]).not.toBe(geometry.primary[1])
    await expect(details.locator('.avalon-details-header img')).toHaveCount(0)
    expect(await application.evaluate(() => (globalThis as any).cinematicFixture.reads)).toBe(1)
    const veils = await backdrop
      .locator('.avalon-backdrop-veil')
      .evaluate((node) => getComputedStyle(node).backgroundImage)
    expect(veils).toContain('55%')
    expect(
      await backdrop
        .locator('.avalon-backdrop-reading-veil')
        .evaluate((node) => getComputedStyle(node).backgroundImage),
    ).toContain('32%')
    for (const preview of await previews.all()) {
      const image = preview.locator('img'),
        box = (await image.boundingBox())!
      expect(box.width / uiScale).toBeGreaterThan(longTitle ? 300 : 400)
      expect(box.width / box.height).toBeCloseTo(16 / 9, 1)
      expect(await image.evaluate((node) => getComputedStyle(node).objectFit)).toBe('contain')
      if (!longTitle) await withinReading(preview, reading)
      await previewEdges(image)
      expect(
        (await requests()).some((request) => request.id === 'detailshot' && request.width >= box.width - 1),
      ).toBe(true)
    }
    if (!longTitle)
      await withinReading(details.getByRole('button', { name: 'View gallery →', exact: true }), reading)
    if (hasJournal) {
      await expect(
        details.getByText('Found the mountain camp. Next time, follow the coast toward the lighthouse.', {
          exact: true,
        }),
      ).toBeVisible()
      const metrics = await details.locator('.avalon-history-figures').evaluate((node) => {
        const divider = node.children[1]!,
          box = divider.getBoundingClientRect()
        return {
          divider: { width: getComputedStyle(divider).borderLeftWidth, height: box.height },
          values: [...node.querySelectorAll('strong')].map((value) => ({
            text: value.textContent,
            size: getComputedStyle(value).fontSize,
            weight: getComputedStyle(value).fontWeight,
          })),
        }
      })
      expect(parseFloat(metrics.divider.width) * uiScale).toBeGreaterThanOrEqual(0.99)
      expect(metrics.divider.height / uiScale).toBeGreaterThan(60)
      expect(metrics.values.map((value) => value.text)).toEqual(['2h', '5d'])
      for (const value of metrics.values) {
        expect(parseFloat(value.size)).toBe(60)
        expect(value.weight).toBe('700')
      }
    }
    await page.screenshot({ path: info.outputPath('cinematic-2560.png') })
    await size(3840, 2160)
    await expect
      .poll(async () =>
        (await requests()).some(
          (request) => `${request.provider}:${request.id}` === key && request.width >= 3840,
        ),
      )
      .toBe(true)
    for (const preview of await previews.all()) {
      const box = (await preview.boundingBox())!
      await expect
        .poll(async () =>
          (await requests()).some((request) => request.id === 'detailshot' && request.width >= box.width - 1),
        )
        .toBe(true)
    }
    await scales(1.4, 1.2)
    await size(1280, 720)
    const compact = await details.evaluate((node) => {
      const hero = node.querySelector('.avalon-details-header')!.getBoundingClientRect(),
        tabs = node.querySelector('.avalon-details-tabs')!.getBoundingClientRect()
      const personal = node.querySelector('.avalon-personal-history')!,
        column = personal.getBoundingClientRect()
      return {
        heroBottom: hero.bottom,
        tabTop: tabs.top,
        readingHeight: node.querySelector('.avalon-details-reading')!.getBoundingClientRect().height,
        overflow: [...personal.querySelectorAll('p,span,strong,button')]
          .filter((child) => {
            const box = child.getBoundingClientRect()
            return (
              box.width > 0 &&
              (box.left < column.left - 1 ||
                box.right > column.right + 1 ||
                child.scrollWidth > child.clientWidth + 1)
            )
          })
          .map((child) => child.textContent),
      }
    })
    expect(compact.heroBottom).toBeLessThanOrEqual(compact.tabTop + 1)
    expect(compact.readingHeight).toBeGreaterThan(100)
    expect(compact.overflow).toEqual([])
    for (const preview of await previews.all()) {
      expect((await preview.boundingBox())!.height).toBeGreaterThanOrEqual(100)
      await preview.focus()
      await page.evaluate(
        () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
      )
      await expect(preview).toBeFocused()
      await withinReading(preview, reading)
    }
    await page.screenshot({ path: info.outputPath('cinematic-720-scaled.png') })
    await details.getByRole('button', { name: 'Read more →', exact: true }).click()
    await expect(
      reading.getByText(Array(12).fill('Explore the mountain coast and find your way home.').join(' '), {
        exact: true,
      }),
    ).toBeVisible()
    await page.keyboard.press('Escape')
    await page.keyboard.press('Escape')
    await expect(details).toHaveCount(0)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Activity', exact: true })
      .click()
    await expect.poll(() => page.evaluate(() => (window as any).cinematicUrls.size)).toBe(0)
  })
}
