// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
async function openIgdb(mode: 'desktop' | 'fullscreen') {
  if (mode === 'fullscreen')
    fireEvent.click(await screen.findByRole('button', { name: 'Set up IGDB metadata' }))
  return (await screen.findByLabelText('Client secret')) as HTMLInputElement
}
const providerEntries = [
  '',
  'Set up IGDB metadata',
  'Set up Steam',
  'Set up Epic',
  'Check GOG Galaxy',
  'Choose theme and appearance',
  'Choose app settings',
  'Choose library settings',
]

describe.each(['desktop', 'fullscreen'] as const)('%s setup parity', (mode) => {
  it('closes Steam consent before navigating the wizard and traps the active layer', async () => {
    const { request, client } = fixture(2)
    window.winnow.steamSignIn = vi.fn()
    render(
      <QueryClientProvider client={client}>
        <Setup mode={mode} />
      </QueryClientProvider>,
    )
    if (mode === 'fullscreen') fireEvent.click(await screen.findByRole('button', { name: 'Set up Steam' }))
    const signIn = await screen.findByRole('button', { name: 'Sign in to Steam' })
    const navigation = (
      mode === 'fullscreen' ? ['Back to setup'] : ['Back', 'Continue', 'Skip this step', 'Skip setup']
    ).map((name) => screen.getByRole('button', { name }) as HTMLButtonElement)
    await waitFor(() => expect(navigation.every((button) => !button.disabled)).toBe(true))
    fireEvent.click(signIn)
    const consent = screen.getByRole('dialog', { name: 'Before you sign in' })
    expect(consent.contains(document.activeElement)).toBe(true)
    await waitFor(() => expect(navigation.every((button) => button.disabled)).toBe(true))
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Before you sign in' })).toBeNull())
    await waitFor(() =>
      expect(navigation.every((button) => button.isConnected && !button.disabled)).toBe(true),
    )
    expect(screen.getByRole('dialog').querySelector('.setup-header h2')?.textContent).toBe(
      mode === 'fullscreen' ? 'Steam' : 'Your Steam library',
    )
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
      await waitFor(() =>
        expect(document.activeElement).toBe(
          screen.getByRole('button', {
            name:
              mode === 'fullscreen' && index > 0 && index < 8
                ? providerEntries[index]
                : index === 0
                  ? 'Get started'
                  : index === 8
                    ? 'Open my library'
                    : 'Continue',
          }),
        ),
      )
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
    const secret = await openIgdb(mode)
    expect(secret.type).toBe('password')
    fireEvent.change(secret, { target: { value: 'unsaved-secret' } })
    const otherMode = mode === 'desktop' ? 'fullscreen' : 'desktop'
    view.rerender(
      <QueryClientProvider client={client}>
        <Setup mode={otherMode} />
      </QueryClientProvider>,
    )
    expect((await openIgdb(otherMode)).value).toBe('')
    fireEvent.change(screen.getByLabelText('Client secret'), { target: { value: 'discard-again' } })
    if (otherMode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
    fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    await screen.findByRole('heading', { name: 'Your Steam library' })
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect((await openIgdb(otherMode)).value).toBe('')
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
    fireEvent.change(await openIgdb(mode), { target: { value: 'secret' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save credentials' }))
    await waitFor(() =>
      expect(
        (
          screen.getByRole('button', {
            name: mode === 'fullscreen' ? 'Back to setup' : 'Continue',
          }) as HTMLButtonElement
        ).disabled,
      ).toBe(true),
    )
    expect(request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
    finish({ ok: true, status: 200, data: 0 })
    await screen.findByText(
      'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
    )
    expect((screen.getByLabelText('Client secret') as HTMLInputElement).value).toBe('')
    await waitFor(() =>
      expect(
        (
          screen.getByRole('button', {
            name: mode === 'fullscreen' ? 'Back to setup' : 'Continue',
          }) as HTMLButtonElement
        ).disabled,
      ).toBe(false),
    )
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
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
    if (mode === 'fullscreen')
      fireEvent.click(await screen.findByRole('button', { name: 'Choose app settings' }))
    const checkbox = await screen.findByRole(mode === 'fullscreen' ? 'switch' : 'checkbox', {
      name: /Start in fullscreen/,
    })
    await waitFor(() => expect((checkbox as HTMLInputElement).disabled).toBe(false))
    fireEvent.click(checkbox)
    await screen.findByRole('alert')
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
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

it('keeps desktop IGDB saves and feedback outside the field scroller', async () => {
  const { request, client } = fixture(1)
  render(
    <QueryClientProvider client={client}>
      <Setup mode="desktop" />
    </QueryClientProvider>,
  )
  const secret = await openIgdb('desktop')
  const form = screen.getByRole('form', { name: 'IGDB credentials' })
  const fields = form.querySelector('.igdb-settings-fields')!
  expect(fields.contains(secret)).toBe(true)
  expect(fields.contains(screen.getByLabelText('Client ID'))).toBe(true)
  for (const name of ['Save credentials', 'Remove saved credentials']) {
    const action = within(form).getByRole('button', { name })
    expect(fields.contains(action)).toBe(false)
    expect(action.closest('.igdb-settings-actions')).toBeTruthy()
  }
  fireEvent.click(within(form).getByRole('button', { name: 'Save credentials' }))
  const failure = await screen.findByRole('alert')
  expect(fields.contains(failure)).toBe(false)
  expect(document.activeElement).toBe(secret)
  expect(request.mock.calls.some(([input]) => input.route === 'connections.igdb.put')).toBe(false)
})

it.each(['Escape', 'button'] as const)(
  'fullscreen provider %s returns to the same IGDB step and discards the draft',
  async (back) => {
    const { request, client } = fixture(1)
    render(
      <QueryClientProvider client={client}>
        <Setup mode="fullscreen" />
      </QueryClientProvider>,
    )
    const entry = await screen.findByRole('button', { name: 'Set up IGDB metadata' })
    await waitFor(() => expect(document.activeElement).toBe(entry))
    expect(screen.queryByLabelText('Client secret')).toBeNull()
    const secret = await openIgdb('fullscreen')
    expect(screen.getByRole('heading', { name: 'IGDB metadata' })).toBeTruthy()
    fireEvent.change(secret, { target: { value: 'discard-provider-draft' } })
    if (back === 'Escape') fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    else fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
    await screen.findByRole('heading', { name: 'Fill in the details' })
    expect(screen.getByText('SETUP · 2 OF 9')).toBeTruthy()
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Set up IGDB metadata' })),
    )
    expect(
      request.mock.calls.some(
        ([input]) => input.route === 'setup.put' || input.route === 'connections.igdb.put',
      ),
    ).toBe(false)
    expect((await openIgdb('fullscreen')).value).toBe('')
  },
)

describe.each(['Escape', 'button'] as const)('fullscreen %s provider return', (back) => {
  it.each([
    [2, 'Steam'],
    [3, 'Epic'],
    [4, 'GOG'],
    [5, 'Appearance'],
    [6, 'Application'],
    [7, 'Library'],
  ] as const)('step %i opens %s and restores its cursor and focused entry', async (step, title) => {
    const { request, client } = fixture(step)
    render(
      <QueryClientProvider client={client}>
        <Setup mode="fullscreen" appearance={<button>Choose theme</button>} />
      </QueryClientProvider>,
    )
    const entry = await screen.findByRole('button', { name: providerEntries[step] })
    await waitFor(() => expect(document.activeElement).toBe(entry))
    fireEvent.click(entry)
    expect(screen.getByRole('dialog').querySelector('.setup-header h2')?.textContent).toBe(title)
    expect(screen.getByRole('dialog').getAttribute('data-setup-provider')).toBe(String(step))
    if (back === 'Escape') fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    else fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: providerEntries[step] })),
    )
    expect(screen.getByText(`SETUP · ${step + 1} OF 9`)).toBeTruthy()
    expect(request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
    expect(screen.getByRole('dialog').hasAttribute('data-setup-provider')).toBe(false)
  })
})

it('fullscreen controller hints follow wizard depth and focused editable fields', async () => {
  const { client } = fixture(0)
  render(
    <QueryClientProvider client={client}>
      <Setup mode="fullscreen" />
    </QueryClientProvider>,
  )
  const hints = await screen.findByRole('group', { name: 'Setup controls' })
  const labels = () => [...hints.children].map((hint) => hint.textContent?.replace(/\s+/g, ' ').trim())
  expect(labels()).toEqual(['A Select'])
  expect(hints.querySelectorAll('svg')).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Get started' }))
  await screen.findByRole('button', { name: 'Set up IGDB metadata' })
  expect(labels()).toEqual(['A Select', 'B Previous step'])
  const secret = await openIgdb('fullscreen')
  expect(labels()).toEqual(['A Select', 'B Back'])
  act(() => secret.focus())
  expect(labels()).toEqual(['A Select', 'B Back', 'Y Keyboard'])
  expect(hints.querySelectorAll('svg')).toHaveLength(3)
  act(() => screen.getByRole('button', { name: 'Back to setup' }).focus())
  expect(labels()).toEqual(['A Select', 'B Back'])
  fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
  expect(labels()).toEqual(['A Select', 'B Previous step'])
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
