import { describe, expect, it, vi } from 'vitest'
import { ApiError, createClientId, launchMessage, primaryAction, request } from '../src/renderer/api/client'
import { feedSchema, librarySchema, workspaceSchema } from '../src/renderer/api/hooks'
import type { GameEntry, Workspace } from '../src/renderer/api/types'
import { readEpicCallback, type EpicChallenge } from '../src/renderer/api/auth'

const entry: GameEntry = {
  ownershipId: 1,
  releaseId: 2,
  workId: 3,
  title: 'A game',
  store: 'steam',
  installed: true,
  playtimeMinutes: 40,
}
const workspace = {
  externalIds: [{ releaseId: 2, provider: 'steam', providerId: '123' }],
  pluginActions: {},
  epicLaunchKeys: {},
  works: [],
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
} satisfies Workspace
describe('frontend API contracts', () => {
  it('requires the workspace domain work name instead of a projected library title', () => {
    expect(
      workspaceSchema.parse({ ...workspace, works: [{ id: 3, name: 'A game', igdbId: 4 }] }).works[0].name,
    ).toBe('A game')
    expect(() => workspaceSchema.parse({ ...workspace, works: [{ id: 3, title: 'A game' }] })).toThrow()
  })
  it('keeps numeric feed confidence and unknown additive fields while rejecting malformed identity', () => {
    expect(
      feedSchema.parse({ shelves: [], candidateCount: 0, confidence: 2, failed: false }).confidence,
    ).toBe(2)
    expect(() => librarySchema.parse({ games: [{ workId: '3' }], lists: [] })).toThrow()
    const result = librarySchema.parse({
      games: [
        {
          workId: 3,
          title: 'A game',
          bucket: 'unplayed',
          playtimeMinutes: 0,
          futureField: true,
          entries: [entry],
        },
      ],
      lists: [],
    })
    expect(result.games[0]?.futureField).toBe(true)
  })
  it('only offers launch actions supported by public store facts', () => {
    expect(primaryAction(entry)).toBeNull()
    expect(primaryAction(entry, workspace)).toBe('Play')
    expect(primaryAction({ ...entry, installed: false }, workspace)).toBe('Install')
    expect(primaryAction({ ...entry, store: 'manual' }, workspace)).toBeNull()
    expect(primaryAction(entry, { ...workspace, externalIds: [] })).toBeNull()
    expect(
      primaryAction(
        { ...entry, store: 'plugin:itch' },
        { ...workspace, pluginActions: { '1': { canPlay: true, canOpenStore: false } } },
      ),
    ).toBe('Play')
  })
  it('reports handoff without claiming the game is running', () => {
    expect(launchMessage(0)).toContain('Sent to the launcher')
    expect(launchMessage(2)).toContain('could not accept')
  })
  it('creates distinct Guid N client identifiers accepted by the backend string-ID contract', () => {
    const first = createClientId()
    const second = createClientId()
    expect(first).toMatch(/^[0-9a-f]{32}$/)
    expect(second).not.toBe(first)
  })
  it('does not retry a conflict or hide its current state', async () => {
    const bridgeRequest = vi
      .fn()
      .mockResolvedValue({ ok: false, status: 409, message: 'Revision changed', data: { revision: 'new' } })
    vi.stubGlobal('window', { winnow: { request: bridgeRequest } })
    try {
      await request('journal.put', { sessionId: 1 }, { expectedRevision: 'old' })
      expect.fail('Expected conflict')
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError)
      expect((error as ApiError).conflict).toBe(true)
      expect((error as ApiError).current).toEqual({ revision: 'new' })
    }
    expect(bridgeRequest).toHaveBeenCalledTimes(1)
    vi.unstubAllGlobals()
  })
  it('binds a pasted Epic callback to this attempt and rejects unrelated origins, state, and expired challenges', () => {
    const challenge: EpicChallenge = {
      attemptId: 'a',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      request: {
        startUrl: 'https://www.epicgames.com/id/login',
        consentNotice: 'Connect',
        redirectUrl: 'https://localhost/callback',
        redirectCodeParameter: 'code',
        expectedState: 'state-one',
        stateParameter: 'state',
      },
    }
    expect(readEpicCallback('https://localhost/callback?code=abc&state=state-one', challenge)).toEqual({
      code: 'abc',
      state: 'state-one',
      kind: 0,
    })
    expect(() => readEpicCallback('https://localhost/callback?code=abc&state=other', challenge)).toThrow(
      'different sign-in',
    )
    expect(() =>
      readEpicCallback('https://example.com/callback?code=abc&state=state-one', challenge),
    ).toThrow('not the callback')
    expect(() => readEpicCallback('abc', challenge)).toThrow('complete address')
    expect(() =>
      readEpicCallback('https://localhost/callback?code=abc&state=state-one', {
        ...challenge,
        expiresAt: '2000-01-01T00:00:00Z',
      }),
    ).toThrow('expired')
  })
})
