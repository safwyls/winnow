// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { EntryActions } from '../src/renderer/features/Details'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameEntry, Workspace } from '../src/renderer/api/types'

afterEach(cleanup)
const label = 'Played history — not proof of ownership'
const entry: GameEntry = {
  ownershipId: 41,
  releaseId: 7,
  workId: 3,
  title: 'Xbox fixture',
  store: 'plugin:xbox',
  installed: true,
  playtimeMinutes: 0,
}
function workspace(canPlay: boolean, canOpenStore: boolean): Workspace {
  return {
    works: [{ id: 3, name: 'Xbox fixture' }],
    externalIds: [{ releaseId: 7, provider: 'plugin:xbox', providerId: 'stable-package' }],
    preferences: { showExplicitContent: false, showNonGameEntries: false, maturityCap: 'Mature' },
    epicLaunchKeys: {},
    pluginActions: { 41: { sourceLabel: label, canPlay, canOpenStore } },
  }
}

it('plugin inclusion evidence remains when uninstall and unload remove executable actions', () => {
  const view = render(<EntryActions entry={entry} workspace={workspace(true, true)} />)
  expect(screen.getByText(label)).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Play' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Open store' })).toBeTruthy()
  view.rerender(<EntryActions entry={{ ...entry, installed: false }} workspace={workspace(false, true)} />)
  expect(screen.queryByRole('button', { name: 'Play' })).toBeNull()
  expect(screen.getByRole('button', { name: 'Open store' })).toBeTruthy()
  expect(screen.getByText(label)).toBeTruthy()
  view.rerender(<EntryActions entry={{ ...entry, installed: false }} workspace={workspace(false, false)} />)
  expect(screen.queryByRole('button', { name: 'Open store' })).toBeNull()
  expect(screen.getByText(label)).toBeTruthy()
})

it('plugin controls submit exact ownership and named actions without provider command text', async () => {
  const request = vi.fn(async (_input: ApiRequest) => ({ ok: true, status: 200, data: 0 }))
  const openExternal = vi.fn()
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, openExternal } })
  render(<EntryActions entry={entry} workspace={workspace(true, true)} />)
  fireEvent.click(screen.getByRole('button', { name: 'Play' }))
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Open store' }) as HTMLButtonElement).disabled).toBe(false),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Open store' }))
  await waitFor(() => expect(request).toHaveBeenCalledTimes(2))
  expect(request.mock.calls.map(([input]) => input)).toEqual([
    {
      route: 'actions.execute',
      params: { ownershipId: 41 },
      body: { action: 'Play', operationId: expect.any(String) },
    },
    {
      route: 'actions.execute',
      params: { ownershipId: 41 },
      body: { action: 'OpenStore', operationId: expect.any(String) },
    },
  ])
  expect(openExternal).not.toHaveBeenCalled()
})
