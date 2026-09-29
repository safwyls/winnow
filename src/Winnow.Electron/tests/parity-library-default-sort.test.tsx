// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  LibraryPresentationPreferences,
  usePresentationPreferences,
} from '../src/renderer/features/SettingsPreferences'
import { libraryDefaultSort, useAvalonLists } from '../src/renderer/themes/avalon-list-state'
import type { Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'

afterEach(() => {
  cleanup()
  for (const mode of ['desktop', 'fullscreen'])
    for (const key of ['sort', 'default-sort', 'sort-before-list'])
      clearViewState(`avalon:library:${mode}:${key}`)
})

function CurrentLibraryOrder({ mode }: { mode: Mode }) {
  const preferences = usePresentationPreferences()
  const fallback = libraryDefaultSort(preferences.values.DefaultSort)
  const { savedSort, setSort } = useAvalonLists(mode, [], true, preferences.loaded ? fallback : undefined)
  return (
    <>
      <output aria-label="Current browsing order">{savedSort ?? fallback}</output>
      <button onClick={() => setSort('title-desc')}>Browse Z–A temporarily</button>
    </>
  )
}

describe.each(['desktop', 'fullscreen'] as const)('saved default in %s', (mode) => {
  it.each([
    ['DormantLongest', 'dormant'],
    ['RecentlyPlayed', 'recent'],
    ['PlaytimeHighToLow', 'time'],
    ['PlaytimeLowToHigh', 'time-low'],
    ['NameAscending', 'title'],
    ['NameDescending', 'title-desc'],
  ])(
    'round trips %s through Settings and keeps a later temporary order out of the saved preference',
    async (value, order) => {
      const stored = new Map([
        ['DefaultSort', value === 'NameAscending' ? 'DormantLongest' : 'NameAscending'],
      ])
      const request = vi.fn(async (input: ApiRequest) => {
        if (input.route === 'preferences.presentation.put')
          stored.set(input.params!.preference as string, (input.body as { value: string }).value)
        return {
          ok: true,
          status: 200,
          data:
            input.route === 'preferences.presentation.get'
              ? [...stored].map(([preference, value]) => ({ preference, value }))
              : {},
        }
      })
      Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
      function mount() {
        return render(
          <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <LibraryPresentationPreferences />
            <CurrentLibraryOrder mode={mode} />
          </QueryClientProvider>,
        )
      }
      const initial = mount()
      await waitFor(() =>
        expect((screen.getByLabelText('Default library sort') as HTMLSelectElement).value).toBe(
          stored.get('DefaultSort'),
        ),
      )
      fireEvent.click(screen.getByRole('button', { name: 'Browse Z–A temporarily' }))
      fireEvent.change(screen.getByLabelText('Default library sort'), { target: { value } })
      await waitFor(() => expect(screen.getByLabelText('Current browsing order').textContent).toBe(order))
      expect(stored.get('DefaultSort')).toBe(value)
      fireEvent.click(screen.getByRole('button', { name: 'Browse Z–A temporarily' }))
      expect(screen.getByLabelText('Current browsing order').textContent).toBe('title-desc')
      initial.unmount()
      mount()
      await waitFor(() =>
        expect((screen.getByLabelText('Default library sort') as HTMLSelectElement).value).toBe(value),
      )
      expect(screen.getByLabelText('Current browsing order').textContent).toBe('title-desc')
      expect(
        request.mock.calls.filter(([input]) => input.route === 'preferences.presentation.put'),
      ).toHaveLength(1)
    },
  )
})
