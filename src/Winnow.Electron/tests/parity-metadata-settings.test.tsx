// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import { MetadataSyncSettings } from '../src/renderer/features/MetadataSyncSettings'
import { ArtworkSourcePreferences } from '../src/renderer/features/SettingsPreferences'
import { Settings } from '../src/renderer/features/Settings'
import { metadataSync } from '../src/renderer/features/metadata-sync'
import { clearViewState } from '../src/renderer/viewState'

afterEach(() => {
  cleanup()
  for (const key of ['desktop:settings:tab', 'fullscreen:settings:tab', 'artwork:source-order:status'])
    clearViewState(key)
})
function fixture(saved: Record<string, string> = {}) {
  const values = { ...saved }
  let refuse = false
  let hold: Promise<void> | undefined
  let state = 'completed',
    result = 0
  const transport = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route === 'preferences.presentation.put') {
      if (hold) await hold
      if (refuse) return { ok: false, status: 500, message: 'private storage failure' }
      values[String(input.params!.preference)] = (input.body as { value: string }).value
    }
    if (input.route === 'preferences.presentation.get')
      data = Object.entries(values).map(([preference, value]) => ({ preference, value }))
    if (input.route === 'preferences.artworkSources')
      data = [
        { id: 'steam', label: 'Steam' },
        { id: 'plugin:steamgriddb', label: 'SteamGridDB' },
        { id: 'igdb', label: 'IGDB' },
      ]
    if (input.route === 'connections.igdb.get')
      data = {
        clientId: '',
        isReadable: false,
        hasSavedCredentials: false,
        hasConfigurationCredentials: false,
      }
    if (input.route === 'library.get') data = { games: [], lists: [] }
    if (input.route === 'operations.get') data = []
    if (input.route === 'operations.metadata') {
      if (hold) await hold
      data = {
        id: (input.body as { operationId: string }).operationId,
        kind: 'metadata-sync',
        state,
        metadataResult: result,
        message: 'private diagnostic detail',
      }
    }
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request: transport } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  })
  return {
    values,
    transport,
    client,
    outcome(value: number, status = 'completed') {
      result = value
      state = status
    },
    refuse(value: boolean) {
      refuse = value
    },
    hold() {
      let release!: () => void
      hold = new Promise<void>((resolve) => {
        release = resolve
      })
      return () => {
        release()
        hold = undefined
      }
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
const syncCalls = (f: ReturnType<typeof fixture>) =>
  f.transport.mock.calls.filter(([input]) => input.route === 'operations.metadata')

it('shares busy state between both buttons, refuses rapid activation and survives unmounting all settings', async () => {
  const f = fixture(),
    release = f.hold()
  let view = render(
    <>
      <MetadataSyncSettings />
      <MetadataSyncSettings mode="fullscreen" />
    </>,
    { wrapper: f.wrapper },
  )
  const [desktop, fullscreen] = screen.getAllByRole('button', {
    name: 'Sync metadata now',
  }) as HTMLButtonElement[]
  fullscreen.focus()
  fireEvent.click(fullscreen)
  fireEvent.click(fullscreen)
  fireEvent.click(desktop)
  expect(desktop.disabled).toBe(true)
  expect(fullscreen.getAttribute('aria-disabled')).toBe('true')
  expect(document.activeElement).toBe(fullscreen)
  expect(syncCalls(f)).toHaveLength(1)
  for (const status of screen.getAllByRole('status')) {
    expect(status.textContent).toBe('Starting metadata sync…')
    expect(status.getAttribute('aria-live')).toBe('polite')
  }
  view.unmount()
  view = render(<MetadataSyncSettings />, { wrapper: f.wrapper })
  expect((screen.getByRole('button', { name: 'Sync metadata now' }) as HTMLButtonElement).disabled).toBe(true)
  await act(async () => {
    release()
    await metadataSync(f.client).sync()
  })
  expect(screen.getByRole('status').textContent).toContain('finished')
  expect((screen.getByRole('button', { name: 'Sync metadata now' }) as HTMLButtonElement).disabled).toBe(
    false,
  )
  expect(syncCalls(f)).toHaveLength(1)
})

it.each([
  [1, 'credentials in IGDB metadata'],
  [2, 'Try again'],
  [3, 'Reopen the library'],
  [0, 'Check your connection'],
])('shares actionable outcome %s and enables retry on either surface', async (result, guidance) => {
  const f = fixture()
  f.outcome(Number(result), result === 0 ? 'failed' : 'completed')
  render(
    <>
      <MetadataSyncSettings />
      <MetadataSyncSettings mode="fullscreen" />
    </>,
    { wrapper: f.wrapper },
  )
  const buttons = screen.getAllByRole('button', { name: 'Sync metadata now' })
  fireEvent.click(buttons[0])
  await waitFor(() =>
    expect(
      screen.getAllByRole('status').every((status) => status.textContent?.includes(String(guidance))),
    ).toBe(true),
  )
  expect(document.body.textContent).not.toContain('private diagnostic')
  f.outcome(0)
  fireEvent.click(buttons[1])
  await waitFor(() =>
    expect(screen.getAllByRole('status').every((status) => status.textContent?.includes('finished'))).toBe(
      true,
    ),
  )
  expect(syncCalls(f)).toHaveLength(2)
  expect(syncCalls(f)[0][0].body).not.toEqual(syncCalls(f)[1][0].body)
})

it('shares the Operations refresh with Metadata settings instead of allowing a second operation', async () => {
  const f = fixture(),
    release = f.hold()
  render(<Settings />, { wrapper: f.wrapper })
  const tabs = screen.getByRole('navigation', { name: 'Settings section' })
  fireEvent.click(within(tabs).getByRole('button', { name: 'Operations' }))
  const refresh = screen.getByRole('button', { name: 'Refresh library metadata' }) as HTMLButtonElement
  fireEvent.click(refresh)
  expect(refresh.disabled).toBe(true)
  await screen.findByText('Starting metadata sync…')
  fireEvent.click(within(tabs).getByRole('button', { name: 'Metadata & artwork' }))
  const sync = screen.getByRole('button', { name: 'Sync metadata now' }) as HTMLButtonElement
  expect(sync.disabled).toBe(true)
  fireEvent.click(sync)
  expect(syncCalls(f)).toHaveLength(1)
  f.outcome(1)
  await act(async () => {
    release()
    await metadataSync(f.client).sync()
  })
  await screen.findByText('Add credentials in IGDB metadata, then try again.')
  fireEvent.click(within(tabs).getByRole('button', { name: 'Operations' }))
  await screen.findByText('Add credentials in IGDB metadata, then try again.')
  expect(
    (screen.getByRole('button', { name: 'Refresh library metadata' }) as HTMLButtonElement).disabled,
  ).toBe(false)
})

it.each(['IGDB metadata', 'Artwork source order'] as const)(
  'opens the separate %s reading page and returns to its originating action',
  async (title) => {
    const f = fixture()
    render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
    fireEvent.click(
      within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', {
        name: 'Metadata & artwork',
      }),
    )
    fireEvent.click(await screen.findByRole('button', { name: title }))
    const region = await screen.findByRole('region', { name: title })
    expect(within(region).getByRole('heading', { level: 1 }).textContent).toBe(title)
    expect((await within(region).findByRole('heading', { level: 2 })).textContent).toBe(
      title === 'IGDB metadata' ? 'Credentials' : 'Sources · preferred first',
    )
    expect(region.querySelectorAll('hr').length).toBeGreaterThan(0)
    expect(screen.queryByRole('navigation', { name: 'Settings section' })).toBeNull()
    await waitFor(() =>
      expect(document.activeElement).toBe(
        within(region).getByRole('button', {
          name: title === 'IGDB metadata' ? 'Get IGDB credentials' : 'Move Steam down',
        }),
      ),
    )
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: title })))
  },
)

