// @vitest-environment jsdom
import { chooseOption } from './choice-test-utils'
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
    const link = await screen.findByRole('button', { name: 'Open links in' })
    await waitFor(() => expect((link as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(link)
    expect(screen.getByRole('menuitemradio', { name: 'Steam client, when available' })).toBeTruthy()
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    chooseOption(screen.getByLabelText('Open links in'), 'store')
    await waitFor(() => expect(stored).toBe('store'))
    view.unmount()
    view = mount()
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Open links in' }) as HTMLButtonElement).value).toBe(
        'store',
      ),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Open links in' }))
    expect(
      screen
        .getByRole('menuitemradio', { name: 'Steam client, when available' })
        .getAttribute('aria-checked'),
    ).toBe('true')
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    view.unmount()
    available = false
    request.mockClear()
    mount()
    await waitFor(() =>
      expect((screen.getByLabelText('Open links in') as HTMLButtonElement).value).toBe('browser'),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Open links in' }))
    expect(screen.getByRole('menu')).toBeTruthy()
    expect(screen.queryByRole('menuitemradio', { name: 'Steam client, when available' })).toBeNull()
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    expect(stored).toBe('store')
    expect(request.mock.calls.every(([input]) => input.route !== 'preferences.presentation.put')).toBe(true)
  })
})
