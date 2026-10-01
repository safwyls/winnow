// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Settings } from '../src/renderer/features/Settings'
import { AccountStatistics } from '../src/renderer/features/Accounts'
import { AcquisitionExport } from '../src/renderer/features/AcquisitionExport'
import { FullscreenLibrarySettings } from '../src/renderer/features/FullscreenLibrarySettings'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) clearViewState(`${mode}:settings:tab`)
})
function fixture(exportAcquisitions?: () => Promise<{ saved: boolean; ownershipCount: number }>) {
  const request = vi.fn(async ({ route }: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      route === 'preferences.library.get'
        ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' }
        : route === 'preferences.accountVisibility.get'
          ? { accountConfirmed: false, ownAccountOnly: false, hiddenCount: 0 }
          : route === 'statistics.account'
            ? { hasAnything: false }
            : route === 'journal.preferences.get'
              ? { promptAfterPlay: false }
              : [],
  }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, exportAcquisitions } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  return {
    request,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
describe.each(['desktop', 'fullscreen'] as const)('%s acquisition export', (mode) => {
  describe.each(['library', 'accounts'] as const)('%s command', (surface) => {
    const mount = (exporter?: () => Promise<{ saved: boolean; ownershipCount: number }>) => {
      const f = fixture(exporter)
      render(surface === 'library' ? <Settings mode={mode} /> : <AccountStatistics mode={mode} />, {
        wrapper: f.wrapper,
      })
      if (surface === 'library') fireEvent.click(screen.getByRole('button', { name: 'Library' }))
      return {
        ...f,
        button: screen.getByRole('button', {
          name: surface === 'library' ? 'Export acquisition CSV' : 'Export acquisitions',
        }) as HTMLButtonElement,
      }
    }
    it.each([
      [true, false, 'Exported 0 ownership records.'],
      [false, false, 'Export cancelled.'],
      [false, true, 'Could not save the export. Try another location.'],
    ] as const)(
      'Command_reports_saved_cancelled_and_failed_destinations (%s, %s)',
      async (saved, fail, expected) => {
        const exporter = vi.fn(async () => {
          if (fail) throw new Error('Disk full /private/destination')
          return { saved, ownershipCount: 0 }
        })
        const f = mount(exporter)
        fireEvent.click(f.button)
        const status = await screen.findByText(expected)
        expect(status.getAttribute('role')).toBe('status')
        expect(status.getAttribute('aria-live')).toBe('polite')
        expect(exporter).toHaveBeenCalledExactlyOnceWith()
        expect(document.body.textContent).not.toContain('Disk full')
        expect(document.body.textContent).not.toContain('/private/destination')
        await waitFor(() => expect(f.button.disabled).toBe(false))
        expect(f.request.mock.calls.some(([input]) => input.body != null)).toBe(false)
      },
    )
    it('Missing_export_service_disables_the_command', () => {
      expect(mount().button.disabled).toBe(true)
    })
    it('keeps one pending write and clears failed feedback on a successful retry', async () => {
      let finish!: (value: { saved: boolean; ownershipCount: number }) => void
      const exporter = vi
        .fn<() => Promise<{ saved: boolean; ownershipCount: number }>>()
        .mockRejectedValueOnce(new Error('Disk full'))
        .mockImplementationOnce(
          () =>
            new Promise((resolve) => {
              finish = resolve
            }),
        )
      const f = mount(exporter)
      fireEvent.click(f.button)
      await screen.findByText('Could not save the export. Try another location.')
      await waitFor(() => expect(f.button.disabled).toBe(false))
      f.button.focus()
      fireEvent.click(f.button)
      fireEvent.click(f.button)
      await screen.findByText('Preparing acquisition CSV…')
      await waitFor(() => expect(exporter).toHaveBeenCalledTimes(2))
      expect(f.button.disabled).toBe(true)
      expect(screen.queryByText('Could not save the export. Try another location.')).toBeNull()
      f.button.blur()
      await act(async () => finish({ saved: true, ownershipCount: 3 }))
      await screen.findByText('Exported 3 ownership records.')
      expect(document.activeElement).toBe(f.button)
    })
  })
})
it('uses singular ownership record and shares the pending lock across entry points', async () => {
  let finish!: (value: { saved: boolean; ownershipCount: number }) => void
  const exporter = vi.fn(
    () =>
      new Promise<{ saved: boolean; ownershipCount: number }>((resolve) => {
        finish = resolve
      }),
  )
  const f = fixture(exporter)
  render(
    <>
      <AcquisitionExport />
      <AcquisitionExport label="Export acquisitions" />
    </>,
    { wrapper: f.wrapper },
  )
  fireEvent.click(screen.getByRole('button', { name: 'Export acquisition CSV' }))
  fireEvent.click(screen.getByRole('button', { name: 'Export acquisitions' }))
  await waitFor(() => expect(exporter).toHaveBeenCalledOnce())
  expect((screen.getByRole('button', { name: 'Export acquisitions' }) as HTMLButtonElement).disabled).toBe(
    true,
  )
  await act(async () => finish({ saved: true, ownershipCount: 1 }))
  await screen.findByText('Exported 1 ownership record.')
})
it('does not add management commands to first-run fullscreen library preferences', () => {
  const f = fixture(vi.fn(async () => ({ saved: true, ownershipCount: 0 })))
  render(<FullscreenLibrarySettings setup />, { wrapper: f.wrapper })
  expect(screen.queryByRole('button', { name: 'Export acquisition CSV' })).toBeNull()
})
