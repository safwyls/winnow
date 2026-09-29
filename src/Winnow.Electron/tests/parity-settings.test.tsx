// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { LibraryPreferenceForm, PluginCard, Settings, SteamAccount } from '../src/renderer/features/Settings'
import {
  ArtworkSourcePreferences,
  FullscreenPreferences,
  LibraryPresentationPreferences,
  OfficialPluginInstall,
} from '../src/renderer/features/SettingsPreferences'
import type { ApiRequest } from '../src/shared/bridge'
import type { PluginSnapshot } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

afterEach(() => {
  cleanup()
  clearViewState('desktop:settings:tab')
  clearViewState('fullscreen:settings:tab')
})
function fixture(responder: (input: ApiRequest) => unknown, native = {}) {
  const request = vi.fn(async (input: ApiRequest) => ({ ok: true, status: 200, data: responder(input) }))
  Object.defineProperty(window, 'winnow', {
    value: { request, openExternal: vi.fn(), ...native },
    configurable: true,
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return {
    request,
    client,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
const plugin: PluginSnapshot = {
  id: 'xbox',
  name: 'Xbox',
  version: '1.0.0',
  description: 'Xbox games',
  capabilities: 'Library',
  enabled: true,
  isLoaded: true,
  restartRequired: false,
  status: 'Ready',
  canConfigure: true,
  hasAccount: false,
  accountConnected: false,
  settings: [
    {
      key: 'normal',
      label: 'Normal field',
      value: 'kept',
      isSecret: false,
      isRequired: false,
      hasStoredSecret: false,
      isBoolean: false,
      isAdvanced: false,
    },
    {
      key: 'override',
      label: 'Application ID override',
      value: 'existing-override',
      isSecret: false,
      isRequired: false,
      hasStoredSecret: false,
      isBoolean: false,
      isAdvanced: true,
    },
    {
      key: 'secret',
      label: 'Advanced secret',
      value: null,
      isSecret: true,
      isRequired: false,
      hasStoredSecret: true,
      isBoolean: false,
      isAdvanced: true,
    },
  ],
}

describe.each(['desktop', 'fullscreen'] as const)('%s settings parity', (mode) => {
  it('prefills an official install link without installing before explicit confirmation', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'operations.detail' ? { state: 'Queued', message: 'Queued' } : {},
    )
    render(<OfficialPluginInstall initialRequest={{ pluginId: 'xbox', releaseTag: 'v1.2.3' }} />, { wrapper })
    expect((screen.getByLabelText('Provider') as HTMLSelectElement).value).toBe('xbox')
    expect((screen.getByLabelText('Release tag') as HTMLInputElement).value).toBe('v1.2.3')
    expect(request).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Install provider' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith(
        expect.objectContaining({
          route: 'operations.plugin',
          body: expect.objectContaining({ request: { pluginId: 'xbox', releaseTag: 'v1.2.3' } }),
        }),
      ),
    )
  })
  it('requires confirmation before resetting fullscreen appearance and preserves desktop settings', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'preferences.presentation.get' ? [] : null,
    )
    render(<FullscreenPreferences mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Reset fullscreen appearance…' }))
    expect(request.mock.calls.some(([input]) => input.route === 'preferences.presentation.put')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('alertdialog')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Reset fullscreen appearance…' }))
    fireEvent.click(screen.getByRole('button', { name: 'Reset fullscreen appearance' }))
    await waitFor(() =>
      expect(
        request.mock.calls.filter(([input]) => input.route === 'preferences.presentation.put'),
      ).toHaveLength(5),
    )
    expect(
      request.mock.calls
        .filter(([input]) => input.route === 'preferences.presentation.put')
        .every(([input]) => String(input.params?.preference).startsWith('Fullscreen')),
    ).toBe(true)
  })

  it('keeps expansions ungrouped by default and saves the shared cover fit preference', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'preferences.presentation.get' ? [] : null,
    )
    render(<LibraryPresentationPreferences />, { wrapper })
    const grouped = screen.getByRole('checkbox', {
      name: 'Group expansions with their base game',
    }) as HTMLInputElement
    expect(grouped.checked).toBe(false)
    const fit = screen.getByLabelText('Cover artwork')
    await waitFor(() => expect((fit as HTMLSelectElement).disabled).toBe(false))
    fireEvent.change(fit, { target: { value: 'fill' } })
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'preferences.presentation.put',
        params: { preference: 'CoverArtMode' },
        body: { value: 'fill' },
      }),
    )
  })

  it('requests only a named official package and keeps its install operation visible', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'operations.detail' ? { state: 'Succeeded', message: 'SteamGridDB installed.' } : null,
    )
    render(<OfficialPluginInstall />, { wrapper })
    fireEvent.change(screen.getByLabelText('Release tag'), { target: { value: 'v1.2.3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Install provider' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith(
        expect.objectContaining({
          route: 'operations.plugin',
          body: {
            operationId: expect.stringMatching(/^[a-f0-9]{32}$/),
            request: { pluginId: 'steamgriddb', releaseTag: 'v1.2.3' },
          },
        }),
      ),
    )
    await screen.findByText('SteamGridDB installed.')
  })

  it('reopens setup from Application without resetting preferences', async () => {
    const { request, wrapper, client } = fixture((input) =>
      input.route === 'preferences.presentation.get'
        ? []
        : input.route === 'connections.get'
          ? { steam: {} }
          : input.route === 'plugins.get' || input.route === 'operations.get'
            ? []
            : null,
    )
    render(<Settings mode={mode} />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Application' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Run setup again' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({ route: 'setup.put', params: undefined, body: { step: 0 } }),
    )
    await waitFor(() =>
      expect(client.getQueryData(['api', 'setup.get', undefined])).toEqual({ step: 0, problem: null }),
    )
    expect(request.mock.calls.some(([input]) => input.route === 'preferences.presentation.put')).toBe(false)
  })
  it('starts advanced settings collapsed and preserves hidden values and stored secrets', async () => {
    const { request, wrapper } = fixture(() => null)
    render(
      <div className={`mode-${mode}`}>
        <PluginCard plugin={plugin} />
      </div>,
      { wrapper },
    )
    expect(screen.queryByLabelText('Application ID override')).toBeNull()
    const advanced = screen.getByRole('button', { name: 'Show advanced settings: Xbox' })
    expect(advanced.getAttribute('aria-expanded')).toBe('false')
    fireEvent.change(screen.getByLabelText('Normal field'), { target: { value: 'changed' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'plugins.settings',
        params: { pluginId: 'xbox' },
        body: { values: { normal: 'changed', override: 'existing-override' } },
      }),
    )
    fireEvent.click(advanced)
    expect((screen.getByLabelText('Advanced secret') as HTMLInputElement).type).toBe('password')
    fireEvent.change(screen.getByLabelText('Advanced secret'), { target: { value: 'replacement' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Xbox settings' }))
    await screen.findByText('Provider settings saved.')
    await waitFor(() => expect((screen.getByLabelText('Advanced secret') as HTMLInputElement).value).toBe(''))
    expect(
      request.mock.calls.some(
        ([input]) => (input.body as { values?: Record<string, string> })?.values?.secret === 'replacement',
      ),
    ).toBe(true)
  })

  it('removes saved secrets explicitly and never replaces them with an empty save', async () => {
    const { request, wrapper } = fixture(() => null)
    render(
      <div className={`mode-${mode}`}>
        <PluginCard plugin={plugin} />
      </div>,
      { wrapper },
    )
    fireEvent.click(screen.getByRole('button', { name: 'Show advanced settings: Xbox' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove saved Advanced secret' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'plugins.removeSecret',
        params: { pluginId: 'xbox', key: 'secret' },
        body: undefined,
      }),
    )
  })

  it('saves library visibility against fresh preferences without overwriting another frontend', async () => {
    const current = { showNonGameEntries: true, showExplicitContent: true, maturityCap: 'adults_only' }
    const { request, wrapper } = fixture((input) =>
      input.route === 'preferences.library.get' ? current : null,
    )
    render(
      <LibraryPreferenceForm
        initial={{ showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' }}
      />,
      { wrapper },
    )
    fireEvent.change(screen.getByLabelText('Maturity cap'), { target: { value: 'teen' } })
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'preferences.library.put',
        params: undefined,
        body: { ...current, maturityCap: 'teen' },
      }),
    )
  })

  it('orders available artwork sources and saves the backend-compatible format', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'preferences.presentation.get'
        ? [{ preference: 'ArtworkSourceOrder', value: 'steam,steamgriddb,igdb' }]
        : input.route === 'preferences.artworkSources'
          ? [
              { id: 'steam', label: 'Steam' },
              { id: 'plugin:steamgriddb', label: 'SteamGridDB' },
              { id: 'igdb', label: 'IGDB' },
            ]
          : null,
    )
    render(<ArtworkSourcePreferences />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'Move SteamGridDB up' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'preferences.presentation.put',
        params: { preference: 'ArtworkSourceOrder' },
        body: { value: 'plugin:steamgriddb,steam,igdb' },
      }),
    )
  })

  it('uses percentage units for fullscreen safe margins and exposes the full text scale range', async () => {
    const { request, wrapper } = fixture((input) =>
      input.route === 'preferences.presentation.get'
        ? [{ preference: 'FullscreenSafeMargin', value: '5' }]
        : null,
    )
    render(<FullscreenPreferences mode={mode} />, { wrapper })
    const margin = await screen.findByRole('slider', { name: 'Screen edge margin · 5%' })
    await waitFor(() => expect((margin as HTMLInputElement).disabled).toBe(false))
    fireEvent.change(margin, { target: { value: '8' } })
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'preferences.presentation.put',
        params: { preference: 'FullscreenSafeMargin' },
        body: { value: '8' },
      }),
    )
    expect(screen.getByRole('slider', { name: 'Text size · 100%' }).getAttribute('max')).toBe('1.4')
  })

  it('requires consent before Steam sign-in and passes the persistence choice to main', async () => {
    const steamSignIn = vi
      .fn()
      .mockResolvedValue({ signedIn: true, persisted: false, refreshTokenCaptured: false })
    const { wrapper } = fixture(() => null, { steamSignIn })
    render(<SteamAccount />, { wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    expect(steamSignIn).not.toHaveBeenCalled()
    expect(screen.getByText(/does not read or save your password/)).toBeTruthy()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Stay signed in on this computer' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await waitFor(() =>
      expect(steamSignIn).toHaveBeenCalledWith({ consentGranted: true, staySignedIn: false }),
    )
    await screen.findByText(/This session was not saved to disk/)
    expect(screen.getByText(/Steam did not provide a renewable session/)).toBeTruthy()
  })
})
