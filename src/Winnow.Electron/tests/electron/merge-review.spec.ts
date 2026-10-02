import { prebuiltFixture as fixture, prebuiltActivationHelper } from './prebuilt-backend'
import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Locator,
  type Page,
} from '@playwright/test'

import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { libraryAction } from './library-controls'
import { assertAccessibleControls, assertDirectionalReachability } from './controller-accessibility-helpers'
import type { ApiRequest } from '../../src/shared/bridge'
import type { Mode } from '../../src/renderer/api/types'
import {
  buildMergeCards,
  mergeMemberLabels,
  type MergeReview,
} from '../../src/renderer/features/parity-merge-model'

const longTitle = "Metal Gear Solid V: The Phantom Pain — The Definitive Experience Collector's Edition"

let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
const queue = () => page.locator('.merge-queue')
const details = () => page.locator('.avalon-details')
const proposal = (title = 'Bastion') =>
  queue().getByRole('article', { name: `${title} proposal`, exact: true })

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(path: string, body: unknown = {}): Promise<T> {
  const response = await fetch(new URL(`/__fixture/merges/${path}`, endpoint.address), {
    method: 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(15000),
  })
  if (!response.ok) throw Error(`Merge fixture ${path}: HTTP ${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
function database<T>(read: (db: DatabaseSync) => T): T {
  const db = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    db.exec('PRAGMA busy_timeout=5000')
    return read(db)
  } finally {
    db.close()
  }
}
function sourceRecords() {
  return database((db) =>
    Object.fromEntries(
      ['works', 'releases', 'ownerships', 'external_ids'].map((table) => [
        table,
        db.prepare(`SELECT * FROM ${table} ORDER BY rowid`).all(),
      ]),
    ),
  )
}
async function tap(button: number) {
  // Standard Gamepad API frames are simulated; this is not a physical-controller test.
  for (const pressed of [false, true, false])
    await page.evaluate(
      async ({ button, pressed }) => {
        Object.defineProperty(navigator, 'getGamepads', {
          configurable: true,
          value: () => [
            {
              index: 0,
              connected: true,
              mapping: 'standard',
              axes: [0, 0, 0, 0],
              buttons: Array.from({ length: 17 }, (_, index) => ({
                pressed: pressed && index === button,
                touched: false,
                value: pressed && index === button ? 1 : 0,
              })),
            },
          ],
        })
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      },
      { button, pressed },
    )
}
async function activate(control: Locator, keyboard = false) {
  await expect(control).toBeEnabled()
  await control.scrollIntoViewIfNeeded()
  await control.focus()
  await expect(control).toBeFocused()
  if (mode === 'fullscreen' && !keyboard) await tap(0)
  else await page.keyboard.press('Enter')
}
async function surface(width = mode === 'desktop' ? 1230 : 1920, height = mode === 'desktop' ? 850 : 1080) {
  await app.evaluate(
    ({ BrowserWindow }, { mode, width, height }) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setContentSize(width, height)
      window.isFullScreen = () => mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
}
async function openQueue() {
  await activate(
    page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('button', { name: 'Library', exact: true }),
  )
  await activate(await libraryAction(page, 'Manage library'))
  await activate(page.getByRole('button', { name: 'Identity review', exact: true }))
  await expect(queue()).toBeVisible()
  await expect(queue().getByRole('button', { name: 'Refresh suggestions', exact: true })).toBeEnabled()
}
async function setPane(width: number, height: number) {
  // Avalonia's source fixture is the pane without its250px shell. Measure and size the actual
  // Electron queue pane, rather than calling a950px outer window a950px merge pane.
  await surface(width + 250, height)
  for (let attempt = 0; attempt < 3; attempt++) {
    const actual = (await queue().boundingBox())!.width
    if (Math.abs(actual - width) < 1) break
    await app.evaluate(({ BrowserWindow }, delta) => {
      const window = BrowserWindow.getAllWindows()[0]!,
        [current, height] = window.getContentSize()
      window.setContentSize(Math.round(current + delta), height)
    }, width - actual)
    await page.evaluate(() => new Promise<void>((done) => requestAnimationFrame(() => done())))
  }
  expect((await queue().boundingBox())!.width).toBeCloseTo(width, 0)
}
async function shot(name: string, subject: Locator) {
  await subject.scrollIntoViewIfNeeded()
  await expect(subject).toBeInViewport()
  const box = (await subject.boundingBox())!
  const size = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
  expect(box.x).toBeGreaterThanOrEqual(-1)
  expect(box.x + box.width).toBeLessThanOrEqual(size.width + 1)
  expect(box.y).toBeGreaterThanOrEqual(-1)
  expect(box.y + box.height).toBeLessThanOrEqual(size.height + 1)
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}
async function seedPair(
  title = 'Bastion',
  rightStore = 'epic',
  multipleStores = false,
  markSweepComplete = false,
) {
  return control<{ left: number; right: number }>('seed-pair', {
    title,
    rightStore,
    multipleStores,
    markSweepComplete,
  })
}
async function publish() {
  await control('publish')
}
async function seedPrey(unowned: boolean) {
  database((db) => {
    db.exec('BEGIN')
    for (let id = 1; id <= 2; id++) {
      db.prepare("INSERT INTO works(id,name,first_release_year) VALUES(?,'Prey',?)").run(
        id,
        id === 1 ? 2017 : null,
      )
      db.prepare("INSERT INTO releases(id,work_id,name,platform) VALUES(?,?,'Prey','windows')").run(id, id)
      if (id === 1 || !unowned)
        db.prepare("INSERT INTO ownerships(id,release_id,store,installed) VALUES(?,?,'steam',0)").run(id, id)
    }
    db.exec(
      "INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES(1,2,0.8,'pending'); COMMIT",
    )
  })
  await publish()
}
async function labels() {
  const review = await api<MergeReview>({ route: 'identity.get' })
  return mergeMemberLabels(buildMergeCards(review).find((card) => !card.actId)!)
}
async function closeDetails() {
  if (mode === 'fullscreen') await tap(1)
  else await activate(details().getByRole('button', { name: 'Close game details', exact: true }))
  await expect(details()).toHaveCount(0)
}
async function before(left: Locator, right: Locator) {
  const a = (await left.boundingBox())!,
    b = (await right.boundingBox())!
  expect(a.x + a.width).toBeLessThanOrEqual(b.x + 1)
}
async function contained(card: Locator) {
  const overflow = await card.evaluate((node) => {
    const box = node.getBoundingClientRect()
    return {
      horizontal: node.scrollWidth > node.clientWidth + 1,
      outside: [...node.querySelectorAll('button,input,select,h4,strong,span,p')]
        .filter((child) => {
          const r = child.getBoundingClientRect()
          return r.width > 0 && r.height > 0 && (r.left < box.left - 1 || r.right > box.right + 1)
        })
        .map((child) => child.className),
    }
  })
  expect(overflow).toEqual({ horizontal: false, outside: [] })
}

test.beforeEach(async ({}, info) => {
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-merges-'))
  errors.length = 0
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/merge-review-main.mjs'), '--data-dir', directory, '--no-sync'],
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
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect(
    (await fetch(new URL('/__fixture/merges/publish', endpoint.address), { method: 'POST' })).status,
  ).toBe(401)
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus)
      await info.attach('merge-review-failure', { body: await page.screenshot(), contentType: 'image/png' })
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
  } finally {
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen'] as const) {
  test(`${surfaceName} source platform picker chooses Steam accessibly and persists on reopen`, async () => {
    await openQueue()
    if (mode === 'desktop') await setPane(980, 640)
    await assertAccessibleControls(page, queue())
    if (mode === 'fullscreen') await assertDirectionalReachability(page, queue(), tap)
    const preferences = () =>
      api<{ preference: string; value: string | null }[]>({ route: 'preferences.presentation.get' })
    if (mode === 'desktop') {
      const select = queue().getByRole('combobox', { name: 'Preferred main platform', exact: true })
      await select.focus()
      // Native HTML select is Electron's equivalent of the source flyout. Use its real keyboard path.
      await page.keyboard.press('Alt+ArrowDown')
      await page.keyboard.press('ArrowDown')
      await page.keyboard.press('Enter')
      await expect(select).toHaveValue('steam')
      await expect(select).toBeFocused()
      await expect(select).toHaveAccessibleDescription(/Steam/)
      const bounds = (await select.boundingBox())!,
        parent = (await queue().boundingBox())!
      expect(bounds.x).toBeGreaterThanOrEqual(parent.x - 1)
      expect(bounds.x + bounds.width).toBeLessThanOrEqual(parent.x + parent.width + 1)
      await before(select, queue().locator('.merge-proposal-count'))
      await shot('platform-steam', select)
    } else {
      const trigger = queue().getByRole('button', { name: 'Preferred platform · None', exact: true })
      await queue()
        .getByRole('button', { name: /^Sort ·/ })
        .focus()
      await tap(13)
      await tap(13)
      await expect(trigger).toBeFocused()
      await tap(0)
      const sheet = page.getByRole('dialog', { name: 'Preferred platform for pending headers', exact: true })
      await assertAccessibleControls(page, sheet)
      await expect(sheet.getByRole('button', { name: 'None', exact: true })).toBeFocused()
      await tap(13)
      await expect(sheet.getByRole('button', { name: 'Steam', exact: true })).toBeFocused()
      await shot('platform-options', sheet.getByRole('heading'))
      await tap(0)
      await expect(sheet).toHaveCount(0)
      await expect(
        queue().getByRole('button', { name: 'Preferred platform · Steam', exact: true }),
      ).toBeFocused()
    }
    await expect
      .poll(
        async () => (await preferences()).find((item) => item.preference === 'PreferredMergePlatform')?.value,
      )
      .toBe('steam')
    expect((await api<MergeReview>({ route: 'identity.get' })).history).toEqual([])
    await page.reload()
    await surface()
    await openQueue()
    if (mode === 'desktop')
      await expect(
        queue().getByRole('combobox', { name: 'Preferred main platform', exact: true }),
      ).toHaveValue('steam')
    else
      await expect(
        queue().getByRole('button', { name: 'Preferred platform · Steam', exact: true }),
      ).toBeVisible()
  })

  test(`${surfaceName} source refresh reports busy failure recovery and one real pending match in its minimum pane`, async () => {
    await openQueue()
    if (mode === 'desktop') await setPane(950, 740)
    else await surface(1280, 720)
    await expect(queue().locator('article')).toHaveCount(0)
    await seedPair('Bastion', 'psn', false, true)
    const refresh = page.getByRole('button', { name: 'Refresh suggestions', exact: true })
    const status = page.locator('.merge-refresh-status')
    await expect(refresh).toHaveAttribute('title', 'Refresh suggestions')
    await expect(status).not.toBeVisible()
    if (mode === 'desktop') {
      await expect(refresh).toHaveText('')
      await expect(refresh.locator('svg')).toHaveCount(1)
      const selected = page.getByRole('button', { name: /^Merge selected/ }),
        a = (await refresh.boundingBox())!,
        b = (await selected.boundingBox())!
      expect(a.y + a.height / 2).toBeCloseTo(b.y + b.height / 2, 0)
      expect(b.x - a.x - a.width).toBeGreaterThanOrEqual(-1)
      expect(b.x - a.x - a.width).toBeLessThanOrEqual(12)
    }
    await app.evaluate(() => {
      ;(globalThis as any).__mergeReviewNative.holdRefresh = true
    })
    if (mode === 'desktop') await refresh.click()
    else {
      await queue()
        .getByRole('button', { name: /^Sort ·/ })
        .focus()
      for (let step = 0; step < 3; step++) await tap(13)
      await expect(refresh).toBeFocused()
      await tap(0)
    }
    await expect(refresh).toBeDisabled()
    await expect(status).toHaveText('Checking your library for matches…')
    await expect(status).toHaveAttribute('aria-live', 'polite')
    await expect.poll(() => app.evaluate(() => (globalThis as any).__mergeReviewNative.refreshCalls)).toBe(1)
    await refresh.dispatchEvent('click')
    expect(await app.evaluate(() => (globalThis as any).__mergeReviewNative.refreshCalls)).toBe(1)
    await shot('refresh-busy', status)
    await app.evaluate(() => (globalThis as any).__mergeReviewNative.release('fail'))
    await expect(refresh).toBeEnabled()
    await expect(status).toHaveText("Couldn't refresh suggestions. Choose Refresh suggestions to try again.")
    await shot('refresh-failed', status)
    await activate(refresh, true)
    await expect.poll(() => app.evaluate(() => (globalThis as any).__mergeReviewNative.refreshCalls)).toBe(2)
    await app.evaluate(() => (globalThis as any).__mergeReviewNative.release('complete'))
    await expect(status).toHaveText('Suggestions refreshed. Your previous answers are kept.')
    await expect(refresh).toBeEnabled()
    await expect(proposal()).toBeVisible()
    const review = await api<MergeReview>({ route: 'identity.get' })
    expect(review.candidates).toHaveLength(1)
    expect(review.history).toEqual([])
    await shot('refresh-complete', status)
  })

  test(`${surfaceName} source automatic publication replaces Bastion with Hades while pending count remains one`, async () => {
    await seedPair('Bastion', 'psn', false, true)
    await publish()
    await openQueue()
    if (mode === 'fullscreen') await surface(1280, 720)
    await expect(proposal()).toBeVisible()
    database((db) => db.prepare("DELETE FROM merge_candidates WHERE status='pending'").run())
    await seedPair('Hades', 'psn', false, true)
    // The source refresh stub performs no sweep. This authenticated test-only boundary publishes
    // the real backend event after the exact replacement; the renderer receives no canned snapshot.
    await publish()
    await expect(proposal('Hades')).toBeVisible()
    await expect(proposal()).toHaveCount(0)
    expect((await api<MergeReview>({ route: 'identity.get' })).candidates).toHaveLength(1)
    await shot('same-count-publication', proposal('Hades').locator(mode === 'desktop' ? 'h4' : 'strong'))
  })

  test(`${surfaceName} source Prey member opens its exact Details entry and returns focus without changing header`, async () => {
    await seedPrey(false)
    await openQueue()
    const names = await labels(),
      card = proposal('Prey'),
      initial = await api<MergeReview>({ route: 'identity.get' })
    let returning: Locator
    if (mode === 'desktop') {
      const row = card.locator('.merge-row').nth(1)
      returning = row.locator('[data-merge-row]')
      await activate(row.getByRole('button', { name: `Details for ${names[1]}`, exact: true }))
    } else {
      returning = card.getByRole('button')
      await activate(returning)
      await activate(page.getByRole('dialog').locator('[data-merge-member="2"]'))
      await activate(page.getByRole('dialog').getByRole('button', { name: 'Open game', exact: true }))
    }
    await expect(details().locator('h1')).toHaveText('Prey')
    await expect(details().locator('[data-details-identity-line]')).toHaveCount(0)
    await activate(details().getByRole('tab', { name: 'Library', exact: true }))
    await expect(details().locator('.avalon-copy')).toHaveCount(1)
    await shot('prey-details-member', details().locator('h1'))
    await closeDetails()
    await expect(returning).toBeFocused()
    if (mode === 'desktop') await expect(card.locator('.merge-row').first().getByRole('radio')).toBeChecked()
    else {
      await activate(returning)
      await expect(page.getByRole('dialog').locator('[data-merge-member="1"]')).toHaveAccessibleName(
        /Header$/,
      )
      await tap(1)
    }
    const after = await api<MergeReview>({ route: 'identity.get' })
    expect(after.revision).toBe(initial.revision)
    expect(after.history).toEqual(initial.history)
  })

  test(`${surfaceName} a Prey member without an owned tile refuses Details and preserves review choices`, async () => {
    await seedPrey(true)
    await openQueue()
    const before = await api<MergeReview>({ route: 'identity.get' }),
      names = await labels(),
      card = proposal('Prey')
    if (mode === 'desktop')
      await activate(
        card
          .locator('.merge-row')
          .nth(1)
          .getByRole('button', { name: `Details for ${names[1]}`, exact: true }),
      )
    else {
      await activate(card.getByRole('button'))
      await activate(page.getByRole('dialog').locator('[data-merge-member="2"]'))
      await activate(page.getByRole('dialog').getByRole('button', { name: 'Open game', exact: true }))
    }
    await expect(details()).toHaveCount(0)
    await expect(
      page.getByRole('status').filter({ hasText: 'This game is not available in the current library view.' }),
    ).toBeVisible()
    await expect(card).toBeVisible()
    expect((await api<MergeReview>({ route: 'identity.get' })).revision).toBe(before.revision)
  })
}

test('desktop exact Bastion row body radio and Details keep pointer and keyboard actions independent', async () => {
  await seedPair()
  await publish()
  await openQueue()
  await setPane(1200, 900)
  const card = proposal(),
    rows = card.locator('.merge-row'),
    names = await labels(),
    before = sourceRecords()
  await rows.nth(1).locator('.merge-cover').click()
  await expect(rows.nth(1).getByRole('radio')).toBeChecked()
  await expect(rows.first().getByRole('radio')).not.toBeChecked()
  await expect(details()).toHaveCount(0)
  await expect(rows.nth(1).locator('.merge-row-mark')).toHaveText('Header')
  const detailButton = rows.nth(1).getByRole('button', { name: `Details for ${names[1]}`, exact: true })
  await activate(detailButton, true)
  await expect(details()).toBeVisible()
  await closeDetails()
  await expect(rows.nth(1).getByRole('radio')).toBeChecked()
  await rows.first().getByRole('radio').focus()
  await page.keyboard.press('Space')
  await expect(rows.first().getByRole('radio')).toBeChecked()
  await expect(rows.nth(1).getByRole('radio')).not.toBeChecked()
  await detailButton.click()
  await expect(details()).toBeVisible()
  await closeDetails()
  await expect(rows.first().getByRole('radio')).toBeChecked()
  expect(sourceRecords()).toEqual(before)
  await activate(card.getByRole('button', { name: /^Same game:/ }))
  await expect(card).toHaveCount(0)
  const saved = queue().getByRole('article', { name: 'Bastion saved group', exact: true })
  await expect(saved.locator('.merge-row')).toHaveCount(0)
  expect((await api<MergeReview>({ route: 'identity.get' })).history).toMatchObject([
    { parentWorkId: 1, childWorkId: 2 },
  ])
})

test('desktop source long title multi-store rows retain trailing actions through1670 950 1030 950pane resizes', async () => {
  await seedPair(longTitle, 'epic', true)
  await publish()
  await openQueue()
  const card = proposal(longTitle),
    names = await labels()
  for (const [index, width] of [1670, 950, 1030, 950].entries()) {
    await setPane(width, 700)
    await card.locator('h4').scrollIntoViewIfNeeded()
    await contained(card)
    await before(card.locator('h4'), card.locator('.merge-confidence'))
    await before(card.locator('.merge-confidence'), card.getByRole('button', { name: /^Same game:/ }))
    const row = card.locator('.merge-row').nth(1)
    await before(row.locator('.merge-row-title strong'), row.locator('.merge-row-mark'))
    await before(row.locator('.merge-row-mark'), row.locator('.merge-row-stores'))
    await row.getByRole('button', { name: `Details for ${names[1]}`, exact: true }).click()
    await expect(details()).toBeVisible()
    await closeDetails()
    await expect(row.getByRole('radio')).not.toBeChecked()
    await row.getByRole('checkbox').uncheck()
    await expect(row.getByRole('checkbox')).not.toBeChecked()
    await row.getByRole('checkbox').check()
    await expect(row.getByRole('checkbox')).toBeChecked()
    await shot(`long-title-pane-${index}-${width}`, card.locator('h4'))
  }
  await activate(card.getByRole('button', { name: /^Same game:/ }))
  const saved = queue().getByRole('article', { name: `${longTitle} saved group`, exact: true })
  await expect(saved).toBeVisible()
  await contained(saved)
  await shot('long-title-resolved', saved.getByRole('combobox'))
})

for (const [width, title] of [
  [1920, 'Bastion'],
  [1280, longTitle],
] as const) {
  test(`fullscreen source${width} member actions keep Open game separate from Make header with readable sheets`, async () => {
    await seedPair(title)
    await publish()
    await surface(width, (width * 9) / 16)
    await openQueue()
    const card = proposal(title)
    await activate(card.getByRole('button'))
    await activate(page.getByRole('dialog').locator('[data-merge-member="2"]'))
    const sheet = page.getByRole('dialog'),
      promote = sheet.getByRole('button', { name: 'Make header', exact: true })
    await expect(sheet.getByRole('button', { name: 'Open game', exact: true })).toBeVisible()
    const reference = Math.min(width / 1920, (width * 9) / 16 / 1080)
    for (const [control, size] of [
      [promote, 28],
      [sheet.getByRole('heading'), 40],
      [sheet.locator('p').first(), 22],
    ] as const)
      expect(await control.evaluate((node) => parseFloat(getComputedStyle(node).fontSize))).toBeCloseTo(
        (size * reference) / Math.max(1, reference),
        2,
      )
    const footer = sheet.locator('.merge-sheet-hints')
    await expect(footer).toContainText('A Select')
    await expect(footer).toContainText('B Back')
    for (const button of ['A', 'B']) {
      const glyph = footer.locator(`[data-merge-sheet-glyph="${button}"] svg`)
      await expect(glyph).toHaveAttribute('viewBox', '8 8 48 48')
      await expect(glyph).toBeInViewport()
    }
    await contained(sheet)
    await assertAccessibleControls(page, sheet)
    await assertDirectionalReachability(page, sheet, tap)
    await shot(`member-${width}`, promote)
    await activate(promote, true)
    await expect(sheet.locator('[data-merge-member="2"]')).toHaveAccessibleName(/Header$/)
    await activate(sheet.locator('[data-merge-member="2"]'))
    await expect(sheet.getByRole('button', { name: 'Open game', exact: true })).toBeVisible()
    await expect(sheet.getByRole('button', { name: 'Make header', exact: true })).toHaveCount(0)
  })
}
