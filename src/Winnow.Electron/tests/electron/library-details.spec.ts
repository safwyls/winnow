import { libraryAction, returnToLibrary } from './library-controls'
import { selectCollection, collectionChoice, expectCollection } from './collection-controls'
import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { LibraryResponse, ManualGame, Metadata } from '../../src/renderer/api/types'
import { buildMergeCards, type MergeReview } from '../../src/renderer/features/parity-merge-model'

let application: ElectronApplication, page: Page, directory: string
const failures: string[] = []
type IdentityTrace = {
  path: string
  method: string
  expected?: string
  revision?: string
  status?: number
  message?: string
}
type TraceHost = { __identityTrace: IdentityTrace[] }
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-library-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [
      resolve('tests/electron/identity-trace-main.mjs'),
      '--data-dir',
      directory,
      '--seed-sample',
      '--no-sync',
    ],
    env: environment,
    chromiumSandbox: true,
    timeout: 60_000,
  })
  page = await application.firstWindow()
  await page.evaluate(() => {
    const trace: { event: string; disabled: boolean; form: boolean; time: number }[] = []
    Object.assign(window, { relationshipInputTrace: trace })
    for (const type of ['pointerdown', 'pointerup', 'click'])
      document.addEventListener(
        type,
        (event) => {
          const button = (event.target as Element).closest<HTMLButtonElement>('button')
          if (button?.textContent?.trim() !== 'Create a relationship') return
          trace.push({
            event: type,
            disabled: button.disabled,
            form: Boolean(document.querySelector('select[required]')),
            time: Date.now(),
          })
          requestAnimationFrame(() =>
            trace.push({
              event: `${type}:frame`,
              disabled: button.disabled,
              form: Boolean(document.querySelector('select[required]')),
              time: Date.now(),
            }),
          )
        },
        true,
      )
  })
  page.on('pageerror', (error) => failures.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.avalon-cover').first()).toBeVisible()
})
test.afterAll(async () => closeFixture(application, directory))
test.afterEach(async ({}, info) => {
  if (info.status !== info.expectedStatus) {
    await info.attach('identity-revisions', {
      body: JSON.stringify(
        await application.evaluate(() => (globalThis as unknown as TraceHost).__identityTrace),
        null,
        2,
      ),
      contentType: 'application/json',
    })
    await info.attach('relationship-input', {
      body: JSON.stringify(
        await page.evaluate(
          () => (window as unknown as { relationshipInputTrace: unknown }).relationshipInputTrace,
        ),
        null,
        2,
      ),
      contentType: 'application/json',
    })
  }
})
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function surface(mode: 'desktop' | 'fullscreen') {
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1440, 900)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
}
async function manual(title: string) {
  return api<ManualGame>({
    route: 'manual.create',
    body: {
      title,
      firstReleaseYear: 2006,
      platformLabel: 'PC',
      executablePath: null,
      installPath: null,
      igdbId: null,
      steamAppId: null,
    },
  })
}
async function openRelationship() {
  const button = page.getByRole('button', { name: 'Create a relationship' })
  for (let attempt = 0; attempt < 3; attempt++) {
    await expect(button).toBeEnabled()
    const start = await page.evaluate(
      () => (window as unknown as { relationshipInputTrace: unknown[] }).relationshipInputTrace.length,
    )
    await button.click()
    const events = await page.evaluate(async (start) => {
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      return (
        window as unknown as { relationshipInputTrace: { event: string; disabled: boolean }[] }
      ).relationshipInputTrace.slice(start)
    }, start)
    if (await page.getByRole('combobox', { name: 'Main game', exact: true }).count()) return
    // A refresh can disable the target after Playwright's actionability check. Retry
    // only when native input confirms that Chromium suppressed the click entirely.
    expect(events.some((event) => event.event === 'click')).toBe(false)
    expect(events.some((event) => event.event === 'pointerdown' && event.disabled)).toBe(true)
  }
  await expect(page.getByRole('combobox', { name: 'Main game', exact: true })).toBeVisible()
}
async function controller() {
  await page.evaluate(() => {
    const state = { pressed: [] as number[] }
    ;(window as unknown as { mergeController: typeof state }).mergeController = state
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
            touched: false,
            value: state.pressed.includes(index) ? 1 : 0,
          })),
        },
      ],
    })
  })
  return async (button: number) => {
    for (const value of [[], [button], []])
      await page.evaluate(async (value) => {
        ;(window as unknown as { mergeController: { pressed: number[] } }).mergeController.pressed = value
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      }, value)
  }
}
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} searches real workspace names, groups editions and undoes the exact saved identity act`, async () => {
    const parent = await manual(`Parity ${mode} primary`),
      child = await manual(`Parity ${mode} edition`)
    await surface(mode)
    await (await libraryAction(page, 'Manage library')).click()
    await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    await openRelationship()
    await page.getByRole('combobox', { name: 'Main game', exact: true }).selectOption(String(parent.workId))
    await page.getByLabel('Find games to include').fill(`Parity ${mode}`)
    await page.getByRole('checkbox', { name: child.title, exact: true }).check()
    const before = await api<LibraryResponse>({ route: 'library.get' })
    expect(before.games.find((game) => game.workId === parent.workId)?.entries).toHaveLength(1)
    expect(before.games.some((game) => game.workId === child.workId)).toBe(true)
    await page.getByRole('button', { name: 'Confirm relationship', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Undo last decision' })).toBeEnabled()
    const grouped = await api<LibraryResponse>({ route: 'library.get' })
    expect(
      grouped.games
        .find((game) => game.workId === parent.workId)
        ?.entries.map((entry) => entry.releaseId)
        .sort(),
    ).toEqual([parent.releaseId, child.releaseId].sort())
    expect(grouped.games.some((game) => game.workId === child.workId)).toBe(false)
    await page.getByRole('button', { name: 'Undo last decision' }).click()
    await expect(page.getByText('Decision undone.', { exact: true })).toBeVisible()
    const restored = await api<LibraryResponse>({ route: 'library.get' })
    expect(restored.games.find((game) => game.workId === parent.workId)?.entries).toHaveLength(1)
    expect(restored.games.find((game) => game.workId === child.workId)?.entries).toHaveLength(1)
    await openRelationship()
    await page.getByLabel('Find games to include').fill(child.title)
    await expect(page.getByRole('checkbox', { name: child.title, exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Cancel relationship' }).click()
    await page.getByRole('button', { name: 'Close tools' }).click()
    expect(
      await application.evaluate(() =>
        (globalThis as unknown as TraceHost).__identityTrace.some(
          (entry) => entry.path === '/api/v1/identity/review/link' && entry.status === 200,
        ),
      ),
    ).toBe(true)
    expect(failures).toEqual([])
  })
  test(`${mode} saves a metadata year through the real API and refreshes its open live list without losing another draft`, async () => {
    const game = await manual(`Year parity ${mode}`)
    const list = await api<{ id: number }>({
      route: 'list.live',
      body: { name: `2006 parity ${mode}`, filter: { yearFrom: 2006, yearTo: 2006, search: game.title } },
    })
    // Desktop can hold multiple unfinished fields. Fullscreen edits one field at a
    // time, so carry a real desktop draft into that surface before saving the year.
    await surface('desktop')
    // The API emits the same library invalidation used by the production renderer.
    await expect(await collectionChoice(page, list.id)).toHaveCount(1)
    await selectCollection(page, list.id)
    await page.getByRole('button', { name: `View ${game.title}`, exact: true }).click()
    await page.getByRole('button', { name: 'More', exact: true }).click()
    await page.getByRole('button', { name: 'Edit details', exact: true }).click()
    await page.getByLabel('Name', { exact: true }).fill('An unfinished title')
    if (mode === 'fullscreen') {
      await page.locator('.metadata-dialog').getByRole('button', { name: 'Back', exact: true }).click()
      await page.getByRole('button', { name: 'Close game details', exact: true }).click()
      await surface(mode)
      await selectCollection(page, list.id)
      await page.getByRole('button', { name: `View ${game.title}`, exact: true }).click()
      await page.getByRole('button', { name: 'More', exact: true }).click()
      await page.getByRole('button', { name: 'Edit details', exact: true }).click()
      await page
        .locator('.metadata-field-menu')
        .getByRole('button', { name: /^Release year ·/ })
        .click()
    }
    await page.getByLabel('Release year', { exact: true }).fill('2017')
    await page.getByRole('button', { name: 'Save release year', exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: /^Saved\.$/ })).toBeVisible()
    await expect(
      page.locator('.metadata-dialog').getByRole('button', { name: 'Back', exact: true }),
    ).toBeEnabled()
    if (mode === 'fullscreen')
      await page
        .locator('.metadata-field-menu')
        .getByRole('button', { name: /^Name ·/ })
        .click()
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue('An unfinished title')
    if (mode === 'fullscreen')
      await page.locator('.metadata-dialog').getByRole('button', { name: 'Back', exact: true }).click()
    await page.locator('.metadata-dialog').getByRole('button', { name: 'Back', exact: true }).click()
    await expect(page.locator('.metadata-dialog')).toHaveCount(0)
    await page
      .getByRole('button', {
        name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        exact: true,
      })
      .click()
    await expect(page.locator('.avalon-details')).toHaveCount(0)
    await expect(page.getByText('No games match these filters.', { exact: true })).toBeVisible()
    await expectCollection(page, list.id)
    const saved = await api<LibraryResponse>({ route: 'library.get' })
    expect(saved.games.find((item) => item.workId === game.workId)?.firstReleaseYear).toBe(2017)
    expect(saved.games.find((item) => item.workId === game.workId)?.title).toBe(game.title)
    expect(saved.lists.find((item) => item.id === list.id)?.releaseIds).toEqual([])
    await (await libraryAction(page, 'Leave this list')).click()
    await returnToLibrary(page)
    expect(failures).toEqual([])
  })
  test(`${mode} reviews grouped proposals, merges selected groups and retracts their exact acts through one Undo`, async () => {
    await surface(mode)
    await (await libraryAction(page, 'Manage library')).click()
    await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    const queue = page.locator('.merge-queue')
    await expect(queue.locator('.merge-card:not(.resolved)').nth(1)).toBeVisible()
    if (mode === 'desktop') {
      const rows = queue.locator('[data-merge-row]')
      await expect(rows.first()).toBeEnabled()
      await expect(rows.nth(1)).toBeEnabled()
      await rows.first().focus()
      await rows.first().press('ArrowDown')
      await expect(rows.nth(1)).toBeFocused()
    }
    const before = await api<MergeReview>({ route: 'identity.get' })
    const standing = new Set(before.history.filter((link) => !link.retractedAt).map((link) => link.actId))
    for (const index of [0, 1]) {
      const card = queue.locator('.merge-card:not(.resolved)').nth(index)
      if (mode === 'desktop') await card.getByRole('checkbox', { name: /^Select .* group$/ }).check()
      else {
        await card.getByRole('button').click()
        await page.getByRole('button', { name: 'Select for grouping', exact: true }).click()
        await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
      }
    }
    await queue.getByRole('button', { name: 'Merge 2 selected', exact: true }).click()
    if (mode === 'fullscreen') await page.getByRole('button', { name: 'Continue', exact: true }).click()
    await expect(queue.getByRole('button', { name: 'Undo review decisions', exact: true })).toBeEnabled()
    await expect(queue.getByText('Rolled up 2 groups.', { exact: true })).toBeVisible()
    const linked = await api<MergeReview>({ route: 'identity.get' })
    const newActs = [
      ...new Set(
        linked.history
          .filter((link) => !link.retractedAt && !standing.has(link.actId))
          .map((link) => link.actId),
      ),
    ]
    expect(newActs).toHaveLength(2)
    await queue.getByRole('button', { name: 'Undo review decisions', exact: true }).click()
    await expect(
      queue.getByText('Decision undone. Your games and play history are kept.', { exact: true }),
    ).toBeVisible()
    const undone = await api<MergeReview>({ route: 'identity.get' })
    expect(
      undone.history.filter((link) => newActs.includes(link.actId)).every((link) => link.retractedAt),
    ).toBe(true)
    await expect(queue.locator('.merge-card:not(.resolved)').nth(1)).toBeVisible()
    const geometry = await queue
      .locator('.merge-card:not(.resolved)')
      .first()
      .evaluate((card) => {
        const bounds = card.getBoundingClientRect()
        return {
          left: bounds.left,
          right: bounds.right,
          width: document.documentElement.clientWidth,
          overflow: card.scrollWidth > card.clientWidth + 1,
        }
      })
    expect(geometry.left).toBeGreaterThanOrEqual(0)
    expect(geometry.right).toBeLessThanOrEqual(geometry.width)
    expect(geometry.overflow).toBe(false)
    await page.getByRole('button', { name: 'Close tools' }).click()
    expect(failures).toEqual([])
  })
  test(`${mode} keeps long merge members and independent actions inside the pane with correct focus hierarchy`, async ({}, testInfo) => {
    const review = await api<MergeReview>({ route: 'identity.get' })
    const proposal = buildMergeCards(review).find((card) => !card.actId && card.kind === 'same_game')!
    const title = "Metal Gear Solid V: The Phantom Pain — The Definitive Experience Collector's Edition"
    for (const row of proposal.rows) {
      const metadata = await api<Metadata>({ route: 'metadata.get', params: { workId: row.workId } })
      await api({
        route: 'metadata.put',
        params: { workId: row.workId },
        body: { field: 'name', value: title, expectedRevision: metadata.revision },
      })
    }
    await surface(mode)
    await (await libraryAction(page, 'Manage library')).click()
    await page.getByRole('button', { name: 'Identity review', exact: true }).click()
    const card = page.getByRole('article', { name: `${title} proposal`, exact: true })
    await expect(card).toBeVisible()
    const assertFits = async () => {
      const overflow = await page
        .locator(mode === 'desktop' ? '.merge-card' : '.merge-sheet')
        .evaluateAll((cards) =>
          cards.flatMap((card) => {
            const bounds = card.getBoundingClientRect()
            return [...card.querySelectorAll<HTMLElement>('button,input,select,.merge-row-mark,.merge-store')]
              .filter((control) => control.getBoundingClientRect().width > 0)
              .filter((control) => {
                const rect = control.getBoundingClientRect()
                return rect.left < bounds.left - 1 || rect.right > bounds.right + 1
              })
              .map((control) => ({
                html: control.outerHTML,
                window: innerWidth,
                card: { left: bounds.left, right: bounds.right },
                children: [...(control.closest('.merge-row')?.children ?? [])].map((entry) => {
                  const rect = entry.getBoundingClientRect()
                  const style = getComputedStyle(entry)
                  return {
                    class: entry.className,
                    left: rect.left,
                    width: rect.width,
                    flex: style.flex,
                    min: style.minWidth,
                  }
                }),
              }))
          }),
        )
      expect(overflow).toEqual([])
    }
    if (mode === 'desktop') {
      for (const width of [1920, 1200, 1280, 1200]) {
        await application.evaluate(
          ({ BrowserWindow }, width) => BrowserWindow.getAllWindows()[0]!.setContentSize(width, 800),
          width,
        )
        await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
        await card.scrollIntoViewIfNeeded()
        await assertFits()
      }
      const rows = card.locator('.merge-row')
      const radio = rows.nth(1).getByRole('radio')
      await expect(radio).toBeEnabled()
      await rows.nth(1).locator('.merge-cover').click()
      await expect(radio).toBeChecked()
      await expect(rows.first().getByRole('radio')).toBeEnabled()
      await rows.first().getByRole('radio').press('Space')
      await expect(rows.first().getByRole('radio')).toBeChecked()
      await application.evaluate(() => {
        Object.assign(globalThis, { __identityHoldNext: true })
      })
      await page.getByRole('button', { name: 'Refresh suggestions', exact: true }).click()
      await expect
        .poll(() =>
          application.evaluate(
            () => typeof (globalThis as unknown as { __identityRelease?: () => void }).__identityRelease,
          ),
        )
        .toBe('function')
      await expect(radio).toBeDisabled()
      let coverClickFinished = false
      const coverClick = rows
        .nth(1)
        .locator('.merge-cover')
        .click()
        .then(() => {
          coverClickFinished = true
        })
      try {
        // The image shares the row's disabled state while a refreshed revision is outstanding.
        await page.waitForTimeout(180)
        expect(coverClickFinished).toBe(false)
      } finally {
        await application.evaluate(() =>
          (globalThis as unknown as { __identityRelease?: () => void }).__identityRelease?.(),
        )
        await coverClick
      }
      await expect(radio).toBeChecked()
      await rows.first().getByRole('radio').press('Space')
      await expect(rows.first().getByRole('radio')).toBeChecked()
      await rows.nth(1).getByRole('checkbox').uncheck()
      await expect(card.getByRole('button', { name: /^Same game: / })).toBeDisabled()
      await rows.nth(1).getByRole('checkbox').check()
      const cover = rows.first().locator('.merge-cover')
      await api({
        route: 'preferences.presentation.put',
        params: { preference: 'DimDormantCovers' },
        body: { value: 'false' },
      })
      await expect(cover).toHaveCSS('filter', 'none')
      await api({
        route: 'preferences.presentation.put',
        params: { preference: 'DimDormantCovers' },
        body: { value: 'true' },
      })
      await expect(cover).not.toHaveCSS('filter', 'none')
      await page.screenshot({ path: testInfo.outputPath('merge-desktop-1200.png') })
      await rows
        .nth(1)
        .getByRole('button', { name: /^Details for / })
        .click()
      await expect(page.locator('.avalon-details')).toBeVisible()
      await page
        .getByRole('button', {
          name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
          exact: true,
        })
        .click()
      await expect(page.locator('.merge-queue')).toBeVisible()
      await expect(card.locator('[data-merge-row]').nth(1)).toBeFocused()
    } else {
      for (const width of [1920, 1280]) {
        await application.evaluate(
          ({ BrowserWindow }, width) =>
            BrowserWindow.getAllWindows()[0]!.setContentSize(width, (width * 9) / 16),
          width,
        )
        await expect.poll(() => page.evaluate(() => innerWidth)).toBe(width)
        const button = card.getByRole('button')
        await button.click()
        await page
          .getByRole('button', { name: / · Included$/ })
          .first()
          .click()
        const promote = page.getByRole('button', { name: 'Make header', exact: true })
        await expect(page.getByRole('button', { name: 'Open game', exact: true })).toBeVisible()
        await assertFits()
        await page.screenshot({ path: testInfo.outputPath(`merge-fullscreen-member-${width}.png`) })
        await expect(promote).toBeEnabled()
        await promote.press('Enter')
        await page.getByRole('button', { name: / · Header$/ }).click()
        await expect(page.getByRole('button', { name: 'Make header', exact: true })).toHaveCount(0)
        await page.keyboard.press('Escape')
        await expect(page.getByRole('button', { name: /^Same game: / })).toBeVisible()
        await page.keyboard.press('Escape')
        await expect(button).toBeFocused()
      }
      const tap = await controller()
      await tap(0)
      await expect(page.getByRole('dialog')).toBeVisible()
      await tap(3)
      await expect(page.getByRole('button', { name: 'Remove from selection', exact: true })).toBeVisible()
      await tap(2)
      await expect(page.getByRole('dialog', { name: 'Group these entries?', exact: true })).toBeVisible()
      await tap(1)
      await expect(page.getByRole('button', { name: /^Same game: / })).toBeVisible()
      await tap(1)
      await expect(card.getByRole('button')).toBeFocused()
      await page.evaluate(() =>
        Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [] }),
      )
      await card.getByRole('button').click()
      await page.getByRole('button', { name: / · Header$/ }).click()
      await page.getByRole('button', { name: 'Open game', exact: true }).click()
      await expect(page.locator('.avalon-details')).toBeVisible()
      await page
        .getByRole('button', {
          name: 'B · Back to Library',
          exact: true,
        })
        .click()
      await expect(card.getByRole('button')).toBeFocused()
    }
    await page.getByRole('button', { name: 'Close tools' }).click()
    expect(failures).toEqual([])
  })
}
