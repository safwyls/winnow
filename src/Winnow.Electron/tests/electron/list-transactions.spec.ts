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
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { closeFixture } from './fixture-cleanup'
import { selectCollection, expectCollection } from './collection-controls'
import { libraryAction, returnToLibrary } from './library-controls'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'
import type { ApiRequest } from '../../src/shared/bridge'
import type { GameList, LibraryResponse, Mode } from '../../src/renderer/api/types'

// Frozen ListAtomicWriteTests, ListPromptParityTests, ListWriteParityTests and RailListControlsTests.
// All list writes use the production HTTP API and temporary SQLite, including failing SQL triggers.
let application: ElectronApplication, page: Page, directory: string
const errors: string[] = []
test.beforeEach(async () => {
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-list-transactions-'))
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/list-transactions-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: Object.fromEntries(
      Object.entries(process.env).filter(
        ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
      ),
    ) as Record<string, string>,
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await application.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click({ timeout: 45000 })
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
})
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus && page && !page.isClosed())
    await info.attach('list-failure', { body: await page.screenshot(), contentType: 'image/png' })
  await closeFixture(application, directory)
  expect(errors).toEqual([])
})
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
function sql(statement: string) {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    db.exec(statement)
  } finally {
    db.close()
  }
}
function storedItems() {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    return db.prepare('SELECT list_id, release_id, position FROM list_items ORDER BY list_id, position').all()
  } finally {
    db.close()
  }
}
async function seed(count: number, mode: Mode, width = 1920, height = 1080) {
  sql(`WITH RECURSIVE seq(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM seq WHERE id<${count})
    INSERT INTO works(id,name,sort_name) SELECT id,'Game '||id,'Game '||id FROM seq;
    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
    INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
    INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
    INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
    INSERT INTO list_items(list_id,release_id,position) VALUES(1,${count},0),(1,1,1);`)
  await application.evaluate(() => {
    ;(globalThis as any).__listTransactions.feedFixture = true
  })
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  await application.evaluate(
    ({ BrowserWindow }, size) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(size.width, size.height)
      window.webContents.send('winnow:fullscreen:changed', size.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    Object.assign(window, { listPad: state })
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
  await expect(page.locator('.avalon-library [data-avalon-game]')).toHaveCount(count)
}
async function accept(target: Locator, mode: Mode) {
  await target.focus()
  if (mode === 'desktop') {
    await target.press('Enter')
    return
  }
  for (const pressed of [[], [0], []])
    await page.evaluate(async (pressed) => {
      ;(window as any).listPad.pressed = pressed
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    }, pressed)
}
async function savedList(id = 1) {
  return (await api<LibraryResponse>({ route: 'library.get' })).lists.find((list) => list.id === id)!
}
const card = (id: number) => page.locator(`.avalon-library [data-avalon-game="${id}"]`)
const visibleIds = () =>
  page
    .locator('.avalon-library [data-avalon-game]')
    .evaluateAll((nodes) => nodes.map((node) => Number((node as HTMLElement).dataset.avalonGame)))

for (const mode of ['desktop', 'fullscreen'] as const) {
  for (const scenario of ['latest', 'failure', 'refresh', 'compensation_failure'] as const) {
    test(`${mode} Game 2 membership preserves queued intent and SQLite result after ${scenario}`, async () => {
      await seed(3, mode)
      if (scenario === 'failure')
        sql("CREATE TRIGGER fail_list BEFORE INSERT ON list_items BEGIN SELECT RAISE(ABORT,'failure'); END;")
      if (scenario === 'compensation_failure')
        sql("CREATE TRIGGER fail_list BEFORE DELETE ON list_items BEGIN SELECT RAISE(ABORT,'failure'); END;")
      await card(2).click()
      await page.getByRole('tab', { name: 'Library', exact: true }).click()
      const row = page.getByRole('checkbox', { name: 'Add to Try next', exact: true })
      await expect(row).toBeVisible()
      const before = await savedList()
      const rowHandle = await row.elementHandle()
      await application.evaluate(() => {
        ;(globalThis as any).__listTransactions.holdAppend = true
      })
      if (mode === 'desktop') {
        await row.focus()
        await row.press('Space')
      } else await accept(row, mode)
      await expect(page.getByText('Saving list changes…', { exact: true })).toBeVisible()
      await expect
        .poll(() => application.evaluate(() => (globalThis as any).__listTransactions.appendHeld))
        .toBe(true)
      expect(storedItems().map((item) => item.release_id)).toEqual([3, 1])
      if (scenario === 'latest' || scenario === 'compensation_failure') {
        if (mode === 'desktop') await rowHandle!.press('Space')
        else await accept(page.getByRole('checkbox', { name: 'Remove from Try next', exact: true }), mode)
      }
      if (scenario === 'refresh') {
        const reads = await application.evaluate(() => (globalThis as any).__listTransactions.libraryReads)
        await application.evaluate(({ BrowserWindow }) =>
          BrowserWindow.getAllWindows()[0]!.webContents.send('winnow:event', {
            kind: 'library.changed',
            epoch: 'list-fixture',
            sequence: 1,
            occurredAt: new Date().toISOString(),
          }),
        )
        await page.waitForTimeout(100)
        expect(await application.evaluate(() => (globalThis as any).__listTransactions.libraryReads)).toBe(
          reads,
        )
      }
      await application.evaluate(() => (globalThis as any).__listTransactions.releaseAppend())
      await expect(page.getByText('Saving list changes…', { exact: true })).toHaveCount(0)
      const member = scenario === 'refresh' || scenario === 'compensation_failure'
      const current = page.getByRole('checkbox', {
        name: `${member ? 'Remove from' : 'Add to'} Try next`,
        exact: true,
      })
      await expect(current).toBeChecked({ checked: member })
      expect(await current.evaluate((node, original) => node === original, rowHandle)).toBe(true)
      expect((await savedList()).releaseIds).toEqual(member ? [3, 1, 2] : [3, 1])
      const writes = await application.evaluate(() => (globalThis as any).__listTransactions.writes)
      expect(writes[0].body).toEqual({ releaseIds: [2], expectedRevision: before.revision })
      if (scenario.includes('failure')) {
        await expect(page.getByRole('alert')).toHaveText("Couldn't save list changes. Try again.")
        sql('DROP TRIGGER fail_list;')
        if (mode === 'desktop') {
          await current.focus()
          await current.press('Space')
        } else await accept(current, mode)
        await expect(page.getByText('Saving list changes…', { exact: true })).toHaveCount(0)
        await expect(
          page.getByRole('checkbox', { name: `${member ? 'Add to' : 'Remove from'} Try next`, exact: true }),
        ).toBeChecked({ checked: !member })
        expect((await savedList()).releaseIds.includes(2)).toBe(!member)
        await expect(page.getByRole('alert')).toHaveCount(0)
      }
    })
  }

  for (const origin of ['library', 'details', 'feed'] as const) {
    test(`${mode} ${origin} list prompt retains Second list and original choice after SQLite create failure`, async ({}, info) => {
      await seed(2, mode)
      if (origin === 'details') await card(1).click()
      else if (origin === 'feed') {
        await page
          .getByRole('navigation', { name: 'Main navigation' })
          .getByRole('button', { name: 'For you', exact: true })
          .click()
        await page.locator('[data-avalon-game="1"]').first().focus()
      } else await card(1).focus()
      await (
        await libraryAction(
          page,
          origin === 'details' || (origin === 'feed' && mode === 'desktop') ? 'Add to list' : 'Add to list…',
        )
      ).click()
      const prompt = page.getByRole('dialog', { name: 'Add Game 1 to a list', exact: true })
      await expect(prompt.getByRole('group', { name: 'Existing lists' }).getByRole('button')).toHaveCount(1)
      await expect(prompt.getByRole('button', { name: 'Try next', exact: true })).toBeVisible()
      await prompt.getByLabel('New list name').fill('Second list')
      sql("CREATE TRIGGER fail_list BEFORE INSERT ON lists BEGIN SELECT RAISE(ABORT,'fixture failure'); END;")
      await accept(prompt.getByRole('button', { name: 'New list', exact: true }), mode)
      await expect(prompt.getByRole('alert')).toBeVisible()
      await expect(prompt.getByLabel('New list name')).toHaveValue('Second list')
      await expect(prompt.getByRole('button', { name: 'Try next', exact: true })).toBeVisible()
      expect((await api<LibraryResponse>({ route: 'library.get' })).lists.map((list) => list.name)).toEqual([
        'Try next',
      ])
      // A failed HTTP response needs reconciliation before an explicit retry; drafts stay intact.
      await prompt.getByRole('button', { name: 'Check saved lists', exact: true }).click()
      await prompt.getByRole('button', { name: 'Create another anyway', exact: true }).click()
      await expect(prompt.getByRole('button', { name: 'Try next', exact: true })).toBeEnabled()
      await expect(prompt.getByRole('button', { name: 'New list', exact: true })).toBeEnabled()
      await page.screenshot({ path: info.outputPath(`${mode}-${origin}-prompt-retry.png`) })
      sql('DROP TRIGGER fail_list;')
      await accept(prompt.getByRole('button', { name: 'New list', exact: true }), mode)
      await expect(prompt).toHaveCount(0)
      const lists = (await api<LibraryResponse>({ route: 'library.get' })).lists
      expect(lists.find((list) => list.name === 'Second list')?.releaseIds).toEqual([1])
      expect(lists.find((list) => list.id === 1)?.releaseIds).toEqual([2, 1])
      expect(lists).toHaveLength(2)
    })
  }

  for (const action of ['rename', 'filter', 'delete', 'reorder'] as const) {
    test(`${mode} failed ${action} retains the committed open list, name, rules and ordered SQLite rows`, async ({}, info) => {
      await seed(4, mode)
      const list =
        action === 'filter'
          ? await api<GameList>({
              route: 'list.live',
              body: { name: 'Saved rules', filter: { yearFrom: 2000 } },
            })
          : await savedList()
      await selectCollection(page, list.id)
      const before = await savedList(list.id),
        rows = storedItems()
      sql(
        action === 'delete'
          ? "CREATE TRIGGER fail_list BEFORE DELETE ON lists BEGIN SELECT RAISE(ABORT,'failure'); END;"
          : action === 'reorder'
            ? "CREATE TRIGGER fail_list BEFORE UPDATE ON list_items WHEN NEW.position=1 BEGIN SELECT RAISE(ABORT,'after first move'); END;"
            : "CREATE TRIGGER fail_list BEFORE UPDATE ON lists BEGIN SELECT RAISE(ABORT,'failure'); END;",
      )
      if (action === 'reorder') {
        await card(4).focus()
        await (await libraryAction(page, 'Move later')).click()
      } else {
        await (await libraryAction(page, 'Manage library')).click()
        const editor = page
          .locator('.feature-panel')
          .filter({ has: page.getByRole('heading', { name: list.name, exact: true }) })
          .first()
        if (action === 'rename') {
          await editor.getByRole('button', { name: 'Edit', exact: true }).click()
          await editor.getByLabel('List name', { exact: true }).fill('Rejected')
          await editor.getByRole('button', { name: 'Save list', exact: true }).click()
        } else if (action === 'filter') {
          await editor.getByRole('button', { name: 'Edit live filters', exact: true }).click()
          await editor.getByLabel('Released from', { exact: true }).fill('2020')
          await editor.getByRole('button', { name: 'Save live filters', exact: true }).click()
        } else {
          await editor.getByRole('button', { name: 'Delete list…', exact: true }).click()
          await page
            .getByRole('dialog', { name: `Delete ${list.name}?` })
            .getByRole('button', { name: 'Delete list', exact: true })
            .click()
        }
      }
      await expect(page.getByRole('alert').first()).toBeVisible()
      expect(await savedList(list.id)).toEqual(before)
      expect(storedItems()).toEqual(rows)
      if (action === 'rename')
        await expect(
          page
            .locator('.feature-panel')
            .filter({ has: page.getByRole('heading', { name: list.name, exact: true }) })
            .first()
            .getByLabel('List name', { exact: true }),
        ).toHaveValue('Rejected')
      if (action === 'filter')
        await expect(page.getByLabel('Released from', { exact: true })).toHaveValue('2020')
      await page.screenshot({ path: info.outputPath(`${mode}-${action}-failed.png`) })
      if (action === 'delete') await page.getByRole('button', { name: 'Keep list', exact: true }).click()
      if (action !== 'reorder') {
        const close = page.getByRole('button', { name: 'Close tools', exact: true })
        if (await close.isVisible()) await close.click()
        else await page.keyboard.press('Escape')
      } else await returnToLibrary(page)
      await expectCollection(page, list.id)
      if (action !== 'filter') await expect.poll(visibleIds).toEqual([4, 1])
    })
  }

  test(`${mode} failed removal leaves Try next open with both original members and a retry message`, async () => {
    await seed(3, mode)
    await selectCollection(page, 1)
    await card(3).focus()
    sql("CREATE TRIGGER fail_list BEFORE DELETE ON list_items BEGIN SELECT RAISE(ABORT,'injected'); END;")
    await (await libraryAction(page, 'Remove from Try next')).click()
    await expect(page.getByRole('alert')).toHaveText("Couldn't save list changes. Try again.")
    expect((await savedList()).releaseIds).toEqual([3, 1])
    await returnToLibrary(page)
    await expectCollection(page, 1)
    await expect.poll(visibleIds).toEqual([3, 1])
  })
}

test('desktop STATS sits between Merges and All games and opens account statistics through Enter at 1200x900', async ({}, info) => {
  await seed(2, 'desktop', 1200, 900)
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const feed = nav.getByRole('button', { name: 'For you', exact: true })
  const merges = nav.getByRole('button', { name: 'Merges', exact: true })
  const stats = nav.getByRole('button', { name: 'STATS', exact: true })
  const all = page.locator('.avalon-buckets').getByRole('button', { name: /^All games/ })
  const bounds = await Promise.all([feed, merges, stats, all].map((button) => button.boundingBox()))
  for (let index = 1; index < bounds.length; index++)
    expect(bounds[index]!.y).toBeGreaterThan(bounds[index - 1]!.y)
  await expect(page.getByText('ACCOUNT', { exact: true })).toHaveCount(0)
  await stats.press('Enter')
  const summarySections = page.getByRole('navigation', { name: 'Library summary section', exact: true })
  await expect(summarySections.getByRole('button', { name: 'Gameplay', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await summarySections.getByRole('button', { name: 'Spending', exact: true }).press('Enter')
  await expect(page.getByText(/No Steam spending has been captured/)).toBeVisible()
  await expect(stats).toHaveAttribute('aria-current', 'page')
  await page.screenshot({ path: info.outputPath('desktop-stats-rail.png') })
  const activity = nav.getByRole('button', { name: 'Activity', exact: true })
  await activity.press('Enter')
  await expect(page.getByRole('region', { name: 'Account spending', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'History', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(activity).toHaveAttribute('aria-current', 'page')
  await expect(stats).not.toHaveAttribute('aria-current', 'page')
  await stats.press('Enter')
  await expect(page.getByRole('region', { name: 'Account spending', exact: true })).toBeVisible()
  await all.press('Enter')
  await expect(page.getByRole('region', { name: 'Account spending', exact: true })).toHaveCount(0)
  await expect(page.locator('.avalon-library')).toBeVisible()
})

test('fullscreen Library summary remains keyboard reachable from Activity at 1920x1080', async ({}, info) => {
  await seed(2, 'fullscreen')
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Activity', exact: true })
    .click()
  const summary = page.getByRole('button', { name: 'Library summary', exact: true })
  await summary.focus()
  await summary.press('Enter')
  await expect(page.getByRole('region', { name: 'Library summary', exact: true })).toBeVisible()
  await expect(summary).toHaveAttribute('aria-pressed', 'true')
  await page.screenshot({ path: info.outputPath('fullscreen-library-summary.png') })
})

test('fullscreen Library tools uses readable single-column editors and visible controller targets at 1280x720 with 140 percent text', async ({}, info) => {
  await seed(4, 'desktop', 1280, 720)
  const live = await api<GameList>({
    route: 'list.live',
    body: { name: 'Saved rules', filter: { yearFrom: 2000 } },
  })
  await (await libraryAction(page, 'Manage library')).click()
  const tools = page.locator('.library-tools')
  const newList = tools
    .locator('.feature-panel')
    .filter({ has: page.getByRole('heading', { name: 'A new list', exact: true }) })
    .first()
  const fontSize = (target: Locator) => target.evaluate((node) => parseFloat(getComputedStyle(node).fontSize))
  const desktop = {
    label: await fontSize(newList.locator('.field').first()),
    input: await fontSize(newList.getByLabel('List name', { exact: true })),
    button: await fontSize(newList.getByRole('button', { name: 'Discard draft', exact: true })),
  }
  let currentMode: Mode = 'desktop'
  async function resize(mode: Mode, width: number, height: number) {
    await application.evaluate(
      ({ BrowserWindow }, { mode, width, height, changed }) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.setContentSize(width, height)
        if (changed) window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
        window.focus()
      },
      { mode, width, height, changed: currentMode !== mode },
    )
    currentMode = mode
    await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
    await expect(page.locator('.startup-presentation')).toHaveCount(0)
  }
  await resize('fullscreen', 1920, 1080)
  if (!(await tools.isVisible())) {
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await (await libraryAction(page, 'Manage library')).click()
  }
  const editor = tools
    .locator('.feature-panel')
    .filter({ has: page.getByRole('heading', { name: 'Saved rules', exact: true }) })
    .first()
  await editor.getByRole('button', { name: 'Edit live filters', exact: true }).click()
  await editor.getByLabel('Released from', { exact: true }).fill('2020')
  sql("CREATE TRIGGER fail_list BEFORE UPDATE ON lists BEGIN SELECT RAISE(ABORT,'failure'); END;")
  const measurements: unknown[] = []
  for (const [width, height, scale] of [
    [1920, 1080, 1],
    [1280, 720, 1.4],
  ]) {
    await resize('fullscreen', width, height)
    await page.evaluate(
      (scale) => document.documentElement.style.setProperty('--fullscreen-text-scale', String(scale)),
      scale,
    )
    await editor.getByLabel('Released from', { exact: true }).fill('2020')
    await editor.getByRole('button', { name: 'Save live filters', exact: true }).click()
    await expect(editor.getByRole('alert')).toBeVisible()
    expect((await savedList(live.id)).filter?.yearFrom).toBe(2000)
    const measured = {
      width,
      height,
      scale,
      input: await fontSize(editor.getByLabel('Released from', { exact: true })),
      label: await fontSize(
        editor.locator('.field').filter({ has: page.getByLabel('Released from', { exact: true }) }),
      ),
      button: await fontSize(editor.getByRole('button', { name: 'Save live filters', exact: true })),
      heading: await fontSize(editor.getByRole('heading', { name: 'Saved rules', exact: true })),
      error: await fontSize(editor.getByRole('alert')),
      checkboxLabel: await fontSize(newList.locator('.check-field')),
      columns: await tools
        .locator('.feature-grid')
        .evaluate((node) => getComputedStyle(node).gridTemplateColumns.split(' ').length),
    }
    expect(measured.input).toBeCloseTo(24 * scale)
    expect(measured.label).toBeCloseTo(20 * scale)
    expect(measured.button).toBeCloseTo(24 * scale)
    expect(measured.heading).toBeCloseTo(32 * scale)
    expect(measured.error).toBeCloseTo(24 * scale)
    expect(measured.checkboxLabel).toBeCloseTo(24 * scale)
    expect(measured.columns).toBe(1)
    expect(await tools.evaluate((node) => node.scrollWidth <= node.clientWidth + 1)).toBe(true)
    for (const panel of await tools.locator('.feature-panel').all())
      expect(await panel.evaluate((node) => node.scrollWidth <= node.clientWidth + 1)).toBe(true)
    await assertAccessibleControls(page, tools)
    const tap = async (button: number) => {
      for (const pressed of [[], [button], []])
        await page.evaluate(async (pressed) => {
          ;(window as any).listPad.pressed = pressed
          await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
        }, pressed)
      const focus = await tools.evaluate((root) => {
        const active = document.activeElement
        if (!(active instanceof HTMLElement) || !root.contains(active)) return null
        const bounds = active.getBoundingClientRect()
        let left = 0,
          top = 0,
          right = innerWidth,
          bottom = innerHeight
        for (let parent = active.parentElement; parent; parent = parent.parentElement) {
          const style = getComputedStyle(parent),
            clip = parent.getBoundingClientRect()
          if (['auto', 'scroll', 'hidden'].includes(style.overflowX)) {
            left = Math.max(left, clip.left)
            right = Math.min(right, clip.right)
          }
          if (['auto', 'scroll', 'hidden'].includes(style.overflowY)) {
            top = Math.max(top, clip.top)
            bottom = Math.min(bottom, clip.bottom)
          }
        }
        return {
          label: active.getAttribute('aria-label') ?? active.textContent,
          x: bounds.left,
          y: bounds.top,
          right: bounds.right,
          bottom: bounds.bottom,
          clip: { left, top, right, bottom },
        }
      })
      if (focus) {
        expect(focus.x, `${focus.label} left`).toBeGreaterThanOrEqual(focus.clip.left - 1)
        expect(focus.y, `${focus.label} top`).toBeGreaterThanOrEqual(focus.clip.top - 1)
        expect(focus.right, `${focus.label} right`).toBeLessThanOrEqual(focus.clip.right + 1)
        expect(focus.bottom, `${focus.label} bottom`).toBeLessThanOrEqual(focus.clip.bottom + 1)
      }
    }
    const routes = await assertDirectionalReachability(page, tools, tap)
    measurements.push({ ...measured, routes })
    await editor.getByLabel('Released from', { exact: true }).focus()
    await editor.getByLabel('Released from', { exact: true }).scrollIntoViewIfNeeded()
    await page.screenshot({
      path: info.outputPath(`fullscreen-library-tools-${width}-text${Math.round(scale * 100)}.png`),
    })
  }
  await resize('desktop', 1280, 720)
  if (!(await tools.isVisible())) {
    await page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true })
      .click()
    await (await libraryAction(page, 'Manage library')).click()
  }
  expect({
    label: await fontSize(newList.locator('.field').first()),
    input: await fontSize(newList.getByLabel('List name', { exact: true })),
    button: await fontSize(newList.getByRole('button', { name: 'Discard draft', exact: true })),
  }).toEqual(desktop)
  await info.attach('library-tools-type-and-layout', {
    body: JSON.stringify({ desktop, fullscreen: measurements }, null, 2),
    contentType: 'application/json',
  })
})
