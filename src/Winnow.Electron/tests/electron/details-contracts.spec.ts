import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { expectReadableDetails, expectReadingGutter } from './details-readability'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-details-contracts-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/details-contracts-main.mjs'),
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
async function surface(
  mode: 'desktop' | 'fullscreen',
  options: { longTitle?: boolean; child?: boolean; rich?: boolean; populated?: boolean } = {},
) {
  await application.evaluate(({}, options) => {
    Object.assign((globalThis as any).detailsContractsFixture, {
      longTitle: false,
      child: false,
      rich: false,
      populated: true,
      ...options,
      revision: 'Before',
      writes: [],
      members: [],
      listRevision: 0,
    })
    Object.assign(globalThis, { __galleryCount: 2 })
  }, options)
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.focus()
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page.evaluate(async () => {
    for (const [preference, value] of [
      ['FullscreenTextScale', '1'],
      ['FullscreenInterfaceScale', '1'],
      ['FullscreenSafeMargin', '3'],
    ] as const) {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference },
        body: { value },
      })
      if (!result.ok) throw Error(result.message)
    }
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
async function details(child = false) {
  await (
    child
      ? page.getByRole('button', { name: 'View Expansion', exact: true })
      : page.locator('.avalon-cover').first()
  ).click()
  await expect(page.locator('.avalon-details')).toBeVisible()
  return page.locator('.avalon-details')
}
async function controller(button: number) {
  await page.evaluate(async (button) => {
    const state = { pressed: -1 }
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: index === state.pressed,
            value: index === state.pressed ? 1 : 0,
          })),
        },
      ],
    })
    const frames = () =>
      new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    await frames()
    state.pressed = button
    await frames()
    state.pressed = -1
    await frames()
  }, button)
}
async function publish(kind: string, resource: string) {
  await application.evaluate(
    ({ BrowserWindow }, event) => BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', event),
    { kind, resource },
  )
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} single-copy achievements remain visible without an identity relationship`, async () => {
    await surface(mode)
    const detail = await details()
    await detail.getByRole('tab', { name: 'Library', exact: true }).click()
    await expect(detail.locator('.avalon-copy')).toHaveCount(1)
    await expect(detail.getByText('Steam: 5 of 20 unlocked · 25%', { exact: true })).toBeVisible()
    await expect(detail.getByRole('heading', { name: 'Related games & editions' })).toHaveCount(0)
  })
  test(`${mode} reload preserves the local Journal section and an open unsaved editor`, async () => {
    await surface(mode)
    const detail = await details()
    await detail.getByRole('tab', { name: 'Journal', exact: true }).click()
    await detail.getByRole('button', { name: 'Edit note', exact: true }).click()
    const editor = page.getByRole('dialog', { name: 'Remember this session', exact: true })
    await editor.getByRole('textbox').fill('Open draft survives the replacement')
    await application.evaluate(() => {
      ;(globalThis as any).detailsContractsFixture.revision = 'After'
    })
    await publish('library.changed', 'library')
    await expect(detail.locator('h1')).toContainText('After')
    await expect(editor.getByRole('textbox')).toHaveValue('Open draft survives the replacement')
    await editor.getByRole('button', { name: 'Close journal editor', exact: true }).click()
    await expect(detail.getByRole('tab', { name: 'Journal', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await expect(detail).toHaveCount(0)
    expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
  })
  test(`${mode} expansion-end separation keeps the safe choice and writes the child identity only after confirmation`, async () => {
    await surface(mode, { longTitle: false, child: true })
    const detail = await details(true)
    if (mode === 'fullscreen') await detail.getByRole('tab', { name: 'Library', exact: true }).click()
    else await detail.locator('.detail-expansions summary').click()
    await detail.getByRole('button', { name: 'Separate Expansion…', exact: true }).click()
    const confirmation = page.getByRole('dialog', { name: 'Separate Expansion from Base game?', exact: true })
    await expect(confirmation.getByRole('button', { name: 'Keep relationship' })).toBeFocused()
    expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
    if (mode === 'fullscreen') {
      await controller(13)
      await controller(0)
    } else await confirmation.getByRole('button', { name: 'Separate games', exact: true }).click()
    await expect(confirmation).toBeHidden()
    const writes = await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)
    expect(writes).toEqual([{ path: '/api/v1/identity/links/99', body: { expectedLinkId: 271 } }])
  })
}
test('fullscreen history initially focuses Lifetime and controller Right selects the original observed session', async () => {
  await surface('fullscreen')
  const detail = await details()
  await detail.getByRole('button', { name: 'Play history →', exact: true }).click()
  await expect(detail.getByRole('button', { name: 'Lifetime', exact: true })).toBeFocused()
  await controller(15)
  await controller(0)
  await expect(detail.getByRole('button', { name: 'Tracked sessions', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(detail.locator('.activity-timeline-bar.tracked')).toHaveCount(1)
  await expect(detail.getByText('Only sessions observed by Winnow are shown.', { exact: true })).toBeVisible()
})
test('fullscreen ten-repeat long title keeps every hero action above section navigation', async ({}, info) => {
  await surface('fullscreen', { longTitle: true, child: false })
  const detail = await details()
  const bounds = await detail.evaluate((node) => {
    const title = node.querySelector('h1')!,
      hero = node.querySelector('.avalon-details-header')!.getBoundingClientRect()
    return {
      title: title.getBoundingClientRect().toJSON(),
      lineHeight: parseFloat(getComputedStyle(title).lineHeight),
      hero: hero.toJSON(),
      tabs: node.querySelector('[role="tablist"]')!.getBoundingClientRect().toJSON(),
      actions: [...node.querySelectorAll('.avalon-details-header button')].map((button) =>
        button.getBoundingClientRect().toJSON(),
      ),
    }
  })
  expect(bounds.title.height).toBeGreaterThan(bounds.lineHeight)
  expect(bounds.hero.bottom).toBeLessThanOrEqual(bounds.tabs.top)
  for (const action of bounds.actions) {
    expect(action.top).toBeGreaterThanOrEqual(bounds.hero.top)
    expect(action.bottom).toBeLessThanOrEqual(bounds.hero.bottom + 1)
  }
  await page.screenshot({ path: info.outputPath('long-title.png') })
})
test('a pending desktop journal prompt appears on fullscreen attachment and Back dismisses the shared prompt', async () => {
  await surface('desktop')
  await publish('session.ended', 'sessions/888')
  await expect(page.locator('.session-notifications.mode-desktop .session-prompt')).toBeVisible()
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.session-notifications.mode-fullscreen .session-prompt')).toBeVisible()
  await controller(1)
  await expect(page.locator('.session-prompt')).toHaveCount(0)
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false),
  )
  await expect(page.locator('.avalon-shell.desktop')).toBeVisible()
  await expect(page.locator('.session-prompt')).toHaveCount(0)
  expect(await application.evaluate(() => (globalThis as any).detailsContractsFixture.writes)).toEqual([])
})

for (const section of ['Updates', 'Journal', 'Library'])
  for (const populated of [false, true]) {
    test(`fullscreen ${section} preserves the original ${populated ? 'populated' : 'empty'} action graph at both source scale boundaries`, async ({}, info) => {
      await surface('fullscreen', { rich: true, populated })
      const detail = await details()
      await detail.getByRole('tab', { name: section, exact: true }).click()
      if (section === 'Journal')
        await expect(detail.locator('.timeline-entry')).toHaveCount(populated ? 4 : 0)
      if (section === 'Updates') await expect(detail.locator('.update-row')).toHaveCount(populated ? 4 : 0)
      if (section === 'Library' && populated) await expect(detail.getByRole('checkbox')).toHaveCount(1)
      for (const [width, height, text, ui] of [
        [2560, 1440, 1, 1],
        [1280, 720, 1.4, 1.2],
      ]) {
        await application.evaluate(
          ({ BrowserWindow }, size) => BrowserWindow.getAllWindows()[0]!.setContentSize(size[0], size[1]),
          [width, height],
        )
        await page.evaluate(
          async ({ text, ui }) => {
            for (const [preference, value] of [
              ['FullscreenTextScale', text],
              ['FullscreenInterfaceScale', ui],
            ]) {
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
        await expect(page.locator('body')).toHaveCSS('zoom', String(ui))
        await expect(page.locator('html')).toHaveCSS('--fullscreen-text-scale', String(text))
        const reading = detail.locator('.avalon-details-reading')
        await expectReadingGutter(reading)
        await expectReadableDetails(detail)
        const geometry = await reading.evaluate((node) => {
          const bounds = node.getBoundingClientRect(),
            width = node.clientWidth
          return {
            height: bounds.height,
            overflow: [...node.querySelectorAll<HTMLElement>('p,h2,h3,span,label,button')]
              .filter((element) => element.getBoundingClientRect().height > 0)
              .filter((element) => {
                const box = element.getBoundingClientRect()
                return (
                  box.left < bounds.left - 1 ||
                  box.right > bounds.right + 1 ||
                  element.scrollWidth > element.clientWidth + 1
                )
              })
              .map((element) => ({
                text: element.textContent,
                width: element.clientWidth,
                scroll: element.scrollWidth,
              })),
          }
        })
        expect(geometry.height).toBeGreaterThan(100)
        expect(geometry.overflow).toEqual([])
        const actions = reading.locator(
          'button:visible:enabled, input[type="checkbox"]:visible:enabled, a[href]:visible',
        )
        const count = await actions.count()
        for (let index = 0; index < count; index++)
          await actions
            .nth(index)
            .evaluate((node, index) => node.setAttribute('data-contract-action', String(index)), index)
        const visited = new Set<string>()
        await detail.getByRole('tab', { name: 'Overview', exact: true }).focus()
        for (let step = 0; step <= count; step++) {
          await controller(13)
          const focus = await reading.evaluate((node) => {
            const active = document.activeElement as HTMLElement,
              viewport = node.getBoundingClientRect(),
              box = active.getBoundingClientRect()
            return {
              id: active.getAttribute('data-contract-action'),
              top: box.top - viewport.top,
              bottom: box.bottom - viewport.top,
              height: box.height,
              viewport: viewport.height,
            }
          })
          if (focus.id === null) continue
          visited.add(focus.id)
          expect(focus.top).toBeLessThan(focus.viewport)
          expect(focus.bottom).toBeGreaterThan(0)
          if (focus.height <= focus.viewport) {
            expect(focus.top).toBeGreaterThanOrEqual(-1)
            expect(focus.bottom).toBeLessThanOrEqual(focus.viewport + 1)
          }
        }
        expect(visited.size).toBe(count)
        if (section === 'Journal' && !populated)
          await expect(
            reading.getByText('No notes yet. After you play, Winnow will ask how it went.', { exact: true }),
          ).toBeVisible()
        if (populated)
          expect(
            await reading.locator('.timeline-entry,.update-row,.avalon-copy').evaluateAll(
              (nodes, scale) =>
                nodes.some((node) => {
                  // Chromium snaps a one-CSS-pixel rule at fractional interface zoom.
                  const pixels = parseFloat(getComputedStyle(node).borderBottomWidth) * scale
                  return pixels >= 0.99 && pixels <= scale + 0.01
                }),
              ui,
            ),
          ).toBe(true)
        if (section === 'Library' && populated) {
          const membership = reading.getByRole('checkbox')
          const originalMembership = await membership.elementHandle()
          for (const selected of [true, false]) {
            await membership.focus()
            await controller(0)
            if (selected) await expect(membership).toBeChecked()
            else await expect(membership).not.toBeChecked()
            await expect(reading.getByText('Saving list changes…', { exact: true })).toHaveCount(0)
            await expect(membership).toBeFocused()
            expect(await membership.evaluate((node, original) => node === original, originalMembership)).toBe(
              true,
            )
            await expect(membership).toHaveAccessibleName(
              `${selected ? 'Remove from' : 'Add to'} Long adventures to revisit after finishing the mountain expedition with friends`,
            )
          }
        }
        await page.screenshot({ path: info.outputPath(`${section}-${populated}-${width}.png`) })
      }
    })
  }
