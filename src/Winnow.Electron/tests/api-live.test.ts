import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { librarySchema, feedSchema, workspaceSchema } from '../src/renderer/api/hooks'
import type { ActivityPage, JournalResponse, ManualGame, Metadata } from '../src/renderer/api/types'
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
