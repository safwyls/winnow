import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication } from '@playwright/test'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import electronPath from 'electron'
import { profileDirectory } from '../../src/main/storage'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import { prebuiltActivationHelper, prebuiltBackend } from './prebuilt-backend'

test('development appearance overrides remain live on both surfaces without changing either saved appearance store', async () => {
  const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-session-'))
  const preferenceFile = join(
    profileDirectory(join(directory, 'electron-userdata'), directory),
    'preferences.json',
  )
  const persisted = structuredClone(DEFAULT_PROFILE)
  persisted.settings.avalon = { palette: 'nightshift' }
  const original = JSON.stringify(persisted)
  await mkdir(dirname(preferenceFile), { recursive: true })
  await writeFile(preferenceFile, original)
  let app: ElectronApplication | undefined
  let endpoint: { address: string; token: string } | undefined
  const errors: string[] = []
  try {
    app = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [
        resolve('.'),
        '--data-dir',
        directory,
        '--seed-sample',
        '--no-sync',
        '--theme=tungsten',
        '--transparency=60',
        '--layout=flush',
      ],
      env: {
        ...(Object.fromEntries(
          Object.entries(process.env).filter(
            ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
          ),
        ) as Record<string, string>),
        WINNOW_BACKEND_PATH: prebuiltBackend,
        WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
      },
      chromiumSandbox: true,
      timeout: 60_000,
    })
    const page = await app.firstWindow()
    page.on('pageerror', (error) => errors.push(error.message))
    await expect(page.locator('.avalon-cover').first()).toBeVisible()
    endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
    if (!endpoint || new URL(endpoint.address).hostname !== '127.0.0.1')
      throw Error('Unexpected fixture address')
    const saved = async () => {
      const response = await fetch(new URL('/api/v1/preferences/presentation', endpoint!.address), {
        headers: { Authorization: `Bearer ${endpoint!.token}` },
        redirect: 'error',
      })
      expect(response.ok).toBe(true)
      return await response.json()
    }
    const before = await saved()
    for (const [mode, palette] of [
      ['desktop', 'box-art'],
      ['fullscreen', 'bottle-green'],
    ] as const) {
      await app.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setFullScreen(false)
        window.setContentSize(1440, 1000)
        window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
      const choice = page.getByRole('combobox', { name: /^Avalon palette/ })
      if (mode === 'desktop') await expect(choice).toHaveValue('tungsten')
      await choice.selectOption(palette)
      await expect
        .poll(() =>
          page.evaluate(async () => {
            const profile = (await window.winnow.loadPreferences()) as {
              settings: { avalon: { palette: string } }
            }
            return profile?.settings?.avalon?.palette
          }),
        )
        .toBe(palette)
      for (const [preference, value] of [
        ['Transparency', '12'],
        ['Transparency', '0'],
        ['Backdrop', 'mica'],
        ['TranslucentWall', 'true'],
        ['TranslucentWall', 'false'],
        ['Layout', 'floating'],
        ['Typography', '{}'],
      ]) {
        const result = await page.evaluate(
          ({ preference, value }) =>
            window.winnow.request({
              route: 'preferences.presentation.put',
              params: { preference },
              body: { value },
            }),
          { preference, value },
        )
        expect(result.ok).toBe(true)
      }
      await page.reload()
      await expect(page.locator('.avalon-cover').first()).toBeVisible()
      await page.getByRole('button', { name: 'Theme Studio', exact: true }).click()
      await expect(page.getByRole('combobox', { name: /^Avalon palette/ })).toHaveValue(palette)
      expect(await readFile(preferenceFile, 'utf8')).toBe(original)
      expect(await saved()).toEqual(before)
    }
    const ordinary = await page.evaluate(() =>
      window.winnow.request({
        route: 'preferences.presentation.put',
        params: { preference: 'DefaultSort' },
        body: { value: 'LastPlayedDescending' },
      }),
    )
    expect(ordinary.ok).toBe(true)
    expect(await saved()).toContainEqual({ preference: 'DefaultSort', value: 'LastPlayedDescending' })
    expect(await readFile(preferenceFile, 'utf8')).toBe(original)
    expect(errors).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
})
