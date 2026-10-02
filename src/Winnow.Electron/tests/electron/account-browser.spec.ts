import { _electron as electron, expect, test } from '@playwright/test'
import { build } from 'esbuild'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'
const root = fileURLToPath(new URL('../../../../.tmp/', import.meta.url)),
  harness = join(root, 'account-browser-harness.cjs')
test.beforeAll(async () => {
  mkdirSync(root, { recursive: true })
  await build({
    entryPoints: [fileURLToPath(new URL('./account-browser-main.ts', import.meta.url))],
    outfile: harness,
    bundle: true,
    platform: 'node',
    format: 'cjs',
    external: ['electron'],
    logLevel: 'silent',
  })
})
test('fullscreen account composer inserts only a fresh masked draft and discards it on cancel navigation or input lock', async () => {
  const directory = mkdtempSync(join(root, 'winnow-electron-account-input-'))
  const instance = await electron.launch({
    args: [harness, '--data-dir', directory],
    env: { ...process.env, ELECTRON_DISABLE_SECURITY_WARNINGS: 'true' },
  })
  try {
    await expect
      .poll(() => instance.evaluate(() => !!(globalThis as any).accountInputTest?.window()))
      .toBe(true)
    await expect
      .poll(() =>
        instance
          .context()
          .pages()
          .some((page) => page.url() === 'https://account.example.test/login'),
      )
      .toBe(true)
    const page = instance
      .context()
      .pages()
      .find((page) => page.url() === 'https://account.example.test/login')!
    const controls = instance.windows().find((page) => page.url() === 'about:blank')!
    const press = async (button: number) => {
      await instance.evaluate(() => {
        ;(globalThis as any).accountInputTest.state.pressed = []
      })
      await new Promise((resolve) => setTimeout(resolve, 120))
      await instance.evaluate((_electron, button) => {
        ;(globalThis as any).accountInputTest.state.pressed = [button]
      }, button)
      await new Promise((resolve) => setTimeout(resolve, 120))
      await instance.evaluate(() => {
        ;(globalThis as any).accountInputTest.state.pressed = []
      })
      await new Promise((resolve) => setTimeout(resolve, 120))
    }
    await page.getByLabel('Password').fill('existing-fixture-')
    await press(3)
    await expect(controls.getByRole('heading', { name: 'Type into the focused browser field' })).toBeVisible()
    await expect(controls.getByLabel('Text to insert')).toHaveAttribute('type', 'password')
    await expect(controls.getByLabel('Text to insert')).toHaveValue('')
    await controls.getByRole('link', { name: 'a', exact: true }).click({ noWaitAfter: true })
    await controls.getByRole('link', { name: 'Shift', exact: true }).click({ noWaitAfter: true })
    await controls.getByRole('link', { name: 'B', exact: true }).click({ noWaitAfter: true })
    await controls.getByRole('link', { name: 'Symbols', exact: true }).click({ noWaitAfter: true })
    await controls.getByRole('link', { name: '@', exact: true }).click({ noWaitAfter: true })
    await expect(controls.getByLabel('Text to insert')).toHaveValue('aB@')
    await controls.getByRole('link', { name: 'Done', exact: true }).click({ noWaitAfter: true })
    await expect(page.getByLabel('Password')).toHaveValue('existing-fixture-aB@')
    await press(3)
    await controls.getByLabel('Text to insert').fill('cancelled-fixture')
    await press(1)
    await expect(controls.getByLabel('Text to insert')).toHaveCount(0)
    await expect(page.getByLabel('Password')).toHaveValue('existing-fixture-aB@')
    await press(3)
    await controls.getByLabel('Text to insert').fill('stale-fixture')
    await instance.evaluate(() =>
      (globalThis as any).accountInputTest.browser.loadURL('https://account.example.test/changed'),
    )
    await expect(controls.getByLabel('Text to insert')).toHaveCount(0)
    await expect(page.getByLabel('Password')).toHaveValue('')
    await page.getByLabel('Password').click()
    await press(3)
    await controls.getByLabel('Text to insert').fill('locked-fixture')
    await instance.evaluate(() => (globalThis as any).accountInputTest.browser.setInputEnabled(false))
    await expect(controls.getByLabel('Text to insert')).toHaveCount(0)
    await press(3)
    await expect(controls.getByLabel('Text to insert')).toHaveCount(0)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await press(0)
    await expect(page.locator('output')).toHaveText('Ready')
    await expect(page.getByLabel('Password')).toHaveValue('')
    expect(
      await instance.evaluate(() => (globalThis as any).accountInputTest.state.providerScriptCalls),
    ).toBe(0)
    expect(await instance.evaluate(() => (globalThis as any).accountInputTest.state.forbidden)).toEqual([])
    await press(1)
    await expect
      .poll(() => instance.evaluate(() => !!(globalThis as any).accountInputTest.window()))
      .toBe(false)
  } finally {
    await instance.close()
    rmSync(directory, { recursive: true, force: true })
  }
})
