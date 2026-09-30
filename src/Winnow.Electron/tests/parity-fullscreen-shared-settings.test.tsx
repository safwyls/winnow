// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ApiRequest, ApplicationInfo } from '../src/shared/bridge'
import type { LibraryPreferences } from '../src/renderer/api/types'
import { FullscreenLibrarySettings } from '../src/renderer/features/FullscreenLibrarySettings'
import {
  ApplicationPreferences,
  LibraryPresentationPreferences,
  usePresentationPreferences,
} from '../src/renderer/features/SettingsPreferences'
import { libraryDefaultSort, useAvalonLists } from '../src/renderer/themes/avalon-list-state'
import { useLibrary } from '../src/renderer/api/hooks'
import { Settings } from '../src/renderer/features/Settings'
import { clearViewState } from '../src/renderer/viewState'
import { ApplicationUpdates } from '../src/renderer/features/Updates'
import { initialUpdateSnapshot } from '../src/main/application-updater'

afterEach(() => {
  cleanup()
  clearViewState('fullscreen:settings:tab')
  for (const mode of ['desktop', 'fullscreen'])
    for (const key of ['sort', 'default-sort', 'sort-before-list'])
      clearViewState(`avalon:library:${mode}:${key}`)
})
function fixture(initial: Record<string, string> = {}) {
  const values = { ...initial }
  let library: LibraryPreferences = {
    showNonGameEntries: false,
    showExplicitContent: false,
    maturityCap: 'adults_only',
  }
  let journal = false,
    ownAccountOnly = false,
    refuse = false
  let directory: string | null = null
  const info: ApplicationInfo = {
    version: '1.0.0',
    platform: 'win32',
    packaged: true,
    autostartSupported: true,
    openAtLogin: false,
    steamStoreAvailable: true,
  }
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route.endsWith('.put') && refuse)
      return { ok: false, status: 500, message: 'Could not save this preference.' }
    if (input.route === 'preferences.presentation.put')
      values[String(input.params!.preference)] = (input.body as { value: string }).value
    if (input.route === 'preferences.presentation.get')
      data = Object.entries(values).map(([preference, value]) => ({ preference, value }))
    if (input.route === 'preferences.library.put') library = input.body as LibraryPreferences
    if (input.route === 'preferences.library.get') data = library
    if (input.route === 'journal.preferences.put')
      journal = (input.body as { promptAfterPlay: boolean }).promptAfterPlay
    if (input.route === 'journal.preferences.get') data = { promptAfterPlay: journal }
    if (input.route === 'connections.visibility.put')
      ownAccountOnly = (input.body as { ownAccountOnly: boolean }).ownAccountOnly
    if (input.route === 'connections.visibility.get')
      data = { accountConfirmed: true, ownAccountOnly, hiddenCount: 2 }
    if (input.route === 'library.visibility')
      data = { ratingCapHidden: library.maturityCap === 'teen' ? 1 : 0 }
    if (input.route === 'library.get')
      data = {
        games: library.showNonGameEntries
          ? [{ workId: 2, title: 'Library tool', bucket: 'unplayed', playtimeMinutes: 0, entries: [] }]
          : [],
        lists: [],
      }
    if (input.route === 'operations.get') data = []
    if (input.route === 'plugins.directory') data = directory ? { directory } : null
    return { ok: true, status: 200, data }
  })
  const setOpenAtLogin = vi.fn(async (value: boolean) => {
    info.openAtLogin = value
  })
  const openDataFolder = vi.fn(async () => {})
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, applicationInfo: async () => ({ ...info }), setOpenAtLogin, openDataFolder },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return {
    values,
    request,
    client,
    info,
    setOpenAtLogin,
    openDataFolder,
    setDirectory: (value: string) => {
      directory = value
    },
    library: () => library,
    updateLibrary: (value: LibraryPreferences) => {
      library = value
    },
    refuse: (value: boolean) => {
      refuse = value
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
function LibraryRows() {
  return <FullscreenLibrarySettings onTools={() => {}} onSpending={() => {}} onRecommendations={() => {}} />
}
async function ready(name: string, role = 'button') {
  const row = (await screen.findByRole(role, { name: new RegExp(`^${name}$`) })) as HTMLButtonElement
  await waitFor(() => expect(row.disabled).toBe(false))
  return row
}
async function setDirection(row: HTMLButtonElement, key: 'ArrowLeft' | 'ArrowRight', value: string) {
  fireEvent.keyDown(row, { key })
  await waitFor(() => expect(row.textContent).toContain(value))
  await waitFor(() => expect(row.disabled).toBe(false))
}
function LibraryObserver() {
  const library = useLibrary()
  const preferences = usePresentationPreferences()
  const fallback = libraryDefaultSort(preferences.values.DefaultSort)
  const order = useAvalonLists('fullscreen', [], true, preferences.loaded ? fallback : undefined)
  return (
    <>
      <output aria-label="Browsing order">{order.savedSort ?? fallback}</output>
      <output aria-label="Visible titles">{library.data?.games.map((game) => game.title).join(', ')}</output>
    </>
  )
}

describe('fullscreen Library uses the shared application preferences', () => {
  it('shares all six default sort choices with desktop and an independent Library observer', async () => {
    const f = fixture()
    render(
      <>
        <LibraryPresentationPreferences />
        <LibraryRows />
        <LibraryObserver />
      </>,
      { wrapper: f.wrapper },
    )
    const row = await ready('Default library sort')
    const desktop = screen.getByRole('combobox', { name: 'Default library sort' }) as HTMLSelectElement
    const values = [
      'DormantLongest',
      'RecentlyPlayed',
      'PlaytimeHighToLow',
      'PlaytimeLowToHigh',
      'NameAscending',
      'NameDescending',
    ]
    const orders = ['dormant', 'recent', 'time', 'time-low', 'title', 'title-desc']
    for (let i = 1; i < values.length; i++) {
      fireEvent.keyDown(row, { key: 'ArrowRight' })
      await waitFor(() => expect(desktop.value).toBe(values[i]))
      await waitFor(() => expect(row.disabled).toBe(false))
      expect(screen.getByLabelText('Browsing order').textContent).toBe(orders[i])
    }
    const count = f.request.mock.calls.length
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    expect(f.request.mock.calls).toHaveLength(count)
    fireEvent.change(desktop, { target: { value: 'RecentlyPlayed' } })
    await waitFor(() => expect(row.textContent).toContain('Recently played'))
    await waitFor(() => expect(row.disabled).toBe(false))
    await setDirection(row, 'ArrowLeft', 'Dormant longest')
    expect(desktop.value).toBe('DormantLongest')
  })
  it('falls back from an invalid saved sort without rewriting it on load', async () => {
    const f = fixture({ DefaultSort: 'removed-sort' })
    render(<LibraryRows />, { wrapper: f.wrapper })
    expect((await ready('Default library sort')).textContent).toContain('Dormant longest')
    expect(f.values.DefaultSort).toBe('removed-sort')
    expect(f.request.mock.calls.some(([input]) => input.route.endsWith('.put'))).toBe(false)
  })
  it('shares journal and expansion switches with desktop in both directions', async () => {
    const f = fixture()
    render(
      <>
        <LibraryPresentationPreferences />
        <LibraryRows />
      </>,
      { wrapper: f.wrapper },
    )
    for (const [label, desktopLabel] of [
      ['Journal after playing', 'Ask for a note after playing'],
      ['Group expansions', 'Group expansions with their base game'],
    ]) {
      const row = await ready(label, 'switch')
      const desktop = screen.getByRole('checkbox', {
        name: new RegExp(`^${desktopLabel}`),
      }) as HTMLInputElement
      await setDirection(row, 'ArrowRight', 'On')
      await waitFor(() => expect(desktop.checked).toBe(true))
      fireEvent.click(desktop)
      await waitFor(() => expect(row.getAttribute('aria-checked')).toBe('false'))
    }
  })
  it('refreshes an independent Library and preserves the newest other visibility preferences', async () => {
    const f = fixture()
    render(
      <>
        <LibraryRows />
        <LibraryObserver />
      </>,
      { wrapper: f.wrapper },
    )
    const row = await ready('Non-game entries', 'switch')
    f.updateLibrary({ showNonGameEntries: false, showExplicitContent: true, maturityCap: 'teen' })
    await setDirection(row, 'ArrowRight', 'On')
    expect(f.library()).toEqual({ showNonGameEntries: true, showExplicitContent: true, maturityCap: 'teen' })
    await waitFor(() => expect(screen.getByLabelText('Visible titles').textContent).toBe('Library tool'))
    const cap = await ready('Content age limit')
    expect(cap.textContent).toContain('Teen')
    await setDirection(cap, 'ArrowLeft', 'Preteen')
    expect(f.library()).toEqual({
      showNonGameEntries: true,
      showExplicitContent: true,
      maturityCap: 'preteen',
    })
  })
  it('retains the confirmed state after a failed visibility save and permits retry', async () => {
    const f = fixture()
    render(<LibraryRows />, { wrapper: f.wrapper })
    const row = await ready('Explicit content', 'switch')
    f.refuse(true)
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    await screen.findByText(/^Could not save this preference\./)
    expect(row.getAttribute('aria-checked')).toBe('false')
    await waitFor(() => expect(row.disabled).toBe(false))
    f.refuse(false)
    await setDirection(row, 'ArrowRight', 'On')
  })
  it('returns from Library tools to its originating row', async () => {
    const f = fixture()
    render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
    fireEvent.click(
      within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', {
        name: 'Library',
      }),
    )
    fireEvent.click(await ready('Library tools'))
    const back = screen.getByRole('button', { name: 'Back to Library' })
    await waitFor(() => expect(document.activeElement).toBe(back))
    fireEvent.keyDown(back, { key: 'Escape' })
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Library tools' })),
    )
  })
})

