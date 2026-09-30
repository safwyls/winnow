import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

async function launch(width = 1280, height = 720, scale = 1) {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-metadata-settings-'))
  const application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/metadata-settings-main.mjs'),
      '--data-dir',
      directory,
      '--no-sync',
      '--seed-sample',
    ],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  try {
    const page = await application.firstWindow(),
      errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({
      timeout: 45000,
    })
    await page.evaluate(async (scale) => {
      const result = await window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenTextScale' },
        body: { value: String(scale) },
      })
      if (!result.ok) throw Error('Could not save text scale')
      const controller = { pressed: [] as number[] }
      Object.assign(window, { metadataPad: controller })
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            mapping: 'standard',
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: controller.pressed.includes(i) })),
          },
        ],
      })
    }, scale)
    const mode = async (fullscreen: boolean) => {
      await application.evaluate(
        ({ BrowserWindow }, { fullscreen, width, height }) => {
          const window = BrowserWindow.getAllWindows()[0]!
          window.setFullScreen(false)
          window.setContentSize(width, height)
          window.webContents.send('winnow:fullscreen:changed', fullscreen)
          window.focus()
        },
        { fullscreen, width, height },
      )
      await expect(page.locator('.avalon-shell')).toHaveClass(
        new RegExp(fullscreen ? 'fullscreen' : 'desktop'),
      )
    }
    const open = async () => {
      await page.getByRole('button', { name: 'Settings', exact: true }).click()
      await page
        .getByRole('navigation', { name: 'Settings section' })
        .getByRole('button', { name: 'Metadata & artwork', exact: true })
        .click()
    }
    const tap = async (button: number) => {
      await page.waitForTimeout(200)
      for (const pressed of [[], [button], []])
        await page.evaluate(async (pressed) => {
          ;(window as any).metadataPad.pressed = pressed
          await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
        }, pressed)
    }
    return { application, page, directory, errors, mode, open, tap }
  } catch (error) {
    await closeFixture(application, directory)
    throw error
  }
}
function sync(page: Page) {
  return page.getByRole('button', { name: 'Sync metadata now', exact: true })
}
function status(page: Page) {
  return page.getByRole('region', { name: 'Library metadata', exact: true }).getByRole('status')
}

test('desktop Enter and fullscreen A share one metadata operation across navigation progress completion and late callbacks', async () => {
  const f = await launch()
  try {
    const { page, application, mode, open, tap } = f
    await open()
    await sync(page).focus()
    await page.keyboard.press('Enter')
    await expect(sync(page)).toBeDisabled()
    await expect(status(page)).toHaveText('Matching games with IGDB…')
    await page.keyboard.press('Enter')
    await mode(true)
    await open()
    await sync(page).focus()
    await tap(0)
    await expect(sync(page)).toHaveAttribute('aria-disabled', 'true')
    await tap(13)
    await expect(page.getByRole('button', { name: 'IGDB metadata', exact: true })).toBeFocused()
    await page.getByRole('button', { name: 'Library', exact: true }).first().click()
    await application.evaluate(() => {
      ;(globalThis as any).__metadataFixture.message = 'Updating game details…'
    })
    await open()
    await expect(status(page)).toHaveText('Updating game details…')
    await application.evaluate(() => {
      ;(globalThis as any).__metadataFixture.state = 'completed'
    })
    await expect(status(page)).toContainText('Metadata sync finished.')
    await expect(sync(page)).toHaveAttribute('aria-disabled', 'false')
    await application.evaluate(() => {
      ;(globalThis as any).__metadataFixture.message = 'Late progress from a completed run'
    })
    await page.waitForTimeout(1700)
    await expect(status(page)).toContainText('Metadata sync finished.')
    await mode(false)
    await open()
    await expect(status(page)).toContainText('Metadata sync finished.')
    await expect(sync(page)).toBeEnabled()
    expect(await application.evaluate(() => (globalThis as any).__metadataFixture.starts.length)).toBe(1)
    expect(f.errors).toEqual([])
  } finally {
    await closeFixture(f.application, f.directory)
  }
})

