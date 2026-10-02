import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
type Fixture = {
  kind: string
  delay: boolean
  requests: { id: string; width: number }[]
  aborted: number
  pending: (() => void)[]
  selections: number[]
}
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-backdrop-'))
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/backdrop-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await page.evaluate(() => {
    const live = new Set<string>(),
      create = URL.createObjectURL.bind(URL),
      revoke = URL.revokeObjectURL.bind(URL)
    ;(window as unknown as { backdropUrls: Set<string> }).backdropUrls = live
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
})
test.afterAll(async () => closeFixture(application, directory))
async function surface(mode: 'desktop' | 'fullscreen', width = 1920, height = 1080) {
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(value.width, value.height)
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
    },
    { mode, width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await expect.poll(() => page.evaluate(() => [innerWidth, innerHeight])).toEqual([width, height])
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
}
async function choose(kind: string, delay = false) {
  await application.evaluate(
    ({ BrowserWindow }, value) => {
      const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
      fixture.kind = value.kind
      fixture.delay = value.delay
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
        kind: 'preferences.changed',
        resource: 'ArtworkSourceOrder',
        epoch: 'fixture',
        sequence: Date.now(),
      })
    },
    { kind, delay },
  )
}
const backdrop = () => page.locator('.avalon-backdrop').first()
const urls = () => page.evaluate(() => (window as unknown as { backdropUrls: Set<string> }).backdropUrls.size)

