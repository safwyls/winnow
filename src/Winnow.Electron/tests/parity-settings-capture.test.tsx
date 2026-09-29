// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SteamAccount } from '../src/renderer/features/Settings'
import { SteamCapture } from '../src/renderer/features/SteamCapture'
import { ApplicationPreferences } from '../src/renderer/features/SettingsPreferences'
import type { ApiRequest, SteamCaptureResult } from '../src/shared/bridge'

afterEach(cleanup)
const captured: SteamCaptureResult = {
  pages: {
    licensesHtml: '<table>private purchase contents</table>',
    historyHtml: '<table>private history</table>',
    additionalLicensesHtml: [],
    source: 0,
    capturedAt: '2026-09-28T12:00:00Z',
    steamId: '76561198000000001',
  },
  captureDetail: 'Account pages captured. Review them before importing.',
}
function fixture(native: Record<string, unknown>) {
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'imports.steam.pages'
        ? {
            licensesOutcome: 'Parsed',
            historyOutcome: 'Parsed',
            licenseFactsRecorded: 2,
            transactionFactsRecorded: 3,
            ownershipsFilled: 1,
            licenseFactsAlreadyRecorded: 0,
            transactionFactsAlreadyRecorded: 0,
          }
        : input.route === 'preferences.presentation.get'
          ? []
          : null,
  }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, ...native } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return {
    request,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}

describe.each(['desktop', 'fullscreen'])('%s Steam capture consent and import parity', (mode) => {
  it('keeps purchase permission off by default and resets an abandoned consent without opening Steam', () => {
    const steamSignIn = vi.fn(),
      { wrapper } = fixture({ steamSignIn })
    render(
      <div className={`mode-${mode}`}>
        <SteamAccount />
      </div>,
      { wrapper },
    )
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    let permission = screen.getByRole('checkbox', {
      name: 'Also capture purchase history and licences',
    }) as HTMLInputElement
    expect(permission.checked).toBe(false)
    fireEvent.click(permission)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel sign-in' }))
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    permission = screen.getByRole('checkbox', {
      name: 'Also capture purchase history and licences',
    }) as HTMLInputElement
    expect(permission.checked).toBe(false)
    expect(steamSignIn).not.toHaveBeenCalled()
  })

  it('reviews separately opted-in sign-in pages and imports only after explicit confirmation', async () => {
    const steamSignIn = vi
      .fn()
      .mockResolvedValue({ signedIn: true, persisted: true, refreshTokenCaptured: true, ...captured })
    const { request, wrapper } = fixture({ steamSignIn })
    render(
      <div className={`mode-${mode}`}>
        <SteamAccount />
      </div>,
      { wrapper },
    )
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Also capture purchase history and licences' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await screen.findByRole('region', { name: 'Review Steam capture' })
    expect(steamSignIn).toHaveBeenCalledWith({
      consentGranted: true,
      staySignedIn: true,
      capturePurchaseHistory: true,
    })
    expect(request).not.toHaveBeenCalled()
    expect(screen.queryByText('private purchase contents')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Import captured pages' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'imports.steam.pages',
        params: undefined,
        body: captured.pages,
      }),
    )
    await screen.findByText('2 licence facts and 3 transaction facts recorded; 1 library entries filled.')
  })

  it('requires separate capture consent then imports partial pages under their observed identity', async () => {
    const steamCapturePages = vi
      .fn()
      .mockResolvedValue({
        ...captured,
        licensesTruncated: true,
        pages: { ...captured.pages, steamId: null },
      })
    const { request, wrapper } = fixture({ steamCapturePages })
    render(
      <div className={`mode-${mode}`}>
        <SteamCapture />
      </div>,
      { wrapper },
    )
    fireEvent.click(screen.getByRole('button', { name: 'Capture account pages in Winnow' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel capture' }))
    expect(steamCapturePages).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Capture account pages in Winnow' }))
    fireEvent.click(screen.getByRole('button', { name: 'Agree and import pages' }))
    await screen.findByText('2 licence facts and 3 transaction facts recorded; 1 library entries filled.')
    expect(steamCapturePages).toHaveBeenCalledWith({ consentGranted: true })
    expect(request).toHaveBeenCalledExactlyOnceWith({ route: 'imports.steam.pages', params: undefined, body: { ...captured.pages, steamId: null } })
    expect(screen.queryByRole('button', { name: 'Import captured pages' })).toBeNull()
  })

  it('hides unavailable Steam link routing while keeping browser and in-app choices', async () => {
    const { wrapper } = fixture({
      applicationInfo: vi
        .fn()
        .mockResolvedValue({
          version: '1',
          platform: 'win32',
          packaged: true,
          autostartSupported: true,
          openAtLogin: false,
          steamStoreAvailable: false,
        }),
    })
    render(
      <div className={`mode-${mode}`}>
        <ApplicationPreferences />
      </div>,
      { wrapper },
    )
    await screen.findByText('Winnow 1')
    expect(screen.queryByRole('option', { name: 'Steam client, when available' })).toBeNull()
    expect(screen.getByRole('option', { name: 'Default browser' })).toBeTruthy()
    expect(screen.getByRole('option', { name: 'In Winnow' })).toBeTruthy()
  })
})
