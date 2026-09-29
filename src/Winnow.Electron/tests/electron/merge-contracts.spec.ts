import { test, expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtemp, readFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import electronPath from 'electron'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ManualGame } from '../../src/renderer/api/types'
import { buildMergeCards, mergeTitle, type MergeReview } from '../../src/renderer/features/parity-merge-model'

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
test.afterAll(async () => {
  let timer: ReturnType<typeof setTimeout> | undefined
  if (app)
    try {
      await Promise.race([
        app.close(),
        new Promise<void>((done) => {
          timer = setTimeout(() => {
            app.process().kill()
            done()
          }, 5000)
        }),
      ])
    } finally {
      clearTimeout(timer)
    }
  if (directory)
    try {
      const endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
      if (new URL(endpoint.address).hostname !== '127.0.0.1')
        throw Error('Unexpected fixture backend address')
      await fetch(new URL('/api/v1/lifecycle/shutdown', endpoint.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${endpoint.token}` },
        signal: AbortSignal.timeout(5000),
      })
    } catch {
      /* Keep the temporary fixture for diagnostics. */
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
  await page.getByRole('button', { name: 'Manage library', exact: true }).click()
  await page.getByRole('button', { name: 'Identity review', exact: true }).click()
}
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
    const initial = await section
      .locator('article')
      .evaluateAll((cards) => cards.map((card) => card.getAttribute('aria-label')))
    const card = queue.getByRole('article', { name: `${title} proposal`, exact: true })
    if (mode === 'fullscreen') await card.getByRole('button').click()
    await (mode === 'desktop' ? card : page.getByRole('dialog'))
      .getByRole('button', { name: 'Same game', exact: true })
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
      .getByRole('button', { name: 'Separate again', exact: true })
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
    } else await queue.getByRole('combobox', { name: 'Sort proposals', exact: true }).selectOption('title')
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
    await page.screenshot({ path: info.outputPath(`merge-${mode}-header.png`) })
    await api({
      route: 'identity.undo',
      body: { expectedRevision: after.revision, actIds: [saved.actId], candidateIds: [], refusedPairs: [] },
    })
    await page.getByRole('button', { name: 'Close tools', exact: true }).click()
  })
}
