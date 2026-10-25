// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details } from '../src/renderer/features/Details'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameList, LibraryGame } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

const game: LibraryGame = {
  workId: 1,
  title: 'Dragonwilds',
  bucket: 'started',
  playtimeMinutes: 60,
  entries: [
    {
      ownershipId: 11,
      releaseId: 100,
      workId: 1,
      store: 'steam',
      title: 'Dragonwilds',
      installed: true,
      playtimeMinutes: 60,
    },
    {
      ownershipId: 12,
      releaseId: 200,
      workId: 1,
      store: 'gog',
      title: 'Dragonwilds',
      installed: true,
      playtimeMinutes: 60,
    },
  ],
}
const initial: GameList = { id: 30, name: 'Quiet evenings', isLive: false, releaseIds: [], revision: 'r1' }
const ok = (data: unknown) => ({ ok: true, status: 200, data })
function setup(mode: 'desktop' | 'fullscreen', handler: (input: ApiRequest) => unknown, list = initial) {
  const request = vi.fn(
    async (input: ApiRequest) =>
      (await handler(input)) ??
      ok(
        input.route === 'library.get'
          ? { games: [game], lists: [list] }
          : input.route === 'game.details'
            ? {
                workId: 1,
                readAtUtc: '2026-09-28T20:00:00Z',
                events: [],
                sessions: {},
                journalEntries: [],
                ratings: [],
                achievements: [],
              }
            : input.route === 'library.workspace'
              ? { externalIds: [], epicLaunchKeys: {}, pluginActions: {}, works: [] }
              : {},
      ),
  )
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(['api', 'library.get'], { games: [game], lists: [list] })
  const tree = () => (
    <QueryClientProvider client={client}>
      <Details mode={mode} workId={1} />
    </QueryClientProvider>
  )
  return { ...render(tree()), client, request, tree }
}
afterEach(() => {
  cleanup()
  for (const mode of ['desktop', 'fullscreen'])
    for (const key of ['tab', 'editing']) clearViewState(`${mode}:details:1:${key}`)
})

describe.each(['desktop', 'fullscreen'] as const)('queued list membership in %s', (mode) => {
  it.each(['latest', 'failure', 'refresh', 'compensation failure'])(
    'preserves latest intent and reports the saved result after %s',
    async (scenario) => {
      let finish!: () => void
      const held = new Promise<void>((resolve) => {
        finish = resolve
      })
      let current = initial,
        writes = 0,
        failed = false
      const view = setup(mode, async (input) => {
        if (input.route === 'library.get') return ok({ games: [game], lists: [current] })
        if (!input.route.startsWith('list.member.')) return undefined
        writes++
        if (writes === 1) await held
        const add = input.route === 'list.member.add'
        if (!failed && ((scenario === 'failure' && add) || (scenario === 'compensation failure' && !add))) {
          failed = true
          return { ok: false, status: 400, message: 'Write failed' }
        }
        current = { ...current, releaseIds: add ? [100] : [], revision: `r${writes + 1}` }
        return ok(current)
      })
      const checkbox = await screen.findByRole('checkbox', { name: 'Add to Quiet evenings' })
      fireEvent.click(checkbox)
      await screen.findByText('Saving list changes…')
      expect((checkbox as HTMLInputElement).disabled).toBe(false)
      if (scenario === 'latest' || scenario === 'compensation failure') fireEvent.click(checkbox)
      if (scenario === 'refresh') {
        act(() =>
          view.client.setQueryData(['api', 'library.get'], { games: [game], lists: [{ ...initial }] }),
        )
        expect((checkbox as HTMLInputElement).checked).toBe(true)
      }
      await act(async () => finish())
      await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
      const member = scenario === 'refresh' || scenario === 'compensation failure'
      expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(member)
      expect(current.releaseIds.includes(100)).toBe(member)
      expect(
        view.client
          .getQueryData<{ lists: GameList[] }>(['api', 'library.get'])!
          .lists[0]!.releaseIds.includes(100),
      ).toBe(member)
      const calls = view.request.mock.calls.filter(([input]) => input.route.startsWith('list.member.'))
      expect(calls[0]![0].body).toEqual({ releaseIds: [100], expectedRevision: 'r1' })
      if (scenario === 'latest' || scenario === 'compensation failure')
        expect(calls[1]![0].body).toEqual({ releaseIds: [100], expectedRevision: 'r2' })
      if (scenario.includes('failure')) {
        expect(screen.getByText("Couldn't save list changes. Try again.")).toBeTruthy()
        fireEvent.click(screen.getByRole('checkbox'))
        await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
        expect(screen.queryByText("Couldn't save list changes. Try again.")).toBeNull()
        expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(!member)
      } else expect(screen.queryByRole('alert')).toBeNull()
    },
  )
})

it('removes every member edition while adding only the primary release', async () => {
  const list = { ...initial, releaseIds: [100, 200, 300] }
  const view = setup(
    'desktop',
    (input) =>
      input.route === 'list.member.remove' ? ok({ ...list, releaseIds: [300], revision: 'r2' }) : undefined,
    list,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Remove from Quiet evenings' }))
  await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
  expect(view.request.mock.calls.find(([input]) => input.route === 'list.member.remove')![0].body).toEqual({
    releaseIds: [100, 200],
    expectedRevision: 'r1',
  })
})

it('retains pending intent across a mode remount and continues the compensating write', async () => {
  let finish!: (value: unknown) => void
  const view = setup('desktop', (input) =>
    input.route === 'list.member.add'
      ? new Promise((resolve) => {
          finish = resolve
        })
      : input.route === 'list.member.remove'
        ? ok({ ...initial, revision: 'r3' })
        : undefined,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Quiet evenings' }))
  view.unmount()
  render(
    <QueryClientProvider client={view.client}>
      <Details mode="fullscreen" workId={1} />
    </QueryClientProvider>,
  )
  const checkbox = await screen.findByRole('checkbox', { name: 'Remove from Quiet evenings' })
  expect(screen.getByText('Saving list changes…')).toBeTruthy()
  fireEvent.click(checkbox)
  await act(async () => finish(ok({ ...initial, releaseIds: [100], revision: 'r2' })))
  await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
  expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false)
  expect(view.request.mock.calls.filter(([input]) => input.route.startsWith('list.member.'))).toHaveLength(2)
})

it('blocks blind retries after an uncertain write until the saved membership can be checked', async () => {
  let online = false
  setup('desktop', (input) =>
    ['list.member.add', 'library.get'].includes(input.route)
      ? online
        ? ok({ games: [game], lists: [{ ...initial, releaseIds: [100], revision: 'r2' }] })
        : { ok: false, status: 503, message: 'Disconnected' }
      : undefined,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Quiet evenings' }))
  await screen.findByRole('button', { name: 'Check saved list' })
  expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  online = true
  fireEvent.click(screen.getByRole('button', { name: 'Check saved list' }))
  await waitFor(() => expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(false))
  expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true)
})
