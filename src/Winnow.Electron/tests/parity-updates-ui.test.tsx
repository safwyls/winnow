// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ApplicationUpdates, UpdateStatus } from '../src/renderer/features/Updates'
import { BackendRestart } from '../src/renderer/features/BackendRestart'
import { initialUpdateSnapshot } from '../src/main/application-updater'
import type { ApplicationUpdateSnapshot } from '../src/shared/bridge'
afterEach(cleanup)
function fixture(initial: Partial<ApplicationUpdateSnapshot> = {}) {
  let snapshot = { ...initialUpdateSnapshot(), ...initial }
  const listeners = new Set<(value: ApplicationUpdateSnapshot) => void>()
  const publish = (patch: Partial<ApplicationUpdateSnapshot>) => {
    snapshot = { ...snapshot, ...patch }
    listeners.forEach((listener) => listener(snapshot))
  }
  const updateAction = vi.fn(async (action: string, value?: boolean) => {
    if (action === 'automatic' || action === 'beta')
      publish({ [action === 'automatic' ? 'automatic' : 'includeBeta']: value })
    return snapshot
  })
  const restartBackend = vi.fn(async () => {})
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      updateSnapshot: vi.fn(async () => snapshot),
      updateAction,
      restartBackend,
      onUpdate: (listener: (value: ApplicationUpdateSnapshot) => void) => {
        listeners.add(listener)
        return () => listeners.delete(listener)
      },
    },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  return { publish, updateAction, restartBackend, wrapper }
}
describe.each(['desktop', 'fullscreen'])('%s update presentation parity', (mode) => {
  it('shares preferences and staged readiness while restart requires explicit activation', async () => {
    const { wrapper, publish, updateAction } = fixture()
    render(
      <div className={`mode-${mode}`}>
        <ApplicationUpdates />
        <UpdateStatus />
      </div>,
      { wrapper },
    )
    const automatic = await screen.findByLabelText('Download updates automatically')
    await waitFor(() => expect((automatic as HTMLInputElement).disabled).toBe(false))
    fireEvent.click(automatic)
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('automatic', false))
    fireEvent.click(screen.getByLabelText('Include beta releases'))
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('beta', true))
    act(() => publish({ canRestart: true, status: 'Update ready.', availableVersion: '2.0.0' }))
    const header = await screen.findByRole('complementary', { name: 'Application update' })
    expect(within(header).getByRole('button', { name: 'Restart to update' })).toBeTruthy()
    expect(updateAction).not.toHaveBeenCalledWith('restart', undefined)
    fireEvent.click(within(header).getByRole('button', { name: 'Restart to update' }))
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('restart', undefined))
  })
  it('offers explicit update-and-restart and recovers focus when download becomes ready', async () => {
    const { wrapper, publish, updateAction } = fixture({ canDownload: true, availableVersion: '2.0.0' })
    render(
      <div className={`mode-${mode}`}>
        <UpdateStatus />
      </div>,
      { wrapper },
    )
    const download = await screen.findByRole('button', { name: 'Download update' })
    download.focus()
    expect(updateAction).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Update and restart' }))
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('update-and-restart', undefined))
    download.focus()
    act(() => publish({ canDownload: false, canRestart: true, status: 'Ready.' }))
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Restart to update' })),
    )
  })
  it('keeps recovery guidance visible during later checks and exposes cancellation with progress', async () => {
    const { wrapper, publish, updateAction } = fixture({
      recoveryStatus: 'The previous update did not start.',
      busy: true,
      canCancel: true,
      progress: 25,
    })
    render(
      <div className={`mode-${mode}`}>
        <ApplicationUpdates />
        <UpdateStatus />
      </div>,
      { wrapper },
    )
    const header = await screen.findByRole('complementary', { name: 'Application update' })
    expect(within(header).getByRole('alert').textContent).toContain('previous update')
    expect((within(header).getByRole('progressbar') as HTMLProgressElement).value).toBe(25)
    fireEvent.click(within(header).getByRole('button', { name: 'Cancel download' }))
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('cancel', undefined))
    act(() => publish({ canCancel: false, status: 'Checking for updates…' }))
    expect(within(header).getByRole('alert').textContent).toContain('previous update')
  })
  it('hides unavailable native actions and opens manual downloads through the shared command', async () => {
    const { wrapper, updateAction } = fixture({
      downloadUrl: 'https://github.com/safwyls/winnow/releases',
      availableVersion: '2.0.0',
    })
    render(<ApplicationUpdates />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'Download in browser' }))
    expect(screen.queryByRole('button', { name: 'Restart to update' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Download update' })).toBeNull()
    await waitFor(() => expect(updateAction).toHaveBeenCalledWith('manual-download', undefined))
  })
  it('starts a provider restart and reports a friendly failure without pretending success', async () => {
    const { wrapper, restartBackend } = fixture()
    restartBackend.mockRejectedValue(new Error('Backend did not reconnect'))
    render(
      <div className={`mode-${mode}`}>
        <BackendRestart />
      </div>,
      { wrapper },
    )
    fireEvent.click(screen.getByRole('button', { name: 'Restart library service' }))
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toContain(
        'Could not restart the library service. Try again.',
      ),
    )
    expect(restartBackend).toHaveBeenCalledOnce()
  })
})
