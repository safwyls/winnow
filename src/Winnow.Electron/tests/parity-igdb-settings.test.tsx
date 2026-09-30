// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { IgdbConnectionPanel } from '../src/renderer/features/IgdbSettings'
import type { IgdbConnection } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
const empty: IgdbConnection = {
  clientId: '',
  hasSavedCredentials: false,
  isReadable: false,
  hasConfigurationCredentials: false,
}
const saved: IgdbConnection = {
  ...empty,
  clientId: 'previous-id',
  hasSavedCredentials: true,
  isReadable: true,
}
function fixture(initial = empty, override?: (input: ApiRequest) => unknown) {
  let stored = { ...initial }
  const request = vi.fn(async (input: ApiRequest) => {
    const result = await override?.(input)
    if (result !== undefined) return result
    let data: unknown = stored
    if (input.route === 'connections.igdb.put') {
      const body = input.body as { clientId: string; clientSecret: string }
      stored = { ...stored, clientId: body.clientId.trim(), hasSavedCredentials: true, isReadable: true }
      data = 0
    }
    if (input.route === 'connections.igdb.delete') {
      stored = { ...stored, clientId: '', hasSavedCredentials: false, isReadable: false }
      data = stored.hasConfigurationCredentials
    }
    return { ok: true, status: 200, data }
  })
  const openExternal = vi.fn(async (_url: string) => ({ opened: true }))
  Object.defineProperty(window, 'winnow', { value: { request, openExternal }, configurable: true })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return {
    request,
    openExternal,
    client,
    stored: () => stored,
    update: (value: IgdbConnection) => {
      stored = value
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
async function fields(id = 'client-id', secret = 'entered-secret') {
  const client = (await screen.findByLabelText('Client ID')) as HTMLInputElement
  const password = screen.getByLabelText('Client secret') as HTMLInputElement
  fireEvent.change(client, { target: { value: id } })
  fireEvent.change(password, { target: { value: secret } })
  return { client, password }
}

describe.each(['desktop', 'fullscreen'] as const)('%s IGDB settings parity', (mode) => {
  it.each([
    [empty, 'Add your Twitch application credentials'],
    [saved, 'Credentials are saved on this device'],
    [{ ...saved, isReadable: false }, 'Saved credentials need to be re-entered'],
    [
      { ...empty, hasConfigurationCredentials: true },
      'IGDB credentials are supplied by environment variables or configuration',
    ],
  ] as const)(
    'loads the credential state %j without copying or revealing a secret',
    async (snapshot, status) => {
      const { wrapper, request } = fixture(snapshot)
      render(<IgdbConnectionPanel mode={mode} />, { wrapper })
      expect(((await screen.findByLabelText('Client ID')) as HTMLInputElement).value).toBe(snapshot.clientId)
      const secret = screen.getByLabelText('Client secret') as HTMLInputElement
      expect(secret.value).toBe('')
      expect(secret.type).toBe('password')
      expect(screen.getByText(new RegExp(status))).toBeTruthy()
      expect(request.mock.calls.every(([input]) => input.route === 'connections.igdb.get')).toBe(true)
    },
  )
  it('explains configuration fallback beside an unreadable saved pair', async () => {
    const { wrapper } = fixture({ ...saved, isReadable: false, hasConfigurationCredentials: true })
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    await screen.findByText(/Saved credentials need to be re-entered.*environment variables or configuration/)
    expect((screen.getByLabelText('Client secret') as HTMLInputElement).value).toBe('')
  })
  it.each([
    ['', 'entered-secret'],
    ['client-id', '   '],
  ])('rejects missing fields locally without writing: %j', async (id, secret) => {
    const { wrapper, request } = fixture()
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    const inputs = await fields(id, secret)
    fireEvent.submit(screen.getByRole('form', { name: 'IGDB credentials' }))
    expect((await screen.findByRole('alert')).textContent).toContain(
      'Enter both your client ID and client secret',
    )
    expect(request.mock.calls.filter(([input]) => input.route === 'connections.igdb.put')).toHaveLength(0)
    expect(document.activeElement).toBe(!id.trim() ? inputs.client : inputs.password)
  })
  it('acknowledges an accepted save with normalized identity clear secret and queued metadata refresh', async () => {
    const { wrapper, request, stored } = fixture()
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    const inputs = await fields(' client-id ', ' entered-secret ')
    fireEvent.click(screen.getByRole('button', { name: 'Save credentials' }))
    await screen.findByText(
      'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
    )
    expect(inputs.client.value).toBe('client-id')
    expect(inputs.password.value).toBe('')
    expect(stored()).toEqual({ ...saved, clientId: 'client-id' })
    expect(request.mock.calls.find(([input]) => input.route === 'connections.igdb.put')![0].body).toEqual({
      clientId: 'client-id',
      clientSecret: ' entered-secret ',
    })
    expect(document.body.textContent).not.toContain('Restart')
  })
  it('preserves the saved pair and masked draft when protection is refused', async () => {
    const { wrapper, stored } = fixture(saved, (input) =>
      input.route === 'connections.igdb.put' ? { ok: true, status: 200, data: 2 } : undefined,
    )
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    const inputs = await fields()
    fireEvent.click(screen.getByRole('button', { name: 'Save credentials' }))
    expect((await screen.findByRole('alert')).textContent).toContain('nothing was saved')
    expect(inputs.password.value).toBe('entered-secret')
    expect(inputs.password.type).toBe('password')
    expect(stored()).toEqual(saved)
    expect(document.body.textContent).not.toContain('entered-secret')
  })
  it.each([false, true])(
    'removes accepted credentials immediately and explains remaining configuration: %s',
    async (configured) => {
      const { wrapper, stored } = fixture({ ...saved, hasConfigurationCredentials: configured })
      render(<IgdbConnectionPanel mode={mode} />, { wrapper })
      const inputs = await fields('another-draft', 'draft-secret')
      fireEvent.click(screen.getByRole('button', { name: 'Remove saved credentials' }))
      const status = await screen.findByText(/Saved credentials removed. The change is active now./)
      expect(status.textContent).toContain('The change is active now.')
      expect(status.textContent?.includes('environment variables')).toBe(configured)
      expect(inputs.client.value).toBe('')
      expect(inputs.password.value).toBe('')
      expect(stored().hasSavedCredentials).toBe(false)
      await waitFor(() =>
        expect(
          (screen.getByRole('button', { name: 'Remove saved credentials' }) as HTMLButtonElement).disabled,
        ).toBe(false),
      )
    },
  )
  it.each([false, true])(
    'keeps failed credential mutation drafts and hides exception details, remove %s',
    async (remove) => {
      let refuse = true
      const route = remove ? 'connections.igdb.delete' : 'connections.igdb.put'
      const { wrapper, stored } = fixture(saved, (input) => {
        if (input.route === route && refuse) throw new Error('entered-secret filesystem exception')
      })
      render(<IgdbConnectionPanel mode={mode} />, { wrapper })
      const inputs = await fields()
      const button = screen.getByRole('button', {
        name: remove ? 'Remove saved credentials' : 'Save credentials',
      })
      fireEvent.click(button)
      const alert = await screen.findByRole('alert')
      expect(alert.textContent).toContain(`Could not ${remove ? 'remove' : 'save'} IGDB credentials`)
      expect(document.body.textContent).not.toContain('entered-secret')
      expect(document.body.textContent).not.toContain('filesystem exception')
      expect(inputs.client.value).toBe('client-id')
      expect(inputs.password.value).toBe('entered-secret')
      expect(stored()).toEqual(saved)
      refuse = false
      fireEvent.click(button)
      await screen.findByText(
        remove
          ? 'Saved credentials removed. The change is active now.'
          : 'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
      )
      expect(inputs.password.value).toBe('')
    },
  )
  it('makes failed reads actionable without exposing service exception details', async () => {
    let refuse = true
    const { wrapper } = fixture(saved, (input) =>
      input.route === 'connections.igdb.get' && refuse
        ? { ok: false, status: 500, message: 'entered-secret storage path' }
        : undefined,
    )
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    expect((await screen.findByRole('alert')).textContent).toBe(
      'Could not read IGDB settings. Try loading them again.',
    )
    expect(document.body.textContent).not.toContain('entered-secret')
    refuse = false
    fireEvent.click(screen.getByRole('button', { name: 'Retry IGDB settings' }))
    expect(((await screen.findByLabelText('Client ID')) as HTMLInputElement).value).toBe('previous-id')
  })
  it.each(['success', 'refusal', 'exception'])(
    'uses the shared Twitch setup destination and handles %s',
    async (outcome) => {
      const { wrapper, openExternal } = fixture()
      if (outcome === 'refusal') openExternal.mockResolvedValue({ opened: false })
      if (outcome === 'exception') openExternal.mockRejectedValue(new Error('entered-secret exception'))
      render(<IgdbConnectionPanel mode={mode} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Get IGDB credentials' }))
      await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://dev.twitch.tv/console/apps'))
      if (outcome !== 'success')
        expect((await screen.findByRole('alert')).textContent).toBe(
          'Could not open the page. Visit dev.twitch.tv/console/apps to create a Twitch application.',
        )
      else expect(screen.queryByRole('alert')).toBeNull()
      expect(document.body.textContent).not.toContain('entered-secret')
    },
  )
  it('refreshes untouched client identity while preserving an ordinary in-progress draft', async () => {
    const fixture_ = fixture(saved)
    render(<IgdbConnectionPanel mode={mode} />, { wrapper: fixture_.wrapper })
    const input = (await screen.findByLabelText('Client ID')) as HTMLInputElement
    fixture_.update({ ...saved, clientId: 'external-change' })
    await act(async () => {
      await fixture_.client.invalidateQueries({ queryKey: ['api'] })
    })
    await waitFor(() => expect(input.value).toBe('external-change'))
    fireEvent.change(input, { target: { value: 'unsaved-draft' } })
    fixture_.update({ ...saved, clientId: 'next-external-change' })
    await act(async () => {
      await fixture_.client.invalidateQueries({ queryKey: ['api'] })
    })
    await waitFor(() =>
      expect(
        fixture_.client.getQueryData<IgdbConnection>(['api', 'connections.igdb.get', undefined])?.clientId,
      ).toBe('next-external-change'),
    )
    expect(input.value).toBe('unsaved-draft')
  })
  it('admits only one explicit save and disables credential editing until it completes', async () => {
    let release!: () => void
    const pending = new Promise<void>((resolve) => {
      release = resolve
    })
    const { wrapper, request } = fixture(empty, async (input) => {
      if (input.route === 'connections.igdb.put') await pending
    })
    render(<IgdbConnectionPanel mode={mode} />, { wrapper })
    const inputs = await fields()
    const form = screen.getByRole('form', { name: 'IGDB credentials' })
    fireEvent.submit(form)
    fireEvent.submit(form)
    await waitFor(() => expect(inputs.password.disabled).toBe(true))
    expect(inputs.client.disabled).toBe(true)
    expect(request.mock.calls.filter(([input]) => input.route === 'connections.igdb.put')).toHaveLength(1)
    await act(async () => release())
    await screen.findByText(
      'Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.',
    )
    expect(inputs.password.value).toBe('')
    expect(inputs.password.disabled).toBe(false)
  })
})
