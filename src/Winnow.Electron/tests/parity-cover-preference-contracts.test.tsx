// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import { DisplayPreferences } from '../src/renderer/features/DisplayPreferences'
import { LibraryPresentationPreferences } from '../src/renderer/features/SettingsPreferences'
import { FullscreenAppearance } from '../src/renderer/features/FullscreenAppearance'

const clients: QueryClient[] = []
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  cleanup()
  for (const client of clients.splice(0)) client.clear()
  vi.unstubAllGlobals()
})

type Surface = 'desktop Display' | 'desktop Library' | 'fullscreen Appearance'
function fixture(initial: string | null) {
  let mode = initial
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'preferences.presentation.put') {
      expect(input.params?.preference).toBe('CoverArtMode')
      mode = (input.body as { value: string }).value
    }
    const data =
      input.route === 'preferences.presentation.get'
        ? [{ preference: 'CoverArtMode', value: mode }]
        : input.route === 'preferences.library.get'
          ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' }
          : input.route === 'library.visibility'
            ? { ratingCapHidden: 0 }
            : input.route === 'journal.preferences.get'
              ? { promptAfterPlay: false }
              : null
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  return {
    stored: () => mode,
    writes: () => request.mock.calls.filter(([input]) => input.route.endsWith('.put')),
    async open(surface: Surface) {
      const client = new QueryClient({
        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
      })
      clients.push(client)
      const view = render(
        <QueryClientProvider client={client}>
          {surface === 'desktop Display' ? (
            <DisplayPreferences />
          ) : surface === 'desktop Library' ? (
            <LibraryPresentationPreferences />
          ) : (
            <FullscreenAppearance />
          )}
        </QueryClientProvider>,
      )
      if (surface === 'desktop Display')
        fireEvent.click(screen.getByRole('button', { name: 'Display preferences' }))
      const control = (await screen.findByRole(surface === 'fullscreen Appearance' ? 'button' : 'combobox', {
        name: surface === 'desktop Library' ? 'Cover artwork' : 'Cover art',
      })) as HTMLSelectElement | HTMLButtonElement
      await waitFor(() => expect(control.disabled).toBe(false))
      return {
        control,
        close: () => {
          view.unmount()
          client.clear()
        },
      }
    },
  }
}
function selected(control: HTMLSelectElement | HTMLButtonElement) {
  if (control instanceof HTMLSelectElement) return control.value
  const valueId = control
    .getAttribute('aria-describedby')
    ?.split(' ')
    .find((id) => id.endsWith('-value'))
  return document.getElementById(valueId ?? '')?.textContent?.toLowerCase()
}
async function choose(control: HTMLSelectElement | HTMLButtonElement, mode: 'fit' | 'fill') {
  if (control instanceof HTMLSelectElement) fireEvent.change(control, { target: { value: mode } })
  else fireEvent.keyDown(control, { key: mode === 'fill' ? 'ArrowRight' : 'ArrowLeft' })
  await waitFor(() => expect(selected(control)).toBe(mode))
  await waitFor(() => expect(control.disabled).toBe(false))
}

describe.each(['desktop Display', 'desktop Library', 'fullscreen Appearance'] as const)(
  '%s cover preference source contracts',
  (surface) => {
    it.each([null, 'invalid', 'fit', 'fill'])(
      'loads source mode %s without writing it back',
      async (initial) => {
        const f = fixture(initial)
        const { control } = await f.open(surface)
        expect(selected(control)).toBe(initial === 'fill' ? 'fill' : 'fit')
        expect(f.stored()).toBe(initial)
        expect(f.writes()).toHaveLength(0)
      },
    )

    it('persists Fill, reloads it independently, ignores a missing choice and persists Fit', async () => {
      const f = fixture(null)
      const first = await f.open(surface)
      await choose(first.control, 'fill')
      expect(f.stored()).toBe('fill')
      expect(f.writes()).toHaveLength(1)
      first.close()
      const reopened = await f.open(surface)
      expect(selected(reopened.control)).toBe('fill')
      expect(f.writes()).toHaveLength(1)
      if (reopened.control instanceof HTMLSelectElement) {
        fireEvent.change(reopened.control, { target: { selectedIndex: -1 } })
      } else {
        fireEvent.keyDown(reopened.control, { key: 'ArrowUp' })
      }
      expect(f.stored()).toBe('fill')
      expect(f.writes()).toHaveLength(1)
      expect(selected(reopened.control)).toBe('fill')
      await choose(reopened.control, 'fit')
      expect(f.stored()).toBe('fit')
      expect(f.writes()).toHaveLength(2)
      reopened.close()
      const final = await f.open(surface)
      expect(selected(final.control)).toBe('fit')
      expect(f.writes()).toHaveLength(2)
    })
  },
)
