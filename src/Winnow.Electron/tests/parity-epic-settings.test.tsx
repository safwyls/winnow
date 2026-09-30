// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { EpicAccount, Settings } from '../src/renderer/features/Settings'
import type { EpicSignInResult } from '../src/shared/epic'
afterEach(cleanup)
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { resolve, promise }
}
const preparation = {
  attemptId: 'a'.repeat(32),
  expiresAt: new Date(Date.now() + 600000).toISOString(),
  consentNotice: 'Epic fixture notice: account access requires explicit consent.',
}
function fixture(mode: string, result: Partial<EpicSignInResult> = {}) {
  const bridge = {
    prepareEpicSignIn: vi.fn(async () => preparation),
    epicSignIn: vi.fn(async () => ({
      succeeded: true,
      failure: 0,
      persisted: true,
      displayName: 'Fixture account',
      ...result,
    })),
    cancelEpicSignIn: vi.fn(async () => true),
    openEpicSignInInBrowser: vi.fn(async () => {}),
    completeEpicSignIn: vi.fn(async () => ({ succeeded: true, failure: 0, persisted: true })),
    request: vi.fn<(input: any) => Promise<any>>(async () => ({ ok: true, status: 200, data: null })),
  }
  Object.defineProperty(window, 'winnow', { configurable: true, value: bridge })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <div className={`mode-${mode}`}>
        <EpicAccount />
      </div>
    </QueryClientProvider>,
  )
  return { ...bridge, ...view, client }
}
async function consent() {
  fireEvent.click(await screen.findByRole('button', { name: 'Connect Epic Games' }))
  expect(await screen.findByText(preparation.consentNotice)).toBeTruthy()
  fireEvent.click(screen.getByLabelText('I agree to connect this account'))
}
describe.each(['desktop', 'fullscreen'])('%s Epic connection workflow', (mode) => {
  async function settings(epic: { isLive: boolean; displayName: string | null } | null = null) {
    const h = fixture(mode),
      state = { epic }
    h.request.mockImplementation(async (input) => {
      if (input.route === 'connections.epic.signOut') state.epic = null
      return {
        ok: true,
        status: 200,
        data:
          input.route === 'connections.get'
            ? {
                steam: {
                  hasApiKey: false,
                  apiKeyIsAppManaged: false,
                  hasSession: false,
                  sessionUsable: false,
                  sessionExpiresAt: null,
                  sessionAccount: null,
                },
                steamHealth: 0,
                epic: state.epic,
              }
            : input.route === 'plugins.get' || input.route === 'operations.get'
              ? []
              : input.route === 'connections.visibility.get'
                ? { accountConfirmed: false, ownAccountOnly: false, hiddenCount: 0 }
                : null,
      }
    })
    h.rerender(
      <QueryClientProvider client={h.client}>
        <Settings mode={mode as 'desktop' | 'fullscreen'} />
      </QueryClientProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: /^EPIC/ }))
    return { ...h, state }
  }
  it('opening and refreshing settings only reads account state without starting sign-in', async () => {
    const h = await settings()
    await screen.findByText('Installed games are available through the local Epic library.')
    await h.client.invalidateQueries({ queryKey: ['api'] })
    expect(h.prepareEpicSignIn).not.toHaveBeenCalled()
    expect(h.epicSignIn).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Connect Epic Games' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Sign out of Epic' })).toBeNull()
    expect(screen.getByText('NOT SIGNED IN').getAttribute('data-tone')).toBe('quiet')
  })
  it.each([null, 'Account A'])(
    'a live connection renders account name %s or explains its absence without offering another sign-in',
    async (displayName) => {
      const h = await settings({ isLive: true, displayName })
      await screen.findByText(
        displayName ? 'Connected as Account A.' : 'Connected. Epic did not provide a display name.',
      )
      expect(screen.getByRole('button', { name: 'Sign out of Epic' })).toBeTruthy()
      expect(screen.queryByRole('button', { name: 'Sign in to Epic again' })).toBeNull()
      expect(screen.getByText('SIGNED IN').getAttribute('data-tone')).toBe('live')
      expect(h.prepareEpicSignIn).not.toHaveBeenCalled()
    },
  )
  it('successful sign-in keeps its run-only warning after query refresh, then sign-out forgets that account', async () => {
    const h = await settings()
    h.epicSignIn.mockImplementation(async () => {
      h.state.epic = { isLive: true, displayName: 'Account B' }
      return { succeeded: true, failure: 0, persisted: false, displayName: 'Account B' }
    })
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    await screen.findByText('Connected as Account B.')
    expect(screen.getByText(/lasts for this run only/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Connect Epic Games' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Epic' }))
    await screen.findByRole('button', { name: 'Connect Epic Games' })
    expect(screen.queryByText(/Account B/)).toBeNull()
    expect(h.request).toHaveBeenCalledWith(expect.objectContaining({ route: 'connections.epic.signOut' }))
  })
  it('a failed renewal preserves the expired account identity and refreshing does not disturb its pending attempt', async () => {
    const h = await settings({ isLive: false, displayName: 'Account A' }),
      pending = deferred<EpicSignInResult>()
    h.epicSignIn.mockReturnValue(pending.promise as any)
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Epic again' }))
    await screen.findByText(preparation.consentNotice)
    fireEvent.click(screen.getByLabelText('I agree to connect this account'))
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    await h.client.invalidateQueries({ queryKey: ['api'] })
    expect(screen.getByRole('button', { name: 'Cancel Epic sign-in' })).toBeTruthy()
    expect(screen.getByText(/Waiting for Epic sign-in/)).toBeTruthy()
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Sign out of Epic' }).disabled).toBe(true)
    pending.resolve({ succeeded: false, failure: 4, persisted: false })
    await screen.findByText(/Check the current connection/)
    expect(screen.getByText('Epic sign-in expired for Account A. Sign in again to reconnect.')).toBeTruthy()
    expect(screen.getByText('SESSION EXPIRED').getAttribute('data-tone')).toBe('attention')
    expect(screen.getByRole('button', { name: 'Sign in to Epic again' })).toBeTruthy()
  })
  it('shows canonical consent before either embedded or external navigation and refreshes after completion', async () => {
    const h = fixture(mode),
      invalidate = vi.spyOn(h.client, 'invalidateQueries')
    fireEvent.click(screen.getByRole('button', { name: 'Connect Epic Games' }))
    await screen.findByText(preparation.consentNotice)
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Open Epic sign-in window' }).disabled).toBe(
      true,
    )
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Continue in your browser' }).disabled).toBe(
      true,
    )
    expect(h.epicSignIn).not.toHaveBeenCalled()
    expect(h.openEpicSignInInBrowser).not.toHaveBeenCalled()
    fireEvent.click(screen.getByLabelText('I agree to connect this account'))
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    expect(await screen.findByText('Epic Games connected as Fixture account.')).toBeTruthy()
    expect(h.epicSignIn).toHaveBeenCalledWith({ attemptId: preparation.attemptId, consentGranted: true })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['api'] })
    expect(screen.queryByLabelText('Final sign-in address')).toBeNull()
  })
  it('withdraws repeated sign-in actions while pending and cancels without launching fallback', async () => {
    const h = fixture(mode),
      pending = deferred<EpicSignInResult>()
    h.epicSignIn.mockReturnValue(pending.promise as any)
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Open Epic sign-in window' }).disabled).toBe(
      true,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Cancel Epic sign-in' }))
    expect(await screen.findByText('Sign-in cancelled. Nothing was changed.')).toBeTruthy()
    expect(h.openEpicSignInInBrowser).not.toHaveBeenCalled()
    pending.resolve({ succeeded: false, failure: 6, persisted: false })
    expect(screen.queryByLabelText('Final sign-in address')).toBeNull()
  })
  it('can cancel during challenge preparation and ignores its late renderer result', async () => {
    const h = fixture(mode),
      pending = deferred<typeof preparation>()
    h.prepareEpicSignIn.mockReturnValue(pending.promise)
    fireEvent.click(screen.getByRole('button', { name: 'Connect Epic Games' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel Epic sign-in' }))
    await screen.findByText('Sign-in cancelled. Nothing was changed.')
    pending.resolve(preparation)
    await waitFor(() => expect(screen.queryByText(preparation.consentNotice)).toBeNull())
  })
  it('dismisses an expired consent even when main has already released its attempt', async () => {
    const h = fixture(mode)
    await consent()
    h.cancelEpicSignIn.mockResolvedValue(false)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel Epic sign-in' }))
    await screen.findByText('Sign-in cancelled. Nothing was changed.')
    expect(screen.queryByText(preparation.consentNotice)).toBeNull()
  })
  it('shows the run-only persistence limitation without inventing a missing display name', async () => {
    fixture(mode, { persisted: false, displayName: null })
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    expect(await screen.findByText(/Epic did not provide a display name/)).toBeTruthy()
    expect(screen.getByText(/lasts for this run only/)).toBeTruthy()
  })
  it.each([
    [1, /No Epic OAuth client credentials/],
    [2, /rejected the OAuth client credentials/],
    [3, /Codes are single-use/],
    [4, /Check the current connection/],
    [5, /did not understand/],
    [6, /Sign-in cancelled/],
    [7, /window could not open/],
    [8, /without handing back a usable code/],
    [9, /without an Epic account being signed in/],
  ] as const)('explains failure %s with its specific remedy', async (failure, text) => {
    fixture(mode, { succeeded: false, failure, canRetryManually: [7, 8, 9].includes(failure) })
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    expect(await screen.findByText(text)).toBeTruthy()
    expect(!!screen.queryByLabelText('Final sign-in address')).toBe([7, 8, 9].includes(failure))
  })
  it.each([7, 8, 9])(
    'continues embedded failure %s in the manual form without preparing another attempt',
    async (failure) => {
      const h = fixture(mode, { succeeded: false, failure, persisted: false, canRetryManually: true })
      await consent()
      fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
      const input = await screen.findByLabelText<HTMLInputElement>('Final sign-in address')
      expect(input.type).toBe('password')
      expect(h.openEpicSignInInBrowser).not.toHaveBeenCalled()
      expect(screen.queryByRole('button', { name: 'Open Epic sign-in window' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Continue in your browser' }))
      await waitFor(() => expect(input.disabled).toBe(false))
      expect(h.openEpicSignInInBrowser).toHaveBeenCalledExactlyOnceWith({
        attemptId: preparation.attemptId,
        consentGranted: true,
      })
      const callback = 'https://localhost/launcher/authorized?code=PRIVATE-MANUAL-CODE&state=expected'
      fireEvent.change(input, { target: { value: callback } })
      fireEvent.click(screen.getByRole('button', { name: 'Finish connecting' }))
      await screen.findByText('Epic Games connected. Epic did not provide a display name.')
      expect(h.completeEpicSignIn).toHaveBeenCalledExactlyOnceWith({
        attemptId: preparation.attemptId,
        consentGranted: true,
        callback,
      })
      expect(h.prepareEpicSignIn).toHaveBeenCalledOnce()
      expect(h.epicSignIn).toHaveBeenCalledOnce()
      expect(h.cancelEpicSignIn).not.toHaveBeenCalled()
      expect(h.request).not.toHaveBeenCalled()
      expect(screen.queryByLabelText('Final sign-in address')).toBeNull()
      expect(document.body.textContent).not.toContain('PRIVATE-MANUAL-CODE')
    },
  )
  it('opens the system-browser fallback explicitly and submits the masked final address through named IPC only', async () => {
    const h = fixture(mode)
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Continue in your browser' }))
    const input = await screen.findByLabelText('Final sign-in address')
    expect(input.getAttribute('type')).toBe('password')
    const callback = 'https://localhost/launcher/authorized?code=fixture&state=expected'
    fireEvent.change(input, { target: { value: callback } })
    fireEvent.click(screen.getByRole('button', { name: 'Finish connecting' }))
    await waitFor(() =>
      expect(h.completeEpicSignIn).toHaveBeenCalledWith({
        attemptId: preparation.attemptId,
        consentGranted: true,
        callback,
      }),
    )
    expect(h.request).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.queryByLabelText('Final sign-in address')).toBeNull())
  })
  it('clears old failure messages on a fresh attempt and cancels an owned attempt on unmount', async () => {
    const h = fixture(mode, { succeeded: false, failure: 3 })
    await consent()
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    await screen.findByText(/Codes are single-use/)
    fireEvent.click(screen.getByRole('button', { name: 'Connect Epic Games' }))
    await screen.findByText(preparation.consentNotice)
    expect(screen.queryByText(/Codes are single-use/)).toBeNull()
    h.unmount()
    expect(h.cancelEpicSignIn).toHaveBeenCalledTimes(1)
  })
  it('Escape cancels consent without bubbling into fullscreen navigation', async () => {
    const h = fixture(mode),
      parent = vi.fn()
    await consent()
    document.body.addEventListener('keydown', parent)
    try {
      fireEvent.keyDown(screen.getByLabelText('I agree to connect this account'), { key: 'Escape' })
    } finally {
      document.body.removeEventListener('keydown', parent)
    }
    await screen.findByText('Sign-in cancelled. Nothing was changed.')
    expect(parent).not.toHaveBeenCalled()
    expect(h.cancelEpicSignIn).toHaveBeenCalled()
  })
})
