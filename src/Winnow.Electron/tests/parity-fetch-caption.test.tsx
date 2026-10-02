// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import type { ApiRequest, BackendEvent, ConnectionState } from '../src/shared/bridge'
import { FetchCaption, FetchStatusContext, useFetchStatusHost } from '../src/renderer/features/FetchStatus'

afterEach(cleanup)

function fixture() {
  const events = new Set<(event: BackendEvent) => void>()
  const connections = new Set<(state: ConnectionState) => void>()
  const pending: { input: ApiRequest; resolve(value: unknown): void }[] = []
  const request = vi.fn((input: ApiRequest) => new Promise((resolve) => pending.push({ input, resolve })))
  const cancelRequest = vi.fn().mockResolvedValue(undefined)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      cancelRequest,
      onEvent: (listener: (event: BackendEvent) => void) => {
        events.add(listener)
        return () => {
          events.delete(listener)
        }
      },
      onConnection: (listener: (state: ConnectionState) => void) => {
        connections.add(listener)
        return () => {
          connections.delete(listener)
        }
      },
    },
  })
  const finish = async (index: number, remaining: number) =>
    act(async () => pending[index].resolve({ ok: true, status: 200, data: { total: 1247, remaining } }))
  return { events, connections, pending, request, cancelRequest, finish }
}
function Host({
  mode = 'desktop',
  enabled = true,
  dialog = false,
}: {
  mode?: 'desktop' | 'fullscreen'
  enabled?: boolean
  dialog?: boolean
}) {
  const status = useFetchStatusHost(enabled)
  return (
    <FetchStatusContext.Provider value={status}>
      <header>
        <FetchCaption mode={mode} />
      </header>
      {dialog && (
        <div role="dialog">
          <input aria-label="Current draft" defaultValue="keep this draft" />
        </div>
      )}
    </FetchStatusContext.Provider>
  )
}

it('reads the initial snapshot, refreshes on reconnect/resync and continues behind dialogs and fullscreen', async () => {
  const f = fixture()
  const view = render(<Host enabled={false} />)
  await act(async () => {})
  expect(f.request).not.toHaveBeenCalled()
  view.rerender(<Host />)
  await waitFor(() => expect(f.pending).toHaveLength(1))
  expect(f.pending[0].input.route).toBe('progress.get')
  await f.finish(0, 997)
  await screen.findByRole('status', { name: 'Fetching details, 997 titles left' })
  expect(screen.getByRole('status').getAttribute('aria-live')).toBe('polite')
  expect(screen.getByRole('status').getAttribute('aria-atomic')).toBe('true')
  view.rerender(<Host dialog />)
  const input = screen.getByRole('textbox') as HTMLInputElement
  input.focus()
  await act(async () => f.events.forEach((listener) => listener({ kind: 'resync-required' })))
  await f.finish(1, 1247)
  expect(screen.getByRole('status', { name: 'Fetching details, 1,247 titles left' })).toBeTruthy()
  expect(document.activeElement).toBe(input)
  expect(input.value).toBe('keep this draft')
  view.rerender(<Host mode="fullscreen" />)
  expect(screen.queryByRole('status')).toBeNull()
  await act(async () =>
    f.connections.forEach((listener) => listener({ connected: true, message: 'Connected' })),
  )
  await f.finish(2, 1)
  expect(screen.queryByRole('status')).toBeNull()
  view.rerender(<Host />)
  expect(screen.getByRole('status', { name: 'Fetching details, 1 title left' })).toBeTruthy()
  view.unmount()
  expect(f.events.size).toBe(0)
  expect(f.connections.size).toBe(0)
})

it('cancels bridge reads superseded by events and refuses their late replies after completion or unmount', async () => {
  const f = fixture()
  const view = render(<Host />)
  await waitFor(() => expect(f.pending).toHaveLength(1))
  await act(async () =>
    f.events.forEach((listener) => listener({ kind: 'progress.changed', resource: 'progress' })),
  )
  expect(f.cancelRequest).toHaveBeenCalledWith(f.pending[0].input.requestId)
  await f.finish(1, 0)
  await f.finish(0, 997)
  expect(screen.queryByRole('status')).toBeNull()
  await act(async () => f.events.forEach((listener) => listener({ kind: 'progress.changed' })))
  view.unmount()
  expect(f.cancelRequest).toHaveBeenCalledWith(f.pending[2].input.requestId)
  await f.finish(2, 1247)
  expect(screen.queryByRole('status')).toBeNull()
  expect(f.events.size).toBe(0)
})

it('clears active work on disconnect, cancels its pending read and resumes only on reconnect', async () => {
  const f = fixture()
  render(<Host />)
  await waitFor(() => expect(f.pending).toHaveLength(1))
  await f.finish(0, 997)
  await screen.findByRole('status', { name: 'Fetching details, 997 titles left' })
  await act(async () => f.events.forEach((listener) => listener({ kind: 'progress.changed' })))
  await act(async () =>
    f.connections.forEach((listener) => listener({ connected: false, message: 'Disconnected' })),
  )
  expect(f.cancelRequest).toHaveBeenCalledWith(f.pending[1].input.requestId)
  expect(screen.queryByRole('status')).toBeNull()
  await f.finish(1, 40)
  expect(screen.queryByRole('status')).toBeNull()
  await act(async () => f.events.forEach((listener) => listener({ kind: 'progress.changed' })))
  expect(f.pending).toHaveLength(2)
  await act(async () =>
    f.connections.forEach((listener) => listener({ connected: true, message: 'Connected' })),
  )
  await f.finish(2, 1)
  await screen.findByRole('status', { name: 'Fetching details, 1 title left' })
})
