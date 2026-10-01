import { libraryAction } from './library-controls'
import { closeFixture } from './fixture-cleanup'
import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import { DatabaseSync } from 'node:sqlite'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ManualGame } from '../../src/renderer/api/types'
import {
  buildMergeCards,
  mergeMemberLabels,
  mergeTitle,
  type MergeReview,
} from '../../src/renderer/features/parity-merge-model'

let app: ElectronApplication, page: Page, directory: string
test.beforeAll(async () => {
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-merge-contracts-'))
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
    ),
  ) as Record<string, string>
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('.'), '--data-dir', directory, '--seed-sample', '--no-sync'],
    env: environment,
    chromiumSandbox: true,
    timeout: 60_000,
  })
  page = await app.firstWindow()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  if (await page.getByRole('dialog', { name: 'Winnow setup' }).count())
    await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
})
test.afterAll(async () => closeFixture(app, directory))
async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const result = await window.winnow.request(input)
    if (!result.ok) throw Error(`${input.route}: ${result.status} ${result.message}`)
    return result.data
  }, input) as Promise<T>
}
async function surface(mode: 'desktop' | 'fullscreen') {
  await app.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1280, 800)
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await (await libraryAction(page, 'Manage library')).click()
  await page.getByRole('button', { name: 'Identity review', exact: true }).click()
}
test('dedicated desktop Merges keeps its rail identity and Details cursor then Escape returns to Library', async ({}, info) => {
  // Seeded review-only releases intentionally have no tile. This Details route needs an owned member.
  const review = await api<MergeReview>({ route: 'identity.get' })
  const firstMember = buildMergeCards(review).find((card) => !card.actId)!.rows[0]!
  const database = new DatabaseSync(join(directory, 'winnow.db'))
  try {
    database.exec('PRAGMA busy_timeout=5000')
    database
      .prepare("INSERT INTO ownerships(release_id,store,installed) VALUES(?,'steam',0)")
      .run(firstMember.releaseIds[0]!)
  } finally {
    database.close()
  }
  await page.reload()
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await app.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(1280, 800)
    window.webContents.send('winnow:fullscreen:changed', false)
  })
  const rail = page.getByRole('navigation', { name: 'Main navigation' })
  const destination = rail.getByRole('button', { name: 'Merges', exact: true })
  await expect(destination).toHaveText('Merges')
  await expect(destination).toHaveAttribute(
    'title',
    'Entries that might be one game, and what you have rolled up',
  )
  await expect(destination).toHaveCSS('opacity', '1')
  await expect(destination.locator('small')).toHaveCount(0)
  await destination.click()
  await expect(page.getByRole('heading', { name: 'Merges', exact: true, level: 1 })).toBeVisible()
  await expect(destination).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('navigation', { name: 'Library tools', exact: true })).toHaveCount(0)
  const sort = page.getByRole('button', { name: /^Sort ·/ })
  await sort.click()
  await expect(page.getByRole('menu', { name: 'Sort order', exact: true })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(sort).toBeFocused()
  await expect(destination).toHaveAttribute('aria-current', 'page')
  const row = page.locator('[data-merge-row]').first()
  await expect(row).toBeEnabled()
  const cursor = await row.getAttribute('data-merge-row')
  const before = await api<MergeReview>({ route: 'identity.get' })
  await row.focus()
  for (const key of ['Home', 'End', 'ArrowLeft', 'ArrowRight', 'r']) {
    await page.keyboard.press(key)
    await expect(row).toBeFocused()
  }
  await row
    .locator('..')
    .getByRole('button', { name: /^Details for / })
    .click()
  await expect(page.locator('.avalon-details')).toBeVisible()
  await page.getByRole('button', { name: 'Close game details', exact: true }).click()
  await expect(destination).toHaveAttribute('aria-current', 'page')
  await expect(page.locator(`[data-merge-row="${cursor}"]`)).toBeFocused()
  await page.screenshot({ path: info.outputPath('merges-desktop-page.png') })
  await page.keyboard.press('Escape')
  await expect(rail.getByRole('button', { name: 'Library', exact: true })).toHaveAttribute(
    'aria-current',
    'page',
  )
  await expect(page.getByRole('heading', { name: 'Merges', exact: true, level: 1 })).toHaveCount(0)
  const after = await api<MergeReview>({ route: 'identity.get' })
  expect(after.history).toEqual(before.history)
  expect(after.candidates).toEqual(before.candidates)
})
for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} keeps a real answered slot through separation and opens keyboard sort actions`, async ({}, info) => {
    await surface(mode)
    const queue = page.locator('.merge-queue'),
      review = await api<MergeReview>({ route: 'identity.get' })
    const proposed = buildMergeCards(review).find((card) => !card.actId && card.kind === 'same_game')!
    const title = mergeTitle(proposed),
      section = queue.getByRole('region', {
        name: proposed.section === 'stores' ? 'Across stores' : 'Editions',
        exact: true,
      })
    const card = queue.getByRole('article', { name: `${title} proposal`, exact: true })
    await expect(card).toBeVisible()
    const initial = await section
      .locator('article')
      .evaluateAll((cards) => cards.map((card) => card.getAttribute('aria-label')))
    if (mode === 'fullscreen') await card.getByRole('button').click()
    await (mode === 'desktop' ? card : page.getByRole('dialog'))
      .getByRole('button', { name: /^Same game: / })
      .click()
    if (mode === 'fullscreen') await page.getByRole('button', { name: 'Continue', exact: true }).click()
    const saved = queue.getByRole('article', { name: `${title} saved group`, exact: true })
    await expect(saved).toBeVisible()
    await expect(queue.getByRole('button', { name: 'Undo review decisions', exact: true })).toBeEnabled()
    expect(
      await section
        .locator('article')
        .evaluateAll((cards) =>
          cards.map((card) => card.getAttribute('aria-label')?.replace(' saved group', ' proposal')),
        ),
    ).toEqual(initial)
    const linked = await api<MergeReview>({ route: 'identity.get' })
    expect(linked.workspace.works.length).toBe(review.workspace.works.length)
    expect(linked.workspace.releases.length).toBe(review.workspace.releases.length)
    expect((linked.workspace.ownerships as unknown[]).length).toBe(
      (review.workspace.ownerships as unknown[]).length,
    )
    if (mode === 'fullscreen') await saved.getByRole('button').click()
    await (mode === 'desktop' ? saved : page.getByRole('dialog'))
      .getByRole('button', { name: /^Separate again: / })
      .click()
    await expect(card).toBeVisible()
    expect(
      await section
        .locator('article')
        .evaluateAll((cards) => cards.map((card) => card.getAttribute('aria-label'))),
    ).toEqual(initial)
    if (mode === 'fullscreen') {
      const sort = queue.getByRole('button', { name: /^Sort ·/ })
      await sort.focus()
      await sort.press('Enter')
      const sheet = page.getByRole('dialog', { name: 'Sort possible matches', exact: true })
      await sheet.getByRole('button', { name: 'Strongest match', exact: true }).press('End')
      await expect(sheet.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
      await page.keyboard.press('Escape')
      await expect(sort).toBeFocused()
      await sort.click()
      await page.getByRole('button', { name: 'Title', exact: true }).click()
      await expect(queue.getByRole('button', { name: 'Sort · Title', exact: true })).toBeFocused()
    } else {
      const trigger = queue.getByRole('button', { name: /^Sort ·/ })
      await trigger.click()
      await page.getByRole('menuitemradio', { name: 'Title', exact: true }).click()
      await expect(trigger).toBeFocused()
    }
    await page.screenshot({ path: info.outputPath(`merge-${mode}-queue.png`) })
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
  })
  test(`${mode} changes a real saved-group header storefront without rewriting its identity act`, async ({}, info) => {
    const parent = await api<ManualGame>({
      route: 'manual.create',
      body: {
        title: `Header matrix ${mode}`,
        firstReleaseYear: null,
        platformLabel: 'PC',
        executablePath: null,
        installPath: null,
        igdbId: null,
        steamAppId: null,
      },
    })
    const before = await api<MergeReview>({ route: 'identity.get' })
    const owned = (before.workspace.ownerships as { store: string; releaseId: number }[]).find(
      (entry) => entry.store === 'gog',
    )!
    const child = before.workspace.releases.find((release) => release.id === owned.releaseId)!.workId
    const childTitle = before.workspace.works.find((work) => work.id === child)!.name
    const saved = await api<{ actId: number }>({
      route: 'identity.link',
      body: {
        expectedRevision: before.revision,
        parentWorkId: parent.workId,
        childWorkIds: [child],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    await surface(mode)
    const queue = page.locator('.merge-queue'),
      card = queue.getByRole('article', { name: `${parent.title} saved group`, exact: true })
    await expect(card).toBeVisible()
    if (mode === 'desktop')
      await card
        .getByRole('combobox', { name: `Header store for ${parent.title}`, exact: true })
        .selectOption('gog')
    else {
      await card.getByRole('button').click()
      await page.getByRole('button', { name: 'Header store · Automatic', exact: true }).click()
      await page.getByRole('button', { name: 'GOG', exact: true }).click()
      await expect(page.getByRole('dialog', { name: childTitle, exact: true })).toBeVisible()
      await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
    }
    await expect(queue.getByRole('article', { name: `${childTitle} saved group`, exact: true })).toBeVisible()
    const after = await api<MergeReview>({ route: 'identity.get' })
    expect(after.history.filter((link) => link.actId === saved.actId)).toMatchObject([
      { parentWorkId: parent.workId, childWorkId: child, retractedAt: null },
    ])
    expect((after.workspace.preferredHeaderStores as Record<string, string>)[String(parent.workId)]).toBe(
      'gog',
    )
    const changedCard = queue.getByRole('article', { name: `${childTitle} saved group`, exact: true })
    await changedCard.scrollIntoViewIfNeeded()
    await expect(changedCard).toBeInViewport()
    const geometry = await changedCard.evaluate((element) => {
      const box = element.getBoundingClientRect()
      return {
        left: box.left,
        right: box.right,
        width: window.innerWidth,
        overflow: element.scrollWidth > element.clientWidth,
      }
    })
    expect(geometry.left).toBeGreaterThanOrEqual(0)
    expect(geometry.right).toBeLessThanOrEqual(geometry.width)
    expect(geometry.overflow).toBe(false)
    await page.screenshot({ path: info.outputPath(`merge-${mode}-header.png`) })
    await api({
      route: 'identity.undo',
      body: { expectedRevision: after.revision, actIds: [saved.actId], candidateIds: [], refusedPairs: [] },
    })
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
  })
  test(`${mode} persists a preferred platform through reload while keeping an override until the preference is cleared`, async () => {
    const seed = await api<MergeReview>({ route: 'identity.get' })
    const owned = (seed.workspace.ownerships as { store: string; releaseId: number }[]).find(
      (entry) => entry.store === 'gog',
    )!
    const release = seed.workspace.releases.find((entry) => entry.id === owned.releaseId)!
    const game = seed.workspace.works.find((work) => work.id === release.workId)! as {
      name: string
      firstReleaseYear?: number | null
    }
    await api({
      route: 'manual.create',
      body: { title: game.name, firstReleaseYear: game.firstReleaseYear, platformLabel: 'PC' },
    })
    await api({ route: 'identity.refresh', body: {} })
    await surface(mode)
    let review = await api<MergeReview>({ route: 'identity.get' })
    const proposed = buildMergeCards(review).find(
      (card) => !card.actId && card.section === 'stores' && card.rows.length > 1,
    )!
    expect(proposed).toBeTruthy()
    const preferredRow = proposed.rows.find((row) =>
      row.stores.some((store) => ['steam', 'epic', 'gog'].includes(store)),
    )!
    const overrideRow = proposed.rows.find((row) => row.workId !== preferredRow.workId)!
    const store = preferredRow.stores.find((store) => ['steam', 'epic', 'gog'].includes(store))!
    const label = ({ steam: 'Steam', epic: 'Epic Games', gog: 'GOG' } as Record<string, string>)[store]!
    const choosePlatform = async (value: string, name: string) => {
      if (mode === 'desktop')
        await page.getByRole('combobox', { name: 'Preferred main platform', exact: true }).selectOption(value)
      else {
        await page.getByRole('button', { name: /^Preferred platform ·/ }).click()
        await page.getByRole('dialog').getByRole('button', { name, exact: true }).click()
      }
      await expect
        .poll(
          async () =>
            (
              await api<{ preference: string; value: string }[]>({ route: 'preferences.presentation.get' })
            ).find((item) => item.preference === 'PreferredMergePlatform')?.value,
        )
        .toBe(value)
    }
    const checkHeader = async (workId: number) => {
      review = await api<MergeReview>({ route: 'identity.get' })
      const current = buildMergeCards(review).find((card) => card.key === proposed.key)!
      const title = current.rows.find((row) => row.workId === workId)!.title
      const card = page
        .locator('.merge-queue')
        .getByRole('article', { name: `${title} proposal`, exact: true })
      await expect(card).toBeVisible()
      const memberLabel = mergeMemberLabels(current)[current.rows.findIndex((row) => row.workId === workId)]!
      if (mode === 'desktop')
        await expect(
          card.getByRole('radio', { name: `Make ${memberLabel} the main game`, exact: true }),
        ).toBeChecked()
      else {
        await card.getByRole('button').click()
        await expect(
          page.getByRole('dialog').getByRole('button', { name: `${memberLabel} · Header`, exact: true }),
        ).toBeVisible()
        await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
      }
      return { card, memberLabel }
    }
    await choosePlatform(store, label)
    await page.reload()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    await surface(mode)
    await checkHeader(preferredRow.workId)
    const parentIndex = proposed.rows.findIndex((row) => row.workId === overrideRow.workId)
    const originalLabel = mergeMemberLabels(proposed)[parentIndex]!
    const title = preferredRow.title
    const card = page.locator('.merge-queue').getByRole('article', { name: `${title} proposal`, exact: true })
    if (mode === 'desktop')
      await card.getByRole('radio', { name: `Make ${originalLabel} the main game`, exact: true }).check()
    else {
      await card.getByRole('button').click()
      await page
        .getByRole('dialog')
        .getByRole('button', { name: `${originalLabel} · Included`, exact: true })
        .click()
      await page.getByRole('button', { name: 'Make header', exact: true }).click()
      await expect(
        page.getByRole('dialog').getByRole('button', { name: `${originalLabel} · Header`, exact: true }),
      ).toBeVisible()
      await page.getByRole('button', { name: 'Back to proposals', exact: true }).click()
    }
    await checkHeader(overrideRow.workId)
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
    await surface(mode)
    await checkHeader(overrideRow.workId)
    await choosePlatform('', 'None')
    await checkHeader(overrideRow.workId)
    await page.reload()
    await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible()
    await surface(mode)
    if (mode === 'desktop')
      await expect(page.getByRole('combobox', { name: 'Preferred main platform', exact: true })).toHaveValue(
        '',
      )
    else
      await expect(page.getByRole('button', { name: 'Preferred platform · None', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
  })
}
