// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ApplicationPreferences } from '../src/renderer/features/SettingsPreferences'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
describe.each(['desktop', 'fullscreen'])('%s link preferences', (mode) => {
  it('persists the chosen store destination across remount and displays browser when that client becomes unavailable without rewriting it', async () => {
    let stored = 'in-app',
      available = true
    const request = vi.fn(async (input: ApiRequest) => {
      if (input.route === 'preferences.presentation.put') stored = (input.body as { value: string }).value
      return {
        ok: true,
        status: 200,
        data:
          input.route === 'preferences.presentation.get'
            ? [{ preference: 'LinkDestination', value: stored }]
            : {},
      }
    })
    const applicationInfo = vi.fn(async () => ({
      version: 'test',
      platform: 'win32',
      packaged: true,
      autostartSupported: false,
      openAtLogin: false,
      steamStoreAvailable: available,
    }))
    Object.defineProperty(window, 'winnow', { configurable: true, value: { request, applicationInfo } })
    function mount() {
      const client = new QueryClient({
        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
      })
      return render(
        <QueryClientProvider client={client}>
          <div className={`mode-${mode}`}>
            <ApplicationPreferences setup />
          </div>
        </QueryClientProvider>,
      )
    }
    let view = mount()
    await screen.findByRole('option', { name: 'Steam client, when available' })
    fireEvent.change(screen.getByLabelText('Open links in'), { target: { value: 'store' } })
    await waitFor(() => expect(stored).toBe('store'))
    view.unmount()
    view = mount()
    await screen.findByRole('option', { name: 'Steam client, when available' })
    expect((screen.getByLabelText('Open links in') as HTMLSelectElement).value).toBe('store')
    view.unmount()
    available = false
    request.mockClear()
    mount()
    await waitFor(() =>
      expect((screen.getByLabelText('Open links in') as HTMLSelectElement).value).toBe('browser'),
    )
    expect(screen.queryByRole('option', { name: 'Steam client, when available' })).toBeNull()
    expect(stored).toBe('store')
    expect(request.mock.calls.every(([input]) => input.route !== 'preferences.presentation.put')).toBe(true)
  })
})
