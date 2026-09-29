// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SessionNotifications } from '../src/renderer/features/SessionNotifications'
import { JournalPromptPreference } from '../src/renderer/features/SettingsPreferences'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest, BackendEvent } from '../src/shared/bridge'

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  for (const id of [10, 20]) clearViewState(`draft:journal:${id}`)
})
function fixture(native: Record<string, unknown> = {}) {
  let listener: ((event: BackendEvent) => void) | undefined
  let failures: unknown[] = []
  let enabled = true
  const library = {
    games: [1, 2].map((id) => ({
      workId: id,
      title: `Game ${id}`,
      bucket: 'started',
      playtimeMinutes: 60,
      entries: [
        {
          ownershipId: id,
          releaseId: id,
          workId: id,
          title: `Game ${id}`,
          installed: true,
          store: 'steam',
          playtimeMinutes: 60,
        },
      ],
    })),
    lists: [],
  }
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'journal.preferences.put')
      enabled = (input.body as { promptAfterPlay: boolean }).promptAfterPlay
    const data =
      input.route === 'library.get'
        ? library
        : input.route === 'diagnostics.get'
          ? { sessionFailures: failures }
          : input.route === 'journal.preferences.get'
            ? { promptAfterPlay: enabled }
            : input.route === 'journal.prompt'
              ? {
                  sessionId: input.params!.sessionId,
                  ownershipId: Number(input.params!.sessionId) / 10,
                  durationSeconds: 3600,
                }
              : input.route === 'journal.get'
                ? { sessionId: input.params!.sessionId, note: null, rating: null, revision: 'original' }
                : null
    return { ok: true, status: 200, data }
  })
  const openDataFolder = vi.fn().mockResolvedValue(undefined)
  const unsubscribe = vi.fn()
  Object.defineProperty(window, 'winnow', {
    value: {
      request,
      openDataFolder,
      onEvent: (callback: typeof listener) => {
        listener = callback
        return unsubscribe
      },
      ...native,
    },
    configurable: true,
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(['api', 'library.get'], library)
  return {
    request,
    openDataFolder,
    unsubscribe,
    client,
    setEnabled: (value: boolean) => {
      enabled = value
    },
    setFailures: (value: unknown[]) => {
      failures = value
    },
    send: async (kind: string, resource?: string) => {
      await act(async () => {
        listener?.({ kind, resource })
        await Promise.resolve()
      })
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}

describe.each(['desktop', 'fullscreen'] as const)('%s session notification parity', (mode) => {
  it('opens only the current native notification and never resurrects dismissed sessions', async () => {
    let activate: ((sessionId: number) => void) | undefined
    const clearJournalNotification = vi.fn().mockResolvedValue(undefined)
    const notifySessionEnded = vi.fn().mockResolvedValue(true)
    const { send, wrapper } = fixture({
      notifySessionEnded,
      clearJournalNotification,
      onJournalNotificationActivated: (callback: typeof activate) => {
        activate = callback
        return vi.fn()
      },
    })
    render(<SessionNotifications mode={mode} />, { wrapper })
    await send('session.ended', 'sessions/10')
    await waitFor(() => expect(notifySessionEnded).toHaveBeenCalledWith({ sessionId: 10, title: 'Game 1' }))
    expect(screen.queryByLabelText('Your note')).toBeNull()
    await send('session.ended', 'sessions/20')
    await waitFor(() => expect(notifySessionEnded).toHaveBeenCalledWith({ sessionId: 20, title: 'Game 2' }))
    act(() => activate?.(10))
    expect(screen.queryByLabelText('Your note')).toBeNull()
    act(() => activate?.(20))
    await screen.findByLabelText('Your note')
    expect(screen.getByRole('heading', { name: 'Game 2' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss journal prompt' }))
    expect(clearJournalNotification).toHaveBeenCalledWith(20)
    act(() => activate?.(20))
    expect(screen.queryByLabelText('Your note')).toBeNull()
  })

  it('falls back without taking focus when the native notification fails', async () => {
    const { send, wrapper } = fixture({
      notifySessionEnded: vi.fn().mockRejectedValue(new Error('unavailable')),
    })
    render(
      <>
        <button>Current control</button>
        <SessionNotifications mode={mode} />
      </>,
      { wrapper },
    )
    screen.getByRole('button', { name: 'Current control' }).focus()
    await send('session.ended', 'sessions/10')
    await screen.findByLabelText('Your note')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Current control' }))
  })

  it('uses the exact finished session, deduplicates events and writes only on Save', async () => {
    const { request, send, wrapper } = fixture()
    render(
      <>
        <button>Current control</button>
        <SessionNotifications mode={mode} />
      </>,
      { wrapper },
    )
    screen.getByRole('button', { name: 'Current control' }).focus()
    await send('session.ended', 'sessions/10')
    const note = await screen.findByLabelText('Your note')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Current control' }))
    await send('session.ended', 'sessions/10')
    expect(request.mock.calls.filter(([input]) => input.route === 'journal.prompt')).toHaveLength(1)
    expect(request.mock.calls.some(([input]) => input.route === 'journal.put')).toBe(false)
    fireEvent.change(note, { target: { value: '  Keep this exact sitting.  ' } })
    fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '4' } })
    await send('session.ended', 'sessions/20')
    expect(screen.getByRole('heading', { name: 'Game 1' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Game 2' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'journal.put',
        params: { sessionId: 10 },
        body: { note: 'Keep this exact sitting.', rating: 4, expectedRevision: 'original' },
      }),
    )
    await waitFor(() => expect(screen.queryByLabelText('Your note')).toBeNull())
  })

  it('dismisses an empty answer without writing and ignores repeated dismissed events', async () => {
    const { request, send, wrapper } = fixture()
    render(<SessionNotifications mode={mode} />, { wrapper })
    await send('session.ended', 'sessions/10')
    fireEvent.click(await screen.findByRole('button', { name: 'Save note' }))
    expect(screen.queryByLabelText('Your note')).toBeNull()
    expect(request.mock.calls.some(([input]) => input.route === 'journal.put')).toBe(false)
    await send('session.ended', 'sessions/10')
    expect(screen.queryByLabelText('Your note')).toBeNull()
  })

  it('checks the opt-in preference and never prompts for generic library changes', async () => {
    const { request, send, setEnabled, wrapper } = fixture()
    setEnabled(false)
    render(<SessionNotifications mode={mode} />, { wrapper })
    await send('library.changed', 'sessions/10/journal')
    await send('session.ended', 'sessions/10')
    expect(request.mock.calls.some(([input]) => input.route === 'journal.prompt')).toBe(false)
    expect(screen.queryByLabelText('Your note')).toBeNull()
  })

  it('dismisses untouched prompts after two minutes but keeps a typed draft', async () => {
    const { send, wrapper } = fixture()
    render(<SessionNotifications mode={mode} />, { wrapper })
    await send('session.ended', 'sessions/10')
    await screen.findByLabelText('Your note')
    // Rescheduling with fake time preserves the existing timer's actual 120 second duration.
    vi.useFakeTimers({ shouldAdvanceTime: true })
    await send('session.ended', 'sessions/20')
    await screen.findByRole('heading', { name: 'Game 2' })
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: 'Do not lose this note.' } })
    await act(async () => {
      vi.advanceTimersByTime(121_000)
    })
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('Do not lose this note.')
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: '' } })
    await act(async () => {
      vi.advanceTimersByTime(120_000)
    })
    expect(screen.queryByLabelText('Your note')).toBeNull()
  })

  it('shows a quiet tracking failure, opens logs, and clears only after full recovery', async () => {
    const { send, setFailures, openDataFolder, wrapper } = fixture()
    render(
      <>
        <button>Current control</button>
        <SessionNotifications mode={mode} />
      </>,
      { wrapper },
    )
    screen.getByRole('button', { name: 'Current control' }).focus()
    setFailures([{ operation: 'ExecutableIndex' }, { operation: 'Tick' }])
    await send('diagnostics.changed', 'sessions')
    await screen.findByText(/Session tracking needs attention/)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Current control' }))
    fireEvent.click(screen.getByRole('button', { name: 'Open logs folder' }))
    expect(openDataFolder).toHaveBeenCalledWith('logs')
    setFailures([{ operation: 'ExecutableIndex' }])
    await send('diagnostics.changed', 'sessions')
    expect(screen.getByText(/Session tracking needs attention/)).toBeTruthy()
    setFailures([])
    await send('diagnostics.changed', 'sessions')
    await waitFor(() => expect(screen.queryByText(/Session tracking needs attention/)).toBeNull())
  })
})

it('persists the journal opt-in without writing a session note', async () => {
  const { request, wrapper, setEnabled } = fixture()
  setEnabled(false)
  render(<JournalPromptPreference />, { wrapper })
  const enabled = screen.getByRole('checkbox', { name: /Ask for a note after playing/ }) as HTMLInputElement
  await waitFor(() => expect(enabled.disabled).toBe(false))
  expect(enabled.checked).toBe(false)
  fireEvent.click(enabled)
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith({
      route: 'journal.preferences.put',
      params: undefined,
      body: { promptAfterPlay: true },
    }),
  )
  expect(request.mock.calls.some(([input]) => input.route === 'journal.put')).toBe(false)
})
