// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Setup } from '../src/renderer/features/Setup'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
function fixture(step: number | null, override?: (request: ApiRequest) => unknown) {
  let cursor = step
  const request = vi.fn(async (input: ApiRequest) => {
    const custom = override?.(input)
    if (custom) return custom
    if (input.route === 'setup.put') {
      cursor = (input.body as { step: number | null }).step
      return { ok: true, status: 204 }
    }
    const data =
      input.route === 'setup.get'
        ? { step: cursor, problem: null }
        : input.route === 'connections.igdb.get'
          ? {
              clientId: 'saved-id',
              hasSavedCredentials: false,
              isReadable: true,
              hasConfigurationCredentials: false,
            }
          : input.route === 'connections.get'
            ? { steam: { hasSession: false } }
            : input.route === 'preferences.library.get'
              ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' }
              : input.route === 'preferences.presentation.get'
                ? []
                : null
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { value: { request, openExternal: vi.fn() }, configurable: true })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return { request, client }
}

describe.each(['desktop', 'fullscreen'] as const)('%s setup parity', (mode) => {
  it('closes Steam consent before navigating the wizard and traps the active layer', async () => {
    const { request, client } = fixture(2)
    window.winnow.steamSignIn = vi.fn()
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Steam' }))
    const consent = screen.getByRole('dialog', { name: 'Before you sign in' })
    expect(consent.contains(document.activeElement)).toBe(true)
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Before you sign in' })).toBeNull())
    expect(screen.getByRole('heading', { name: 'Your Steam library' })).toBeTruthy()
    expect(request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
  })

  it('uses the presentation-specific Escape action after nested editors close', async () => {
    const { request, client } = fixture(4)
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    await screen.findByRole('heading', { name: 'Your GOG library' })
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'setup.put',
        params: undefined,
        body: { step: mode === 'fullscreen' ? 3 : 5 },
      }),
    )
  })

  it('resumes every step, persists navigation, and completes only at Ready', async () => {
    const { request, client } = fixture(0)
    const completed = vi.fn()
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} onComplete={completed} appearance={<button>Choose theme</button>} />
      </QueryClientProvider>,
    )
    await screen.findByRole('heading', { name: 'Welcome to Winnow' })
    for (let index = 0; index < 9; index++) {
      await screen.findByText(`SETUP · ${index + 1} OF 9`)
      expect(completed).not.toHaveBeenCalled()
      fireEvent.click(
        screen.getByRole('button', {
          name: index === 0 ? 'Get started' : index === 8 ? 'Open my library' : 'Continue',
        }),
      )
      await waitFor(() =>
        expect(request).toHaveBeenCalledWith({
          route: 'setup.put',
          params: undefined,
          body: { step: index === 8 ? null : index + 1 },
        }),
      )
    }
    await waitFor(() => expect(completed).toHaveBeenCalledTimes(1))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('discards an unsaved masked secret on skip and on presentation change', async () => {
    const { request, client } = fixture(1)
    const view = render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    const secret = (await screen.findByLabelText('Client secret')) as HTMLInputElement
    expect(secret.type).toBe('password')
    fireEvent.change(secret, { target: { value: 'unsaved-secret' } })
    const otherMode = mode === 'desktop' ? 'fullscreen' : 'desktop'
    view.rerender(
      <QueryClientProvider client={client}>
        <Setup mode={otherMode} />
      </QueryClientProvider>,
    )
    expect(((await screen.findByLabelText('Client secret')) as HTMLInputElement).value).toBe('')
    fireEvent.change(screen.getByLabelText('Client secret'), { target: { value: 'discard-again' } })
    fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    await screen.findByRole('heading', { name: 'Your Steam library' })
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect(((await screen.findByLabelText('Client secret')) as HTMLInputElement).value).toBe('')
    expect(request.mock.calls.some(([input]) => input.route === 'connections.igdb.put')).toBe(false)
  })

  it('waits for an explicit credential save and does not save again on Continue', async () => {
    let finish: (result: unknown) => void = () => {}
    const response = new Promise((resolve) => {
      finish = resolve
    })
    const { request, client } = fixture(1, (input) =>
      input.route === 'connections.igdb.put' ? response : null,
    )
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    fireEvent.change(await screen.findByLabelText('Client secret'), { target: { value: 'secret' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save credentials' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(true),
    )
    expect(request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
    finish({ ok: true, status: 200, data: 0 })
    await screen.findByText('IGDB credentials saved.')
    expect((screen.getByLabelText('Client secret') as HTMLInputElement).value).toBe('')
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await screen.findByRole('heading', { name: 'Your Steam library' })
    expect(request.mock.calls.filter(([input]) => input.route === 'connections.igdb.put')).toHaveLength(1)
  })

  it('keeps the current step open after a failed progress write and retries', async () => {
    let fail = true
    const { client } = fixture(4, (input) =>
      input.route === 'setup.put' && fail ? { ok: false, status: 500 } : null,
    )
    const completed = vi.fn()
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} onComplete={completed} />
      </QueryClientProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Skip setup' }))
    await screen.findByText(/Could not save setup progress/)
    expect(screen.getByRole('heading', { name: 'Your GOG library' })).toBeTruthy()
    expect(completed).not.toHaveBeenCalled()
    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Skip setup' }))
    await waitFor(() => expect(completed).toHaveBeenCalledTimes(1))
  })

  it('allows skipping a failed preference save without claiming it saved', async () => {
    const { request, client } = fixture(6, (input) =>
      input.route === 'preferences.presentation.put' ? { ok: false, status: 500 } : null,
    )
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    const checkbox = await screen.findByRole('checkbox', { name: /Start in fullscreen/ })
    await waitFor(() => expect((checkbox as HTMLInputElement).disabled).toBe(false))
    fireEvent.click(checkbox)
    await screen.findByRole('alert')
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await screen.findByText(/A preference could not be saved/)
    expect(request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    await screen.findByRole('heading', { name: 'Choose what appears' })
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    await screen.findByRole('heading', { name: 'How Winnow fits your desktop' })
    fireEvent.click(screen.getByRole('button', { name: 'Skip setup' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})

it('does not interrupt an existing or completed library', async () => {
  const { client } = fixture(null)
  render(
    <QueryClientProvider client={client}>
      <Setup mode="desktop" />
    </QueryClientProvider>,
  )
  await waitFor(() =>
    expect(client.getQueryData(['api', 'setup.get', undefined])).toEqual({ step: null, problem: null }),
  )
  expect(screen.queryByRole('dialog')).toBeNull()
})