for (const [result, guidance] of [
  [1, 'credentials in IGDB metadata'],
  [2, 'Try again'],
  [3, 'Reopen the library'],
  [-1, 'Check your connection'],
] as const)
  test(`metadata outcome ${result} shares guidance and controller retry starts one fresh successful operation`, async () => {
    const f = await launch()
    try {
      await f.application.evaluate((_, result) => {
        Object.assign((globalThis as any).__metadataFixture, {
          state: result === -1 ? 'failed' : 'completed',
          result,
          message: 'Private diagnostic detail',
        })
      }, result)
      await f.open()
      await sync(f.page).focus()
      await f.page.keyboard.press('Enter')
      await expect(status(f.page)).toContainText(guidance)
      await expect(status(f.page)).not.toContainText('Private')
      await f.mode(true)
      await f.open()
      await expect(status(f.page)).toContainText(guidance)
      await f.application.evaluate(() => {
        Object.assign((globalThis as any).__metadataFixture, { state: 'completed', result: 0 })
      })
      await sync(f.page).focus()
      await f.tap(0)
      await expect(status(f.page)).toContainText('Metadata sync finished.')
      const ids = await f.application.evaluate(() => (globalThis as any).__metadataFixture.starts as string[])
      expect(ids).toHaveLength(2)
      expect(new Set(ids).size).toBe(2)
      expect(f.errors).toEqual([])
    } finally {
      await closeFixture(f.application, f.directory)
    }
  })

test('controller starts a single sync that keeps its live progress when settings close and reopen', async () => {
  const f = await launch()
  try {
    await f.mode(true)
    await f.open()
    await sync(f.page).focus()
    await f.tap(0)
    await expect(sync(f.page)).toHaveAttribute('aria-disabled', 'true')
    await expect(status(f.page)).toHaveAttribute('aria-live', 'polite')
    await f.tap(13)
    await expect(f.page.getByRole('button', { name: 'IGDB metadata', exact: true })).toBeFocused()
    await f.page.getByRole('button', { name: 'Winnow home', exact: true }).click()
    await f.application.evaluate(() => {
      ;(globalThis as any).__metadataFixture.message = 'Updating game details…'
    })
    await f.open()
    await expect(sync(f.page)).toHaveAttribute('aria-disabled', 'true')
    await expect(status(f.page)).toHaveText('Updating game details…')
    await f.application.evaluate(() => {
      ;(globalThis as any).__metadataFixture.state = 'completed'
    })
    await expect(status(f.page)).toContainText('Metadata sync finished.')
    await f.mode(false)
    await f.open()
    await expect(sync(f.page)).toBeEnabled()
    await expect(status(f.page)).toContainText('Metadata sync finished.')
    expect(await f.application.evaluate(() => (globalThis as any).__metadataFixture.starts.length)).toBe(1)
    expect(f.errors).toEqual([])
  } finally {
    await closeFixture(f.application, f.directory)
  }
})

test('an uncertain native response retains the accepted metadata identity for controller retry', async () => {
  const f = await launch()
  try {
    await f.application.evaluate(() => {
      Object.assign((globalThis as any).__metadataFixture, { refuseNextStart: true, state: 'completed' })
    })
    await f.mode(true)
    await f.open()
    await sync(f.page).focus()
    await f.tap(0)
    await expect(status(f.page)).toContainText('Check your connection')
    await f.tap(0)
    await expect(status(f.page)).toContainText('Metadata sync finished.')
    const ids = await f.application.evaluate(() => (globalThis as any).__metadataFixture.starts as string[])
    expect(ids).toHaveLength(2)
    expect(new Set(ids).size).toBe(1)
    expect(f.errors).toEqual([])
  } finally {
    await closeFixture(f.application, f.directory)
  }
})

