import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import electronPath from 'electron'
import { mkdtemp } from 'node:fs/promises'
import { DatabaseSync } from 'node:sqlite'
import { join, resolve } from 'node:path'
import { closeFixture } from './fixture-cleanup'
import type { Mode } from '../../src/renderer/api/types'

// Frozen TileActionsTests: Anvil uses the real LibraryFixture; Fez uses TileFixture IDs.
// Every action request is intercepted by this test main before any OS launch can occur.
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeEach(async () => {
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-tile-commands-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/tile-command-contracts-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click({ timeout: 45000 })
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
})
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus && page && !page.isClosed())
    await info.attach('tile-contract-failure', { body: await page.screenshot(), contentType: 'image/png' })
  await closeFixture(application, directory)
  expect(errors).toEqual([])
})
async function seed(mode: Mode, title = 'Anvil', store = 'steam', installed = false) {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    db.prepare('INSERT INTO works(id,name,sort_name) VALUES(1,?,?)').run(title, title)
    db.prepare("INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,?,'windows')").run(title)
    db.prepare('INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,?,?)').run(
      store,
      Number(installed),
    )
    db.prepare('INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,?,?)').run(
      'steam',
      title === 'Anvil' ? '700001' : '620',
    )
    if (title === 'Fez')
      db.exec("INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'gog','1971477531');")
    db.exec(
      "INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at) VALUES(1,0,NULL,'test','2026-08-26T12:00:00Z');",
    )
  } finally {
    db.close()
  }
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1920, 1080)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { tilePad: state })
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [
        {
          index: 0,
          connected: true,
          mapping: 'standard',
          axes: [0, 0, 0, 0],
          buttons: Array.from({ length: 17 }, (_, index) => ({
            pressed: state.pressed.includes(index),
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await expect(page.locator('.avalon-library [data-avalon-game="1"]')).toBeVisible()
}
async function tap(button: number) {
  for (const pressed of [[], [button], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).tilePad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
const records = () =>
  application.evaluate(
    () =>
      (globalThis as any).__tileCommands.records as {
        ownershipId: number
        action: string
        operationId: string
      }[],
  )
async function assertBucketInAX(tile: Locator) {
  await expect(tile).toHaveAttribute('aria-description', 'Never played')
  const session = await page.context().newCDPSession(page)
  try {
    const { root } = await session.send('DOM.getDocument')
    const { nodeId } = await session.send('DOM.querySelector', {
      nodeId: root.nodeId,
      selector: '.avalon-library [data-avalon-game="1"]',
    })
    const { node } = await session.send('DOM.describeNode', { nodeId })
    const { nodes } = await session.send('Accessibility.getPartialAXTree', {
      backendNodeId: node.backendNodeId,
      fetchRelatives: false,
    })
    expect(nodes.find((entry) => entry.backendDOMNodeId === node.backendNodeId)?.description?.value).toBe(
      'Never played',
    )
  } finally {
    await session.detach()
  }
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} exact Anvil fixture keeps rail bucket, grid Details and Install on the library command`, async ({}, info) => {
    await seed(mode)
    const tile = page.locator('.avalon-library [data-avalon-game="1"]')
    await assertBucketInAX(tile)
    await expect(page.getByRole('button', { name: /^Never played/ })).toBeVisible()
    if (mode === 'desktop') {
      await tile.hover()
      await tile.locator('..').getByRole('button', { name: 'Install', exact: true }).click()
      await expect.poll(async () => (await records()).length).toBe(1)
      expect((await records())[0]).toMatchObject({ ownershipId: 1, action: 'Install' })
      await expect(page.locator('.avalon-details')).toHaveCount(0)
      await tile.hover()
      await tile.locator('..').getByRole('button', { name: 'Details', exact: true }).click()
    } else {
      await tile.focus()
      await tap(2)
      expect(await records()).toEqual([])
      await tap(0)
    }
    const detail = page.locator('.avalon-details')
    await expect(detail.getByRole('heading', { name: 'Anvil', exact: true })).toBeVisible()
    const before = mode === 'desktop' ? 1 : 0
    expect(await records()).toHaveLength(before)
    await detail
      .locator('.avalon-details-actions')
      .getByRole('button', { name: 'Install', exact: true })
      .click()
    await expect.poll(async () => (await records()).length).toBe(before + 1)
    expect((await records()).every((record) => record.ownershipId === 1 && record.action === 'Install')).toBe(
      true,
    )
    expect(new Set((await records()).map((record) => record.operationId)).size).toBe(before + 1)
    await page.screenshot({ path: info.outputPath(`${mode}-anvil-install.png`) })
  })

  for (const store of ['steam', 'gog']) {
    test(`${mode} Fez ${store} tile and Details share one pending primary operation and exact ownership`, async ({}, info) => {
      await seed(mode, 'Fez', store, true)
      const tile = page.locator('.avalon-library [data-avalon-game="1"]')
      await application.evaluate(() => {
        ;(globalThis as any).__tileCommands.hold = true
      })
      if (mode === 'desktop') {
        await tile.hover()
        const primary = tile.locator('..').getByRole('button', { name: 'Play', exact: true })
        await expect(primary).toHaveAttribute(
          'title',
          `Launch through ${store === 'steam' ? 'Steam' : 'GOG'}`,
        )
        await primary.click()
      } else {
        await tile.focus()
        await tap(2)
      }
      await expect.poll(() => application.evaluate(() => (globalThis as any).__tileCommands.held)).toBe(true)
      expect(await records()).toEqual([{ ownershipId: 1, action: 'Play', operationId: expect.any(String) }])
      const first = (await records())[0].operationId
      await tile.click()
      const detail = page.locator('.avalon-details')
      await expect(detail.getByRole('heading', { name: 'Fez', exact: true })).toBeVisible()
      const primary = detail
        .locator('.avalon-details-actions')
        .getByRole('button', { name: 'Play', exact: true })
      await expect(primary).toBeVisible()
      if (await primary.isEnabled()) await primary.click()
      await page.evaluate(
        () => new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done()))),
      )
      expect(await records()).toEqual([{ ownershipId: 1, action: 'Play', operationId: first }])
      await application.evaluate(() => {
        ;(globalThis as any).__tileCommands.release()
      })
      await expect(primary).toBeEnabled()
      await primary.click()
      await expect.poll(async () => (await records()).length).toBe(2)
      expect((await records())[1]).toMatchObject({ ownershipId: 1, action: 'Play' })
      expect((await records())[1].operationId).not.toBe(first)
      await detail.getByRole('button', { name: 'More', exact: true }).click()
      if (mode === 'fullscreen')
        await page.locator('.avalon-actions-panel').evaluate(async (panel) => {
          await Promise.all(panel.getAnimations().map((animation) => animation.finished))
        })
      const link = page.getByRole('button', {
        name: store === 'gog' ? 'Show in GOG Galaxy' : 'Store page',
        exact: true,
      })
      await expect(link).toBeVisible()
      await expect(link).toHaveAttribute(
        'title',
        store === 'gog'
          ? 'goggalaxy://opengameview/gog_1971477531'
          : 'https://store.steampowered.com/app/620/',
      )
      expect(await records()).toHaveLength(2)
      await page.screenshot({ path: info.outputPath(`${mode}-fez-${store}-actions.png`) })
    })
  }
}
