import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'

import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} original Steam run destination uses the authenticated action once without reader or fallback`, async ({}, info) => {
    const directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-plugin-actions-'))
    const application = await electron.launch({
      executablePath: electronPath as unknown as string,
      args: [resolve('tests/electron/link-action-routing-main.mjs'), '--data-dir', directory, '--no-sync'],
      env: {
        ...(Object.fromEntries(
          Object.entries(process.env).filter(
            ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
          ),
        ) as Record<string, string>),
        WINNOW_BACKEND_PATH: fixture,
        WINNOW_ACTIVATION_HELPER_PATH: prebuiltActivationHelper,
      },
      chromiumSandbox: true,
    })
    try {
      const page = await application.firstWindow()
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({
        timeout: 45000,
      })
      await expect(page.locator('.startup-presentation')).toHaveCount(0)
      const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8')) as {
        address: string
        token: string
      }
      const control = async (path: string, body?: unknown) => {
        const response = await fetch(new URL(`/__fixture/plugin-actions/${path}`, endpoint.address), {
          method: body === undefined ? 'GET' : 'POST',
          headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
          ...(body === undefined ? {} : { body: JSON.stringify(body) }),
        })
        expect(response.ok).toBe(true)
        return response.status === 204 ? null : response.json()
      }
      await control('seed-steam', {})
      expect(
        await page.evaluate(async () => (await window.winnow.applicationInfo!()).steamStoreAvailable),
      ).toBe(true)
      await page.evaluate(async () => {
        const saved = await window.winnow.request({
          route: 'preferences.presentation.put',
          params: { preference: 'LinkDestination' },
          body: { value: 'in-app' },
        })
        if (!saved.ok) throw Error(saved.message)
        const rejected = await window.winnow.openExternal('steam://run/440')
        if (rejected.opened) throw Error('The reading bridge must not dispatch game actions')
      })
      await application.evaluate(({ BrowserWindow }, mode) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setContentSize(1920, 1080)
        window.setFullScreen(mode === 'fullscreen')
        window.focus()
      }, mode)
      await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
      await page
        .getByRole('navigation', { name: 'Main navigation' })
        .getByRole('button', { name: 'Library', exact: true })
        .click()
      await page.locator('.avalon-library [data-avalon-game="440"]').click()
      await expect(
        page.locator('.avalon-details').getByRole('heading', { name: 'Steam fixture', exact: true }),
      ).toBeVisible()
      await page.locator('.avalon-details-actions').getByRole('button', { name: 'Play', exact: true }).click()
      await expect.poll(async () => (await control('state')).shellAttempts).toEqual(['steam://run/440'])
      const state = await control('state')
      expect(state.ownerships).toEqual([
        { id: 440, releaseId: 440, store: 'steam', installed: true, intentLive: true },
      ])
      expect(state.calls).toEqual([])
      const dispatch = await application.evaluate(({ BrowserWindow }) => ({
        ...(globalThis as any).__pluginGameActions,
        windows: BrowserWindow.getAllWindows().length,
      }))
      expect(dispatch.actions).toEqual([
        {
          path: '/api/v1/entries/440/actions',
          body: { operationId: expect.any(String), action: 'Play' },
          status: 200,
          result: 0,
        },
      ])
      expect(dispatch.shellAttempts).toEqual([])
      expect(dispatch.windows).toBe(1)
      await expect(page.getByText(/Opened in your browser/)).toHaveCount(0)
      expect(errors).toEqual([])
      await page.screenshot({ path: info.outputPath(`${mode}-steam-action.png`) })
    } finally {
      await closeFixture(application, directory)
    }
  })
}
