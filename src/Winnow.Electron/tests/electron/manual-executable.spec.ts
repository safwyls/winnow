import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { libraryAction } from './library-controls'
import type { ApiRequest } from '../../src/shared/bridge'
import type { ManualGame } from '../../src/renderer/api/types'

type Mode = 'desktop' | 'fullscreen'
let application: ElectronApplication | undefined, page: Page, directory: string, mode: Mode
let files: Record<'Celeste' | 'Tunic' | 'Braid' | 'Prey' | 'Iconoclasts' | 'CP' | 'empty', string>
const errors: string[] = []
const bytes = 'Inert executable fixture. This file must never be executed.'

test.beforeEach(async ({}, info) => {
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-manual-executable-'))
  files = Object.fromEntries(
    ['Celeste', 'Tunic', 'Braid', 'Prey', 'Iconoclasts'].map((name) => [
      name,
      join(directory, 'files', name, name === 'Iconoclasts' ? 'game.exe' : `${name}.exe`),
    ]),
  ) as typeof files
  files.CP = join(directory, 'files', 'CP', 'bin', 'game.exe')
  // Five uninformative parent folders match the inspector's bounded walk; the disposable root cannot become a guessed game title.
  files.empty = join(directory, 'files', 'games', 'bin', 'win64', 'binaries', 'build', 'game.exe')
  for (const path of Object.values(files)) {
    await mkdir(dirname(path), { recursive: true })
    await writeFile(path, bytes)
  }
  errors.length = 0
  application = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/manual-executable-main.mjs'), '--data-dir', directory, '--no-sync'],
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
  await expect(page.getByRole('button', { name: 'Skip setup', exact: true })).toBeVisible({ timeout: 45000 })
  await page.getByRole('button', { name: 'Skip setup', exact: true }).click()
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  await application.evaluate(({ BrowserWindow }, mode) => {
    const window = BrowserWindow.getAllWindows()[0]!
    window.setFullScreen(false)
    window.setContentSize(mode === 'desktop' ? 1100 : 1920, mode === 'desktop' ? 800 : 1080)
    window.isFullScreen = () => mode === 'fullscreen'
    window.webContents.send('winnow:fullscreen:changed', mode === 'fullscreen')
    window.focus()
  }, mode)
  await expect(page.locator('.avalon-shell')).toHaveClass(new RegExp(mode))
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('button', { name: 'Library', exact: true })
    .click()
  await (await libraryAction(page, 'Manage library')).click()
  await page.getByRole('button', { name: 'Manual games', exact: true }).click()
})

