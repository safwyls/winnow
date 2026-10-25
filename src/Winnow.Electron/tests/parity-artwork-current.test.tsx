// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ArtworkBrowser } from '../src/renderer/features/artwork-browser'
import type { ArtworkCandidate, ArtworkState, CoverKey, Mode } from '../src/renderer/api/types'
import type { ApiRequest, WinnowBridge } from '../src/shared/bridge'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
})
const automatic = (key: CoverKey): ArtworkCandidate => ({
  sourceId: 'automatic',
  sourceName: 'Automatic',
  assetId: `${key.provider}:${key.id}`,
  previewKey: key,
  isCurrent: true,
})
const imageSource = (key: CoverKey) => `data:image/png;base64,${btoa(`${key.provider}:${key.id}`)}`
const manual: ArtworkCandidate = {
  sourceId: 'igdb',
  sourceName: 'IGDB',
  assetId: 'manual',
  offerId: 'manual-offer',
  isCurrent: false,
  previewKey: { provider: 'test', id: 'valid' },
}
function mount(mode: Mode, options: { coverWorkId?: number; holdCanonical?: boolean; saved?: boolean } = {}) {
  let state: ArtworkState = {
    current: options.saved
      ? { ...manual, previewKey: { provider: 'user', id: 'chosen' } }
      : automatic({ provider: 'steam', id: '620' }),
    revision: 'canonical-one',
  }
  let projected: CoverKey = { provider: 'igdb', id: 'preferred' }
  let releaseCanonical!: () => void
  const canonical = options.holdCanonical
    ? new Promise<void>((resolve) => {
        releaseCanonical = resolve
      })
    : Promise.resolve()
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = {}
    if (input.route === 'artwork.get') {
      await canonical
      data = state
    }
    if (input.route === 'artworkState')
      data = { current: { previewKey: projected }, revision: 'display-only' }
    if (input.route === 'artwork.sources') data = [{ id: 'igdb', name: 'IGDB', slots: [0, 1, 2] }]
    if (input.route === 'artwork.browse') data = { items: [manual], nextCursor: null, canRetry: false }
    if (input.route === 'artwork.put') {
      state = {
        current: { ...manual, previewKey: { provider: 'user', id: 'chosen' }, isCurrent: true },
        revision: 'canonical-two',
      }
      data = { success: true, message: 'Artwork saved.' }
    }
    if (input.route === 'artwork.reset') {
      state = { current: automatic({ provider: 'steam', id: '620' }), revision: 'canonical-three' }
      data = { success: true, message: 'Artwork reset.' }
    }
    return { ok: true, status: 200, data }
  })
  const artwork = vi.fn(async (provider: string, id: string) => imageSource({ provider, id }))
  window.winnow = { request, artwork, cancelRequest: vi.fn(async () => true) } as unknown as WinnowBridge
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  let read = 0
  const publish = (key: CoverKey) => {
    projected = key
    client.setQueryData(['artwork', options.coverWorkId ?? 1, false], {
      selection: { current: { previewKey: key }, revision: 'display-only' },
      read: ++read,
    })
  }
  publish(projected)
  const view = render(
    <QueryClientProvider client={client}>
      <ArtworkBrowser
        workId={1}
        coverWorkId={options.coverWorkId}
        title="Game"
        mode={mode}
        initialSlot="Cover"
      />
    </QueryClientProvider>,
  )
  const writes = () =>
    request.mock.calls
      .map(([input]) => input)
      .filter((input) => ['artwork.put', 'artwork.reset'].includes(input.route))
  return { ...view, request, artwork, publish, releaseCanonical, writes, client }
}
async function expectPreview(key: CoverKey) {
  await waitFor(() =>
    expect(screen.getByRole('img', { name: 'Selected artwork preview' }).getAttribute('src')).toBe(
      imageSource(key),
    ),
  )
}

