import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
const surfaces = [
  { mode: 'desktop', width: 1200, height: 640 },
  { mode: 'fullscreen', width: 1280, height: 720 },
  { mode: 'fullscreen', width: 1920, height: 1080 },
] as const

test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-controller-keyboard-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/fullscreen-lifecycle-main.mjs'),
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
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus)
    await info.attach('keyboard-failure', { body: await page.screenshot(), contentType: 'image/png' })
  expect(errors).toEqual([])
})

async function tap(button: number) {
  for (const pressed of [[], [button], []]) {
    await page.evaluate(async (pressed) => {
      ;(window as any).keyboardPad = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
  }
}

async function open(surface: (typeof surfaces)[number]) {
  await page.reload()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await application.evaluate(({ BrowserWindow }, surface) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(surface.width, surface.height)
    window.webContents.send('winnow:fullscreen:changed', surface.mode === 'fullscreen')
    window.focus()
  }, surface)
  await expect(page.locator(`.avalon-shell.${surface.mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page.evaluate(() => {
    Object.assign(window, { keyboardPad: [] })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: (window as any).keyboardPad.includes(index),
            value: (window as any).keyboardPad.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  if (surface.mode === 'fullscreen') {
    await tap(8)
    await expect(page.getByRole('region', { name: 'Search', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Enter search', exact: true }).focus()
  } else {
    await page.locator('[data-library-search]').fill('')
  }
  await tap(0)
  const keyboard = page.getByRole('dialog', { name: 'Enter text', exact: true })
  await expect(keyboard).toBeVisible()
  await expect(keyboard.getByRole('button', { name: '1', exact: true })).toBeFocused()
  await page.evaluate(() => document.fonts.ready)
  return keyboard
}

for (const surface of surfaces) {
  const label = `${surface.mode} ${surface.width}x${surface.height}`
  test(`controller changes case, types Q and Space, and backspaces from the selected key in ${label}`, async () => {
    const keyboard = await open(surface)
    const original =
      surface.mode === 'fullscreen'
        ? page.getByRole('searchbox', { name: 'Search games', includeHidden: true })
        : page.locator('[data-library-search]')
    await tap(13)
    await tap(13)
    await expect(keyboard.getByRole('button', { name: 'Case', exact: true })).toBeFocused()
    await tap(0)
    await expect(keyboard.getByRole('button', { name: 'Case', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await tap(12)
    await expect(keyboard.getByRole('button', { name: 'Q', exact: true })).toBeFocused()
    await tap(0)
    await expect(original).toHaveValue('Q')
    await tap(12)
    await tap(12)
    await expect(keyboard.getByRole('button', { name: 'Done', exact: true })).toBeFocused()
    await tap(15)
    await expect(keyboard.getByRole('button', { name: 'Space', exact: true })).toBeFocused()
    await tap(0)
    await expect(original).toHaveValue('Q ')
    await tap(2)
    await expect(original).toHaveValue('Q')
    await expect(keyboard.getByRole('button', { name: 'Space', exact: true })).toBeFocused()
  })

  test(`standard keyboard keys fit five rows, expose native names and form an inverted T in ${label}`, async ({}, info) => {
    const keyboard = await open(surface)
    const rows = keyboard.locator('.keyboard-row')
    await expect(rows).toHaveCount(5)
    const expected = [
      [...'1234567890-=', 'Backspace'],
      [...'qwertyuiop[]\\', 'Delete'],
      ['Case', ..."asdfghjkl;'", 'Enter'],
      ['`', ...'zxcvbnm,./', 'Up'],
      ['Done', 'Space', 'Left', 'Down', 'Right'],
    ]
    const cdp = await page.context().newCDPSession(page)
    const tree = await cdp.send('Accessibility.getFullAXTree')
    const accessible = tree.nodes
      .filter((node) => !node.ignored && node.role?.value === 'button')
      .map((node) => node.name?.value)
    const bounds = (await keyboard.boundingBox())!
    expect(bounds.width).toBeCloseTo(
      surface.mode === 'desktop' ? 920 : 1500 * (surface.width / 1920) * 0.85,
      0,
    )
    expect(bounds.x).toBeGreaterThanOrEqual(0)
    expect(bounds.y).toBeGreaterThanOrEqual(0)
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(surface.width + 1)
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(surface.height + 1)
    for (const [row, names] of expected.entries()) {
      await expect(rows.nth(row).getByRole('button')).toHaveCount(names.length)
      for (const name of names) {
        const key = rows.nth(row).getByRole('button', { name, exact: true })
        await expect(key).toHaveAccessibleName(name)
        expect(accessible).toContain(name)
        const box = (await key.boundingBox())!
        expect(box.width).toBeGreaterThan(0)
        expect(box.height).toBeGreaterThan(0)
        expect(box.x).toBeGreaterThanOrEqual(bounds.x)
        expect(box.y).toBeGreaterThanOrEqual(bounds.y)
        expect(box.x + box.width).toBeLessThanOrEqual(bounds.x + bounds.width)
        expect(box.y + box.height).toBeLessThanOrEqual(bounds.y + bounds.height)
      }
    }
    const box = async (name: string) =>
      (await keyboard.getByRole('button', { name, exact: true }).boundingBox())!
    const [up, down, left, right, space] = await Promise.all(
      ['Up', 'Down', 'Left', 'Right', 'Space'].map(box),
    )
    expect(Math.abs(up.x - down.x)).toBeLessThan(0.02)
    expect(up.y).toBeLessThan(down.y)
    expect(left.x).toBeLessThan(down.x)
    expect(right.x).toBeGreaterThan(down.x)
    expect(left.y).toBe(down.y)
    expect(right.y).toBe(down.y)
    expect(space.width).toBeGreaterThan(up.width * 8)
    await page.screenshot({ path: info.outputPath(`keyboard-${surface.mode}-${surface.width}.png`) })
    await cdp.detach()
  })

  test(`controller keyboard edits the original search field and Back restores its focus without leaving ${label}`, async () => {
    const keyboard = await open(surface)
    const original =
      surface.mode === 'fullscreen'
        ? page.getByRole('searchbox', { name: 'Search games', includeHidden: true })
        : page.locator('[data-library-search]')
    await tap(0)
    await expect(original).toHaveValue('1')
    await tap(1)
    await expect(keyboard).toHaveCount(0)
    await expect(original).toBeFocused()
    await expect(original).toHaveValue('1')
    if (surface.mode === 'fullscreen')
      await expect(page.getByRole('region', { name: 'Search', exact: true })).toBeVisible()
    else
      await expect(
        page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'Library', exact: true }),
      ).toHaveAttribute('aria-current', 'page')
    // Further render frames must preserve the original field's restored focus.
    await page.evaluate(async () => {
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    })
    await expect(original).toBeFocused()
  })
}