describe('fullscreen Application uses the shared native and saved preferences', () => {
  it.each([
    ['C:\\Legacy Hoard\\plugins', 'C:\\Legacy Hoard\\logs'],
    ['/home/test/Winnow/plugins', '/home/test/Winnow/logs'],
  ])('shows the active backend log directory for %s', async (directory, logs) => {
    const f = fixture()
    f.setDirectory(directory)
    render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
    await screen.findByText(logs)
    await screen.findByText('Winnow 1.0.0')
    expect((await ready('Start with Windows', 'switch')).disabled).toBe(false)
  })
  it.each(['desktop', 'fullscreen'] as const)(
    '%s reports a failed link preference save and retries from confirmed state',
    async (mode) => {
      const f = fixture()
      render(<ApplicationPreferences mode={mode} />, { wrapper: f.wrapper })
      const control = await ready('Open links in', mode === 'fullscreen' ? 'button' : 'combobox')
      const change = () =>
        mode === 'fullscreen'
          ? fireEvent.click(control)
          : fireEvent.change(control, { target: { value: 'browser' } })
      f.refuse(true)
      change()
      await screen.findByText(/^Could not save this preference\./)
      await waitFor(() => expect(control.disabled).toBe(false))
      expect(f.values.LinkDestination).toBeUndefined()
      if (mode === 'fullscreen') expect(control.textContent).toContain('In Winnow')
      else expect((control as unknown as HTMLSelectElement).value).toBe('in-app')
      f.refuse(false)
      change()
      await waitFor(() => expect(f.values.LinkDestination).toBe('browser'))
      await waitFor(() => expect(control.disabled).toBe(false))
      expect(screen.queryByRole('alert')).toBeNull()
    },
  )
  it.each([
    ['StartInFullscreen', 'Start in fullscreen'],
    ['MinimizeToTray', 'Minimize to tray'],
    ['CloseToTray', 'Close to tray'],
  ])('defaults %s off and reloads both confirmed values from storage', async (key, label) => {
    const f = fixture()
    let mounted = render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
    expect((await ready(label, 'switch')).getAttribute('aria-checked')).toBe('false')
    for (const value of [true, false]) {
      const row = await ready(label, 'switch')
      fireEvent.click(row)
      await waitFor(() => expect(row.getAttribute('aria-checked')).toBe(String(value)))
      await waitFor(() => expect(row.disabled).toBe(false))
      expect(f.values[key]).toBe(String(value))
      mounted.unmount()
      f.client.clear()
      mounted = render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
      expect((await ready(label, 'switch')).getAttribute('aria-checked')).toBe(String(value))
    }
  })
  it.each([
    ['Start in fullscreen', 'Start in fullscreen'],
    ['Minimize to tray', 'Minimize to notification area'],
    ['Close to tray', 'Close to notification area'],
  ])('shares %s between desktop and fullscreen in both directions', async (label, desktopLabel) => {
    const f = fixture()
    render(
      <>
        <ApplicationPreferences />
        <ApplicationPreferences mode="fullscreen" />
      </>,
      { wrapper: f.wrapper },
    )
    const row = await ready(label, 'switch')
    const desktop = screen.getByRole('checkbox', { name: new RegExp(`^${desktopLabel}`) }) as HTMLInputElement
    fireEvent.click(desktop)
    await waitFor(() => expect(row.getAttribute('aria-checked')).toBe('true'))
    await waitFor(() => expect(row.disabled).toBe(false))
    await setDirection(row, 'ArrowLeft', 'Off')
    await waitFor(() => expect(desktop.checked).toBe(false))
  })
  it.each([true, false])('cycles available link destinations with Steam available %s', async (available) => {
    const f = fixture({ LinkDestination: 'store' })
    f.info.steamStoreAvailable = available
    render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
    await screen.findByRole('switch', { name: 'Start with Windows' })
    const row = await ready('Open links in')
    expect(row.textContent).toContain(available ? 'Steam client, when available' : 'Default browser')
    expect(f.values.LinkDestination).toBe('store')
    await setDirection(row, 'ArrowRight', 'In Winnow')
    expect(f.values.LinkDestination).toBe('in-app')
    await setDirection(row, 'ArrowLeft', available ? 'Steam client, when available' : 'Default browser')
    expect(f.values.LinkDestination).toBe(available ? 'store' : 'browser')
  })
  it('keeps native startup registration confirmed after failure and retries without changing other preferences', async () => {
    const f = fixture()
    f.setOpenAtLogin.mockRejectedValueOnce(new Error('Startup registration failed.'))
    render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
    const row = await ready('Start with Windows', 'switch')
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    await screen.findByText('Startup registration failed.')
    expect(row.getAttribute('aria-checked')).toBe('false')
    await waitFor(() => expect(row.disabled).toBe(false))
    await setDirection(row, 'ArrowRight', 'On')
    expect(f.setOpenAtLogin.mock.calls).toEqual([[true], [true]])
    expect(f.values).toEqual({})
    fireEvent.click(screen.getByRole('button', { name: 'Open logs folder' }))
    expect(f.openDataFolder).toHaveBeenCalledWith('logs')
  })
  it('omits unsupported fullscreen startup registration while retaining the desktop explanation', async () => {
    const f = fixture()
    f.info.autostartSupported = false
    render(
      <>
        <ApplicationPreferences />
        <ApplicationPreferences mode="fullscreen" />
      </>,
      { wrapper: f.wrapper },
    )
    const row = (await screen.findByRole('checkbox', { name: /^Start with Windows/ })) as HTMLInputElement
    expect(row.disabled).toBe(true)
    expect(screen.queryByRole('switch', { name: 'Start with Windows' })).toBeNull()
    fireEvent.click(row)
    expect(f.setOpenAtLogin).not.toHaveBeenCalled()
  })
  it('reflects externally refreshed startup preferences', async () => {
    const f = fixture()
    render(<ApplicationPreferences mode="fullscreen" />, { wrapper: f.wrapper })
    const row = await ready('Start in fullscreen', 'switch')
    f.values.StartInFullscreen = 'true'
    await act(async () => {
      await f.client.invalidateQueries({ queryKey: ['api', 'preferences.presentation.get'] })
    })
    await waitFor(() => expect(row.getAttribute('aria-checked')).toBe('true'))
  })
  it('shares confirmed updater preferences through directional switches and desktop checkboxes', async () => {
    const f = fixture()
    let snapshot = initialUpdateSnapshot()
    const updateAction = vi.fn(async (action: string, value?: boolean) => {
      snapshot = { ...snapshot, [action === 'automatic' ? 'automatic' : 'includeBeta']: value }
      return snapshot
    })
    Object.assign(window.winnow, { updateSnapshot: async () => snapshot, updateAction })
    render(
      <>
        <ApplicationUpdates />
        <ApplicationUpdates mode="fullscreen" />
      </>,
      { wrapper: f.wrapper },
    )
    const automatic = await ready('Automatic background updates', 'switch')
    const desktop = screen.getByRole('checkbox', {
      name: 'Download updates automatically',
    }) as HTMLInputElement
    await setDirection(automatic, 'ArrowLeft', 'Off')
    expect(desktop.checked).toBe(false)
    const count = updateAction.mock.calls.length
    fireEvent.keyDown(automatic, { key: 'ArrowLeft' })
    expect(updateAction.mock.calls).toHaveLength(count)
    fireEvent.click(desktop)
    await waitFor(() => expect(automatic.getAttribute('aria-checked')).toBe('true'))
    const beta = await ready('Include beta releases', 'switch')
    await setDirection(beta, 'ArrowRight', 'On')
    expect(
      (screen.getByRole('checkbox', { name: 'Include beta releases' }) as HTMLInputElement).checked,
    ).toBe(true)
  })
})
