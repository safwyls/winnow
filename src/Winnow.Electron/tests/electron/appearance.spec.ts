import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication
let page: Page
let directory: string
const errors: string[] = []
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-appearance-'))
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
})

test('desktop material requests preserve opaque covers and one coat per pane, with flush/floating geometry', async () => {
  await application.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]
    window.setFullScreen(false)
    window.setContentSize(1440, 1000)
    window.webContents.send('winnow:fullscreen:changed', false)
  })
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  const slider = page.getByRole('slider', { name: 'Transparency', exact: true })
  await expect(slider).toBeEnabled()
  await slider.fill('100')
  await expect(slider).toHaveValue('100')
  await page.getByRole('combobox', { name: 'Backdrop', exact: true }).selectOption('acrylic')
  const result = await page.evaluate(() =>
    window.winnow.windowAppearance!({ enabled: true, material: 'acrylic', background: '#0F1C1E' }),
  )
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', result.requested)
  const shell = page.locator('.avalon-shell')
  if (result.requested !== 'none') {
    await expect(shell).toHaveCSS('--avalon-shell-ground', '#040C0D26')
    await expect(shell).toHaveCSS('--avalon-pane-ground', '#0F1C1E96')
    await expect(page.locator('html')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  } else {
    await expect(shell).toHaveCSS('--avalon-pane-ground', '#0F1C1EFF')
  }
  expect(
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].getOpacity()),
  ).toBe(1)
  await page.getByRole('combobox', { name: 'Pane layout', exact: true }).selectOption('floating')
  await expect(shell).toHaveAttribute('data-pane-layout', 'floating')
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
  const floating = await page.evaluate(() => {
    const rail = document.querySelector('.avalon-rail')!.getBoundingClientRect()
    const pane = document.querySelector('.avalon-content')!.getBoundingClientRect()
    return {
      gap: pane.left - rail.right,
      cover: getComputedStyle(document.querySelector('.avalon-cover')!).backgroundColor,
    }
  })
  expect(floating.gap).toBeGreaterThan(0)
  expect(floating.cover).toMatch(/^rgb\(/)
  await page.screenshot({ path: join(directory, 'desktop-floating-acrylic.png') })
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await page.getByRole('combobox', { name: 'Pane layout', exact: true }).selectOption('flush')
  await expect(shell).toHaveAttribute('data-pane-layout', 'flush')
  await page.getByRole('combobox', { name: 'Backdrop', exact: true }).selectOption('mica')
  await expect(page.locator('html')).toHaveAttribute(
    'data-avalon-material',
    result.supported ? 'mica' : 'none',
  )
  await page.getByRole('button', { name: 'Winnow home', exact: true }).click()
  const flushGap = await page.evaluate(
    () =>
      document.querySelector('.avalon-content')!.getBoundingClientRect().left -
      document.querySelector('.avalon-rail')!.getBoundingClientRect().right,
  )
  expect(flushGap).toBeCloseTo(0, 1)
  await page.screenshot({ path: join(directory, 'desktop-flush-mica.png') })
})

test('fullscreen and disabled transparency restore solid surfaces while saved material and reach survive reload', async () => {
  await application.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0].webContents.send('winnow:fullscreen:changed', true),
  )
  await expect(page.locator('.avalon-shell')).toHaveClass(/fullscreen/)
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', 'none')
  await expect(page.locator('.avalon-shell')).toHaveCSS('--avalon-pane-ground', '#0F1C1EFF')
  expect(
    await application.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].getOpacity()),
  ).toBe(1)
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('100')
  await expect(page.getByRole('combobox', { name: 'Backdrop', exact: true })).toHaveValue('mica')
  await page.getByRole('slider', { name: 'Transparency', exact: true }).fill('0')
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')
  await page.getByRole('checkbox', { name: /Include content panes/ }).click()
  await expect(page.getByRole('checkbox', { name: /Include content panes/ })).not.toBeChecked()
  await page.reload()
  await expect(page.locator('.avalon-shell')).toBeVisible()
  await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
  await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('0')
  await expect(page.getByRole('combobox', { name: 'Backdrop', exact: true })).toHaveValue('mica')
  await expect(page.getByRole('combobox', { name: 'Pane layout', exact: true })).toHaveValue('flush')
  await expect(page.getByRole('checkbox', { name: /Include content panes/ })).not.toBeChecked()
  await expect(page.locator('html')).toHaveAttribute('data-avalon-material', 'none')
  expect(errors).toEqual([])
})
test.afterAll(async () => closeFixture(application, directory))
