// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PluginCard, PluginSettings } from '../src/renderer/features/PluginSettings'
import { Settings } from '../src/renderer/features/Settings'
import { ArtworkSourcePreferences } from '../src/renderer/features/SettingsPreferences'
import type { PluginSetting, PluginSnapshot } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'

function field(key: string, label: string, overrides: Partial<PluginSetting> = {}): PluginSetting {
  return {
    key,
    label,
    value: '',
    isSecret: false,
    isBoolean: false,
    isAdvanced: false,
    isRequired: false,
    hasStoredSecret: false,
    ...overrides,
  }
}
const artwork: PluginSnapshot = {
  id: 'community-artwork',
  name: 'Community artwork',
  version: '2.0',
  description: 'Community covers',
  capabilities: 'Artwork',
  enabled: true,
  isLoaded: true,
  restartRequired: false,
  status: 'Active',
  canConfigure: true,
  hasAccount: false,
  accountConnected: false,
  websiteUrl: 'https://example.com/',
  settings: [
    field('apiKey', 'API key', {
      isSecret: true,
      hasStoredSecret: true,
      value: 'must-not-load',
      setupUrl: 'https://example.com/api',
    }),
    field('language', 'Language', { value: 'en', isRequired: true }),
  ],
}
const xbox: PluginSnapshot = {
  ...artwork,
  id: 'xbox',
  name: 'Xbox',
  version: '1.0',
  capabilities: 'Library',
  hasAccount: true,
  accountHosts: ['login.example.com'],
  settings: [
    field('local', 'Scan installed games', { isBoolean: true, value: 'true' }),
    field('override', 'Application ID override', { isAdvanced: true, value: 'existing-override' }),
    field('secret', 'Advanced secret', { isAdvanced: true, isSecret: true, hasStoredSecret: true }),
  ],
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const clients: QueryClient[] = []
function fixture(
  snapshots: PluginSnapshot[] = [structuredClone(artwork), structuredClone(xbox)],
  respond?: (input: ApiRequest) => unknown | Promise<unknown>,
  native = {},
) {
  const request = vi.fn(async (input: ApiRequest) => {
    let data = await respond?.(input)
    if (data === undefined) {
      data =
        input.route === 'plugins.get'
          ? snapshots
          : input.route === 'connections.get'
            ? { steam: {} }
            : input.route === 'operations.get' || input.route === 'preferences.presentation.get'
              ? []
              : null
    }
    return { ok: true, status: 200, data: structuredClone(data) }
  })
  const openExternal = vi.fn(async () => ({ opened: true }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      openExternal,
      openDataFolder: vi.fn(async () => {}),
      restartBackend: vi.fn(async () => {}),
      ...native,
    },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  return {
    request,
    client,
    snapshots,
    openExternal,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
afterEach(() => {
  cleanup()
  clearViewState('plugins:service-restart')
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:settings:tab`)
    clearViewState(`${mode}:plugins:selected`)
    for (const id of ['community-artwork', 'xbox']) clearViewState(`${mode}:plugin:${id}:drafts`)
  }
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe.each(['desktop', 'fullscreen'] as const)('%s plugin settings source contracts', (mode) => {
  it('orders three named artwork providers with working move boundaries', async () => {
    let order = 'plugin:first,plugin:third,plugin:second'
    const { wrapper, request } = fixture(undefined, (input) => {
      if (input.route === 'preferences.presentation.get')
        return [{ preference: 'ArtworkSourceOrder', value: order }]
      if (input.route === 'preferences.artworkSources')
        return ['first', 'second', 'third'].map((id) => ({
          id: `plugin:${id}`,
          label: `${id[0].toUpperCase()}${id.slice(1)} artwork`,
        }))
      if (input.route === 'preferences.presentation.put') order = (input.body as { value: string }).value
    })
    render(
      <div className={`mode-${mode}`}>
        <ArtworkSourcePreferences />
      </div>,
      { wrapper },
    )
    const down = await screen.findByRole('button', { name: 'Move Third artwork down' })
    expect((down as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(down)
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'preferences.presentation.put',
        params: { preference: 'ArtworkSourceOrder' },
        body: { value: 'plugin:first,plugin:second,plugin:third' },
      }),
    )
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Move Third artwork down' }) as HTMLButtonElement).disabled,
      ).toBe(true),
    )
    expect(
      (screen.getByRole('button', { name: 'Move Third artwork up' }) as HTMLButtonElement).disabled,
    ).toBe(false)
    expect(
      (screen.getByRole('button', { name: 'Move First artwork up' }) as HTMLButtonElement).disabled,
    ).toBe(true)
  })
  it('loads lazily on first opening, refreshes on return and preserves the selected runtime tab', async () => {
    const { request, wrapper } = fixture()
    render(<Settings mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Platforms' }))
    await screen.findByRole('button', { name: 'GOG' })
    expect(request.mock.calls.some(([input]) => input.route === 'plugins.get')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Plugins' }))
    expect(
      (await screen.findByRole('tab', { name: 'Community artwork' })).getAttribute('aria-selected'),
    ).toBe('true')
    fireEvent.click(screen.getByRole('tab', { name: 'Xbox' }))
    fireEvent.click(screen.getByRole('button', { name: 'Metadata & artwork' }))
    fireEvent.click(screen.getByRole('button', { name: 'Plugins' }))
    expect((await screen.findByRole('tab', { name: 'Xbox' })).getAttribute('aria-selected')).toBe('true')
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'plugins.get')).toHaveLength(2),
    )
  })
  it('separates runtime names, unloaded diagnostics and ZIP help, retaining tabs until restart', async () => {
    const waiting = { ...xbox, isLoaded: false, restartRequired: true, status: 'Waiting for restart' }
    const invalid = {
      ...xbox,
      id: 'broken',
      name: 'broken.zip',
      canConfigure: false,
      isLoaded: false,
      status: 'Could not unpack plugin ZIP.',
    }
    const { wrapper, snapshots, client } = fixture([waiting, invalid, structuredClone(artwork)])
    render(<PluginSettings mode={mode} />, { wrapper })
    await screen.findByRole('region', { name: 'Community artwork settings' })
    expect(screen.queryByRole('tab', { name: 'Xbox' })).toBeNull()
    fireEvent.click(screen.getByRole('tab', { name: 'Manage plugins' }))
    expect(screen.getByText('Community artwork · 2.0')).not.toBeNull()
    expect(screen.getByText(/ZIPs unpack automatically/)).not.toBeNull()
    expect(screen.getByText('Could not unpack plugin ZIP.')).not.toBeNull()
    const activation = within(screen.getByRole('region', { name: 'broken.zip settings' })).queryByRole(
      'checkbox',
      {
        name: 'Disable plugin: broken.zip',
      },
    ) as HTMLInputElement | null
    if (mode === 'desktop') expect(activation?.disabled).toBe(true)
    else expect(activation).toBeNull()
    snapshots[2].enabled = false
    snapshots[2].restartRequired = true
    await act(() => client.invalidateQueries({ queryKey: ['api', 'plugins.get'] }))
    expect(screen.getByRole('tab', { name: 'Community artwork' })).not.toBeNull()
    expect(screen.getByRole('tab', { name: 'Manage plugins' }).getAttribute('aria-selected')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Open plugins folder' }))
    expect(window.winnow.openDataFolder).toHaveBeenCalledWith('plugins')
  })
  it('starts empty catalogs in Manage plugins with an explicit runtime empty state', async () => {
    const { wrapper } = fixture([])
    render(<PluginSettings mode={mode} />, { wrapper })
    expect(await screen.findByText('No plugins are loaded in this session.')).not.toBeNull()
    expect(screen.getByRole('tab', { name: 'Manage plugins' }).getAttribute('aria-selected')).toBe('true')
    expect((screen.getByRole('button', { name: 'Install provider' }) as HTMLButtonElement).disabled).toBe(
      true,
    )
  })
  it('retains ordinary drafts through tab switches and slow reloads while secrets clear on departure', async () => {
    const pending = deferred<PluginSnapshot[]>()
    let hold = false
    const { wrapper, client } = fixture(undefined, (input) =>
      input.route === 'plugins.get' && hold ? pending.promise : undefined,
    )
    render(<PluginSettings mode={mode} />, { wrapper })
    const secret = await screen.findByLabelText('Community artwork API key')
    expect(secret.getAttribute('type')).toBe('password')
    expect((secret as HTMLInputElement).value).toBe('')
    hold = true
    let reload!: Promise<void>
    act(() => {
      reload = client.invalidateQueries({ queryKey: ['api', 'plugins.get'] })
    })
    fireEvent.change(secret, { target: { value: 'just-entered-secret' } })
    fireEvent.change(screen.getByLabelText('Community artwork Language'), { target: { value: 'fr' } })
    await act(async () => {
      pending.resolve([structuredClone(artwork), structuredClone(xbox)])
      await reload
    })
    expect((secret as HTMLInputElement).value).toBe('just-entered-secret')
    expect((screen.getByLabelText('Community artwork Language') as HTMLInputElement).value).toBe('fr')
    fireEvent.click(screen.getByRole('tab', { name: 'Xbox' }))
    fireEvent.click(screen.getByRole('tab', { name: 'Community artwork' }))
    expect((screen.getByLabelText('Community artwork API key') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Community artwork Language') as HTMLInputElement).value).toBe('fr')
  })
  it('accepts later authoritative settings once a saved ordinary draft has been acknowledged', async () => {
    const snapshots = [structuredClone(artwork)]
    const { wrapper, client } = fixture(snapshots, (input) => {
      if (input.route === 'plugins.settings') snapshots[0].settings[1].value = 'fr'
    })
    render(<PluginSettings mode={mode} />, { wrapper })
    fireEvent.change(await screen.findByLabelText('Community artwork Language'), { target: { value: 'fr' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Community artwork settings' }))
    await screen.findByText('Provider settings saved.')
    expect((screen.getByLabelText('Community artwork Language') as HTMLInputElement).value).toBe('fr')
    snapshots[0].settings[1].value = 'ja'
    await act(() => client.invalidateQueries({ queryKey: ['api', 'plugins.get'] }))
    await waitFor(() =>
      expect((screen.getByLabelText('Community artwork Language') as HTMLInputElement).value).toBe('ja'),
    )
  })
  it('preserves collapsed advanced values and secret replacements, then clears secrets after saving', async () => {
    const { request, wrapper } = fixture()
    render(<PluginCard plugin={xbox} mode={mode} />, { wrapper })
    expect(screen.queryByLabelText('Xbox Application ID override')).toBeNull()
    fireEvent.click(screen.getByLabelText('Xbox Scan installed games'))
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await screen.findByText('Provider settings saved.')
    expect(request).toHaveBeenCalledWith({
      route: 'plugins.settings',
      params: { pluginId: 'xbox' },
      body: { values: { local: 'false', override: 'existing-override' } },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Show advanced settings: Xbox' }))
    fireEvent.change(screen.getByLabelText('Xbox Advanced secret'), { target: { value: 'replacement' } })
    fireEvent.click(screen.getByRole('button', { name: 'Hide advanced settings: Xbox' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'plugins.settings',
        params: { pluginId: 'xbox' },
        body: { values: { local: 'false', override: 'existing-override', secret: 'replacement' } },
      }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Show advanced settings: Xbox' }))
    await waitFor(() =>
      expect((screen.getByLabelText('Xbox Advanced secret') as HTMLInputElement).value).toBe(''),
    )
  })
  it('reveals a missing required advanced value before any save and masks private storage failures', async () => {
    const settings = [field('override', 'Application ID override', { isAdvanced: true, isRequired: true })]
    const { wrapper, request } = fixture(undefined, (input) => {
      if (input.route === 'plugins.settings') throw Error('private-value password disk detail')
    })
    render(<PluginCard plugin={{ ...xbox, settings }} mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    expect(screen.getByRole('alert').textContent).toContain('Enter application id override before saving.')
    expect(document.activeElement).toBe(screen.getByLabelText('Xbox Application ID override'))
    expect(request).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Xbox Application ID override'), {
      target: { value: 'private-value' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toContain('Could not save plugin settings.'),
    )
    expect(screen.getByRole('alert').textContent).not.toContain('private-value')
    expect((screen.getByRole('button', { name: 'Save Xbox settings' }) as HTMLButtonElement).disabled).toBe(
      false,
    )
  })
  it('locks competing writes and account actions during save and never restores a departed secret', async () => {
    const pending = deferred<void>()
    const { wrapper } = fixture([structuredClone(xbox)], (input) =>
      input.route === 'plugins.settings' ? pending.promise : undefined,
    )
    render(<PluginSettings mode={mode} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'Show advanced settings: Xbox' }))
    fireEvent.change(screen.getByLabelText('Xbox Advanced secret'), { target: { value: 'private-value' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await waitFor(() =>
      expect((screen.getByLabelText('Xbox Advanced secret') as HTMLButtonElement).disabled).toBe(true),
    )
    expect(
      (screen.getByRole('checkbox', { name: 'Disable plugin: Xbox' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    expect((screen.getByRole('button', { name: 'Refresh Xbox' }) as HTMLButtonElement).disabled).toBe(true)
    expect(
      (screen.getByRole('button', { name: 'Remove saved Xbox Advanced secret' }) as HTMLButtonElement)
        .disabled,
    ).toBe(true)
    expect((screen.getByRole('button', { name: 'Sign in to Xbox' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('tab', { name: 'Manage plugins' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
      ).toBe(true),
    )
    await act(async () => pending.resolve())
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
    fireEvent.click(screen.getByRole('tab', { name: 'Xbox' }))
    expect(screen.queryByLabelText('Xbox Advanced secret')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Show advanced settings: Xbox' }))
    expect((screen.getByLabelText('Xbox Advanced secret') as HTMLInputElement).value).toBe('')
  })
  it('removes saved secrets explicitly and refresh requires an enabled loaded configurable plugin', async () => {
    const { wrapper, request } = fixture()
    const view = render(<PluginCard plugin={artwork} mode={mode} />, { wrapper })
    fireEvent.change(screen.getByLabelText('Community artwork API key'), { target: { value: 'draft' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remove saved Community artwork API key' }))
    await waitFor(() =>
      expect((screen.getByLabelText('Community artwork API key') as HTMLInputElement).value).toBe(''),
    )
    expect(request).toHaveBeenCalledWith({
      route: 'plugins.removeSecret',
      params: { pluginId: artwork.id, key: 'apiKey' },
      body: undefined,
    })
    for (const override of [{ enabled: false }, { isLoaded: false }, { canConfigure: false }]) {
      view.rerender(<PluginCard plugin={{ ...artwork, ...override }} mode={mode} />)
      expect(
        (screen.getByRole('button', { name: 'Refresh Community artwork' }) as HTMLButtonElement).disabled,
      ).toBe(true)
    }
    view.rerender(<PluginCard plugin={artwork} mode={mode} />)
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Refresh Community artwork' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
  })
  it.each([
    ['https://example.com/api', true],
    ['file:///C:/example.exe', false],
    ['https://user:password@example.com/api', false],
    ['javascript:alert(1)', false],
  ])('offers only plain HTTPS manifest help through the shared destination: %s', async (url, allowed) => {
    const { wrapper, openExternal } = fixture()
    render(
      <PluginCard
        plugin={{ ...artwork, settings: [field('apiKey', 'API key', { setupUrl: url })] }}
        mode={mode}
      />,
      { wrapper },
    )
    const help = screen.queryByRole('button', { name: 'Get Community artwork API key' })
    expect(!!help).toBe(allowed)
    if (help) {
      fireEvent.click(help)
      await waitFor(() => expect(openExternal).toHaveBeenCalledWith(url))
    } else expect(openExternal).not.toHaveBeenCalled()
  })
  it('restarts once, reloads runtime state and keeps cards with a friendly retry after failure', async () => {
    const restart = deferred<void>(),
      restartBackend = vi.fn(() => restart.promise)
    const { wrapper, snapshots } = fixture([{ ...artwork, isLoaded: false }], undefined, { restartBackend })
    render(<PluginSettings mode={mode} />, { wrapper })
    await screen.findByRole('region', { name: 'Community artwork settings' })
    fireEvent.click(screen.getByRole('button', { name: 'Restart library service' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
      ).toBe(true),
    )
    expect(restartBackend).toHaveBeenCalledTimes(1)
    snapshots[0].isLoaded = true
    await act(async () => restart.resolve())
    expect(await screen.findByRole('tab', { name: 'Community artwork' })).not.toBeNull()
    expect(screen.getByText(/The library service restarted/)).not.toBeNull()
    restartBackend.mockRejectedValueOnce(Error('private detail'))
    fireEvent.click(screen.getByRole('button', { name: 'Restart library service' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Try again.')
    expect(screen.getByRole('alert').textContent).not.toContain('private detail')
    fireEvent.click(screen.getByRole('tab', { name: 'Community artwork' }))
    expect(screen.getByRole('region', { name: 'Community artwork settings' })).not.toBeNull()
  })
  it('keeps restart disabled for a backend installation after leaving and reopening plugin settings', async () => {
    let state = 'Running'
    const { wrapper, client } = fixture(undefined, (input) =>
      input.route === 'operations.get'
        ? [
            {
              id: 'install',
              kind: 'plugin-install',
              state,
              message: 'Installing',
              updatedAt: new Date().toISOString(),
            },
          ]
        : undefined,
    )
    render(<Settings mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Plugins' }))
    fireEvent.click(await screen.findByRole('tab', { name: 'Manage plugins' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
      ).toBe(true),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Metadata & artwork' }))
    fireEvent.click(screen.getByRole('button', { name: 'Plugins' }))
    expect(
      (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    state = 'Completed'
    await act(() => client.invalidateQueries({ queryKey: ['api', 'operations.get'] }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Restart library service' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
  })
  it('does not start sign-in on arrival and cancels a challenge issued after departure', async () => {
    const pending = deferred<unknown>()
    const { request, wrapper } = fixture([structuredClone(xbox)], (input) =>
      input.route === 'plugins.signIn' ? pending.promise : undefined,
    )
    render(<PluginSettings mode={mode} />, { wrapper })
    const signin = await screen.findByRole('button', { name: 'Sign in to Xbox' })
    expect(request.mock.calls.some(([input]) => input.route === 'plugins.signIn')).toBe(false)
    fireEvent.click(signin)
    await screen.findByRole('button', { name: 'Cancel Xbox sign-in' })
    fireEvent.click(screen.getByRole('tab', { name: 'Manage plugins' }))
    await act(async () =>
      pending.resolve({
        challenge: {
          attemptId: 'late-attempt',
          userCode: 'XXXX',
          verificationUrl: 'https://login.example.com/device',
          expiresAt: new Date(Date.now() + 60000).toISOString(),
          pollIntervalSeconds: 5,
        },
      }),
    )
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'plugins.cancel')?.[0].body).toMatchObject({
        attemptId: 'late-attempt',
      }),
    )
    expect(request.mock.calls.some(([input]) => input.route === 'plugins.poll')).toBe(false)
    expect(screen.queryByText('XXXX')).toBeNull()
  })
  it('Cancel during provider preparation clears a late code and private provider errors stay hidden', async () => {
    const pending = deferred<unknown>()
    let fail = false
    const { wrapper, request } = fixture(undefined, (input) => {
      if (input.route !== 'plugins.signIn') return
      if (fail) throw Error('private-device-token')
      return pending.promise
    })
    render(<PluginCard plugin={xbox} mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Xbox' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel Xbox sign-in' }))
    await act(async () =>
      pending.resolve({
        challenge: {
          attemptId: 'late-code',
          userCode: 'ABCD-1234',
          verificationUrl: 'https://login.example.com/device',
          expiresAt: new Date(Date.now() + 600000).toISOString(),
          pollIntervalSeconds: 5,
        },
      }),
    )
    await screen.findByText('Sign-in cancelled.')
    expect(screen.queryByText('ABCD-1234')).toBeNull()
    expect(request.mock.calls.filter(([input]) => input.route === 'plugins.cancel')).toHaveLength(1)
    expect(request.mock.calls.some(([input]) => input.route === 'plugins.poll')).toBe(false)
    fail = true
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Xbox' }))
    await screen.findByText('Could not complete sign-in. Check the saved settings and try again.')
    expect(document.body.textContent).not.toContain('private-device-token')
  })
  it('waits the original five seconds, adds five seconds on SlowDown and cancels exactly once at expiry', async () => {
    vi.useFakeTimers()
    const { request, wrapper } = fixture(undefined, (input) => {
      if (input.route === 'plugins.signIn')
        return {
          challenge: {
            attemptId: 'timed',
            userCode: 'ABCD-1234',
            verificationUrl: 'https://login.example.com/device',
            expiresAt: new Date(Date.now() + 600000).toISOString(),
            pollIntervalSeconds: 5,
          },
        }
      if (input.route === 'plugins.poll') return { state: 1, message: 'private-device-token' }
    })
    render(<PluginCard plugin={xbox} mode={mode} />, { wrapper })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Sign in to Xbox' }))
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(screen.getByText('ABCD-1234')).not.toBeNull()
    const polls = () => request.mock.calls.filter(([input]) => input.route === 'plugins.poll').length
    await act(() => vi.advanceTimersByTimeAsync(4000))
    expect(polls()).toBe(0)
    await act(() => vi.advanceTimersByTimeAsync(1000))
    expect(polls()).toBe(1)
    await act(() => vi.advanceTimersByTimeAsync(9000))
    expect(polls()).toBe(1)
    await act(() => vi.advanceTimersByTimeAsync(1000))
    expect(polls()).toBe(2)
    await act(() => vi.advanceTimersByTimeAsync(600000))
    expect(screen.getByText('The sign-in code expired. Start sign-in again for a new code.')).not.toBeNull()
    expect(request.mock.calls.filter(([input]) => input.route === 'plugins.cancel')).toHaveLength(1)
    expect(screen.queryByText('ABCD-1234')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Sign out of Xbox' })).toBeNull()
    expect(document.body.textContent).not.toContain('private-device-token')
  })
  it.each([
    'https://untrusted.example.com/device',
    'https://login.example.com.evil.example/device',
    'http://login.example.com/device',
    'https://user:password@login.example.com/device',
    'https://login.example.com:8443/device',
  ])('never displays or opens an unsafe or undeclared verification address: %s', async (address) => {
    const { request, wrapper, openExternal } = fixture(undefined, (input) =>
      input.route === 'plugins.signIn'
        ? {
            challenge: {
              attemptId: 'unsafe-attempt',
              userCode: 'ABCD-1234',
              verificationUrl: address,
              expiresAt: new Date(Date.now() + 600000).toISOString(),
              pollIntervalSeconds: 5,
            },
          }
        : undefined,
    )
    render(<PluginCard plugin={xbox} mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Xbox' }))
    await screen.findByText('Could not start sign-in. Check the saved settings and try again.')
    expect(screen.queryByText('ABCD-1234')).toBeNull()
    expect(screen.queryByText(address)).toBeNull()
    expect(screen.queryByRole('button', { name: 'Open Xbox sign-in page' })).toBeNull()
    expect(openExternal).not.toHaveBeenCalled()
    expect(request.mock.calls.some(([input]) => input.route === 'plugins.poll')).toBe(false)
    expect(request.mock.calls.filter(([input]) => input.route === 'plugins.cancel')).toHaveLength(1)
  })
  it('keeps surviving selections through catalog removals and falls back only when the selected plugin leaves', async () => {
    const { wrapper, snapshots, client } = fixture()
    render(<PluginSettings mode={mode} />, { wrapper })
    fireEvent.click(await screen.findByRole('tab', { name: 'Xbox' }))
    snapshots.splice(0, 1)
    await act(() => client.invalidateQueries({ queryKey: ['api', 'plugins.get'] }))
    expect(screen.getByRole('tab', { name: 'Xbox' }).getAttribute('aria-selected')).toBe('true')
    snapshots.splice(0, 1)
    await act(() => client.invalidateQueries({ queryKey: ['api', 'plugins.get'] }))
    expect(screen.queryByRole('tab', { name: 'Xbox' })).toBeNull()
    expect(screen.getByRole('tab', { name: 'Manage plugins' }).getAttribute('aria-selected')).toBe('true')
  })
  it('round-trips boolean values and preserves a new checkbox draft through a slow reload', async () => {
    const pending = deferred<PluginSnapshot[]>()
    let hold = false
    const initial = {
      ...xbox,
      settings: [field('console', 'Include console history', { isBoolean: true, value: 'false' })],
    }
    const { wrapper, request, client } = fixture([initial], (input) => {
      if (input.route === 'plugins.get' && hold) return pending.promise
      if (input.route === 'plugins.settings') initial.settings[0].value = 'true'
    })
    render(<PluginSettings mode={mode} />, { wrapper })
    const toggle = (await screen.findByLabelText('Xbox Include console history')) as HTMLInputElement
    expect(toggle.checked).toBe(false)
    fireEvent.click(toggle)
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await screen.findByText('Provider settings saved.')
    expect(request).toHaveBeenCalledWith({
      route: 'plugins.settings',
      params: { pluginId: 'xbox' },
      body: { values: { console: 'true' } },
    })
    hold = true
    let reload!: Promise<void>
    act(() => {
      reload = client.invalidateQueries({ queryKey: ['api', 'plugins.get'] })
    })
    fireEvent.click(toggle)
    await act(async () => {
      pending.resolve([structuredClone(initial)])
      await reload
    })
    expect(toggle.checked).toBe(false)
  })
  it('shows an accessible device code and URL, locks fields, cancels on navigation and signs out after connection', async () => {
    const snapshots = [structuredClone(xbox)]
    let connected = false
    const { request, wrapper, openExternal } = fixture(snapshots, (input) => {
      if (input.route === 'plugins.signIn')
        return {
          challenge: {
            attemptId: 'attempt',
            userCode: 'ABCD-EFGH',
            verificationUrl: 'https://login.example.com/device',
            expiresAt: new Date(Date.now() + 60000).toISOString(),
            pollIntervalSeconds: 1,
          },
        }
      if (input.route === 'plugins.poll') {
        snapshots[0].accountConnected = connected
        return { state: connected ? 2 : 0 }
      }
      if (input.route === 'plugins.signOut') snapshots[0].accountConnected = false
    })
    render(<PluginSettings mode={mode} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Xbox' }))
    expect(await screen.findByLabelText('Sign-in code: ABCD-EFGH')).not.toBeNull()
    expect(screen.getByText('https://login.example.com/device')).not.toBeNull()
    expect((screen.getByLabelText('Xbox Scan installed games') as HTMLButtonElement).disabled).toBe(true)
    expect(openExternal).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Open Xbox sign-in page' }))
    expect(openExternal).toHaveBeenCalledWith('https://login.example.com/device')
    fireEvent.click(screen.getByRole('tab', { name: 'Manage plugins' }))
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'plugins.cancel')).toHaveLength(1),
    )
    fireEvent.click(screen.getByRole('tab', { name: 'Xbox' }))
    connected = true
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Xbox' }))
    await screen.findByText('ABCD-EFGH')
    await waitFor(
      () =>
        expect((screen.getByRole('button', { name: 'Sign out of Xbox' }) as HTMLButtonElement).disabled).toBe(
          false,
        ),
      { timeout: 3000 },
    )
    expect(request.mock.calls.filter(([input]) => input.route === 'plugins.cancel')).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Xbox' }))
    expect(await screen.findByText('Signed out. Imported games remain in your library.')).not.toBeNull()
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Sign in to Xbox' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
  })
})
