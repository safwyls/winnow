import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { build } from 'esbuild'
import { mkdtemp, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

let application: ElectronApplication, page: Page
test.beforeAll(async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-actions-probe-'))
  await build({
    entryPoints: [resolve('tests/electron/actions-probe-renderer.tsx')],
    outfile: join(directory, 'renderer.js'),
    bundle: true,
    platform: 'browser',
    format: 'iife',
    jsx: 'automatic',
    loader: { '.ttf': 'file', '.woff2': 'file' },
    logLevel: 'silent',
  })
  await writeFile(
    join(directory, 'index.html'),
    '<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data:; font-src \'self\'"><link rel="stylesheet" href="renderer.css"></head><body><div id="root"></div><script src="renderer.js"></script></body></html>',
  )
  const env = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/actions-probe-main.mjs'),
      '--data-dir',
      directory,
      join(directory, 'index.html'),
    ],
    env,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  await expect(page.getByRole('button', { name: 'More actions', exact: true })).toBeVisible()
})
test.afterAll(async () => closeFixture(application))

async function press(button: number) {
  for (const buttons of [[], [button], []])
    await page.evaluate(async (buttons) => {
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, index) => ({
              pressed: buttons.includes(index),
              value: buttons.includes(index) ? 1 : 0,
              touched: false,
            })),
          },
        ],
      })
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, buttons)
}
async function state() {
  return page.evaluate(
    () =>
      (
        window as unknown as {
          actionProbe: {
            attached: number
            detached: number
            chosen: number
            disabled: number
            nested: number
            leaked: number
          }
        }
      ).actionProbe,
  )
}

for (const input of ['controller', 'keyboard', 'pointer'])
  test(`action choices invoke once through ${input} and disabled choices reject focus and clicks`, async () => {
    const before = await state()
    const trigger = page.getByRole('button', { name: 'More actions', exact: true })
    await trigger.click()
    const panel = page.getByRole('dialog', { name: 'More actions', exact: true })
    await panel.evaluate(async (node) => {
      await Promise.all(node.getAnimations().map((animation) => animation.finished))
    })
    const disabled = panel.getByRole('button', { name: 'Unavailable action', exact: true })
    await expect(disabled).toBeDisabled()
    await disabled.evaluate((element) => element.focus())
    await expect(disabled).not.toBeFocused()
    const box = (await disabled.boundingBox())!
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
    await expect(panel).toBeVisible()
    expect((await state()).disabled).toBe(0)
    const enabled = panel.getByRole('button', { name: 'Pin this game', exact: true })
    await enabled.focus()
    if (input === 'controller') await press(0)
    else if (input === 'keyboard') await page.keyboard.press('Enter')
    else await enabled.click()
    await expect(panel).toHaveCount(0)
    await expect(trigger).toBeFocused()
    const after = await state()
    expect(after.chosen).toBe(before.chosen + 1)
    expect(after.disabled).toBe(0)
    expect(after.attached).toBe(1)
    expect(after.detached).toBe(0)
  })

test('nested action menu keeps its origin attached and returns to the original trigger', async () => {
  const trigger = page.getByRole('button', { name: 'More actions', exact: true })
  await trigger.click()
  await page.getByRole('button', { name: 'Remove game', exact: true }).focus()
  await press(0)
  const panel = page.getByRole('dialog', { name: 'Remove this game?', exact: true })
  await expect(panel).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(panel.getByRole('button', { name: 'Keep game', exact: true })).toBeFocused()
  expect(await page.locator('main').evaluate((node) => node.isConnected && node.hasAttribute('inert'))).toBe(
    true,
  )
  await press(0)
  await expect(panel).toHaveCount(0)
  await expect(trigger).toBeFocused()
  expect(await state()).toMatchObject({ attached: 1, detached: 0, nested: 1, disabled: 0 })
})

for (const [text, reduced] of [
  [1, true],
  [1.4, true],
  [1, false],
] as const)
  test(`fourteen action panel wraps and scrolls with controller at text ${text}, reduced ${reduced}`, async ({}, info) => {
    await page.evaluate(
      ({ text, reduced }) => {
        document.documentElement.style.setProperty('--fullscreen-text-scale', String(text))
        document.documentElement.dataset.reducedMotion = String(reduced)
      },
      { text, reduced },
    )
    await page.getByRole('button', { name: 'Fourteen actions', exact: true }).click()
    const panel = page.getByRole('dialog', { name: 'More actions', exact: true })
    await expect(panel).toBeVisible()
    await panel.evaluate(async (node) => {
      await Promise.all(node.getAnimations().map((animation) => animation.finished))
    })
    const bounds = (await panel.boundingBox())!
    expect(bounds.width).toBeGreaterThanOrEqual(500)
    expect(bounds.width).toBeLessThanOrEqual(900)
    expect(bounds.width).toBeLessThan(1920 * 0.6)
    expect(Math.abs(bounds.x + bounds.width - 1920)).toBeLessThanOrEqual(2)
    expect(bounds.y).toBe(0)
    expect(Math.abs(bounds.height - 1080)).toBeLessThanOrEqual(2)
    for (const button of await panel.getByRole('button').all()) {
      expect(
        await button.evaluate((node) => parseFloat(getComputedStyle(node).fontSize)),
      ).toBeGreaterThanOrEqual(28 * text - 0.1)
      expect((await button.boundingBox())!.width).toBeLessThanOrEqual(bounds.width)
    }
    const body = panel.locator('.avalon-actions-body')
    expect(await body.evaluate((node) => node.scrollHeight > node.clientHeight)).toBe(true)
    await page.screenshot({ path: info.outputPath('fourteen-actions.png') })
    // Hold the D-pad until all source rows have been visited; production repeat is 180ms.
    await press(-1)
    await page.evaluate(() =>
      Object.defineProperty(navigator, 'getGamepads', {
        configurable: true,
        value: () => [
          {
            index: 0,
            connected: true,
            axes: [0, 0, 0, 0],
            buttons: Array.from({ length: 17 }, (_, index) => ({
              pressed: index === 13,
              value: index === 13 ? 1 : 0,
              touched: false,
            })),
          },
        ],
      }),
    )
    await expect(panel.getByRole('button', { name: 'Close', exact: true })).toBeFocused()
    expect(await body.evaluate((node) => node.scrollTop)).toBeGreaterThan(0)
    await page.evaluate(() =>
      Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
    )
    await page.screenshot({ path: info.outputPath('fourteen-actions-scrolled.png') })
    expect((await state()).detached).toBe(0)
    await page.keyboard.press('Escape')
  })
