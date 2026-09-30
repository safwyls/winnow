import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtemp } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import type { ApiRequest } from '../../src/shared/bridge'

async function api<T>(page: Page, input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status}`)
    return result.data
  }, input) as Promise<T>
}
async function launch(width: number, height: number, scale: number) {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-shared-settings-'))
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
    const errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({
      timeout: 45000,
    })
    await api(page, {
      route: 'preferences.presentation.put',
      params: { preference: 'FullscreenTextScale' },
      body: { value: String(scale) },
    })
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
      const state = { pressed: [] as number[] }
      Object.assign(window, { sharedSettingsPad: state })
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: state.pressed.includes(i) })),
          },
        ],
      })
    })
    const tap = async (button: number) => {
      await page.waitForTimeout(200)
      for (const pressed of [[], [button], []])
        await page.evaluate(async (pressed) => {
          ;(window as any).sharedSettingsPad.pressed = pressed
          await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
        }, pressed)
    }
    return { application, page, directory, tap, errors }
  } catch (error) {
    await closeFixture(application, directory)
    throw error
  }
}

for (const [width, height, scale] of [
  [1280, 720, 1],
  [1280, 720, 1.4],
  [2560, 1440, 1],
  [2560, 1440, 1.4],
] as const)
  test(`fullscreen Library and Application settings retain reading bounds and focus at ${width}x${height} text ${scale}`, async ({}, info) => {
    const { application, page, directory, tap, errors } = await launch(width, height, scale)
    try {
      const tabs = page.getByRole('navigation', { name: 'Settings section' })
      for (const section of ['Library', 'Application'] as const) {
        await tabs.getByRole('button', { name: section, exact: true }).click()
        const reading = page.locator('.fullscreen-appearance-layout > .fullscreen-settings-content')
        await expect(
          reading.getByRole(section === 'Library' ? 'button' : 'switch', {
            name: section === 'Library' ? 'Default library sort' : 'Start in fullscreen',
            exact: true,
          }),
        ).toBeFocused()
        await expect(tabs.getByRole('button', { pressed: true })).toHaveCount(1)
        await expect
          .poll(() =>
            tabs
              .locator('button[aria-pressed="false"]')
              .evaluateAll((buttons) =>
                buttons.every((button) => getComputedStyle(button).borderBottomColor === 'rgba(0, 0, 0, 0)'),
              ),
          )
          .toBe(true)
        await expect(page.getByRole('complementary', { name: `${section} preview` })).toBeVisible()
        const structure = await reading.evaluate((node) => {
          const outer = node.getBoundingClientRect()
          const headings = [...node.querySelectorAll('h2')]
          return {
            titles: headings.map((heading) => heading.textContent),
            rules: headings.map((heading) => parseFloat(getComputedStyle(heading, '::after').height)),
            ruleWidths: headings.map((heading) => parseFloat(getComputedStyle(heading, '::after').width)),
            escaped: [...node.querySelectorAll('strong,p,button')]
              .filter((child) => {
                const rect = child.getBoundingClientRect()
                return rect.width > 0 && (rect.left < outer.left - 1 || rect.right > outer.right + 1)
              })
              .map((child) => child.textContent),
          }
        })
        expect(structure.titles).toContain(section === 'Library' ? 'Library preferences' : 'Startup & window')
        expect(structure.rules.every((height) => height === 1)).toBe(true)
        if (width === 2560) expect(Math.max(...structure.ruleWidths)).toBeGreaterThan(300)
        expect(structure.escaped).toEqual([])
        await page.screenshot({ path: info.outputPath(`${section}-${width}-${scale}.png`) })
        const controls = reading.locator('button:not(:disabled)')
        for (let i = 0; i < (await controls.count()); i++) {
          const control = controls.nth(i)
          await control.focus()
          await control.scrollIntoViewIfNeeded()
          const bounds = await reading.boundingBox(),
            row = await control.boundingBox()
          expect(row!.y).toBeGreaterThanOrEqual(bounds!.y - 1)
          expect(row!.y + row!.height).toBeLessThanOrEqual(bounds!.y + bounds!.height + 1)
        }
        if (section === 'Library') {
          const tools = reading.getByRole('button', { name: 'Library tools', exact: true })
          await tools.focus()
          await tap(0)
          await expect(page.getByRole('button', { name: 'Back to Library', exact: true })).toBeFocused()
          await expect(page.getByRole('navigation', { name: 'Library tools', exact: true })).toBeVisible()
          await tap(1)
          await expect(page.getByRole('button', { name: 'Library tools', exact: true })).toBeFocused()
        }
      }
      expect(errors).toEqual([])
    } finally {
      await closeFixture(application, directory)
    }
  })

test('fullscreen shared settings keep directional controller state across desktop changes and return navigation', async () => {
  const { application, page, directory, tap, errors } = await launch(1920, 1080, 1)
  try {
    const tabs = page.getByRole('navigation', { name: 'Settings section' })
    await tabs.getByRole('button', { name: 'Library', exact: true }).click()
    const sort = page.getByRole('button', { name: 'Default library sort', exact: true })
    await expect(sort).toBeFocused()
    await tap(15)
    await expect(sort).toContainText('Recently played')
    await expect(sort).toBeEnabled()
    await expect(sort).toBeFocused()
    const journal = page.getByRole('switch', { name: 'Journal after playing', exact: true })
    await tap(13)
    await expect(journal).toBeFocused()
    await tap(15)
    await expect(journal).toBeChecked()
    await expect(journal).toBeEnabled()
    await tap(15)
    await expect(journal).toBeChecked()
    await tap(14)
    await expect(journal).not.toBeChecked()
    await expect(journal).toBeEnabled()
    await tabs.getByRole('button', { name: 'Application', exact: true }).click()
    await expect(page.getByText(join(directory, 'logs'), { exact: true })).toHaveCount(1)
    const startup = page.getByRole('switch', { name: 'Start in fullscreen', exact: true })
    await expect(startup).toBeFocused()
    await tap(0)
    await expect(startup).toBeChecked()
    await expect(startup).toBeEnabled()
    await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', false),
    )
    await expect(page.locator('.avalon-shell')).toHaveClass(/desktop/)
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await tabs.getByRole('button', { name: 'Application', exact: true }).click()
    const desktop = page.getByRole('checkbox', { name: /^Start in fullscreen/ })
    await expect(desktop).toBeChecked()
    await desktop.click()
    await expect(desktop).not.toBeChecked()
    await expect(desktop).toBeEnabled()
    await tabs.getByRole('button', { name: 'Library', exact: true }).click()
    const desktopSort = page.getByRole('combobox', { name: 'Default library sort', exact: true })
    await expect(desktopSort).toHaveValue('RecentlyPlayed')
    await desktopSort.selectOption('NameAscending')
    await expect(desktopSort).toBeEnabled()
    await application.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:fullscreen:changed', true),
    )
    await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
    await expect(startup).not.toBeChecked()
    await tabs.getByRole('button', { name: 'Library', exact: true }).click()
    await expect(sort).toContainText('Name A–Z')
    await tabs.getByRole('button', { name: 'Application', exact: true }).click()
    await expect(startup).not.toBeChecked()
    await page.reload()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    expect(
      await api<{ preference: string; value: string }[]>(page, { route: 'preferences.presentation.get' }),
    ).toEqual(
      expect.arrayContaining([
        { preference: 'StartInFullscreen', value: 'false' },
        { preference: 'DefaultSort', value: 'NameAscending' },
      ]),
    )
    expect(errors).toEqual([])
  } finally {
    await closeFixture(application, directory)
  }
})
