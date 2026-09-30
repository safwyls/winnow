// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ApiRequest, BackendEvent } from '../src/shared/bridge'
import type { GameEntry, LibraryGame, Workspace } from '../src/renderer/api/types'
import { ChooseLaunchVersion } from '../src/renderer/features/choose-launch-version'
import { EntryActions } from '../src/renderer/features/Details'
import { LaunchFeedback } from '../src/renderer/features/launch-feedback'
import {
  LaunchFeedbackContext,
  LaunchFeedbackStrip,
  useLaunchFeedbackHost,
} from '../src/renderer/features/LaunchFeedback'

const game: LibraryGame = {
  workId: 1,
  title: 'Two copies',
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [1, 2].map((id) => ({
    ownershipId: id,
    releaseId: id,
    workId: 1,
    title: `Copy ${id}`,
    store: 'steam',
    installed: true,
    playtimeMinutes: 0,
  })),
}
const workspace: Workspace = {
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
  externalIds: [
    { releaseId: 1, provider: 'steam', providerId: '10' },
    { releaseId: 2, provider: 'steam', providerId: '20' },
  ],
  epicLaunchKeys: {},
  pluginActions: {},
  works: [],
}
const statuses: LaunchFeedback[] = []
afterEach(() => {
  cleanup()
  statuses.splice(0).forEach((status) => status.dispose())
  vi.restoreAllMocks()
})
function bridge(outcome = 0) {
  const request = vi.fn(async (_: ApiRequest) => ({ ok: true, status: 200, data: outcome }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  return request
}
function mount(mode: 'desktop' | 'fullscreen', copies = game, facts = workspace) {
  const status = new LaunchFeedback()
  statuses.push(status)
  return {
    status,
    ...render(
      <LaunchFeedbackContext.Provider value={status}>
        <div className={`avalon-shell ${mode}`}>
          {mode === 'fullscreen' ? (
            <ChooseLaunchVersion game={copies} workspace={facts} />
          ) : (
            copies.entries.map((entry) => (
              <EntryActions
                key={entry.ownershipId}
                entry={entry}
                workspace={facts}
                launchTitle={copies.title}
              />
            ))
          )}
        </div>
        <LaunchFeedbackStrip feedback={status} mode={mode} />
      </LaunchFeedbackContext.Provider>,
    ),
  }
}

describe.each(['desktop', 'fullscreen'] as const)('shared launch feedback in %s', (mode) => {
  it('dispatches only the selected ownership and confirms only its watcher observation', async () => {
    const request = bridge(),
      { status } = mount(mode)
    if (mode === 'fullscreen') {
      fireEvent.click(screen.getByRole('button', { name: 'Choose launch version' }))
      expect(request).not.toHaveBeenCalled()
      fireEvent.click((await screen.findAllByRole('button', { name: 'Steam · Installed · Play' }))[1])
    } else fireEvent.click(screen.getAllByRole('button', { name: 'Play' })[1])
    await screen.findByText('Starting Two copies…')
    expect(request).toHaveBeenCalledTimes(1)
    expect(request.mock.calls[0][0]).toMatchObject({
      route: 'actions.execute',
      params: { ownershipId: 2 },
      body: { action: 'Play', operationId: expect.any(String) },
    })
    act(() => status.observe({ kind: 'launch.observed', resource: '1' }))
    expect(screen.getByText('Starting Two copies…')).toBeTruthy()
    act(() => status.observe({ kind: 'launch.observed', resource: '2' }))
    expect(screen.getByText('Two copies is running.')).toBeTruthy()
    expect(screen.getByRole('status').getAttribute('data-waiting')).toBe('false')
  })
  it('shows the store refusal without treating a successful handoff as already running', async () => {
    bridge(2)
    mount(mode)
    if (mode === 'fullscreen') {
      fireEvent.click(screen.getByRole('button', { name: 'Choose launch version' }))
      fireEvent.click((await screen.findAllByRole('button', { name: 'Steam · Installed · Play' }))[0])
    } else fireEvent.click(screen.getAllByRole('button', { name: 'Play' })[0])
    await screen.findByText("Couldn't reach Steam to start Two copies.")
    expect(screen.getByRole('status').getAttribute('data-problem')).toBe('true')
  })
})

it('includes unavailable versions, disables them, and cancels without dispatching', async () => {
  const request = bridge()
  mount('fullscreen', {
    ...game,
    entries: [
      ...game.entries,
      {
        ...game.entries[0],
        ownershipId: 3,
        releaseId: 3,
        store: 'epic',
        installed: null,
      } as unknown as GameEntry,
    ],
  })
  fireEvent.click(screen.getByRole('button', { name: 'Choose launch version' }))
  const panel = await screen.findByRole('dialog')
  expect(
    (
      within(panel).getByRole('button', {
        name: 'Epic Games · Install state unknown · Unavailable',
      }) as HTMLButtonElement
    ).disabled,
  ).toBe(true)
  fireEvent.click(within(panel).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  expect(request).not.toHaveBeenCalled()
})

it('offers Install for an uninstalled chosen version without creating a launch strip', async () => {
  const request = bridge()
  const { status } = mount('fullscreen', {
    ...game,
    entries: game.entries.map((entry) => ({ ...entry, installed: false })),
  })
  fireEvent.click(screen.getByRole('button', { name: 'Choose launch version' }))
  fireEvent.click((await screen.findAllByRole('button', { name: 'Steam · Not installed · Install' }))[1])
  await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
  expect(request.mock.calls[0][0]).toMatchObject({ params: { ownershipId: 2 }, body: { action: 'Install' } })
  expect(status.getSnapshot().open).toBe(false)
})

it('retries an interrupted version selection with its original ownership and operation ID', async () => {
  const request = bridge()
  request.mockResolvedValueOnce({ ok: false, status: 503, data: 0 })
  mount('fullscreen')
  fireEvent.click(screen.getByRole('button', { name: 'Choose launch version' }))
  fireEvent.click((await screen.findAllByRole('button', { name: 'Steam · Installed · Play' }))[1])
  fireEvent.click(await screen.findByRole('button', { name: 'Check the same action again' }))
  await screen.findByText('Starting Two copies…')
  expect(request).toHaveBeenCalledTimes(2)
  expect(request.mock.calls[1][0]).toEqual(request.mock.calls[0][0])
})

it('detaches backend observations and rejects late callbacks when the host is disposed', async () => {
  let listener: ((event: BackendEvent) => void) | undefined
  let status: LaunchFeedback | undefined
  const detach = vi.fn()
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      onEvent: vi.fn((callback) => {
        listener = callback
        return detach
      }),
    },
  })
  function Host() {
    status = useLaunchFeedbackHost()
    return <LaunchFeedbackStrip feedback={status} mode="desktop" />
  }
  const view = render(<Host />)
  act(() => status!.waiting(2, game.title))
  expect(screen.getByText('Starting Two copies…')).toBeTruthy()
  view.unmount()
  expect(detach).toHaveBeenCalledOnce()
  listener!({ kind: 'launch.observed', resource: '2' })
  const send = vi.fn(async () => 0)
  await status!.track(2, game.title, 'Steam', 'Play', send)
  expect(send).not.toHaveBeenCalled()
  expect(status!.getSnapshot().message).toBe('Starting Two copies…')
})
