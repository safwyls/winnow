// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CreateList, ManualEditor } from '../src/renderer/features/LibraryTools'
import { clearViewState } from '../src/renderer/viewState'
import type { ManualGame } from '../src/renderer/api/types'

afterEach(() => {
  cleanup()
  clearViewState('draft:list:new')
  clearViewState('draft:manual:new')
  clearViewState('draft:manual:77')
})
const client = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
describe('retained library drafts', () => {
  it('keeps an in-flight create disabled across remounts and observes its eventual completion', async () => {
    let finish!: (value: unknown) => void
    const request = vi.fn().mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const query = client()
    const first = render(
      <QueryClientProvider client={query}>
        <CreateList />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'Pending list' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create list' }))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
    first.unmount()
    render(
      <QueryClientProvider client={query}>
        <CreateList />
      </QueryClientProvider>,
    )
    expect((screen.getByRole('button', { name: 'Create list' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('List name') as HTMLInputElement).disabled).toBe(true)
    expect(
      (screen.getByLabelText('Keep this list up to date with filters') as HTMLInputElement).disabled,
    ).toBe(true)
    finish({ ok: true, status: 200, data: { id: 4, name: 'Pending list' } })
    await waitFor(() => expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe(''))
    expect((screen.getByLabelText('List name') as HTMLInputElement).disabled).toBe(false)
    expect(request).toHaveBeenCalledTimes(1)
  })
  it('retains manual text, metadata mappings and the observed revision after a remount', async () => {
    const request = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const game: ManualGame = {
      ownershipId: 77,
      workId: 7,
      releaseId: 8,
      title: 'Original',
      revision: 'old-revision',
      igdbMappingRevision: 4,
      igdbId: 5,
      steamAppId: '20',
    }
    const query = client()
    const close = vi.fn()
    const first = render(
      <QueryClientProvider client={query}>
        <ManualEditor initial={game} onClose={close} />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'My edited game' } })
    first.unmount()
    render(
      <QueryClientProvider client={query}>
        <ManualEditor
          initial={{ ...game, revision: 'new-revision', igdbMappingRevision: 6, igdbId: 90 }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('My edited game')
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() => expect(close).toHaveBeenCalled())
    expect(request.mock.calls[0]?.[0].body).toMatchObject({
      title: 'My edited game',
      expectedRevision: 'old-revision',
      expectedIgdbMappingRevision: 4,
      igdbId: 5,
      steamAppId: '20',
    })
  })
  it('blocks list recreation after a lost response until saved lists are checked and a choice is explicit', async () => {
    const request = vi
      .fn()
      .mockImplementation(async ({ route }: { route: string }) =>
        route === 'list.create'
          ? { ok: false, status: 0, message: 'Disconnected' }
          : {
              ok: true,
              status: 200,
              data: {
                games: [],
                lists: [{ id: 9, name: 'An evening', releaseIds: [], isLive: false, revision: 'saved' }],
              },
            },
      )
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const query = client()
    const first = render(
      <QueryClientProvider client={query}>
        <CreateList />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'An evening' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create list' }))
    await screen.findByText('Check whether this list was created')
    first.unmount()
    render(
      <QueryClientProvider client={query}>
        <CreateList />
      </QueryClientProvider>,
    )
    expect((screen.getByRole('button', { name: 'Create list' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('List name') as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: 'Create another anyway' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Check saved lists' }))
    await screen.findByRole('button', { name: 'Use saved list' })
    expect(request.mock.calls.filter((call) => call[0].route === 'list.create')).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Use saved list' }))
    expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe('')
  })
  it('gates uncertain manual creation and preserves the unsaved fields until reconciliation', async () => {
    const request = vi
      .fn()
      .mockImplementation(async ({ route }: { route: string }) =>
        route === 'manual.create'
          ? { ok: false, status: 503, message: 'Interrupted' }
          : {
              ok: true,
              status: 200,
              data: [{ ownershipId: 12, title: 'Small adventure', firstReleaseYear: 2025 }],
            },
      )
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const query = client()
    const close = vi.fn()
    render(
      <QueryClientProvider client={query}>
        <ManualEditor initial={null} onClose={close} />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Small adventure' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await screen.findByText('Check whether this game was added')
    expect((screen.getByRole('button', { name: 'Save game' }) as HTMLButtonElement).disabled).toBe(true)
    for (const label of ['Title', 'Release year', 'Platform'])
      expect((screen.getByLabelText(label) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Small adventure')
    fireEvent.click(screen.getByRole('button', { name: 'Check saved games' }))
    await screen.findByRole('button', { name: 'Use saved game' })
    fireEvent.click(screen.getByRole('button', { name: 'Use saved game' }))
    expect(close).toHaveBeenCalledOnce()
    expect(request.mock.calls.filter((call) => call[0].route === 'manual.create')).toHaveLength(1)
  })
})