it.each(['desktop', 'fullscreen'] as const)(
  '%s persists and reloads source order, refuses endpoints and migrates the provider id',
  async (mode) => {
    const f = fixture({ ArtworkSourceOrder: 'steam,steamgriddb,igdb' })
    let view = render(<ArtworkSourcePreferences mode={mode} />, { wrapper: f.wrapper })
    const steamUp = (await screen.findByRole('button', { name: 'Move Steam up' })) as HTMLButtonElement
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Move SteamGridDB up' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
    expect(steamUp.disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Move IGDB down' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(steamUp)
    expect(f.values.ArtworkSourceOrder).toBe('steam,steamgriddb,igdb')
    const up = screen.getByRole('button', { name: 'Move SteamGridDB up' })
    up.focus()
    fireEvent.click(up)
    await screen.findByText('Artwork source order saved.')
    expect(f.values.ArtworkSourceOrder).toBe('plugin:steamgriddb,steam,igdb')
    if (mode === 'fullscreen')
      await waitFor(() =>
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Move SteamGridDB down' })),
      )
    view.unmount()
    f.client.clear()
    view = render(<ArtworkSourcePreferences mode={mode} />, { wrapper: f.wrapper })
    await screen.findByRole('button', { name: 'Move SteamGridDB down' })
    expect(screen.getAllByRole('listitem').map((item) => item.querySelector('span')!.textContent)).toEqual([
      'SteamGridDB',
      'Steam',
      'IGDB',
    ])
    fireEvent.click(screen.getByRole('button', { name: 'Move SteamGridDB down' }))
    await waitFor(() => expect(f.values.ArtworkSourceOrder).toBe('steam,plugin:steamgriddb,igdb'))
  },
)

it('focuses the child page retry after credential loading fails and restores the form on retry', async () => {
  const f = fixture(),
    original = f.transport.getMockImplementation()!
  let refuse = true
  f.transport.mockImplementation(async (input) =>
    input.route === 'connections.igdb.get' && refuse
      ? { ok: false, status: 500, message: 'private read diagnostic' }
      : original(input),
  )
  render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
  fireEvent.click(
    within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', {
      name: 'Metadata & artwork',
    }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'IGDB metadata' }))
  const retry = await screen.findByRole('button', { name: 'Retry IGDB settings' })
  await waitFor(() => expect(document.activeElement).toBe(retry))
  expect(document.body.textContent).not.toContain('private read')
  refuse = false
  fireEvent.click(retry)
  await screen.findByRole('form', { name: 'IGDB credentials' })
  await waitFor(() =>
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Get IGDB credentials' })),
  )
})