describe.each<Mode>(['desktop', 'fullscreen'])('%s displayed Current artwork', (mode) => {
  it('uses the live preferred cover, preserves the saved manual choice, and follows each projection after reset', async () => {
    const test = mount(mode)
    await screen.findByRole('button', { name: 'Automatic · igdb:preferred' })
    expect(test.client.getQueryData(['api', 'artwork.get', { workId: 1, slot: 'Cover' }])).toEqual({
      current: automatic({ provider: 'steam', id: '620' }),
      revision: 'canonical-one',
    })
    await expectPreview({ provider: 'igdb', id: 'preferred' })
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Use artwork' }).disabled).toBe(true)
    fireEvent.click(await screen.findByRole('button', { name: 'IGDB · manual' }))
    await expectPreview({ provider: 'test', id: 'valid' })
    expect(test.writes()).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    await expectPreview({ provider: 'user', id: 'chosen' })
    await waitFor(() =>
      expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Use automatic' }).disabled).toBe(false),
    )
    expect(test.writes()).toEqual([
      expect.objectContaining({
        route: 'artwork.put',
        params: { workId: 1, slot: 'Cover' },
        body: { revision: 'canonical-one', offerId: 'manual-offer' },
      }),
    ])
    await act(async () => test.publish({ provider: 'igdb', id: 'newprojection' }))
    await expectPreview({ provider: 'user', id: 'chosen' })
    expect(screen.queryByRole('button', { name: 'Automatic · igdb:newprojection' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Use automatic' }))
    await expectPreview({ provider: 'igdb', id: 'newprojection' })
    expect(test.writes()[1]).toMatchObject({
      route: 'artwork.reset',
      params: { workId: 1, slot: 'Cover' },
      body: { revision: 'canonical-two' },
    })
    const canonicalBeforeProjection = test.client.getQueryData([
      'api',
      'artwork.get',
      { workId: 1, slot: 'Cover' },
    ])
    await act(async () => test.publish({ provider: 'steam', id: '440' }))
    await expectPreview({ provider: 'steam', id: '440' })
    expect(screen.getByRole('button', { name: 'Automatic · steam:440' }).textContent).toContain('Current')
    expect(test.writes()).toHaveLength(2)
    expect(test.client.getQueryData(['api', 'artwork.get', { workId: 1, slot: 'Cover' }])).toBe(
      canonicalBeforeProjection,
    )
    expect(canonicalBeforeProjection).toEqual({
      current: automatic({ provider: 'steam', id: '620' }),
      revision: 'canonical-three',
    })
  })

  it('retains an explicit unsaved candidate through a live projection change and makes Current follow subsequent changes', async () => {
    const test = mount(mode)
    const choice = await screen.findByRole('button', { name: 'IGDB · manual' })
    choice.focus()
    fireEvent.click(choice)
    await expectPreview({ provider: 'test', id: 'valid' })
    await act(async () => test.publish({ provider: 'igdb', id: 'newprojection' }))
    await screen.findByRole('button', { name: 'Automatic · igdb:newprojection' })
    await expectPreview({ provider: 'test', id: 'valid' })
    expect(document.activeElement).toBe(choice)
    expect(choice.getAttribute('aria-pressed')).toBe('true')
    expect(test.writes()).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Automatic · igdb:newprojection' }))
    await expectPreview({ provider: 'igdb', id: 'newprojection' })
    await act(async () => test.publish({ provider: 'steam', id: '440' }))
    await expectPreview({ provider: 'steam', id: '440' })
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Use artwork' }).disabled).toBe(true)
    expect(test.writes()).toHaveLength(0)
  })

  it('waits for canonical saved-choice authority before showing an automatic projection', async () => {
    const test = mount(mode, { holdCanonical: true, saved: true })
    await screen.findByRole('button', { name: 'IGDB · manual' })
    expect(screen.queryByRole('button', { name: 'Automatic · igdb:preferred' })).toBeNull()
    expect(screen.queryByRole('img', { name: 'Selected artwork preview' })).toBeNull()
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Use artwork' }).disabled).toBe(true)
    await act(async () => test.releaseCanonical())
    await expectPreview({ provider: 'user', id: 'chosen' })
    expect(screen.queryByRole('button', { name: 'Automatic · igdb:preferred' })).toBeNull()
    expect(test.writes()).toHaveLength(0)
  })

  it('reads the preferred header cover while retaining canonical offers, revision, and mutation identity', async () => {
    const test = mount(mode, { coverWorkId: 2 })
    await expectPreview({ provider: 'igdb', id: 'preferred' })
    await act(async () => {
      await test.client.invalidateQueries({ queryKey: ['artwork', 2, false] })
    })
    expect(
      test.request.mock.calls.some(([input]) => input.route === 'artworkState' && input.params?.workId === 2),
    ).toBe(true)
    const candidate = await screen.findByRole('button', { name: 'IGDB · manual' })
    fireEvent.click(candidate)
    fireEvent.click(screen.getByRole('button', { name: 'Use artwork' }))
    await expectPreview({ provider: 'user', id: 'chosen' })
    const reads = test.request.mock.calls
      .map(([input]) => input)
      .filter((input) => ['artwork.get', 'artwork.browse'].includes(input.route))
    expect(reads.length).toBeGreaterThan(1)
    expect(reads.every((input) => input.params?.workId === 1)).toBe(true)
    expect(test.writes()).toEqual([
      expect.objectContaining({
        params: { workId: 1, slot: 'Cover' },
        body: { revision: 'canonical-one', offerId: 'manual-offer' },
      }),
    ])
  })
})
