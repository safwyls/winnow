import { describe, expect, it } from 'vitest'
import { noActionSentence, primaryAction, primaryEntry } from '../src/shared/game-actions'
import type { GameEntry, Workspace } from '../src/renderer/api/types'

const entry: GameEntry = {
  ownershipId: 1,
  releaseId: 10,
  workId: 1,
  title: 'Bluebird',
  store: 'steam',
  installed: true,
  playtimeMinutes: 0,
}
const workspace: Workspace = {
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
  works: [],
  externalIds: [
    { releaseId: 10, provider: 'steam', providerId: '480' },
    { releaseId: 10, provider: 'gog', providerId: '1207658924' },
    { releaseId: 10, provider: 'epic', providerId: 'Bluebird' },
  ],
  epicLaunchKeys: {
    Bluebird: { namespace: 'example_ns', catalogItemId: 'catalog-id', artifactId: 'Bluebird' },
  },
  pluginActions: {},
}
describe('store action presentation migrated from original tile policies', () => {
  it.each(['steam', 'gog', 'epic'])(
    '%s plays on disk, installs off disk and never labels an unknown state either way',
    (store) => {
      expect(primaryAction({ ...entry, store }, workspace)).toBe('Play')
      expect(primaryAction({ ...entry, store, installed: false }, workspace)).toBe('Install')
      expect(primaryAction({ ...entry, store, installed: null }, workspace)).toBeNull()
    },
  )
  it.each([null, '', 'x/y', 'x:y', 'has space', '%2F', 'a'.repeat(65), 'é', 'x\n'])(
    'refuses malformed or absent Epic key parts: %s',
    (value) => {
      for (const part of ['namespace', 'catalogItemId', 'artifactId']) {
        const invalid = {
          ...workspace,
          epicLaunchKeys: { Bluebird: { ...workspace.epicLaunchKeys.Bluebird, [part]: value } },
        } as Workspace
        expect(primaryAction({ ...entry, store: 'epic' }, invalid)).toBeNull()
        expect(primaryAction({ ...entry, store: 'epic', installed: false }, invalid)).toBeNull()
      }
    },
  )
  it.each(['', '12/34', '12?cmd=x', '1234567890123', 'abc', ' 12', '12\n'])(
    'refuses malformed GOG ids: %s',
    (providerId) => {
      expect(
        primaryAction(
          { ...entry, store: 'gog' },
          { ...workspace, externalIds: [{ releaseId: 10, provider: 'gog', providerId }] },
        ),
      ).toBeNull()
    },
  )
  it('names missing Epic identity without pretending its install state is known', () => {
    const missing = { ...workspace, epicLaunchKeys: {} }
    expect(noActionSentence({ ...entry, store: 'epic' }, missing)).toContain('identifier')
    expect(noActionSentence({ ...entry, store: 'epic', installed: false }, missing)).toContain('identifier')
    expect(noActionSentence({ ...entry, store: 'epic', installed: null }, workspace)).toContain(
      'install state',
    )
  })
  it.each(['steam', 'gog', 'epic'])('stays quiet when %s already has a way in', (store) => {
    expect(noActionSentence({ ...entry, store }, workspace)).toBeNull()
    expect(noActionSentence({ ...entry, store, installed: false }, workspace)).toBeNull()
  })
  it('preserves browsing as a way in even when Steam or GOG install state is unknown', () => {
    for (const store of ['steam', 'gog'])
      expect(noActionSentence({ ...entry, store, installed: null }, workspace)).toBeNull()
  })
  it('selects a viable uninstalled copy ahead of an installed copy without a usable identity', () => {
    const offline = { ...entry, ownershipId: 2, releaseId: 20, store: 'gog' }
    const reachable = { ...entry, installed: false }
    expect(primaryEntry([offline, reachable], workspace)).toBe(reachable)
    expect(primaryEntry([offline], workspace)).toBeUndefined()
  })
  it('prefers an installed viable copy while retaining plugin action support', () => {
    const installed = { ...entry, store: 'gog' }
    expect(primaryEntry([{ ...entry, installed: false }, installed], workspace)).toBe(installed)
    expect(
      primaryAction(
        { ...entry, store: 'plugin:xbox' },
        { ...workspace, pluginActions: { 1: { canPlay: true, canOpenStore: false } } },
      ),
    ).toBe('Play')
  })
  it('prefers a viable uninstalled copy to an installed offline copy and preserves manual launch', () => {
    const waiting = { ...entry, ownershipId: 2, installed: false }
    const observed = {
      ...workspace,
      buckets: [
        {
          ownershipId: 1,
          lifecycle: { status: 5, confidence: 0.98, reason: 'IGDB reports offline status.' },
        },
      ],
    }
    expect(primaryEntry([entry, waiting], observed)).toBe(waiting)
    expect(primaryEntry([entry], observed)).toBe(entry)
    expect(primaryAction(entry, observed)).toBe('Play')
  })
  it('honors a saved Derelict exemption when choosing the installed copy', () => {
    const observed = {
      ...workspace,
      buckets: [
        {
          ownershipId: 1,
          lifecycle: {
            status: 6,
            confidence: 0.99,
            reason: 'Store listing removed.',
            isExemptFromDerelict: true,
          },
        },
      ],
    }
    expect(primaryEntry([entry, { ...entry, ownershipId: 2, installed: false }], observed)).toBe(entry)
  })
})