it('retains confirmed artwork order after refusal and retries with useful guidance', async () => {
  const f = fixture()
  f.refuse(true)
  render(<ArtworkSourcePreferences mode="fullscreen" />, { wrapper: f.wrapper })
  const up = await screen.findByRole('button', { name: 'Move SteamGridDB up' })
  up.focus()
  fireEvent.click(up)
  await screen.findByText(/Could not save artwork source order/)
  expect(document.body.textContent).not.toContain('private storage')
  expect(screen.getAllByRole('listitem')[0].textContent).toContain('Steam')
  expect(f.values.ArtworkSourceOrder).toBeUndefined()
  f.refuse(false)
  fireEvent.click(up)
  await screen.findByText('Artwork source order saved.')
  expect(f.values.ArtworkSourceOrder).toBe('plugin:steamgriddb,steam,igdb')
})

it('does not reclaim focus when an artwork write finishes after navigation', async () => {
  const f = fixture()
  render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
  fireEvent.click(
    within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', {
      name: 'Metadata & artwork',
    }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Artwork source order' }))
  const up = await screen.findByRole('button', { name: 'Move SteamGridDB up' })
  const release = f.hold()
  up.focus()
  fireEvent.click(up)
  fireEvent.keyDown(up, { key: 'Escape' })
  const origin = screen.getByRole('button', { name: 'Artwork source order' })
  await waitFor(() => expect(document.activeElement).toBe(origin))
  await act(async () => {
    release()
  })
  await waitFor(() => expect(f.values.ArtworkSourceOrder).toBe('plugin:steamgriddb,steam,igdb'))
  expect(document.activeElement).toBe(origin)
  fireEvent.click(origin)
  await screen.findByText('Artwork source order saved.')
})