test('fullscreen Home crossfades independent source geometry, keeps visible art while loading, and releases replaced URLs', async ({}, info) => {
  await surface('fullscreen', 2520, 1080)
  await choose('hero')
  await expect(backdrop().locator('[data-key="steam-hero:hero"] img')).toBeVisible()
  await info.attach('wide-hero-viewport-measurement', {
    body: JSON.stringify(
      await backdrop().evaluate((node) => {
        const bounds = node.getBoundingClientRect()
        const image = node.querySelector('img')!
        return {
          viewport: [innerWidth, innerHeight],
          bodyZoom: getComputedStyle(document.body).zoom,
          client: [node.clientWidth, node.clientHeight],
          bounds: bounds.toJSON(),
          image: [image.naturalWidth, image.naturalHeight],
        }
      }),
    ),
    contentType: 'application/json',
  })
  const geometry = async () =>
    backdrop()
      .locator('.avalon-backdrop-art')
      .evaluateAll((nodes) =>
        nodes.map((node) => {
          const box = node.getBoundingClientRect()
          return {
            width: box.width,
            height: box.height,
            left: box.left,
            fitted: node.hasAttribute('data-fitted'),
          }
        }),
      )
  await expect.poll(geometry).toEqual([{ width: 2520, height: 813.75, left: 0, fitted: true }])
  expect(
    await backdrop()
      .locator('img')
      .evaluate((image) => getComputedStyle(image).maskImage),
  ).toContain('98%')
  await page.screenshot({ path: info.outputPath('home-wide-hero.png') })
  await choose('landscape', true)
  await expect
    .poll(() =>
      application.evaluate(
        () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.pending.length,
      ),
    )
    .toBeGreaterThan(0)
  await expect(backdrop().locator('[data-key="steam-hero:hero"] img')).toBeVisible()
  expect(await urls()).toBe(1)
  await page.evaluate(() => {
    const state = { sampled: false, incoming: 0, outgoing: 0, opacity: 0 }
    ;(window as unknown as { backdropMotion: typeof state }).backdropMotion = state
    const sample = () => {
      const root = document.querySelector('.avalon-backdrop[data-crossfading]')
      if (root) {
        const outgoing = root.querySelector<HTMLElement>('[data-outgoing] .avalon-backdrop-art')!
        const incoming = root.querySelector<HTMLElement>('.avalon-backdrop-layer:not([data-outgoing])')!
        const opacity = Number(getComputedStyle(incoming).opacity)
        if (opacity > 0 && opacity < 1) {
          Object.assign(state, {
            sampled: true,
            incoming: incoming.querySelector('.avalon-backdrop-art')!.getBoundingClientRect().height,
            outgoing: outgoing.getBoundingClientRect().height,
            opacity,
          })
          return
        }
      }
      requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
  })
  await application.evaluate(() => {
    const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
    fixture.delay = false
    fixture.pending.splice(0).forEach((finish) => finish())
  })
  await expect
    .poll(() =>
      page.evaluate(() => ({
        sampled: (window as unknown as { backdropMotion: { sampled: boolean } }).backdropMotion.sampled,
        keys: [...document.querySelectorAll('.avalon-backdrop [data-key]')].map((node) =>
          node.getAttribute('data-key'),
        ),
        reduced:
          matchMedia('(prefers-reduced-motion: reduce)').matches ||
          document.documentElement.classList.contains('reduced-motion'),
        loading: document.querySelector('.avalon-backdrop')?.getAttribute('data-loading'),
      })),
    )
    .toMatchObject({ sampled: true, reduced: false })
  const motion = await page.evaluate(
    () =>
      (window as unknown as { backdropMotion: { incoming: number; outgoing: number; opacity: number } })
        .backdropMotion,
  )
  expect(motion.incoming).toBe(1080)
  expect(motion.outgoing).toBe(813.75)
  expect(motion.opacity).toBeGreaterThan(0)
  expect(motion.opacity).toBeLessThan(1)
  await expect(backdrop()).not.toHaveAttribute('data-crossfading')
  await expect.poll(urls).toBe(1)
  await choose('hero')
  await expect(backdrop().locator('[data-key="steam-hero:hero"] img')).toBeVisible()
  await expect(backdrop()).not.toHaveAttribute('data-crossfading')
  for (const width of [3840, 1920]) {
    await application.evaluate(
      ({ BrowserWindow }, width) => BrowserWindow.getAllWindows()[0]!.setContentSize(width, 1080),
      width,
    )
    await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
    await expect.poll(async () => (await geometry())[0].height).toBe(1080)
    const actual = (await geometry())[0]
    expect(actual.width).toBeCloseTo(width === 3840 ? 3344.516129 : 1920, 1)
    expect(actual.left).toBeCloseTo(width === 3840 ? 247.741935 : 0, 1)
  }
})

test('detaching Home cancels the real named image request and releases retained decoded art', async () => {
  await surface('fullscreen')
  await choose('hero')
  await expect(backdrop().locator('img')).toBeVisible()
  const before = await application.evaluate(
    () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.aborted,
  )
  const pendingBefore = await application.evaluate(
    () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.pending.length,
  )
  await choose('landscape', true)
  await expect(backdrop()).toHaveAttribute('data-loading', 'true')
  await expect
    .poll(() =>
      application.evaluate(
        () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.pending.length,
      ),
    )
    .toBeGreaterThan(pendingBefore)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Activity', exact: true })
    .click()
  await expect(backdrop()).toHaveCount(0)
  await expect.poll(urls).toBe(0)
  await expect
    .poll(() =>
      application.evaluate(
        () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.aborted,
      ),
    )
    .toBeGreaterThan(before)
  await application.evaluate(() => {
    const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
    fixture.delay = false
    fixture.pending.splice(0).forEach((finish) => finish())
  })
})

for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} Details uses its original reading veil and releases its backdrop on close`, async ({}, info) => {
    await surface(mode, 1920, 1080)
    await choose('landscape')
    await page.locator('.avalon-cover').first().click({ modifiers: [] })
    const details = page.locator('.avalon-details')
    await expect(details.locator('.avalon-backdrop img')).toBeVisible()
    const veil = await details
      .locator('.avalon-backdrop-veil')
      .evaluate((node) => getComputedStyle(node).backgroundImage)
    if (mode === 'desktop') {
      const color = await details
        .locator('.avalon-backdrop-veil')
        .evaluate((node) => getComputedStyle(node).backgroundColor)
      const originalVeil = await page.evaluate(() => {
        const probe = document.createElement('span')
        probe.style.backgroundColor = '#16282AEB'
        document.body.append(probe)
        const result = getComputedStyle(probe).backgroundColor
        probe.remove()
        return result
      })
      expect(color).toBe(originalVeil)
      expect(veil).toBe('none')
    } else expect(veil).toContain('55%')
    expect(
      await details.locator('.avalon-backdrop img').evaluate((node) => getComputedStyle(node).opacity),
    ).toBe('1')
    await page.screenshot({ path: info.outputPath(`details-${mode}-backdrop.png`) })
    await page.keyboard.press('Escape')
    await expect(details).toHaveCount(0)
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Activity', exact: true })
      .click()
    await expect.poll(urls).toBe(0)
    expect(errors).toEqual([])
  })

test('fullscreen Library retains one dimmed backdrop across selected games and releases its pixels and pending request on departure', async ({}, info) => {
  const pending = () =>
    application.evaluate(
      () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.pending.length,
    )
  await surface('fullscreen')
  await choose('hero')
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  const root = page.locator('.avalon-library-backdrop')
  await expect(root.locator('[data-key="steam-hero:hero"] img')).toBeVisible()
  expect(await root.evaluate((element) => getComputedStyle(element).opacity)).toBe('0.45')
  const cover = page.locator('.avalon-fullscreen-grid [data-row-active="true"] [data-avalon-game]').first()
  await cover.focus()
  await page.evaluate(() => {
    ;(window as unknown as { libraryBackdrop: Element }).libraryBackdrop = document.querySelector(
      '.avalon-library-backdrop .avalon-backdrop',
    )!
  })
  await application.evaluate(() => {
    const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
    fixture.kind = 'landscape'
    fixture.delay = true
  })
  await page.keyboard.press('ArrowRight')
  const selected = Number(await page.locator('.avalon-fullscreen-grid').getAttribute('data-selected-id'))
  await expect
    .poll(() =>
      application.evaluate(() =>
        (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.selections.at(-1),
      ),
    )
    .toBe(selected)
  await expect(root.locator('.avalon-backdrop')).toHaveAttribute('data-loading', 'true')
  await expect(root.locator('[data-key="steam-hero:hero"] img')).toBeVisible()
  expect(
    await page.evaluate(
      () =>
        (window as unknown as { libraryBackdrop: Element }).libraryBackdrop ===
        document.querySelector('.avalon-library-backdrop .avalon-backdrop'),
    ),
  ).toBe(true)
  await expect.poll(pending).toBeGreaterThan(0)
  await application.evaluate(() => {
    const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
    fixture.delay = false
    fixture.pending.splice(0).forEach((finish) => finish())
  })
  await expect(root.locator('[data-key="igdb-backdrop:landscape"] img')).toBeVisible()
  await expect(root.locator('.avalon-backdrop')).not.toHaveAttribute('data-crossfading')
  await expect.poll(urls).toBe(1)
  await page.screenshot({ path: info.outputPath('library-dimmed-backdrop.png') })
  const before = await application.evaluate(
    () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.aborted,
  )
  await choose('hero', true)
  await expect(root.locator('.avalon-backdrop')).toHaveAttribute('data-loading', 'true')
  await expect.poll(pending).toBeGreaterThan(0)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Activity', exact: true })
    .click()
  await expect(root).toHaveCount(0)
  await expect.poll(urls).toBe(0)
  await expect
    .poll(() =>
      application.evaluate(
        () => (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture.aborted,
      ),
    )
    .toBeGreaterThan(before)
  await application.evaluate(() => {
    const fixture = (globalThis as unknown as { __backdropFixture: Fixture }).__backdropFixture
    fixture.delay = false
    fixture.pending.splice(0).forEach((finish) => finish())
  })
  expect(errors).toEqual([])
})
