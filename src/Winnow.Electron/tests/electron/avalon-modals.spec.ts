import { libraryAction } from './library-controls'
import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryResponse } from '../../src/renderer/api/types'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.describe.configure({ mode: 'serial' })
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-modals-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await expect
    .poll(() => page.evaluate(async () => (await window.winnow.request({ route: 'library.get' })).ok))
    .toBe(true)
})

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
async function library(mode: 'desktop' | 'fullscreen') {
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]
    window.setFullScreen(false)
    window.setMinimumSize(800, 600)
    window.setContentSize(mode === 'desktop' ? 800 : 1280, mode === 'desktop' ? 600 : 720)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  await page.locator('.avalon-cover').first().focus()
  await (await libraryAction(page, 'Add to list…')).click()
  await expect(page.getByRole('textbox', { name: 'New list name' })).toBeFocused()
}
async function alignChoices() {
  return page.evaluate(() => {
    const input = document.querySelector('.list-prompt-dialog input')!.getBoundingClientRect()
    const choices = document.querySelector('.list-prompt-choices')!
    const buttons = [...choices.querySelectorAll('button')].map((button) => {
      const box = button.getBoundingClientRect()
      return { widthDelta: Math.abs(box.width - input.width), xDelta: Math.abs(box.x - input.x) }
    })
    return { buttons, scrolling: choices.scrollHeight > choices.clientHeight, count: buttons.length }
  })
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} short list choices align with the name field and Tab stays inside the modal`, async () => {
    const previous = await api<LibraryResponse>({ route: 'library.get' })
    for (const list of previous.lists)
      await api({
        route: 'list.delete',
        params: { listId: list.id },
        body: { expectedRevision: list.revision },
      })
    for (const name of ['Backlog', 'Where did I get these?'])
      await api<GameList>({ route: 'list.create', body: { name, releaseIds: [] } })
    await library(mode)
    await expect(page.getByRole('group', { name: 'Existing lists' }).getByRole('button')).toHaveCount(2)
    const geometry = await alignChoices()
    expect(geometry.scrolling).toBe(false)
    for (const box of geometry.buttons) {
      expect(box.widthDelta).toBeLessThan(1)
      expect(box.xDelta).toBeLessThan(1)
    }
    const origin = await page.locator('.avalon-content').elementHandle()
    await page.keyboard.press('Control+k')
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'New list name' })).toBeFocused()
    for (const key of ['Tab', 'Tab', 'Tab', 'Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab']) {
      await page.keyboard.press(key)
      expect(
        await page.getByRole('dialog').evaluate((dialog) => dialog.contains(document.activeElement)),
      ).toBe(true)
    }
    await page.getByRole('textbox', { name: 'New list name' }).fill('Unconfirmed draft')
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    if (mode === 'desktop')
      await expect(page.getByRole('button', { name: 'Add to list…', exact: true })).toBeFocused()
    else await expect(page.locator('.avalon-library .avalon-cover[data-selected="true"]')).toBeFocused()
    expect(await origin!.evaluate((element) => element.isConnected)).toBe(true)
    expect((await api<LibraryResponse>({ route: 'library.get' })).lists).toHaveLength(2)
  })

  test(`${mode} many long list choices scroll while the name and actions remain visible`, async () => {
    for (let index = 1; index <= 40; index++)
      await api({
        route: 'list.create',
        body: {
          name: `List ${index}: games for a very long weekend with friends and family`,
          releaseIds: [],
        },
      })
    await library(mode)
    await expect(page.getByRole('group', { name: 'Existing lists' }).getByRole('button')).toHaveCount(42)
    const geometry = await alignChoices()
    expect(geometry.scrolling).toBe(true)
    for (const box of geometry.buttons) {
      expect(box.widthDelta).toBeLessThan(1)
      expect(box.xDelta).toBeLessThan(1)
    }
    const last = page.getByRole('group', { name: 'Existing lists' }).getByRole('button').last()
    await last.focus()
    expect(
      await page.locator('.list-prompt-choices').evaluate((element) => element.scrollTop),
    ).toBeGreaterThan(0)
    for (const control of [
      last,
      page.getByRole('textbox', { name: 'New list name' }),
      page.getByRole('button', { name: 'New list', exact: true }),
      page.getByRole('button', { name: 'Cancel', exact: true }),
    ]) {
      const box = await control.boundingBox(),
        viewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.y).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width + 1)
      expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height + 1)
    }
    await page.screenshot({ path: join(directory, `${mode}-long-list-prompt.png`) })
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })

  test(`${mode} destructive list confirmation starts on cancel and Enter preserves the list`, async () => {
    await (await libraryAction(page, 'Manage library')).click()
    const panel = page
      .locator('section.feature-panel')
      .filter({ has: page.getByRole('heading', { name: 'Backlog', exact: true }) })
    const trigger = panel.getByRole('button', { name: 'Delete list…', exact: true })
    await trigger.click()
    const dialog = page.getByRole('dialog', { name: 'Delete Backlog?' })
    await expect(dialog.getByRole('button', { name: 'Keep list' })).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(dialog).toHaveCount(0)
    await expect(trigger).toBeFocused()
    expect(
      (await api<LibraryResponse>({ route: 'library.get' })).lists.some((list) => list.name === 'Backlog'),
    ).toBe(true)
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
    expect(errors).toEqual([])
  })
}
test.afterAll(async () => closeFixture(application, directory))
