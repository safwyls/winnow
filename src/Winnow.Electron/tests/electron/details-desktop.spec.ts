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
import { expectReadableDetails, expectReadingGutter } from './details-readability'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-details-desktop-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/details-desktop-main.mjs'),
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
async function surface(width: number, height: number, longTitle = false) {
  await application.evaluate(({}, longTitle) => {
    ;(globalThis as any).desktopDetailsFixture.longTitle = longTitle
  }, longTitle)
  await page.reload()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, { width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(width, height)
      window.focus()
      window.webContents.send('winnow:fullscreen:changed', false)
    },
    { width, height },
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(/desktop/)
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await page.locator('.avalon-cover').first().click()
  const details = page.locator('.avalon-details.desktop')
  await expect(details.getByRole('button', { name: 'Play', exact: true }).first()).toBeVisible()
  await expect(details.locator('.avalon-details-header .reception-line')).toBeVisible()
  await expect(details.locator('.screenshot-strip img')).toHaveCount(8)
  return details
}
async function contained(control: Locator, container: Locator) {
  const [box, outer] = await Promise.all([control.boundingBox(), container.boundingBox()])
  expect(box).not.toBeNull()
  expect(box!.width).toBeGreaterThan(0)
  expect(box!.height).toBeGreaterThan(0)
  expect(box!.x).toBeGreaterThanOrEqual(outer!.x - 1)
  expect(box!.y).toBeGreaterThanOrEqual(outer!.y - 1)
  expect(box!.x + box!.width).toBeLessThanOrEqual(outer!.x + outer!.width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(outer!.y + outer!.height + 1)
}
for (const [width, height] of [
  [1200, 640],
  [1280, 820],
] as const)
  test(`desktop populated five-tab Details preserves the source ${width}x${height} header, bottom content and scroll`, async ({}, info) => {
    const details = await surface(width, height),
      header = details.locator('.avalon-details-header'),
      reading = details.locator('.avalon-details-reading')
    const initialHeader = await header.boundingBox()
    await contained(details, page.locator('body'))
    const names = ['Overview', 'Activity', 'Updates, 12 unread updates', 'Journal', 'Library']
    expect(await details.getByRole('tab').allTextContents()).toEqual([
      'Overview',
      'Activity',
      'Updates',
      'Journal',
      'Library',
    ])
    for (const [index, name] of names.entries()) {
      const tab = details.getByRole('tab', { name, exact: true })
      await tab.click()
      await expect(tab).toHaveAttribute('aria-selected', 'true')
      await expect(details.getByRole('tab', { selected: true })).toHaveCount(1)
      expect((await reading.boundingBox())!.height).toBeGreaterThanOrEqual(80)
      await contained(reading, details)
      expect(await header.boundingBox()).toEqual(initialHeader)
      if (name === 'Overview') {
        await reading.locator('.detail-expansions summary').click()
        await expect(reading.locator('.detail-expansions')).toHaveAttribute('open', '')
        await expect(
          reading.getByRole('heading', { name: 'The Astral Cartographers: Distant Shores 3', exact: true }),
        ).toBeVisible()
      }
      await expect(reading.locator('.details-relationship')).toHaveCount(name === 'Overview' ? 3 : 0)
      await expect(reading.locator('.activity-tracker')).toHaveCount(name === 'Activity' ? 1 : 0)
      await expect(reading.getByRole('heading', { name: 'Updates', exact: true })).toHaveCount(
        name.startsWith('Updates') ? 1 : 0,
      )
      await expect(reading.getByRole('heading', { name: 'Journal', exact: true })).toHaveCount(
        name === 'Journal' ? 1 : 0,
      )
      await expect(reading.getByRole('heading', { name: 'Owned copies', exact: true })).toHaveCount(
        name === 'Library' ? 1 : 0,
      )
      await expectReadingGutter(reading)
      await expectReadableDetails(details)
      await reading.evaluate((node) => {
        node.scrollTop = node.scrollHeight
      })
      await page.evaluate(
        () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
      )
      const bottom = await reading.evaluate((node) => {
        const viewport = node.getBoundingClientRect()
        const controls = [...node.querySelectorAll('button,p,span,h2,h3,label,input[type="checkbox"]')]
          .filter((control) => {
            const box = control.getBoundingClientRect()
            return box.width > 0 && box.height > 0 && !control.closest('[hidden],[inert]')
          })
          .sort((a, b) => a.getBoundingClientRect().bottom - b.getBoundingClientRect().bottom)
        const last = controls.at(-1)!,
          box = last.getBoundingClientRect()
        return {
          text: last.textContent,
          top: box.top - viewport.top,
          bottom: box.bottom - viewport.top,
          left: box.left - viewport.left,
          right: box.right - viewport.left,
          width: viewport.width,
          height: viewport.height,
          scroll: node.scrollTop,
          max: node.scrollHeight - node.clientHeight,
        }
      })
      expect(bottom.top, bottom.text ?? name).toBeGreaterThanOrEqual(-1)
      expect(bottom.bottom, bottom.text ?? name).toBeLessThanOrEqual(bottom.height + 1)
      expect(bottom.left, bottom.text ?? name).toBeGreaterThanOrEqual(-1)
      expect(bottom.right, bottom.text ?? name).toBeLessThanOrEqual(bottom.width + 1)
      expect(bottom.scroll).toBeGreaterThanOrEqual(0)
      expect(bottom.scroll).toBeLessThanOrEqual(bottom.max + 1)
      expect(await header.boundingBox()).toEqual(initialHeader)
      await page.screenshot({ path: info.outputPath(`${name}-bottom.png`) })
      await details.getByRole('tab', { name: names[(index + 1) % names.length], exact: true }).click()
      await tab.click()
      await expect.poll(() => reading.evaluate((node) => node.scrollTop)).toBeCloseTo(bottom.scroll, 0)
      if (name === 'Overview') await expect(reading.locator('.detail-expansions')).toHaveAttribute('open', '')
    }
    await details.getByRole('button', { name: 'More', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Refetch metadata', exact: true })).toBeVisible()
    await page.screenshot({ path: info.outputPath('management-menu.png') })
    await page.keyboard.press('Escape')
    await details.getByRole('button', { name: 'Close game details', exact: true }).click()
  })

test('desktop original long title, publisher and three stores keep all header actions and glyphs inside the card', async ({}, info) => {
  const details = await surface(1200, 640, true),
    header = details.locator('.avalon-details-header')
  await expect(
    header.getByRole('heading', {
      name: 'The Astral Cartographers: Echoes Beyond the Forgotten Constellations — Complete Collection',
      exact: true,
    }),
  ).toBeVisible()
  await expect(
    header.getByText(/The Independent Cartographic Society and the Interstellar Exploration Cooperative/),
  ).toBeVisible()
  const title = header.getByRole('heading', { level: 1 })
  await expect(title).toHaveCSS('white-space', 'normal')
  await expect(title).toHaveCSS('font-size', '26px')
  await expect(title).toHaveCSS('line-height', '30px')
  expect((await title.boundingBox())!.height).toBeGreaterThan(30)
  expect(
    await title.evaluate(
      (node) => node.scrollWidth <= node.clientWidth + 1 && node.scrollHeight <= node.clientHeight + 1,
    ),
  ).toBe(true)
  await contained(title, header)
  const stores = await header.innerText()
  for (const store of ['Steam', 'Epic', 'GOG']) expect(stores).toContain(store)
  for (const name of ['Play', 'Add to list', 'More', 'Close game details']) {
    const action = header.getByRole('button', { name, exact: true })
    await expect(action).toBeEnabled()
    await contained(action, header)
  }
  for (const name of ['Play', 'Add to list']) {
    const icon = header.getByRole('button', { name, exact: true }).locator('svg')
    await expect(icon).toBeVisible()
    await contained(icon, header)
  }
  expect((await details.locator('.avalon-details-reading').boundingBox())!.height).toBeGreaterThanOrEqual(80)
  await page.screenshot({ path: info.outputPath('long-header.png') })
})

test('desktop screenshots stay inline with a cleared scrollbar and only More opens the action popup', async () => {
  const details = await surface(1200, 640)
  const reading = details.locator('.avalon-details-reading')
  const strip = reading.locator('.screenshot-strip')
  const shot = strip.getByRole('button', { name: 'Open screenshot 8 of 8', exact: true })
  await shot.focus()
  await expect(shot).toBeFocused()
  const geometry = await strip.evaluate((node) => {
    const shot = node.lastElementChild!
    const bounds = node.getBoundingClientRect(),
      image = shot.getBoundingClientRect()
    const style = getComputedStyle(node),
      focus = getComputedStyle(shot)
    return {
      padding: parseFloat(style.paddingBottom),
      clientBottom: bounds.top + node.clientHeight,
      imageBottom: image.bottom,
      right: bounds.right,
      imageRight: image.right,
      outline: parseFloat(focus.outlineWidth),
      shadow: focus.boxShadow,
    }
  })
  expect(geometry.padding).toBeGreaterThanOrEqual(10)
  expect(geometry.imageBottom).toBeLessThanOrEqual(geometry.clientBottom - 9)
  expect(geometry.imageRight).toBeLessThanOrEqual(geometry.right + 1)
  expect(geometry.outline > 0 || geometry.shadow !== 'none').toBe(true)
  await expect(reading.locator('img')).toHaveCount(8)
  await details.locator('.avalon-details-header .reception-line').hover()
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(details.locator('.avalon-details-menu')).toHaveCount(0)
  await details.getByRole('button', { name: 'More', exact: true }).click()
  await expect(details.locator('.avalon-details-menu')).toHaveCount(1)
  await expect(page.getByRole('dialog')).toHaveCount(1)
  expect(await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().length)).toBe(1)
  await page.keyboard.press('Escape')
  await shot.click()
  const gallery = page.getByRole('dialog', { name: 'Screenshot 8 of 8', exact: true })
  await expect(gallery).toBeVisible()
  await expect(gallery.locator('img')).toHaveCount(1)
  await gallery.getByRole('button', { name: 'Close screenshots', exact: true }).click()
  await expect(shot).toBeFocused()
  await expect(reading.locator('img')).toHaveCount(8)
  await expect(details.locator('.avalon-details-menu')).toHaveCount(0)
  await details.getByRole('button', { name: 'Close game details', exact: true }).click()
})
