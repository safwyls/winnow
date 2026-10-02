// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { IdentityTools } from '../src/renderer/features/parity-library'
import { mergeReviewKey } from '../src/renderer/features/parity-merge-query'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import { mergeFixture } from './parity-merge-fixtures'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const key of [
    'queue-section',
    'queue-sort',
    'queue-choices',
    'queue-answered-keys',
    'queue-positions',
    'queue-applied-platform',
    'review-undo',
    'queue-busy',
    'queue-problem',
    'queue-refresh-required',
    'desktop:detail-return',
    'fullscreen:detail-return',
    'last-undo',
  ])
    clearViewState(`identity:${key}`)
  clearViewState('draft:identity-link')
})

for (const mode of ['desktop', 'fullscreen'] as const)
  it(`${mode} waits for an invalidated identity snapshot before accepting a cached proposal`, async () => {
    const review = mergeFixture()
    let release!: (value: typeof review) => void
    const pending = new Promise<typeof review>((resolve) => {
      release = resolve
    })
    let reads = 0
    const request = vi.fn(async (input: ApiRequest) => {
      if (input.route === 'identity.get')
        return { ok: true, status: 200, data: ++reads === 1 ? await pending : structuredClone(review) }
      if (input.route === 'identity.link') {
        if ((input.body as { expectedRevision: string }).expectedRevision !== review.revision)
          return { ok: false, status: 409, message: 'Review changed' }
        review.revision = 'r3'
        return { ok: true, status: 200, data: { revision: 'r3', actId: 100 } }
      }
      return { ok: true, status: 200, data: [] }
    })
    Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    clients.push(client)
    client.setQueryData(mergeReviewKey, structuredClone(review))
    render(
      <QueryClientProvider client={client}>
        <IdentityTools mode={mode} onOpenGame={() => {}} />
      </QueryClientProvider>,
    )
    const accept = screen.getByRole('button', {
      name: 'Accept 1 exact match',
    }) as HTMLButtonElement
    expect(accept.disabled).toBe(false)
    review.revision = 'r2'
    let refresh!: Promise<void>
    await act(async () => {
      refresh = client.invalidateQueries({ queryKey: mergeReviewKey, exact: true })
    })
    await waitFor(() => expect(reads).toBe(1))
    await waitFor(() => expect(accept.disabled).toBe(true))
    expect(
      (screen.getByRole('button', { name: 'Create a relationship' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    expect(
      (
        screen.getByRole(mode === 'desktop' ? 'combobox' : 'button', {
          name: mode === 'desktop' ? 'Preferred main platform' : /^Preferred platform ·/,
        }) as HTMLButtonElement
      ).disabled,
    ).toBe(true)
    if (mode === 'desktop')
      expect(
        (screen.getByRole('button', { name: 'Details for Bastion (GOG)' }) as HTMLButtonElement).disabled,
      ).toBe(true)
    fireEvent.click(accept)
    expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
    await act(async () => {
      release(structuredClone(review))
      await refresh
    })
    await waitFor(() => expect(accept.disabled).toBe(false))
    fireEvent.click(accept)
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'identity.link')?.[0].body).toMatchObject({
        expectedRevision: 'r2',
      }),
    )
    await waitFor(() => expect(screen.getByRole('button', { name: 'Undo review decisions' })).toBeTruthy())
    expect(screen.queryByRole('alert')).toBeNull()
  })

it('fullscreen keeps an open platform choice disabled until the identity refresh finishes', async () => {
  const review = mergeFixture()
  let release!: (value: typeof review) => void
  const pending = new Promise<typeof review>((resolve) => {
    release = resolve
  })
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data: input.route === 'identity.get' ? await pending : [],
  }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  client.setQueryData(mergeReviewKey, structuredClone(review))
  render(
    <QueryClientProvider client={client}>
      <IdentityTools mode="fullscreen" />
    </QueryClientProvider>,
  )
  fireEvent.click(screen.getByRole('button', { name: /^Preferred platform ·/ }))
  const choice = within(screen.getByRole('dialog')).getByRole('button', { name: 'GOG' }) as HTMLButtonElement
  let refresh!: Promise<void>
  await act(async () => {
    refresh = client.invalidateQueries({ queryKey: mergeReviewKey, exact: true })
  })
  await waitFor(() => expect(choice.disabled).toBe(true))
  fireEvent.click(choice)
  expect(screen.getByRole('dialog')).toBeTruthy()
  expect(request.mock.calls.some(([input]) => input.route === 'preferences.presentation.put')).toBe(false)
  await act(async () => {
    release(review)
    await refresh
  })
  await waitFor(() => expect(choice.disabled).toBe(false))
  fireEvent.click(choice)
  await waitFor(() =>
    expect(
      request.mock.calls.find(([input]) => input.route === 'preferences.presentation.put')?.[0],
    ).toMatchObject({
      params: { preference: 'PreferredMergePlatform' },
      body: { value: 'gog' },
    }),
  )
})

for (const mode of ['desktop', 'fullscreen'] as const)
  it(`${mode} retains the Details return target while the unchanged identity snapshot refreshes`, async () => {
    const review = mergeFixture()
    let release!: (value: typeof review) => void
    const pending = new Promise<typeof review>((resolve) => {
      release = resolve
    })
    const request = vi.fn(async (input: ApiRequest) => ({
      ok: true,
      status: 200,
      data: input.route === 'identity.get' ? await pending : [],
    }))
    Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    clients.push(client)
    client.setQueryData(mergeReviewKey, structuredClone(review))
    const onOpenGame = vi.fn()
    const tree = (
      <QueryClientProvider client={client}>
        <IdentityTools mode={mode} onOpenGame={onOpenGame} />
      </QueryClientProvider>
    )
    const view = render(tree)
    if (mode === 'desktop') fireEvent.click(screen.getByRole('button', { name: 'Details for Bastion (GOG)' }))
    else {
      fireEvent.click(within(screen.getByRole('article', { name: 'Bastion proposal' })).getByRole('button'))
      fireEvent.click(screen.getByRole('button', { name: / · Included$/ }))
      fireEvent.click(screen.getByRole('button', { name: 'Open game' }))
    }
    expect(onOpenGame).toHaveBeenCalledWith(2)
    view.unmount()
    await client.invalidateQueries({ queryKey: mergeReviewKey, exact: true })
    render(tree)
    await waitFor(() =>
      expect(request.mock.calls.some(([input]) => input.route === 'identity.get')).toBe(true),
    )
    await act(async () => {
      await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
    })
    expect(screen.getByText('Updating possible matches…')).toBeTruthy()
    const target = screen.getByRole('button', {
      name: mode === 'desktop' ? 'Choose Bastion (GOG)' : /^Bastion ·/,
    })
    await act(async () => {
      release(structuredClone(review))
      await pending
    })
    await waitFor(() => expect(document.activeElement).toBe(target))
    expect(screen.queryByRole('alert')).toBeNull()
  })
