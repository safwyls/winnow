import {
  test,
  expect,
  _electron as electron,
  type ElectronApplication,
  type Page,
  type Locator,
} from '@playwright/test'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { mkdtemp, readFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import electronPath from 'electron'
import { closeFixture } from './fixture-cleanup'
import { expectFullscreenJournalTypography } from './journal-typography'
import type { ApiRequest } from '../../src/shared/bridge'
import type { Mode } from '../../src/renderer/api/types'

const artifacts = resolve('../..', '.tmp/task38117-fixture-artifacts')
const fixture = join(artifacts, 'bin/Winnow.Electron.Fixtures/debug/Winnow.Electron.Fixtures.dll')
let app: ElectronApplication, page: Page, directory: string, mode: Mode
let endpoint: { address: string; token: string }
const errors: string[] = []
const details = () => page.locator('.avalon-details')
const activity = () => page.locator('.journal-page')
const events = () =>
  activity().getByRole('region', { name: 'Activity events', exact: true }).locator('article.timeline-entry')
const editor = () => page.locator('.journal-dialog, .details-journal-editor')
type Seed = {
  workId: number
  ownershipId: number
  sessionId: number
  startedAt: string
  sessionIds: number[]
}
type Call = {
  id: number
  operation: string
  from: string
  until: string
  section: number
  after: unknown
  pageSize: number
  sessionId?: number
  gateId?: string
  completed: boolean
  failed: boolean
  processId: number
  cancellationRequested: boolean
}
type State = {
  processId: number
  controls: { calls: Call[] }
  notes: { sessionId: number; note: string | null; rating: number | null }[]
  sessions: { id: number; durationSeconds: number | null; startedAt: string; endedAt: string | null }[]
  sessionCount: number
  noteCount: number
}
const state = () => control<State>('state')
const calls = async (operation: string) =>
  (await state()).controls.calls.filter((call) => call.operation === operation)
async function seed(kind: string) {
  const result = await control<Seed>('seed', { kind })
  if (kind === 'reload' || kind === 'fullscreen-existing')
    // The API bounds count whole seconds; let a source session created "now" enter that bound.
    await expect
      .poll(() => Math.floor(Date.now() / 1000) * 1000)
      .toBeGreaterThan(Date.parse(result.startedAt))
  return result
}
async function arm(operation: string, target = 'any', behavior = 'hold') {
  return (await control<{ gateId: string }>('arm', { operation, target, behavior, ignoreCancellation: true }))
    .gateId
}
async function entered(gateId: string) {
  await expect
    .poll(async () => (await state()).controls.calls.some((call) => call.gateId === gateId))
    .toBe(true)
}
async function released(gateId: string) {
  await control('release', { gateId })
  await expect
    .poll(async () => (await state()).controls.calls.find((call) => call.gateId === gateId)?.completed)
    .toBe(true)
}
async function enterHistory() {
  await navigate('Activity')
  await expect(activity().getByRole('heading', { name: 'A little history.', exact: true })).toBeVisible()
}
async function saveEditor() {
  await activate(editor().getByRole('button', { name: /^Save(?: note)?$/, exact: true }))
}
function ratingValue() {
  return editor().locator('select[name="rating"], .journal-rating input[name="rating"]')
}
async function chooseRating(value: number) {
  const group = editor().getByRole('group', { name: 'Your rating', exact: true })
  if (await group.count()) {
    const current = Number(await ratingValue().inputValue())
    if (value !== current) {
      const target = value || current
      await activate(group.getByRole('button', { name: `${target} out of 5`, exact: true }))
    }
  } else
    await editor().getByRole('combobox', { name: 'Your rating', exact: true }).selectOption(String(value))
  await expect(ratingValue()).toHaveValue(String(value))
}
async function openSessionEditor(context: 'details' | 'activity', item: Seed, existing: boolean) {
  if (context === 'activity') {
    await enterHistory()
    await activity().getByRole('combobox', { name: 'Time period', exact: true }).selectOption('90')
    await expect(events()).toHaveCount(1)
    await activate(events().getByRole('button', { name: existing ? 'Edit note' : 'Add note', exact: true }))
  } else {
    await openDetails(item.workId)
    if (existing) {
      await openJournal()
      await activate(details().getByRole('button', { name: /^(Edit|Edit note)$/, exact: true }))
    } else {
      await activate(
        mode === 'desktop'
          ? details().getByRole('tab', { name: 'Activity', exact: true })
          : details().getByRole('button', { name: 'Play history →', exact: true }),
      )
      await activate(details().getByRole('button', { name: 'Journal', exact: true }))
    }
  }
  await expect(editor().getByRole('textbox', { name: 'Journal note', exact: true })).toBeVisible()
}

async function api<T>(input: ApiRequest): Promise<T> {
  return page.evaluate(async (input) => {
    const response = await window.winnow.request(input)
    if (!response.ok) throw Error(`${input.route}: ${response.status} ${response.message}`)
    return response.data
  }, input) as Promise<T>
}
async function control<T>(name: string, body?: unknown): Promise<T> {
  const response = await fetch(new URL(`/__fixture/journal-activity/${name}`, endpoint.address), {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Authorization: `Bearer ${endpoint.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(60000),
  })
  if (!response.ok) throw Error(`Journal activity fixture ${name}: HTTP${response.status}`)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}
async function tap(button: number) {
  // Standard Gamepad API frames, not a physical-device test.
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
async function activate(element: Locator) {
  await element.scrollIntoViewIfNeeded()
  await element.focus()
  await expect(element).toBeFocused()
  if (mode === 'fullscreen') await tap(0)
  else await page.keyboard.press('Enter')
}
async function surface(width = 1920, height = 1080) {
  await app.evaluate(
    ({ BrowserWindow }, value) => {
      const window = BrowserWindow.getAllWindows()[0]!
      window.setFullScreen(false)
      window.setMinimumSize(0, 0)
      window.setContentSize(value.width, value.height)
      window.isFullScreen = () => value.mode === 'fullscreen'
      window.webContents.send('winnow:fullscreen:changed', value.mode === 'fullscreen')
      window.focus()
    },
    { mode, width, height },
  )
  await expect(page.locator(`.avalon-shell.${mode}`)).toBeVisible()
  if (mode === 'fullscreen') await tap(-1)
}
async function navigate(name: string) {
  await activate(
    page.getByRole('navigation', { name: 'Main navigation' }).getByRole('button', { name, exact: true }),
  )
}
async function openDetails(workId: number) {
  await navigate('Library')
  await activate(page.locator(`.avalon-library [data-avalon-game="${workId}"]`))
  await expect(details().locator('h1')).toBeVisible()
}
async function openJournal() {
  await activate(
    mode === 'desktop'
      ? details().getByRole('tab', { name: 'Journal', exact: true })
      : details().getByRole('button', { name: 'Open journal →', exact: true }),
  )
  await expect(details().getByRole('heading', { name: 'Journal', exact: true })).toBeVisible()
}
async function openAccount() {
  await activate(page.getByRole('button', { name: 'Settings', exact: true }))
  const sections = page.getByRole('navigation', { name: 'Settings section' })
  if (mode === 'fullscreen') await activate(sections.getByRole('button', { name: 'Library', exact: true }))
  await activate(
    mode === 'desktop'
      ? sections.getByRole('button', { name: 'Spending', exact: true })
      : page.locator('.fullscreen-settings-content').getByRole('button', { name: 'Spending', exact: true }),
  )
  await expect(page.getByRole('region', { name: 'Account spending', exact: true })).toBeVisible()
}
async function capture(name: string, target?: Locator) {
  if (target) {
    await target.scrollIntoViewIfNeeded()
    await expect(target).toBeInViewport()
  }
  await page.screenshot({ path: test.info().outputPath(`${mode}-${name}.png`), animations: 'disabled' })
}

test.beforeAll(async () => {
  test.setTimeout(120000)
  await promisify(execFile)(
    'dotnet',
    [
      'build',
      resolve('../..', 'tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj'),
      '--artifacts-path',
      artifacts,
      '--nologo',
      '--verbosity',
      'quiet',
    ],
    { windowsHide: true, timeout: 115000 },
  ).catch((error: Error & { stdout?: string; stderr?: string }) => {
    throw new Error(`${error.message}\n${error.stdout ?? ''}\n${error.stderr ?? ''}`)
  })
})
test.beforeEach(async ({}, info) => {
  test.setTimeout(90000)
  mode = info.title.startsWith('fullscreen') ? 'fullscreen' : 'desktop'
  errors.length = 0
  directory = await mkdtemp(join(resolve('../..', '.tmp'), 'winnow-electron-journal-activity-'))
  app = await electron.launch({
    executablePath: electronPath as unknown as string,
    args: [resolve('tests/electron/journal-activity-main.mjs'), '--data-dir', directory, '--no-sync'],
    env: {
      ...(Object.fromEntries(
        Object.entries(process.env).filter(
          ([key, value]) => key !== 'ELECTRON_RUN_AS_NODE' && value !== undefined,
        ),
      ) as Record<string, string>),
      WINNOW_BACKEND_PATH: fixture,
    },
    chromiumSandbox: true,
    timeout: 60000,
  })
  page = await app.firstWindow()
  page.on('pageerror', (error) => errors.push(error.message))
  await expect(page.getByRole('button', { name: 'Winnow home', exact: true })).toBeVisible({ timeout: 45000 })
  await expect(page.locator('.startup-presentation')).toHaveCount(0)
  endpoint = JSON.parse(await readFile(join(directory, 'backend/endpoint.json'), 'utf8'))
  expect((await fetch(new URL('/__fixture/journal-activity/state', endpoint.address))).status).toBe(401)
  await surface()
})
test.afterEach(async ({}, info) => {
  try {
    if (info.status !== info.expectedStatus && page) {
      await info.attach('journal-activity-failure', {
        body: await page.screenshot(),
        contentType: 'image/png',
      })
      await info.attach('before-teardown-dom', {
        body: await page.locator('body').ariaSnapshot(),
        contentType: 'text/plain',
      })
    }
    expect(await app.evaluate(() => (globalThis as any).__identityProjections.dispatches)).toEqual([])
    expect(await app.evaluate(() => (globalThis as any).__journalActivityNative.notifications)).toEqual([])
  } finally {
    if (endpoint) await control('release', {}).catch(() => {})
    await closeFixture(app, directory)
  }
  expect(errors).toEqual([])
})

for (const surfaceName of ['desktop', 'fullscreen']) {
  for (const append of [false, true])
    test(`${surfaceName} source ${append ? 'append' : 'first'} page failure retries the same cursor without discarding loaded rows`, async () => {
      await seed('paging')
      const gate = await arm('activity', append ? 'append' : 'first', 'fail')
      await enterHistory()
      if (append) {
        await expect(events()).toHaveCount(1)
        await events().first().focus()
        await activate(activity().getByRole('button', { name: 'Load more', exact: true }))
      }
      await entered(gate)
      await expect(
        activity().getByText("Couldn't read your activity. Try again.", { exact: true }),
      ).toBeVisible()
      if (append) {
        await expect(events()).toHaveCount(1)
        await expect(events().first()).toHaveAttribute('aria-current', 'true')
      }
      await capture(
        `${append ? 'append' : 'first'}-page-retry`,
        activity().getByRole('button', { name: 'Try again', exact: true }),
      )
      await activate(activity().getByRole('button', { name: 'Try again', exact: true }))
      await expect(events()).toHaveCount(append ? 2 : 1)
      await expect(
        activity().getByText("Couldn't read your activity. Try again.", { exact: true }),
      ).toHaveCount(0)
      const requests = await calls('activity')
      expect(requests).toHaveLength(append ? 3 : 2)
      expect(requests.at(-1)!.after).toEqual(requests.at(-2)!.after)
      expect(requests.every((call) => call.pageSize === 50)).toBe(true)
      await test
        .info()
        .attach('source-page-requests', { body: JSON.stringify(requests), contentType: 'application/json' })
    })

  test(`${surfaceName} explicit load more cancels an ignored pending page on disposal without publishing it later`, async () => {
    await seed('paging-disposal')
    await enterHistory()
    await expect(events()).toHaveCount(1)
    await events().first().focus()
    expect(await calls('activity')).toHaveLength(1)
    const gate = await arm('activity', 'append')
    await activate(activity().getByRole('button', { name: 'Load more', exact: true }))
    await entered(gate)
    await navigate('Library')
    await expect(activity()).toHaveCount(0)
    await expect
      .poll(
        async () =>
          (await state()).controls.calls.find((call) => call.gateId === gate)?.cancellationRequested,
      )
      .toBe(true)
    await released(gate)
    await expect(activity()).toHaveCount(0)
    const disposedRequests = await calls('activity')
    expect(disposedRequests).toHaveLength(2)
    await expect
      .poll(() => Math.floor(Date.now() / 1000) * 1000)
      .toBeGreaterThan(Date.parse(disposedRequests[0].until))
    await enterHistory()
    await expect(events()).toHaveCount(1)
    await expect(events().first()).toHaveAttribute('aria-current', 'true')
    const reopenedRequests = await calls('activity')
    expect(reopenedRequests).toHaveLength(3)
    expect(reopenedRequests[2].after).toBeNull()
    expect(Date.parse(reopenedRequests[2].until)).toBeGreaterThan(Date.parse(disposedRequests[0].until))
  })

  test(`${surfaceName} completing a held activity read retains the Updates tab keyboard focus`, async () => {
    await seed('paging')
    const gate = await arm('activity', 'first')
    await enterHistory()
    await entered(gate)
    const updates = activity()
      .getByRole('navigation', { name: 'Activity type' })
      .getByRole('button', { name: 'Updates', exact: true })
    await updates.focus()
    await expect(updates).toBeFocused()
    await released(gate)
    await expect(events()).toHaveCount(1)
    await expect(updates).toBeFocused()
  })

  test(`${surfaceName} saving the selected source older-page note keeps both pages and exactly two activity reads`, async () => {
    await seed('paging')
    await enterHistory()
    await expect(events()).toHaveCount(1)
    await activate(activity().getByRole('button', { name: 'Load more', exact: true }))
    await expect(events()).toHaveCount(2)
    const older = events().last()
    await older.focus()
    await expect(older).toHaveAttribute('aria-current', 'true')
    if (mode === 'fullscreen') await tap(2)
    else await page.keyboard.press('x')
    const field = editor().getByRole('textbox', { name: 'Journal note', exact: true })
    await field.fill('Continue from the old session.')
    await saveEditor()
    await expect(editor()).toHaveCount(0)
    await expect(events()).toHaveCount(2)
    await expect(older).toHaveAttribute('aria-current', 'true')
    await expect(older).toContainText('Continue from the old session.')
    await expect(older).toContainText('Journal entry')
    expect((await state()).notes).toEqual([
      { sessionId: 2, note: 'Continue from the old session.', rating: null },
    ])
    expect(await calls('activity')).toHaveLength(2)
    await capture('older-page-note-retained', older)
  })

  for (const dispose of [false, true])
    test(`${surfaceName} source account ${dispose ? 'cancellation discards the late result' : 'failure retries with an enabled focused action'} while its reader is held`, async () => {
      await seed('account')
      const gate = await arm('account', 'any', dispose ? 'hold' : 'hold-fail')
      await openAccount()
      await entered(gate)
      const account = page.getByRole('region', { name: 'Account spending', exact: true })
      await expect(account.getByText('Reading your account statistics…', { exact: true })).toBeVisible()
      const focus =
        mode === 'fullscreen'
          ? page.locator('[data-settings-child-back]')
          : page
              .getByRole('navigation', { name: 'Settings section' })
              .getByRole('button', { name: 'Spending', exact: true })
      await focus.focus()
      const pulse = await page.evaluate(
        () => new Promise<boolean>((resolve) => requestAnimationFrame(() => resolve(true))),
      )
      expect(pulse).toBe(true)
      const processIds = await app.evaluate(({ BrowserWindow }) => ({
        main: process.pid,
        renderer: BrowserWindow.getAllWindows()[0]!.webContents.getOSProcessId(),
      }))
      const pending = (await state()).controls.calls.find((call) => call.gateId === gate)!
      expect(pending.processId).not.toBe(processIds.main)
      expect(pending.processId).not.toBe(processIds.renderer)
      if (dispose) {
        await navigate('Library')
        await expect
          .poll(
            async () =>
              (await state()).controls.calls.find((call) => call.gateId === gate)?.cancellationRequested,
          )
          .toBe(true)
        await released(gate)
        await expect(account).toHaveCount(0)
        await expect(page.locator('.avalon-library')).toBeVisible()
      } else {
        await released(gate)
        await expect(
          account.getByText("Couldn't read Steam spending. Try again.", { exact: true }),
        ).toBeVisible()
        await expect(focus).toBeFocused()
        const retry = account.getByRole('button', { name: 'Try again', exact: true })
        const retained = await retry.elementHandle()
        await activate(retry)
        await expect(account.getByText('1 transactions · 0 licences.', { exact: true })).toBeVisible()
        const refreshed = account.getByRole('button', { name: 'Refresh Steam spending', exact: true })
        expect(await retained!.evaluate((node) => node.isConnected)).toBe(true)
        await expect(refreshed).toBeEnabled()
        await expect(refreshed).toBeFocused()
        expect(await calls('account')).toHaveLength(2)
        await capture('account-retry-restores-focus', refreshed)
      }
    })

  test(`${surfaceName} source journal id7 saves the edited note and rating before deleting only its note`, async () => {
    const item = await seed('journal-vm')
    await openSessionEditor('details', item, true)
    await editor().getByRole('textbox', { name: 'Journal note', exact: true }).fill('Found the shortcut.')
    await chooseRating(4)
    await saveEditor()
    await expect(editor()).toHaveCount(0)
    expect((await state()).notes).toEqual([{ sessionId: 7, note: 'Found the shortcut.', rating: 4 }])
    await activate(details().getByRole('button', { name: /^(Edit|Edit note)$/, exact: true }))
    await activate(editor().getByRole('button', { name: 'Delete note', exact: true }))
    await activate(editor().getByRole('button', { name: 'Yes, delete note', exact: true }))
    await expect(editor()).toHaveCount(0)
    expect((await state()).notes).toEqual([])
    expect((await state()).sessions.map((session) => session.id)).toEqual([7])
    expect((await calls('delete')).map((call) => call.sessionId)).toEqual([7])
  })

  test(`${surfaceName} empty Details journal distinguishes enabled prompts from prompts that are off`, async () => {
    const item = await seed('empty')
    await api({ route: 'journal.preferences.put', body: { promptAfterPlay: true } })
    await openDetails(item.workId)
    await openJournal()
    await expect(details().getByText(/No notes yet/)).toBeVisible()
    await api({ route: 'journal.preferences.put', body: { promptAfterPlay: false } })
    await expect(details().getByText(/Journal prompts are off/)).toBeVisible()
    expect((await state()).notes).toEqual([])
    await capture('prompt-off-empty-journal', details())
  })

  test(`${surfaceName} recovered source sitting keeps one session and note while an unfinished sitting becomes ten minutes`, async () => {
    const item = await seed('recovered')
    const original = await state()
    expect(original.sessionCount).toBe(1)
    expect(original.notes).toEqual([
      { sessionId: item.sessionId, note: 'Same sitting, same note', rating: 4 },
    ])
    if (mode === 'desktop') {
      await openDetails(item.workId)
      await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
      await activate(details().getByRole('button', { name: 'Tracked sessions', exact: true }))
      await expect(details().locator('.activity-timeline-bar')).toHaveCount(0)
      await activate(details().getByRole('tab', { name: 'Journal', exact: true }))
      await expect(details().getByText('Same sitting, same note', { exact: true })).toBeVisible()
    } else {
      await enterHistory()
      await expect(events()).toHaveCount(1)
      await events().first().focus()
      await expect(events().first()).toHaveAttribute('aria-current', 'true')
      await expect(events().first()).toContainText('Duration not recorded')
    }
    await control('recover-complete', {})
    await expect.poll(async () => (await state()).sessions[0]?.durationSeconds).toBe(600)
    const completed = await state()
    expect(completed.sessionCount).toBe(1)
    expect(completed.noteCount).toBe(1)
    expect(completed.notes).toEqual(original.notes)
    expect(completed.sessions[0].id).toBe(item.sessionId)
    if (mode === 'desktop') {
      await activate(details().getByRole('tab', { name: 'Activity', exact: true }))
      await expect(details().locator('.activity-timeline-bar')).toHaveCount(1)
      await expect(details().locator('.activity-timeline-bar')).toHaveAccessibleName(/0\.17h.*Winnow session/)
      await capture('recovered-ten-minute-bar', details().locator('.activity-tracker'))
    } else {
      await expect(events()).toHaveCount(1)
      await expect(events().first()).toHaveAttribute('aria-current', 'true')
      await expect(events().first()).toContainText('10 min')
      await expect(events().first()).toContainText('Same sitting, same note')
      await capture('recovered-single-activity', events().first())
    }
  })

  test(`${surfaceName} actual notification IPC treats empty and zero native handles as unavailable without callbacks`, async () => {
    await page.evaluate(() => {
      ;(window as any).__notificationActivations = []
      window.winnow.onJournalNotificationActivated!((id) =>
        (window as any).__notificationActivations.push(id),
      )
    })
    for (const length of [0, 8]) {
      await app.evaluate(({ BrowserWindow }, length) => {
        const window = BrowserWindow.getAllWindows()[0]!
        window.isFocused = () => false
        window.getNativeWindowHandle = () => Buffer.alloc(length)
      }, length)
      expect(
        await page.evaluate(() => window.winnow.notifySessionEnded!({ sessionId: 7, title: 'Example' })),
      ).toBe(false)
    }
    expect(await page.evaluate(() => (window as any).__notificationActivations)).toEqual([])
    expect(await app.evaluate(() => (globalThis as any).__journalActivityNative.notifications)).toEqual([])
  })

  test(`${surfaceName} source Bluebird journal id42 retains its draft then saves and confirms note deletion`, async () => {
    const item = await seed('details')
    if (mode === 'desktop') await surface(1200, 640)
    await openDetails(item.workId)
    await openJournal()
    const edit = details().getByRole('button', { name: 'Edit note', exact: true })
    if (mode === 'desktop') {
      for (const action of [edit, details().getByRole('button', { name: 'Delete note', exact: true })]) {
        const padding = await action.evaluate((node) => ({
          horizontal: parseFloat(getComputedStyle(node).paddingRight),
          vertical: parseFloat(getComputedStyle(node).paddingTop),
        }))
        expect(padding).toEqual({ horizontal: 9, vertical: 4 })
      }
    }
    await activate(edit)
    const field = editor().getByRole('textbox', { name: 'Journal note', exact: true })
    await expect(field).toHaveValue('Looking for the key.')
    await field.fill('Found the key behind the waterfall.')
    if (mode === 'desktop') {
      await activate(details().getByRole('tab', { name: 'Library', exact: true }))
      await activate(details().getByRole('tab', { name: 'Journal', exact: true }))
      await expect(field).toHaveValue('Found the key behind the waterfall.')
    }
    await saveEditor()
    await expect(editor()).toHaveCount(0)
    await expect(details().getByText('Found the key behind the waterfall.', { exact: true })).toBeVisible()
    expect((await state()).notes).toEqual([
      { sessionId: 42, note: 'Found the key behind the waterfall.', rating: 3 },
    ])
    if (mode === 'desktop')
      await activate(details().getByRole('button', { name: 'Delete note', exact: true }))
    else {
      await activate(details().getByRole('button', { name: 'Edit note', exact: true }))
      await activate(editor().getByRole('button', { name: 'Delete note', exact: true }))
    }
    await expect(editor().getByRole('heading', { name: 'Delete this note?', exact: true })).toBeVisible()
    await capture(
      'source-journal-delete-confirmation',
      editor().getByRole('button', { name: 'Yes, delete note', exact: true }),
    )
    await activate(editor().getByRole('button', { name: 'Yes, delete note', exact: true }))
    await expect(editor()).toHaveCount(0)
    expect((await state()).notes).toEqual([])
    expect((await calls('delete')).map((call) => call.sessionId)).toEqual([42])
  })

  test(`${surfaceName} library reload preserves selected session2 and its new note before hiding Game2 removes them`, async () => {
    await seed('reload')
    await enterHistory()
    await expect(events()).toHaveCount(2)
    const second = events().last()
    await second.focus()
    await expect(second).toHaveAttribute('aria-current', 'true')
    await expect(second).toContainText('Game 2')
    await control('reload-note', {})
    await expect(second).toContainText('A newly saved note')
    await expect(second).toHaveAttribute('aria-current', 'true')
    await api({ route: 'hidden.put', body: { workIds: [2], hidden: true } })
    await expect(events()).toHaveCount(1)
    await expect(events().first()).toHaveAttribute('aria-current', 'true')
    await expect(activity().getByText('Game 2', { exact: true })).toHaveCount(0)
    await expect(activity().getByText('A newly saved note', { exact: true })).toHaveCount(0)
    expect((await state()).notes).toEqual([{ sessionId: 2, note: 'A newly saved note', rating: null }])
    await capture('hidden-selected-game-removed', events().first())
  })
}

for (const [surfaceName, context] of [
  ['desktop', 'details'],
  ['fullscreen', 'details'],
  ['fullscreen', 'activity'],
] as const) {
  for (const existing of [false, true])
    test(`${surfaceName} source ${context} editor ${existing ? 'existing' : 'empty'} id7 rejects whitespace without writing then trims the note and saves rating5`, async () => {
      const item = await seed(existing ? 'editor-existing' : 'editor-empty')
      await openSessionEditor(context, item, existing)
      const field = editor().getByRole('textbox', { name: 'Journal note', exact: true })
      const rating = ratingValue()
      await expect(rating).toHaveValue(existing ? '4' : '0')
      await field.fill('  \r\n ')
      await chooseRating(0)
      await saveEditor()
      await expect(
        editor().getByText('Add a note or rating, or delete this entry.', { exact: true }),
      ).toBeVisible()
      expect(await calls('write')).toHaveLength(0)
      await expect(editor()).toBeVisible()
      await field.fill('  Remember the other route. \r\n ')
      await chooseRating(5)
      await saveEditor()
      await expect(editor()).toHaveCount(0)
      expect((await state()).notes).toEqual([{ sessionId: 7, note: 'Remember the other route.', rating: 5 }])
      expect(await calls('write')).toHaveLength(1)
    })

  test(`${surfaceName} source ${context} editor keeps its failed draft and rating4 while a held retry blocks conflicting input`, async () => {
    const item = await seed('editor-existing')
    await openSessionEditor(context, item, true)
    const field = editor().getByRole('textbox', { name: 'Journal note', exact: true })
    await field.fill('  Keep my draft.  ')
    const failure = await arm('write', 'any', 'fail')
    await saveEditor()
    await entered(failure)
    await expect(editor().getByRole('alert')).toBeVisible()
    await expect(editor().getByRole('alert')).toHaveText(
      "Couldn't save that. Your changes are still here — try again.",
    )
    await expect(field).toHaveValue('  Keep my draft.  ')
    await expect(ratingValue()).toHaveValue('4')
    expect((await state()).notes).toEqual([{ sessionId: 7, note: 'Original note', rating: 4 }])
    const held = await arm('write')
    await saveEditor()
    await entered(held)
    await expect(field).toBeDisabled()
    if (await editor().locator('select[name="rating"]:visible').count())
      await expect(editor().locator('select[name="rating"]')).toBeDisabled()
    for (const button of await editor().getByRole('button').all())
      if (await button.isVisible()) await expect(button).toBeDisabled()
    await page.keyboard.press('Escape')
    if (mode === 'fullscreen') await tap(1)
    await page.keyboard.press('Enter')
    await expect(editor()).toBeVisible()
    await expect(field).toHaveValue('  Keep my draft.  ')
    expect(await calls('write')).toHaveLength(2)
    await capture(`${context}-held-journal-retry`, editor())
    await released(held)
    await expect(editor()).toHaveCount(0)
    expect((await state()).notes).toEqual([{ sessionId: 7, note: 'Keep my draft.', rating: 4 }])
    expect(await calls('write')).toHaveLength(2)
  })
}

test('fullscreen source week navigation serializes two rapid Left inputs and rejects an obsolete ignored read', async () => {
  await seed('week-race')
  const held = await arm('activity', 'first')
  await enterHistory()
  await entered(held)
  const first = (await calls('activity'))[0]
  await activity().focus()
  await tap(14)
  await tap(14)
  expect(await calls('activity')).toHaveLength(1)
  await released(held)
  await expect.poll(async () => (await calls('activity')).length).toBe(2)
  await expect(events()).toHaveCount(1)
  const requests = await calls('activity')
  const difference = await page.evaluate(
    ({ first, last }) => {
      const expected = new Date(first)
      expected.setDate(expected.getDate() - 14)
      return new Date(last).getTime() - expected.getTime()
    },
    { first: first.from, last: requests[1].from },
  )
  expect(difference).toBe(0)
  expect(Date.parse(requests[1].until) - Date.parse(requests[1].from)).toBe(7 * 86400000)
  await events().first().focus()
  await tap(2)
  await expect(editor()).toBeVisible()
  const reads = await api<{ sessionId: number }>({ route: 'journal.get', params: { sessionId: 2 } })
  expect(reads.sessionId).toBe(2)
  // The editor revision is tied to the selected row, so an actual save proves the late id1 never won.
  await editor().getByRole('textbox', { name: 'Journal note', exact: true }).fill('Latest week only.')
  await saveEditor()
  await expect(editor()).toHaveCount(0)
  expect((await state()).notes).toEqual([{ sessionId: 2, note: 'Latest week only.', rating: null }])
  expect(await calls('activity')).toHaveLength(2)
  await test
    .info()
    .attach('source-week-requests', { body: JSON.stringify(requests), contentType: 'application/json' })
})

test('fullscreen source empty sections clamp week navigation and triggers cycle Journal and Sessions outside root pages', async () => {
  await seed('empty')
  await enterHistory()
  await expect(activity().getByText('No sessions this week', { exact: true })).toBeVisible()
  // React has no separate right-hint slot; the empty local page advertises no trigger actions.
  await expect(activity()).not.toContainText(/\b(?:LT|RT)\b/)
  await activity().focus()
  await tap(14)
  await expect(activity().getByRole('button', { name: 'Next week', exact: true })).toBeEnabled()
  await tap(15)
  await tap(15)
  await expect(activity().getByRole('button', { name: 'Next week', exact: true })).toBeDisabled()
  const type = activity().getByRole('navigation', { name: 'Activity type' })
  await activity().focus()
  await tap(12)
  await expect(type.getByRole('button', { name: 'Sessions', exact: true })).toBeFocused()
  const count = (await calls('activity')).length
  await tap(15)
  expect(await calls('activity')).toHaveLength(count)
  await expect(activity().getByRole('button', { name: 'Next week', exact: true })).toBeDisabled()
  await activate(type.getByRole('button', { name: 'Journal', exact: true }))
  await expect(activity().getByText('No journal entries this week', { exact: true })).toBeVisible()
  await expect(activity().getByText('No sessions this week', { exact: true })).toHaveCount(0)
  await tap(7)
  await expect(type.getByRole('button', { name: 'Sessions', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(activity().getByText('No sessions this week', { exact: true })).toBeVisible()
  await tap(6)
  await expect(type.getByRole('button', { name: 'Journal', exact: true })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(activity().getByText('No journal entries this week', { exact: true })).toBeVisible()
  await expect(activity().getByRole('button', { name: 'Next week', exact: true })).toBeDisabled()
  await capture('empty-journal-week-controller')
})

test('fullscreen source existing Activity note saves the other-route text while retaining rating4', async () => {
  const item = await seed('fullscreen-existing')
  await openSessionEditor('activity', item, true)
  await editor()
    .getByRole('textbox', { name: 'Journal note', exact: true })
    .fill('Try the other route next time.')
  await saveEditor()
  await expect(editor()).toHaveCount(0)
  expect((await state()).notes).toEqual([
    { sessionId: item.sessionId, note: 'Try the other route next time.', rating: 4 },
  ])
})

for (const width of [2560, 1280])
  test(`fullscreen source ${width}px journal groups retain long session identity and controller access to note rating and Save`, async () => {
    await surface(width, (width * 9) / 16)
    await seed('hierarchy')
    await navigate('Library')
    await expect(
      page.getByText("The Long Journey Home — Definitive Collector's Edition", { exact: true }).first(),
    ).toBeVisible()
    await control('publish-ended', {})
    const prompt = page.locator('.session-prompt')
    await expect(prompt).toBeVisible()
    await expect(
      prompt.getByRole('heading', {
        name: "The Long Journey Home — Definitive Collector's Edition",
        exact: true,
      }),
    ).toBeVisible()
    await expect(prompt.getByText('2h 10m', { exact: true })).toBeVisible()
    await expect(prompt.getByRole('button', { name: 'Edit note', exact: true })).toBeVisible()
    await expect(prompt.getByRole('button', { name: 'Edit note', exact: true })).toBeEnabled()
    // The original explicitly calls FocusInitial; passive Electron prompts enter on the first Tab.
    await page.keyboard.press('Tab')
    await expect(prompt.getByRole('button', { name: 'Edit note', exact: true })).toBeFocused()
    await prompt
      .getByRole('textbox', { name: 'Journal note', exact: true })
      .fill('Return to the mountain camp before exploring the next valley.')
    await expect(prompt.getByRole('textbox', { name: 'Journal note', exact: true })).toHaveValue(
      'Return to the mountain camp before exploring the next valley.',
    )
    await expect(prompt.locator('.journal-prompt-group')).toHaveCount(3)
    for (const title of ['YOUR LAST SESSION', 'JOURNAL', 'RATING'])
      await expect(prompt.getByRole('heading', { name: title, exact: true })).toBeVisible()
    await expectFullscreenJournalTypography(prompt, 1)
    await page.keyboard.press('Tab')
    await expect(prompt.getByRole('button', { name: 'Edit note', exact: true })).toBeFocused()
    await tap(13)
    await test.info().attach('source-hierarchy-first-down-target', {
      body: await page.evaluate(() => document.activeElement?.outerHTML ?? 'No focused element'),
      contentType: 'text/plain',
    })
    await expect(prompt.getByRole('button', { name: '1 out of 5', exact: true })).toBeFocused()
    await tap(13)
    const save = prompt.getByRole('button', { name: 'Save', exact: true })
    await expect(save).toBeFocused()
    await expect(save).toBeInViewport()
    const visibleBounds = await save.evaluate((node) => {
      const button = node.getBoundingClientRect()
      let top = 0,
        bottom = innerHeight
      for (let parent = node.parentElement; parent; parent = parent.parentElement) {
        if (/(auto|scroll|hidden|clip)/.test(getComputedStyle(parent).overflowY)) {
          const rectangle = parent.getBoundingClientRect()
          top = Math.max(top, rectangle.top)
          bottom = Math.min(bottom, rectangle.bottom)
        }
      }
      return { top, bottom, buttonTop: button.top, buttonBottom: button.bottom }
    })
    expect(visibleBounds.buttonTop).toBeGreaterThanOrEqual(visibleBounds.top - 1)
    expect(visibleBounds.buttonBottom).toBeLessThanOrEqual(visibleBounds.bottom + 1)
    await test.info().attach('source-hierarchy-save-viewport', {
      body: JSON.stringify(visibleBounds),
      contentType: 'application/json',
    })
    const bounds = await prompt.locator(':scope > div').evaluate((node) => ({
      width: node.clientWidth,
      scrollWidth: node.scrollWidth,
    }))
    expect(bounds.scrollWidth).toBeLessThanOrEqual(bounds.width + 1)
    await expect(prompt.locator('.journal-controller-hints')).toContainText('Y Keyboard')
    await capture(`source-journal-hierarchy-${width}-save`, save)
    await prompt.getByRole('button', { name: 'Edit note', exact: true }).focus()
    await capture(`source-journal-hierarchy-${width}-edit`)
    expect((await state()).notes).toEqual([])
  })
