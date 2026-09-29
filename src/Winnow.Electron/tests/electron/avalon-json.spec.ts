import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { access, mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'

let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
const authored = {
  schemaVersion: 1,
  id: 'native-theme',
  name: 'Native authored theme',
  reason: 'A saved palette from the original themes folder.',
  seeds: {
    ground: '#131018',
    surface: '#1D1926',
    text: '#EFEAF5',
    flare: '#FF4D93',
    volt: '#A98CFF',
    amber: '#FFB63D',
    azure: '#57A8F0',
    danger: '#E04B45',
  },
  typography: { headingFont: 'Georgia', interfaceFont: 'Segoe UI', dataFont: 'Consolas', sizePercent: 110 },
  defaults: { transparency: 0, backdrop: 'mica', reach: 'chrome', layout: 'flush' },
}
test.beforeAll(async () => {
  const root = resolve('../..', '.tmp')
  await access(root)
  directory = await mkdtemp(join(root, 'winnow-electron-json-'))
  await mkdir(join(directory, 'themes'))
  await writeFile(join(directory, 'themes', 'mine.json'), JSON.stringify(authored))
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
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
for (const mode of ['desktop', 'fullscreen'] as const)
  test(`${mode} authored JSON selection, live reload, typography reset and safe export survive restart`, async () => {
    await application.evaluate(({ BrowserWindow }, fullscreen) => {
      const window = BrowserWindow.getAllWindows()[0]
      window.setFullScreen(fullscreen)
      if (!fullscreen) window.setContentSize(1440, 1000)
      window.webContents.send('winnow:fullscreen:changed', fullscreen)
    }, mode === 'fullscreen')
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    const choice = page.getByRole('combobox', { name: /^Avalon palette/ })
    await choice.selectOption('winnow')
    await writeFile(join(directory, 'themes', 'mine.json'), JSON.stringify(authored))
    await choice.selectOption('native-theme')
    await expect(page.locator('html')).toHaveCSS('--bg', '#131018')
    await expect(page.getByLabel('Heading font', { exact: true })).toHaveValue('Georgia')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('110')
    await expect(page.getByRole('combobox', { name: 'Pane layout', exact: true })).toHaveValue('flush')
    await expect(page.getByRole('checkbox', { name: /Include content panes/ })).not.toBeChecked()
    await page.getByLabel('Heading font', { exact: true }).fill('Arial')
    const edited = {
      ...authored,
      name: 'Reloaded authored theme',
      seeds: { ...authored.seeds, ground: '#212030' },
      typography: { ...authored.typography, headingFont: 'Cambria', sizePercent: 90 },
    }
    await writeFile(join(directory, 'themes', 'mine.json'), JSON.stringify(edited))
    await expect(page.locator('html')).toHaveCSS('--bg', '#212030')
    await expect(choice.locator('option:checked')).toHaveText('Reloaded authored theme')
    await expect(page.getByLabel('Heading font', { exact: true })).toHaveValue('Arial')
    await page.getByRole('button', { name: 'Reset theme typography', exact: true }).click()
    await expect(page.getByLabel('Heading font', { exact: true })).toHaveValue('Cambria')
    await expect(page.getByRole('slider', { name: 'Theme text size', exact: true })).toHaveValue('90')
    await page.getByRole('button', { name: 'Export palette as JSON', exact: true }).click()
    await expect(page.getByText(/Exported native-theme.*\.json/)).toBeVisible()
    expect(JSON.parse(await readFile(join(directory, 'themes', 'mine.json'), 'utf8'))).toEqual(edited)
    const exported = (await readdir(join(directory, 'themes'))).filter((name) =>
      name.startsWith('native-theme'),
    )
    expect(exported.length).toBeGreaterThan(0)
    expect(
      JSON.parse(await readFile(join(directory, 'themes', exported.at(-1)!), 'utf8')).typography.headingFont,
    ).toBe('Cambria')
    await page.getByRole('slider', { name: 'Transparency', exact: true }).fill('14')
    await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('14')
    // A range input changes its DOM value before the asynchronous save finishes.
    await expect
      .poll(() =>
        page.evaluate(async () => {
          const response = await window.winnow.request({ route: 'preferences.presentation.get' })
          if (!response.ok) throw Error(`Could not read saved preferences: ${response.status}`)
          return (response.data as { preference: string; value: string }[]).find(
            (row) => row.preference === 'Transparency',
          )?.value
        }),
      )
      .toBe('14')
    await page.reload()
    await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
    await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
    await expect(choice).toHaveValue('native-theme')
    await expect(page.locator('html')).toHaveCSS('--bg', '#212030')
    await expect(page.getByLabel('Heading font', { exact: true })).toHaveValue('Cambria')
    await expect(page.getByRole('slider', { name: 'Transparency', exact: true })).toHaveValue('14')
    await page.screenshot({ path: join(directory, `${mode}-authored-palette.png`) })
    expect(errors).toEqual([])
  })
test.afterAll(async () => {
  if (application) {
    let stop: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        application.close(),
        new Promise<void>((done) => {
          stop = setTimeout(() => {
            application.process().kill()
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(stop)
    }
  }
  if (!directory) return
  try {
    const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    if (new URL(endpoint.address).hostname !== '127.0.0.1') throw Error('Unexpected test backend address')
    await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
      method: 'POST',
      headers: { Authorization: `Bearer ${endpoint.token}` },
      signal: AbortSignal.timeout(5000),
    })
  } catch {
    /* Preserve disposable test artifacts after a failed startup. */
  }
})