for (const [width, height, scale] of [
  [1280, 720, 1],
  [1280, 720, 1.4],
  [2560, 1440, 1],
  [2560, 1440, 1.4],
] as const)
  test(`metadata child pages retain hierarchy reading bounds controller return and saved order at ${width}x${height} text ${scale}`, async ({}, info) => {
    const f = await launch(width, height, scale)
    try {
      const { page, tap } = f
      await f.mode(true)
      await f.open()
      for (const title of ['IGDB metadata', 'Artwork source order'] as const) {
        const origin = page.getByRole('button', { name: title, exact: true })
        await origin.focus()
        await tap(0)
        const reading = page.locator('[data-settings-child-reading]')
        await expect(reading.getByRole('heading', { level: 1, name: title, exact: true })).toBeVisible()
        await expect(reading.getByRole('heading', { level: 2 })).toHaveCount(1)
        expect(
          await reading
            .getByRole('heading', { level: 2 })
            .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
        ).toBeCloseTo(18 * scale)
        if (width === 2560)
          expect(
            await reading
              .getByRole('heading', { level: 1 })
              .evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
          ).toBeCloseTo(64 * scale)
        await expect(page.getByRole('navigation', { name: 'Settings section' })).toHaveCount(0)
        await expect(
          reading.getByRole('button', {
            name: title === 'IGDB metadata' ? 'Get IGDB credentials' : 'Move Steam down',
            exact: true,
          }),
        ).toBeFocused()
        const geometry = await reading.evaluate((node) => {
          const rect = node.getBoundingClientRect()
          const elements = [node, ...node.querySelectorAll('*')]
          return {
            buttonInsets: [...node.querySelectorAll('button')].map((button) => {
              const range = document.createRange()
              range.selectNodeContents(button)
              return range.getBoundingClientRect().left - button.getBoundingClientRect().left
            }),
            rowBorders: [...node.querySelectorAll('.artwork-source-order li')].map((row) => ({
              top: parseFloat(getComputedStyle(row).borderTopWidth),
              bottom: parseFloat(getComputedStyle(row).borderBottomWidth),
            })),
            labelsMatchText: [...node.querySelectorAll('.igdb-settings-form .field')].every(
              (label) =>
                getComputedStyle(label).color ===
                getComputedStyle(node.querySelector('.igdb-settings-form > p')!).color,
            ),
            scrolls: elements.filter((element) => /auto|scroll/.test(getComputedStyle(element).overflowY))
              .length,
            rules: [...node.querySelectorAll('hr')].map((rule) => ({
              height: rule.getBoundingClientRect().height,
              width: rule.getBoundingClientRect().width,
            })),
            escaped: [...node.querySelectorAll('h1,h2,p,label,li>span,button,input')]
              .filter((element) => {
                const child = element.getBoundingClientRect()
                return child.width > 0 && (child.left < rect.left - 1 || child.right > rect.right + 1)
              })
              .map((element) => element.textContent || element.getAttribute('aria-label')),
          }
        })
        expect(geometry.scrolls).toBe(1)
        expect(geometry.rules.some((rule) => rule.height === 1 && rule.width > 300)).toBe(true)
        expect(geometry.escaped).toEqual([])
        expect(geometry.labelsMatchText).toBe(true)
        for (const inset of geometry.buttonInsets) expect(inset).toBeCloseTo(24)
        for (const [index, border] of geometry.rowBorders.entries())
          expect(border).toEqual({ top: index === 0 ? 0 : 1, bottom: 0 })
        const controls = reading.locator('button:enabled,input:enabled')
        for (let i = 0; i < (await controls.count()); i++) {
          await controls.nth(i).focus()
          await controls.nth(i).scrollIntoViewIfNeeded()
          const outer = (await reading.boundingBox())!,
            control = (await controls.nth(i).boundingBox())!
          expect(control.y).toBeGreaterThanOrEqual(outer.y - 1)
          expect(control.y + control.height).toBeLessThanOrEqual(outer.y + outer.height + 1)
        }
        if (title === 'Artwork source order') {
          await expect(reading.getByRole('button', { name: 'Move Steam up', exact: true })).toBeDisabled()
          await expect(reading.getByRole('button', { name: 'Move IGDB down', exact: true })).toBeDisabled()
          await reading.getByRole('button', { name: 'Move SteamGridDB up', exact: true }).focus()
          await tap(0)
          await expect(reading.getByRole('status')).toHaveText('Artwork source order saved.')
          await expect(
            reading.getByRole('button', { name: 'Move SteamGridDB down', exact: true }),
          ).toBeFocused()
          const saved = await page.evaluate(async () => {
            const response = await window.winnow.request({ route: 'preferences.presentation.get' })
            if (!response.ok) throw Error('Could not read preferences')
            return (response.data as { preference: string; value: string }[]).find(
              (item) => item.preference === 'ArtworkSourceOrder',
            )!.value
          })
          expect(saved).toBe('plugin:steamgriddb,steam,igdb')
        }
        await reading.evaluate((node) => {
          node.scrollTop = 0
        })
        await page.screenshot({ path: info.outputPath(`${title}-${width}-${scale}.png`) })
        await tap(1)
        await expect(origin).toBeFocused()
      }
      expect(f.errors).toEqual([])
    } finally {
      await closeFixture(f.application, f.directory)
    }
  })
