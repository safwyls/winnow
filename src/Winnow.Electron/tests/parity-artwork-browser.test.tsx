// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ArtworkBrowser, ArtworkBrowserDialog } from '../src/renderer/features/artwork-browser'
import { MetadataEditor } from '../src/renderer/features/parity-details'
import { clearViewState } from '../src/renderer/viewState'
import type { ArtworkCandidate, ArtworkState, Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('draft:metadata-fields:42')
  clearViewState('metadata-fields:42:sending')
})
const revision = 'A'.repeat(64)
const candidate = (sourceId = 'steam', slot = 'Hero', suffix = 'first'): ArtworkCandidate => ({
  sourceId,
  sourceName: sourceId === 'steam' ? 'Steam' : 'IGDB',
  assetId: `${sourceId}-${slot}-${suffix}`,
  offerId: `${sourceId}-${slot}-${suffix}`,
  creator: 'Painter',
  pageUrl: 'https://www.steamgriddb.com/grid/1',
  width: 1920,
  height: 1080,
  previewKey: { provider: sourceId, id: `${slot}-${suffix}` },
  isCurrent: false,
})
function mount(mode: Mode, handler?: (input: ApiRequest) => unknown, dialog: boolean | 'metadata' = false) {
  const states: Record<string, ArtworkState> = Object.fromEntries(
    ['Hero', 'Cover', 'Icon'].map((slot) => [slot, { current: null, revision }]),
  )
  const order: string[] = [],
    onClose = vi.fn()
  const request = vi.fn(async (input: ApiRequest) => {
    order.push(`${input.route}:${input.params?.slot ?? ''}`)
    const override = await handler?.(input)
    if (override !== undefined) return override
    let data: unknown = {}
    if (input.route === 'artwork.sources')
      data = [
        { id: 'steam', name: 'Steam', slots: [0, 1, 2] },
        { id: 'igdb', name: 'IGDB', slots: [0, 1] },
      ]
    if (input.route === 'artwork.get') data = states[String(input.params!.slot)]
    if (input.route === 'artwork.browse') {
      const { slot, source, cursor } = input.params!
      data = {
        items: [candidate(String(source), String(slot), cursor ? 'second' : 'first')],
        nextCursor: cursor ? null : 'second',
        canRetry: false,
      }
    }
    if (['artwork.put', 'artwork.reset', 'artwork.url'].includes(input.route)) {
      const slot = String(input.params!.slot)
      states[slot] = {
        current:
          input.route === 'artwork.reset' ? candidate('igdb', slot, 'automatic') : candidate('steam', slot),
        revision: 'B'.repeat(64),
      }
      data = { success: true, message: 'Artwork saved.' }
    }
    return { ok: true, status: 200, data }
  })
  const artwork = vi.fn().mockResolvedValue('data:image/png;base64,aA=='),
    cancelRequest = vi.fn().mockResolvedValue(true)
  const openExternal = vi.fn().mockResolvedValue({ opened: true, message: 'Opened in your browser.' })
  const importArtwork = vi.fn().mockResolvedValue(null)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork, cancelRequest, openExternal, importArtwork },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const ui = () => (
    <QueryClientProvider client={client}>
      {dialog === 'metadata' ? (
        <MetadataEditor workId={42} mode={mode} />
      ) : dialog ? (
        <ArtworkBrowserDialog workId={42} title="A distant shore" mode={mode} onClose={onClose} />
      ) : (
        <ArtworkBrowser workId={42} mode={mode} />
      )}
    </QueryClientProvider>
  )
  const view = render(ui())
  const writes = () =>
    request.mock.calls.filter(([input]) =>
      ['artwork.put', 'artwork.reset', 'artwork.url'].includes(input.route),
    )
  return {
    ...view,
    ui,
    states,
    client,
    request,
    artwork,
    cancelRequest,
    openExternal,
    importArtwork,
    writes,
    order,
    onClose,
  }
}
const choose = async (source = 'steam', slot = 'Hero', suffix = 'first') => {
  const button = await screen.findByRole('button', { name: new RegExp(`${source}-${slot}-${suffix}$`) })
  fireEvent.click(button)
  return button
}
describe.each<Mode>(['desktop', 'fullscreen'])('%s artwork browser source contracts', (mode) => {
  it('ignores a cancelled provider result after immediate reopen and refreshes external current art', async () => {
    let finish!: (value: unknown) => void,
      reads = 0
    const test = mount(mode, (input) => {
      if (input.route === 'artwork.browse' && input.params?.source === 'steam' && ++reads === 1)
        return new Promise((resolve) => {
          finish = resolve
        })
      return undefined
    })
    await screen.findByRole('button', { name: /igdb-Hero-first$/ })
    await waitFor(() => expect(reads).toBe(1))
    test.unmount()
    await waitFor(() => expect(test.cancelRequest).toHaveBeenCalled())
    test.states.Hero = { current: candidate('steam', 'Hero', 'external-edit'), revision: 'B'.repeat(64) }
    render(test.ui())
    await screen.findByRole('button', { name: /steam-Hero-first$/ })
    await screen.findByRole('button', { name: /steam-Hero-external-edit$/ })
    await act(async () =>
      finish({
        ok: true,
        status: 200,
        data: { items: [candidate('steam', 'Hero', 'late')], nextCursor: null, canRetry: false },
      }),
    )
    expect(screen.queryByRole('button', { name: /steam-Hero-late$/ })).toBeNull()
    expect(screen.getByRole('button', { name: /steam-Hero-external-edit$/ }).textContent).toContain('Current')
    expect(test.writes()).toHaveLength(0)
  })
  it('keeps the metadata draft and returns focus to its invoking artwork row', async () => {
    const test = mount(
      mode,
      (input) =>
        input.route === 'metadata.get'
          ? {
              ok: true,
              status: 200,
              data: {
                workId: 42,
                title: 'A distant shore',
                revision: 'metadata-one',
                isPinned: false,
                fields: [
                  { field: 'name', value: 'A distant shore', source: 'igdb' },
                  { field: 'cover_url', value: null, source: null },
                ],
              },
            }
          : undefined,
      'metadata',
    )
    const name = await screen.findByLabelText('Name')
    fireEvent.change(name, { target: { value: 'Unfinished title' } })
    const browse = screen.getByRole('button', { name: 'Browse cover artwork' })
    browse.focus()
    fireEvent.click(browse)
    const dialog = await screen.findByRole('dialog', { name: 'A distant shore' })
    expect(within(dialog).getByRole('button', { name: 'Cover artwork' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
    expect(within(dialog).getByRole('button', { name: 'Cover artwork' })).toBe(document.activeElement)
    await choose('steam', 'Cover')
    fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Unfinished title')
    expect(document.activeElement).toBe(browse)
    expect(test.writes()).toHaveLength(0)
  })
  it.each(['fit', 'fill'])(
    'uses the saved %s cover preference and offers both hero crop previews',
    async (cover) => {
      mount(mode, (input) =>
        input.route === 'preferences.get'
          ? { ok: true, status: 200, data: { CoverArtMode: cover } }
          : undefined,
      )
      await choose()
      fireEvent.click(screen.getByRole('button', { name: 'Fullscreen crop · 16:9' }))
      expect((document.querySelector('.artwork-preview-frame') as HTMLElement).style.aspectRatio).toBe(
        '16 / 9',
      )
      fireEvent.click(screen.getByRole('button', { name: /^Desktop crop/ }))
      expect(
        Number(
          (document.querySelector('.artwork-preview-frame') as HTMLElement).style.aspectRatio.split('/')[0],
        ),
      ).toBeCloseTo(4 / 3)
      fireEvent.click(screen.getByRole('button', { name: 'Cover artwork' }))
      await choose('steam', 'Cover')
      expect(document.querySelector('.artwork-preview-frame')?.getAttribute('data-cover-fit')).toBe(cover)
      expect((document.querySelector('.artwork-preview-frame') as HTMLElement).style.aspectRatio).toBe(
        '2 / 3',
      )
    },
  )
  it('previews without writing, saves only the chosen slot and resets only that slot', async () => {
    const test = mount(mode)
    await choose()
    expect(test.writes()).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Icon artwork' }))
    await choose('steam', 'Icon')
    expect(test.writes()).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    await screen.findByText('Artwork saved.')
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Use artwork' }) as HTMLButtonElement).disabled).toBe(true),
    )
    expect(test.writes()).toHaveLength(1)
    expect(test.writes()[0][0]).toMatchObject({
      route: 'artwork.put',
      params: { workId: 42, slot: 'Icon' },
      body: { offerId: 'steam-Icon-first', revision },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Use automatic' }))
    await waitFor(() => expect(test.writes()).toHaveLength(2))
    expect(test.writes()[1][0]).toMatchObject({
      route: 'artwork.reset',
      params: { workId: 42, slot: 'Icon' },
      body: { revision: 'B'.repeat(64) },
    })
  })
  it('loads retries and pages independently while retaining candidate identity and selection', async () => {
    let fail = true
    const test = mount(mode, (input) =>
      input.route === 'artwork.browse' && input.params?.source === 'igdb' && fail
        ? { ok: false, status: 503, message: 'Unavailable' }
        : undefined,
    )
    const first = await choose()
    await screen.findByRole('button', { name: 'Retry IGDB' })
    fireEvent.click(screen.getByRole('button', { name: 'Load more Steam' }))
    await screen.findByRole('button', { name: /steam-Hero-second$/ })
    expect(screen.getByRole('button', { name: /steam-Hero-first$/ })).toBe(first)
    expect(first.getAttribute('aria-pressed')).toBe('true')
    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Retry IGDB' }))
    await screen.findByRole('button', { name: /igdb-Hero-first$/ })
    expect(first.getAttribute('aria-pressed')).toBe('true')
    expect(test.writes()).toHaveLength(0)
  })
  it('makes unsupported icon sources explicit without querying or hiding them', async () => {
    const test = mount(mode)
    await choose()
    fireEvent.click(screen.getByRole('button', { name: 'Icon artwork' }))
    expect(await screen.findByText('IGDB does not offer icon artwork.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'IGDB' })).toBeTruthy()
    expect(
      test.request.mock.calls.some(
        ([input]) =>
          input.route === 'artwork.browse' && input.params?.slot === 'Icon' && input.params.source === 'igdb',
      ),
    ).toBe(false)
  })
  it('retains each slot preview source and gallery position and cycles with controller triggers', async () => {
    const test = mount(mode)
    await choose()
    fireEvent.click(screen.getByRole('button', { name: 'Steam' }))
    const gallery = document.querySelector<HTMLElement>('.artwork-browser-gallery')!
    gallery.scrollTop = 150
    fireEvent.scroll(gallery)
    const slots = document.querySelector<HTMLElement>('[data-controller-page]')!
    for (const name of ['Cover', 'Icon', 'Hero']) {
      fireEvent.keyDown(slots, { key: 'PageDown' })
      expect(screen.getByRole('button', { name: `${name} artwork` })).toBe(document.activeElement)
      await screen.findByRole('button', { name: new RegExp(`steam-${name}-first$`) })
    }
    expect(screen.getByRole('button', { name: /steam-Hero-first$/ }).getAttribute('aria-pressed')).toBe(
      'true',
    )
    expect(screen.getByRole('button', { name: 'Steam' }).getAttribute('aria-pressed')).toBe('true')
    expect(gallery.scrollTop).toBe(150)
    for (const name of ['Icon', 'Cover', 'Hero']) {
      fireEvent.keyDown(slots, { key: 'PageUp' })
      expect(screen.getByRole('button', { name: `${name} artwork` }).getAttribute('aria-pressed')).toBe(
        'true',
      )
    }
    expect(test.writes()).toHaveLength(0)
  })
  it('keeps a refused save selected and prevents slot changes or duplicate saves while pending', async () => {
    let finish!: (value: unknown) => void
    const test = mount(mode, (input) =>
      input.route === 'artwork.put'
        ? new Promise((resolve) => {
            finish = resolve
          })
        : undefined,
    )
    const first = await choose()
    const save = screen.getByRole('button', { name: 'Use artwork' })
    fireEvent.click(save)
    fireEvent.click(save)
    fireEvent.keyDown(document.querySelector('[data-controller-page]')!, { key: 'PageDown' })
    expect(screen.getByRole('button', { name: 'Hero artwork' }).getAttribute('aria-pressed')).toBe('true')
    expect(test.writes()).toHaveLength(1)
    await act(async () =>
      finish({
        ok: true,
        status: 200,
        data: { success: false, message: 'The chosen image could not be saved.' },
      }),
    )
    expect(await screen.findByText('The chosen image could not be saved.')).toBeTruthy()
    expect(first.getAttribute('aria-pressed')).toBe('true')
    expect((save as HTMLButtonElement).disabled).toBe(false)
  })
  it('refreshes the library projection before Current and distinguishes a saved image from refresh failure', async () => {
    const test = mount(mode)
    await choose()
    const refresh = vi
      .spyOn(test.client, 'invalidateQueries')
      .mockRejectedValue(new Error('Projection unavailable'))
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    await screen.findByText(
      'Artwork saved, but the library could not refresh. Reopen this game to refresh it.',
    )
    expect(screen.getByText('Artwork saved.')).toBeTruthy()
    expect(test.writes()).toHaveLength(1)
    expect(refresh).toHaveBeenCalledWith({ queryKey: ['api', 'library.get'] }, { throwOnError: true })
    const lastWrite = test.order.indexOf('artwork.put:Hero')
    expect(test.order.slice(lastWrite + 1)).not.toContain('artwork.get:Hero')
  })
  it('routes source attribution through the shared dispatcher and keeps imports explicit', async () => {
    const test = mount(mode)
    await choose()
    fireEvent.click(screen.getByRole('button', { name: 'Open artwork source' }))
    await screen.findByText('Opened in your browser.')
    expect(test.openExternal).toHaveBeenCalledWith('https://www.steamgriddb.com/grid/1')
    fireEvent.click(screen.getByRole('button', { name: 'Choose file' }))
    await waitFor(() =>
      expect(test.importArtwork).toHaveBeenCalledWith({ workId: 42, slot: 'Hero', revision }),
    )
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Use artwork' }) as HTMLButtonElement).disabled).toBe(false),
    )
    expect(test.writes()).toHaveLength(0)
    fireEvent.change(screen.getByLabelText('Artwork image URL'), { target: { value: ' ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Import URL' }))
    expect(await screen.findByText('Enter a valid image URL.')).toBeTruthy()
    expect(test.writes()).toHaveLength(0)
  })
  it('requires a fresh revision after a conflict before a deliberate retry', async () => {
    let conflict = true
    const test = mount(mode, (input) =>
      input.route === 'artwork.put' && conflict
        ? { ok: false, status: 409, message: 'Artwork changed in another window.' }
        : undefined,
    )
    await choose()
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    const refresh = await screen.findByRole('button', { name: 'Refresh current artwork' })
    expect((screen.getByRole('button', { name: 'Use artwork' }) as HTMLButtonElement).disabled).toBe(true)
    test.states.Hero = { current: null, revision: 'C'.repeat(64) }
    conflict = false
    fireEvent.click(refresh)
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Use artwork' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    await waitFor(() => expect(test.writes()).toHaveLength(2))
    expect(test.writes()[1][0].body).toMatchObject({ revision: 'C'.repeat(64) })
  })
  it('closes its own overlay without writing a preview', async () => {
    const test = mount(mode, undefined, true)
    const dialog = screen.getByRole('dialog', { name: 'A distant shore' })
    await choose()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Back' }))
    expect(test.onClose).toHaveBeenCalledTimes(1)
    expect(test.writes()).toHaveLength(0)
  })
})