test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page)
      await info.attach('manual-executable-failure', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
    if (application)
      expect(await application.evaluate(() => (globalThis as any).__manualExecutable.shellAttempts)).toEqual(
        [],
      )
    for (const path of Object.values(files)) expect(await readFile(path, 'utf8')).toBe(bytes)
  } finally {
    await closeFixture(application, directory)
    application = undefined
  }
  expect(errors).toEqual([])
})

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status}`)
    return response.data
  }, input) as Promise<T>
}
const editor = () => page.locator('.manual-editor')
const field = (name: string) => editor().getByLabel(name, { exact: true })
const button = (name: string) => editor().getByRole('button', { name, exact: true })
async function searches() {
  return application!.evaluate(() => (globalThis as any).__manualExecutable.searches as string[])
}
async function tap(button: number) {
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
async function activate(control: Locator) {
  await control.focus()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function choose(control: Locator, path: string | null) {
  await application!.evaluate(
    ({ app }, { mode, path, folder }) => {
      if (mode === 'desktop') (globalThis as any).__manualExecutable.selections.push(path)
      app.setPath('documents', folder)
    },
    { mode, path, folder: path ? dirname(path) : join(directory, 'files') },
  )
  await activate(control)
  if (mode === 'fullscreen') {
    const picker = page.getByRole('dialog', { name: 'Choose game executable', exact: true })
    await expect(picker).toBeVisible()
    if (path) {
      const entry = picker.getByRole('button', { name: `File ${basename(path)}`, exact: true })
      await expect(entry).toBeEnabled()
      if (test.info().title.includes('browsing an executable')) {
        await entry.focus()
        const hints = picker.getByRole('group', { name: 'File chooser controls' })
        await expect(hints).toContainText('Browse')
        await expect(hints).toContainText('Select')
        await expect(hints).toContainText('Cancel')
        for (const glyph of ['D-pad', 'A', 'B'])
          await expect(hints.locator(`[data-picker-glyph="${glyph}"] svg`)).toBeVisible()
        expect(
          await hints.evaluate((element) => {
            const bounds = element.getBoundingClientRect()
            return (
              bounds.left >= 0 &&
              bounds.top >= 0 &&
              bounds.right <= innerWidth &&
              bounds.bottom <= innerHeight
            )
          }),
        ).toBe(true)
        await page.screenshot({
          path: test.info().outputPath('fullscreen-executable-picker.png'),
          animations: 'disabled',
        })
      }
      await activate(entry)
    } else await tap(1)
    await expect(picker).toHaveCount(0)
  }
  await expect(button('Choose executable')).toBeEnabled()
}
async function begin(path: string) {
  await choose(page.getByRole('button', { name: 'Add from executable…', exact: true }), path)
  await expect(editor()).toBeVisible()
}
function rows() {
  const database = new DatabaseSync(join(directory, 'winnow.db'), { readOnly: true })
  try {
    return database
      .prepare(
        'SELECT (SELECT COUNT(*) FROM works) AS works, (SELECT COUNT(*) FROM releases) AS releases, (SELECT COUNT(*) FROM ownerships) AS ownerships, (SELECT COUNT(*) FROM manual_entries) AS manual',
      )
      .get()
  } finally {
    database.close()
  }
}
async function noWrites() {
  expect(await api<ManualGame[]>({ route: 'manual.get' })).toEqual([])
  expect(rows()).toEqual({ works: 0, releases: 0, ownerships: 0, manual: 0 })
  expect(await application!.evaluate(() => (globalThis as any).__manualExecutable.mutations)).toEqual([])
}
async function save(expected: Partial<ManualGame>) {
  await activate(button('Save game'))
  await expect(editor()).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Add from executable…', exact: true })).toBeFocused()
  const entries = await api<ManualGame[]>({ route: 'manual.get' })
  expect(entries).toHaveLength(1)
  expect(entries[0]).toMatchObject(expected)
  expect(rows()).toEqual({ works: 1, releases: 1, ownerships: 1, manual: 1 })
  const database = new DatabaseSync(join(directory, 'winnow.db'), { readOnly: true })
  try {
    expect(
      database
        .prepare('SELECT executable_path, platform_label FROM manual_entries WHERE ownership_id=?')
        .get(entries[0].ownershipId),
    ).toEqual({ executable_path: entries[0].executablePath, platform_label: entries[0].platformLabel })
    expect(
      database.prepare('SELECT install_path FROM ownerships WHERE id=?').get(entries[0].ownershipId),
    ).toEqual({ install_path: entries[0].installPath })
  } finally {
    database.close()
  }
  expect(await application!.evaluate(() => (globalThis as any).__manualExecutable.mutations)).toHaveLength(1)
}

for (const mode of ['desktop', 'fullscreen'] as const) {
  test(`${mode} browsing an executable proposes Celeste and searches with its title before any save`, async ({}, info) => {
    await begin(files.Celeste)
    await expect(field('Executable path')).toHaveValue(files.Celeste)
    await expect(field('Installation folder')).toHaveValue(dirname(files.Celeste))
    await expect(field('Title')).toHaveValue('Celeste')
    await expect(editor().getByText('Guessed Celeste from the path.', { exact: true })).toBeVisible()
    expect(await searches()).toEqual(['Celeste'])
    await expect(editor().locator('.igdb-candidates .metadata-row')).toHaveCount(1)
    await expect(editor().locator('.igdb-candidates')).toContainText('2018')
    await noWrites()
    await page.screenshot({
      path: info.outputPath(`${mode}-executable-proposal.png`),
      animations: 'disabled',
    })
    await editor().locator('.igdb-candidates').scrollIntoViewIfNeeded()
    await button('Use these details').focus()
    await page.screenshot({
      path: info.outputPath(`${mode}-executable-candidates.png`),
      animations: 'disabled',
    })
  })

  test(`${mode} cancelling executable selection preserves empty and typed drafts and restores invoking focus`, async () => {
    await page.getByRole('button', { name: 'Add a game', exact: true }).click()
    await choose(button('Choose executable'), null)
    await expect(field('Executable path')).toHaveValue('')
    await expect(field('Title')).toHaveValue('')
    await expect(button('Choose executable')).toBeFocused()
    await expect(editor().getByText(/Guessed .* from the path|No title found in the file/)).toHaveCount(0)
    expect(await searches()).toEqual([])
    await field('Title').fill('My own title')
    await field('Platform').fill('PlayStation 2')
    await choose(button('Choose executable'), null)
    await expect(field('Title')).toHaveValue('My own title')
    await expect(field('Platform')).toHaveValue('PlayStation 2')
    await expect(button('Choose executable')).toBeFocused()
    await noWrites()
    await activate(button('Cancel'))
    await expect(editor()).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Add a game', exact: true })).toBeFocused()
  })

  test(`${mode} choosing Cyberpunk metadata fills title year and identity without writing a game`, async ({}, info) => {
    await begin(files.CP)
    await expect(field('Title')).toHaveValue('CP')
    expect(await searches()).toEqual(['CP'])
    await field('Title').fill('Cyberpunk')
    await activate(button('Find IGDB matches'))
    await expect(editor().locator('.igdb-candidates')).toContainText('Cyberpunk 2077')
    expect(await searches()).toEqual(['CP', 'Cyberpunk'])
    await activate(button('Use these details'))
    await expect(field('Title')).toHaveValue('Cyberpunk 2077')
    await expect(field('Release year')).toHaveValue('2020')
    await expect(field('IGDB ID')).toHaveValue('1877')
    await expect(
      editor().getByText('Using details from Cyberpunk 2077. Nothing is saved until you choose Save game.', {
        exact: true,
      }),
    ).toBeVisible()
    await expect(editor().locator('.igdb-candidates')).toHaveCount(0)
    await expect(field('Executable path')).toHaveValue(files.CP)
    await noWrites()
    await field('Title').scrollIntoViewIfNeeded()
    await page.screenshot({ path: info.outputPath(`${mode}-candidate-draft.png`), animations: 'disabled' })
    await editor()
      .getByText('Using details from Cyberpunk 2077. Nothing is saved until you choose Save game.', {
        exact: true,
      })
      .scrollIntoViewIfNeeded()
    await page.screenshot({ path: info.outputPath(`${mode}-candidate-details.png`), animations: 'disabled' })
    await activate(button('Cancel'))
    await noWrites()
  })

  test(`${mode} correcting Prey to Prey 2017 searches the corrected user title`, async () => {
    await begin(files.Prey)
    expect(await searches()).toEqual(['Prey'])
    await expect(editor().locator('.igdb-candidates')).toContainText('2006')
    await field('Title').fill('Prey 2017')
    await activate(button('Find IGDB matches'))
    await expect.poll(searches).toEqual(['Prey', 'Prey 2017'])
    await expect(field('Title')).toHaveValue('Prey 2017')
    await noWrites()
  })

  test(`${mode} dismissing a different metadata proposal preserves Iconoclasts and still saves by hand`, async () => {
    await begin(files.Iconoclasts)
    await field('Title').fill('Iconoclasts')
    await activate(button('Find IGDB matches'))
    await expect(editor().locator('.igdb-candidates')).toContainText('Something Else')
    expect(await searches()).toEqual(['Iconoclasts', 'Iconoclasts'])
    await activate(button('Keep my own details'))
    await expect(editor().locator('.igdb-candidates')).toHaveCount(0)
    await expect(
      editor().getByText('No matching games. You can still fill the form by hand.', { exact: true }),
    ).toHaveCount(0)
    await expect(field('Title')).toHaveValue('Iconoclasts')
    await expect(field('Executable path')).toHaveValue(files.Iconoclasts)
    await expect(field('IGDB ID')).toHaveValue('')
    await noWrites()
    await save({
      title: 'Iconoclasts',
      executablePath: files.Iconoclasts,
      installPath: dirname(files.Iconoclasts),
      igdbId: null,
    })
  })

  test(`${mode} an executable with no usable title saves a hand-typed physical game and platform`, async () => {
    await begin(files.empty)
    await expect(field('Title')).toHaveValue('')
    await expect(
      editor().getByText('No title found in the file. Type one above.', { exact: true }),
    ).toBeVisible()
    await expect(button('Find IGDB matches')).toBeDisabled()
    expect(await searches()).toEqual([])
    await noWrites()
    await field('Title').fill('A Disc I Own')
    await field('Platform').fill('PlayStation 2')
    await save({
      title: 'A Disc I Own',
      platformLabel: 'PlayStation 2',
      executablePath: files.empty,
      installPath: dirname(files.empty),
    })
    expect(await searches()).toEqual([])
  })

  test(`${mode} saving Tunic persists exactly the selected executable and its own installation folder`, async () => {
    await begin(files.Tunic)
    await expect(field('Title')).toHaveValue('Tunic')
    await noWrites()
    await save({ title: 'Tunic', executablePath: files.Tunic, installPath: dirname(files.Tunic) })
    await page.getByRole('button', { name: 'Edit game', exact: true }).click()
    await expect(field('Executable path')).toHaveValue(files.Tunic)
    await expect(field('Installation folder')).toHaveValue(dirname(files.Tunic))
  })

  test(`${mode} rebrowsing replaces its own title guess while retaining the user title for Braid`, async () => {
    await begin(files.Celeste)
    await expect(field('Title')).toHaveValue('Celeste')
    await choose(button('Choose executable'), files.Tunic)
    await expect(field('Title')).toHaveValue('Tunic')
    await field('Title').fill('My own title')
    await choose(button('Choose executable'), files.Braid)
    await expect(field('Title')).toHaveValue('My own title')
    await expect(field('Executable path')).toHaveValue(files.Braid)
    await expect(field('Installation folder')).toHaveValue(dirname(files.Braid))
    expect(await searches()).toEqual(['Celeste', 'Tunic', 'My own title'])
    await noWrites()
  })
}
