import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { beforeAll, describe, expect, it } from 'vitest'
import { librarySchema, feedSchema, workspaceSchema } from '../src/renderer/api/hooks'
import type { ActivityPage, GameList, JournalResponse, ManualGame, Metadata } from '../src/renderer/api/types'
import { createClientId } from '../src/renderer/api/client'
import { journalPeriod } from '../src/renderer/api/journalPeriod'

// Opt in with a throwaway backend; this suite creates and removes test entries.
const dataDir = process.env.WINNOW_TEST_DATA_DIR
describe.skipIf(!dataDir)('live frontend API on an explicitly supplied test library', () => {
  let address: string
  let token: string
  beforeAll(async () => {
    if (!dataDir || !/[\\/]winnow-electron-[^\\/]*$/i.test(dataDir))
      throw new Error('Use an explicit throwaway winnow-electron-* data directory.')
    const endpoint = JSON.parse(await readFile(join(dataDir, 'backend', 'endpoint.json'), 'utf8'))
    const origin = new URL(endpoint.address)
    if (
      endpoint.apiVersion !== '1' ||
      origin.protocol !== 'http:' ||
      origin.hostname !== '127.0.0.1' ||
      origin.username ||
      origin.password ||
      origin.pathname !== '/' ||
      origin.search ||
      origin.hash
    )
      throw new Error('Unsupported test backend discovery.')
    address = origin.href
    token = endpoint.token
  })
  async function api<T = unknown>(path: string, method = 'GET', body?: unknown, status = 200): Promise<T> {
    const response = await fetch(new URL(`/api/v1/${path}`, address), {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      redirect: 'error',
    })
    expect(response.status, `${method} ${path}`).toBe(status)
    const content = await response.text()
    return (content ? JSON.parse(content) : undefined) as T
  }
  it('accepts Journal activity and gameplay ranges from a fractional current timestamp', async () => {
    const now = new Date()
    now.setUTCMilliseconds(987)
    const bounds = journalPeriod(30, now)
    const statistics = await api<{ recordedSeconds: number }>('statistics/gameplay', 'POST', {
      ...bounds,
      asOfUtc: bounds.untilUtc,
      timeBins: [bounds],
    })
    expect(statistics.recordedSeconds).toBeGreaterThanOrEqual(0)
    const activity = await api<ActivityPage>('activity/query', 'POST', {
      ...bounds,
      section: 0,
      pageSize: 10,
    })
    expect(activity.rows).toBeInstanceOf(Array)
  })
  it('matches actual library, feed, workspace, detail and redacted settings contracts', async () => {
    const library = librarySchema.parse(await api('library'))
    feedSchema.parse(await api('feed'))
    const workspace = workspaceSchema.parse(await api('library/workspace'))
    for (const field of ['works', 'externalIds', 'pluginActions', 'epicLaunchKeys'])
      expect(workspace).toHaveProperty(field)
    if (library.games[0]) {
      const details = await api<Record<string, unknown>>(`games/${library.games[0].workId}/details`)
      for (const field of ['events', 'sessions', 'ratings', 'journalEntries', 'achievements'])
        expect(details).toHaveProperty(field)
    }
    expect(await api('connections/stores')).toHaveProperty('steam')
    expect(await api('connections/igdb')).toHaveProperty('hasSavedCredentials')
    expect(await api('connections/plugins')).toBeInstanceOf(Array)
    expect(await api('preferences/library')).toHaveProperty('showExplicitContent')
  }, 30_000)
  it('creates and edits a list, rejects stale revisions, and removes its test list', async () => {
    const created = await api<{ id: number; revision: string }>('lists', 'POST', {
      name: `Electron API test ${crypto.randomUUID()}`,
      releaseIds: [],
    })
    let revision = created.revision
    try {
      const edited = await api<{ revision: string; name: string }>(`lists/${created.id}`, 'PUT', {
        name: 'Electron API test updated',
        description: 'Temporary integration verification',
        expectedRevision: revision,
      })
      expect(edited.name).toBe('Electron API test updated')
      expect(edited.revision).not.toBe(revision)
      revision = edited.revision
      await api(
        `lists/${created.id}`,
        'PUT',
        { name: 'Stale edit must fail', expectedRevision: created.revision },
        409,
      )
    } finally {
      await api(`lists/${created.id}`, 'DELETE', { expectedRevision: revision }, 204)
    }
  })

  it('persists the original manual list order, duplicate-add no-op, move and removal across fresh reads', async () => {
    const entries: ManualGame[] = []
    let list: GameList | undefined
    try {
      for (const title of ['Hades', 'Celeste', 'Tunic'])
        entries.push(await api<ManualGame>('manual-games', 'POST', { title }))
      const [hades, celeste, tunic] = entries.map((entry) => entry.releaseId)
      list = await api<GameList>('lists', 'POST', {
        name: 'Friday night',
        releaseIds: [hades, celeste, tunic],
      })
      expect(list.releaseIds).toEqual([hades, celeste, tunic])
      list = await api<GameList>(`lists/${list.id}/members`, 'POST', {
        releaseIds: [hades],
        expectedRevision: list.revision,
      })
      expect(list.releaseIds).toEqual([hades, celeste, tunic])
      list = await api<GameList>(`lists/${list.id}/order`, 'PUT', {
        releaseIds: [hades, tunic, celeste],
        expectedRevision: list.revision,
      })
      list = await api<GameList>(`lists/${list.id}/members`, 'DELETE', {
        releaseIds: [celeste],
        expectedRevision: list.revision,
      })
      expect(list.releaseIds).toEqual([hades, tunic])
      const reloaded = librarySchema.parse(await api('library'))
      expect(reloaded.lists.find((saved) => saved.id === list!.id)?.releaseIds).toEqual([hades, tunic])
      entries.push(await api<ManualGame>('manual-games', 'POST', { title: 'Dead Cells' }))
      const expanded = librarySchema.parse(await api('library'))
      expect(expanded.games.some((game) => game.workId === entries[3].workId)).toBe(true)
      expect(expanded.lists.find((saved) => saved.id === list!.id)?.releaseIds).toEqual([hades, tunic])
    } finally {
      if (list) await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      for (const entry of entries) await api(`manual-games/${entry.ownershipId}`, 'DELETE', undefined, 204)
    }
  })

  it('starts a footer list empty, persists membership ticks and deletes only the list after rename', async () => {
    const entry = await api<ManualGame>('manual-games', 'POST', { title: 'Hades' })
    let list: GameList | undefined
    try {
      list = await api<GameList>('lists', 'POST', { name: 'Friday night', releaseIds: [] })
      expect(list.isLive).toBe(false)
      expect(list.releaseIds).toEqual([])
      list = await api<GameList>(`lists/${list.id}/members`, 'POST', {
        releaseIds: [entry.releaseId],
        expectedRevision: list.revision,
      })
      expect(
        librarySchema.parse(await api('library')).lists.find((saved) => saved.id === list!.id)?.releaseIds,
      ).toEqual([entry.releaseId])
      list = await api<GameList>(`lists/${list.id}/members`, 'DELETE', {
        releaseIds: [entry.releaseId],
        expectedRevision: list.revision,
      })
      expect(
        librarySchema.parse(await api('library')).lists.find((saved) => saved.id === list!.id)?.releaseIds,
      ).toEqual([])
      list = await api<GameList>(`lists/${list.id}`, 'PUT', {
        name: 'Couch co-op night',
        expectedRevision: list.revision,
      })
      expect(
        librarySchema.parse(await api('library')).lists.find((saved) => saved.id === list!.id)?.name,
      ).toBe('Couch co-op night')
      const deletedId = list.id
      await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      list = undefined
      const reloaded = librarySchema.parse(await api('library'))
      expect(reloaded.lists.some((saved) => saved.id === deletedId)).toBe(false)
      expect(reloaded.games.some((game) => game.workId === entry.workId)).toBe(true)
    } finally {
      if (list) await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      await api(`manual-games/${entry.ownershipId}`, 'DELETE', undefined, 204)
    }
  })

  it('persists live rules independently of a later matching title and accepts a revision-checked replacement cut', async () => {
    const entries: ManualGame[] = []
    let list: GameList | undefined
    const filter = { stores: ['manual'], search: 'Hades', installed: false }
    try {
      entries.push(await api<ManualGame>('manual-games', 'POST', { title: 'Hades' }))
      list = await api<GameList>('lists/live', 'POST', { name: 'My Hades games', filter })
      expect(list.isLive).toBe(true)
      expect(list.filter).toMatchObject(filter)
      entries.push(await api<ManualGame>('manual-games', 'POST', { title: 'Hades II' }))
      const reloaded = librarySchema.parse(await api('library'))
      expect(reloaded.lists.find((saved) => saved.id === list!.id)?.filter).toMatchObject(filter)
      expect(
        reloaded.games.filter((game) => entries.some((entry) => entry.workId === game.workId)),
      ).toHaveLength(2)
      list = await api<GameList>(`lists/${list.id}/filter`, 'PUT', {
        filter: { stores: ['manual'], search: 'Hades II' },
        expectedRevision: list.revision,
      })
      expect(
        librarySchema.parse(await api('library')).lists.find((saved) => saved.id === list!.id)?.filter,
      ).toMatchObject({ stores: ['manual'], search: 'Hades II' })
    } finally {
      if (list) await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      for (const entry of entries) await api(`manual-games/${entry.ownershipId}`, 'DELETE', undefined, 204)
    }
  })
  it('excludes the original Hades Soundtrack from visible membership while retaining both stored list entries', async () => {
    const entries: ManualGame[] = []
    let list: GameList | undefined
    try {
      for (const title of ['Hades', 'Hades Soundtrack'])
        entries.push(await api<ManualGame>('manual-games', 'POST', { title }))
      const database = new DatabaseSync(join(dataDir!, 'winnow.db'))
      try {
        database.prepare('UPDATE works SET steam_app_type = ? WHERE id = ?').run('music', entries[1].workId)
      } finally {
        database.close()
      }
      list = await api<GameList>('lists', 'POST', {
        name: 'Friday night',
        releaseIds: entries.map((entry) => entry.releaseId),
      })
      const reloaded = librarySchema.parse(await api('library'))
      const stored = reloaded.lists.find((saved) => saved.id === list!.id)!
      expect(stored.releaseIds).toHaveLength(2)
      expect(
        reloaded.games
          .filter((game) => game.entries.some((entry) => stored.releaseIds.includes(entry.releaseId)))
          .map((game) => game.title),
      ).toEqual(['Hades'])
    } finally {
      if (list) await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      for (const entry of entries) await api(`manual-games/${entry.ownershipId}`, 'DELETE', undefined, 204)
    }
  })
  it('persists the original Aardvark and Zebra names after renaming Middle', async () => {
    const entry = await api<ManualGame>('manual-games', 'POST', { title: 'Hades' })
    const saved: GameList[] = []
    try {
      for (const name of ['Zebra', 'Middle'])
        saved.push(await api<GameList>('lists', 'POST', { name, releaseIds: [entry.releaseId] }))
      saved[1] = await api<GameList>(`lists/${saved[1].id}`, 'PUT', {
        name: 'Aardvark',
        expectedRevision: saved[1].revision,
      })
      expect(
        librarySchema
          .parse(await api('library'))
          .lists.filter((list) => saved.some((value) => value.id === list.id))
          .map((list) => list.name),
      ).toEqual(expect.arrayContaining(['Aardvark', 'Zebra']))
    } finally {
      for (const list of saved)
        await api(`lists/${list.id}`, 'DELETE', { expectedRevision: list.revision }, 204)
      await api(`manual-games/${entry.ownershipId}`, 'DELETE', undefined, 204)
    }
  })
  it('accepts frontend string IDs for an Epic challenge and cancels without authenticating an account', async () => {
    const clientId = createClientId()
    const challenge = await api<{ attemptId: string }>('connections/stores/epic/sign-in', 'POST', {
      clientId,
    })
    expect(challenge.attemptId).toBeTruthy()
    await api('connections/stores/sign-in/cancel', 'POST', { clientId, attemptId: challenge.attemptId }, 204)
  })
  it('accepts a metadata operation ID without connecting accounts', async (context) => {
    const connection = await api<{ hasSavedCredentials: boolean; hasConfigurationCredentials: boolean }>(
      'connections/igdb',
    )
    if (connection.hasSavedCredentials || connection.hasConfigurationCredentials) {
      context.skip()
      return
    }
    const operationId = createClientId()
    const operation = await api<{ id: string }>('operations/metadata-sync', 'POST', { operationId })
    expect(operation.id).toBe(operationId)
    await api(`operations/${operationId}/cancel`, 'POST', undefined, 204)
  })
  it('creates, updates and removes a manual game and edits metadata with its observed revision', async () => {
    const game = await api<ManualGame>('manual-games', 'POST', {
      title: `Electron API manual test ${crypto.randomUUID()}`,
      firstReleaseYear: 2025,
      platformLabel: 'Test platform',
    })
    try {
      const edited = await api<ManualGame>(`manual-games/${game.ownershipId}`, 'PUT', {
        title: `${game.title} updated`,
        firstReleaseYear: 2026,
        platformLabel: 'Test platform',
        expectedRevision: game.revision,
        expectedIgdbMappingRevision: game.igdbMappingRevision,
      })
      expect(edited.firstReleaseYear).toBe(2026)
      expect(edited.revision).not.toBe(game.revision)
      await api(
        `manual-games/${game.ownershipId}`,
        'PUT',
        {
          title: 'Stale edit must fail',
          expectedRevision: game.revision,
          expectedIgdbMappingRevision: game.igdbMappingRevision,
        },
        409,
      )
      const metadata = await api<Metadata>(`games/${game.workId}/metadata`)
      expect(metadata.fields.some((field) => field.field === 'summary')).toBe(true)
      const outcome = await api<{ outcome: string }>(`games/${game.workId}/metadata`, 'PUT', {
        field: 'summary',
        value: 'Temporary integration summary',
        expectedRevision: metadata.revision,
      })
      expect(outcome.outcome).toBe('Applied')
      const reread = await api<Metadata>(`games/${game.workId}/metadata`)
      expect(reread.fields.find((field) => field.field === 'summary')?.value).toBe(
        'Temporary integration summary',
      )
      await api(
        `games/${game.workId}/metadata`,
        'PUT',
        { field: 'summary', value: 'Stale metadata must fail', expectedRevision: metadata.revision },
        409,
      )
    } finally {
      await api(`manual-games/${game.ownershipId}`, 'DELETE', undefined, 204)
    }
  })
  it('writes and restores a sample session journal while rejecting stale revisions', async (context) => {
    const activity = await api<ActivityPage>('activity/query', 'POST', {
      fromUtc: '2000-01-01T00:00:00Z',
      untilUtc: new Date().toISOString(),
      section: 0,
      pageSize: 10,
    })
    const session = activity.rows.find((row) => row.session)?.session
    if (!session) {
      context.skip()
      return
    }
    const before = await api<JournalResponse>(`sessions/${session.id}/journal`)
    let current = before
    try {
      current = await api<JournalResponse>(`sessions/${session.id}/journal`, 'PUT', {
        note: 'Temporary Electron journal concurrency check',
        rating: 4,
        expectedRevision: before.revision,
      })
      expect(current.note).toBe('Temporary Electron journal concurrency check')
      await api(
        `sessions/${session.id}/journal`,
        'PUT',
        { note: 'Stale draft must fail', rating: 1, expectedRevision: before.revision },
        409,
      )
    } finally {
      if (before.note == null && before.rating == null)
        await api(`sessions/${session.id}/journal`, 'DELETE', { expectedRevision: current.revision }, 204)
      else
        await api(`sessions/${session.id}/journal`, 'PUT', {
          note: before.note,
          rating: before.rating,
          expectedRevision: current.revision,
        })
    }
  })
})
