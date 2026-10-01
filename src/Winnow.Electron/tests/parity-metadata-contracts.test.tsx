// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import { MetadataEditor } from '../src/renderer/features/parity-details'
import { ReceptionLine } from '../src/renderer/features/details-presentation'
import { AvalonBackdropController } from '../src/renderer/themes/avalon-backdrop-model'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameDetails, Metadata, Mode } from '../src/renderer/api/types'

const initialFields: Metadata['fields'] = [
  { field: 'name', value: 'Prey', source: null },
  { field: 'first_release_year', value: '2006', source: null },
  { field: 'summary', value: 'A Cherokee garage mechanic is abducted.', source: null },
  { field: 'cover_url', value: null, source: null },
  { field: 'publisher', value: '2K Games', source: null },
  { field: 'background_url', value: null, source: null },
]
const clients: QueryClient[] = []
const ok = (data: unknown) => ({ ok: true, status: 200, data })
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.restoreAllMocks()
  document.documentElement.classList.remove('reduced-motion')
  for (const workId of [1, 42]) {
    clearViewState(`draft:metadata-fields:${workId}`)
    clearViewState(`metadata-fields:${workId}:sending`)
  }
})

describe('Reception accessibility after a background read', () => {
  const ratings: GameDetails['ratings'] = [
    {
      source: 'igdb_users',
      score: 90,
      ratingCount: 20,
      hasFigure: true,
      observedAt: '2026-09-10T12:00:00Z',
    },
  ]
  const name = 'IGDB user rating: 90 out of 100, from 20 ratings.'
  it('exposes the compact header as one named group while keeping counts in the tooltip', () => {
    render(<ReceptionLine ratings={ratings} compact />)
    const groups = screen.getAllByRole('group', { name })
    expect(groups).toHaveLength(1)
    expect(groups[0].textContent).toBe('IGDB: 90')
    expect(within(groups[0]).queryByRole('group')).toBeNull()
    expect(screen.getByTitle(name).textContent).toBe('IGDB: 90')
  })
  it('exposes the full figure attribution within the named Reception group', () => {
    render(<ReceptionLine ratings={ratings} />)
    const group = screen.getByRole('group', { name: 'Reception' })
    expect(within(group).getAllByRole('group', { name })).toHaveLength(1)
    expect(group.textContent).toBe('IGDB USERS 90 / 20 ratings')
  })
})

// These exact Prey facts exercise renderer composition; native/API fixtures establish persistence.
function setup(
  mode: Mode,
  options: {
    available?: boolean
    picker?: boolean
    surface?: 'avalon' | 'shared' | 'editor'
    allIgdb?: boolean
  } = {},
) {
  const workId = options.allIgdb ? 42 : 1
  const originalTitle = options.allIgdb ? 'Empyrion: Galactic Survival' : 'Prey'
  let saved: Metadata = {
    workId,
    title: originalTitle,
    isPinned: false,
    available: options.available,
    revision: 'metadata-1',
    fields: structuredClone(initialFields).map((field) => ({
      ...field,
      value: options.allIgdb ? `${field.field}-value` : field.value,
      source: options.allIgdb ? 'igdb' : field.source,
    })),
  }
  let version = 1
  const request = vi.fn(async (input: ApiRequest) => {
    const value = (field: string) => saved.fields.find((row) => row.field === field)?.value
    switch (input.route) {
      case 'metadata.get':
        return ok(structuredClone(saved))
      case 'metadata.put':
      case 'metadata.reset': {
        const body = input.body as { field: string; value?: string; expectedRevision: string }
        if (body.expectedRevision !== saved.revision)
          return { ok: false, status: 409, message: 'Metadata changed.', data: saved }
        const reset = input.route === 'metadata.reset'
        saved = {
          ...saved,
          revision: `metadata-${++version}`,
          fields: saved.fields.map((field) =>
            field.field === body.field
              ? {
                  ...field,
                  value: reset ? null : body.value?.trim(),
                  source: reset ? null : 'user',
                }
              : field,
          ),
        }
        if (body.field === 'name' && !reset) saved.title = body.value!.trim()
        return ok({ outcome: 'Applied' })
      }
      case 'library.get':
        return ok({
          games: [
            {
              workId,
              title: saved.title,
              firstReleaseYear: options.allIgdb ? undefined : Number(value('first_release_year')),
              summary: value('summary'),
              publisher: value('publisher'),
              bucket: 'bounced',
              playtimeMinutes: 120,
              lastPlayedAt: '2024-01-02T00:00:00Z',
              entries: [
                {
                  workId,
                  ownershipId: 1,
                  releaseId: 1,
                  title: originalTitle,
                  store: 'gog',
                  installed: false,
                  playtimeMinutes: 120,
                  lastPlayedAt: '2024-01-02T00:00:00Z',
                },
              ],
            },
          ],
          lists: [],
        })
      case 'library.workspace':
        return ok({
          works: [{ id: workId, name: saved.title }],
          externalIds: [],
          pluginActions: {},
          epicLaunchKeys: {},
        })
      case 'game.details':
        return ok({
          workId,
          readAtUtc: '2026-09-01T00:00:00Z',
          sessions: {},
          events: [],
          journalEntries: [],
          ratings: [],
          achievements: [],
          images: [],
          ownerships: [{ id: 1, releaseId: 1, store: 'gog' }],
        })
      case 'metadata.igdb':
        return ok({ workId, revision: 'igdb-1', mappingRevision: 0, pin: null })
      case 'artwork.sources':
        return ok([])
      case 'artwork.backdrops':
        return ok({ candidates: [], coverKey: null })
      case 'artworkState':
        return ok({ current: null, revision: 'art-1' })
      case 'journal.preferences.get':
        return ok({ promptAfterPlay: false })
      default:
        return ok({})
    }
  })
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      artwork: vi.fn().mockResolvedValue(null),
      openExternal: vi.fn(),
      ...(options.picker ? { importArtwork: vi.fn() } : {}),
    },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  })
  clients.push(client)
  const close = vi.fn()
  const content = (next: Mode) => (
    <QueryClientProvider client={client}>
      {options.surface === 'editor' ? (
        <MetadataEditor workId={workId} mode={next} />
      ) : (
        <Details workId={workId} mode={next} presentation={options.surface ?? 'avalon'} onClose={close} />
      )}
    </QueryClientProvider>
  )
  const view = render(content(mode))
  return {
    ...view,
    client,
    request,
    close,
    saved: () => saved,
    mode: (next: Mode) => view.rerender(content(next)),
    available: async (available: boolean) => {
      saved = { ...saved, available }
      await act(async () => {
        await client.invalidateQueries({ queryKey: ['api', 'metadata.get'] })
      })
    },
  }
}
function click(name: string) {
  fireEvent.click(screen.getByRole('button', { name }))
}
function input(label: string) {
  return screen.getByLabelText(label) as HTMLInputElement | HTMLTextAreaElement
}
function set(label: string, value: string) {
  fireEvent.change(input(label), { target: { value } })
}
function row(label: string) {
  return input(label).closest('form')!
}
async function open(title = 'Prey') {
  await screen.findByRole('heading', { name: title, level: 1 })
  click('More')
  const trigger = screen.getByRole('button', { name: 'Edit details' })
  expect(trigger.title).toBe('Edit each field by hand')
  fireEvent.click(trigger)
  await screen.findByRole('dialog', { name: `Edit metadata · ${title}` })
}
async function field(mode: Mode, label: string, source = 'AUTO') {
  if (mode === 'fullscreen') click(`${label} · ${source}`)
  await screen.findByLabelText(label)
}
function writes(view: ReturnType<typeof setup>) {
  return view.request.mock.calls
    .map(([input]) => input)
    .filter((input) =>
      ['metadata.put', 'metadata.reset', 'metadata.art-upload', 'metadata.art-download'].includes(
        input.route,
      ),
    )
}

describe.each<Mode>(['desktop', 'fullscreen'])('%s frozen metadata composition contracts', (mode) => {
  it('The_menu_row_keeps_one_face', async () => {
    setup(mode, { allIgdb: true })
    await open('Empyrion: Galactic Survival')
    click('Back')
    await waitFor(() => expect(document.querySelector('.metadata-editor')).toBeNull())
    click('More')
    const trigger = screen.getByRole('button', { name: 'Edit details' })
    expect(trigger.title).toBe('Edit each field by hand')
    expect(trigger.getAttribute('aria-label') ?? trigger.textContent?.trim()).toBe('Edit details')
  })
  it('The_section_closes_itself', async () => {
    const view = setup(mode, { allIgdb: true })
    await screen.findByRole('heading', { name: 'Empyrion: Galactic Survival', level: 1 })
    fireEvent.click(screen.getByRole('tab', { name: 'Library' }))
    const details = document.querySelector('.avalon-details')
    await open('Empyrion: Galactic Survival')
    click('Back')
    await waitFor(() => expect(document.querySelector('.metadata-editor')).toBeNull())
    expect(document.querySelector('.avalon-details')).toBe(details)
    expect(screen.getByRole('tab', { name: 'Library' }).getAttribute('aria-selected')).toBe('true')
    expect(view.close).not.toHaveBeenCalled()
    expect(writes(view)).toEqual([])
  })
  it('Choosing_an_open_row_again_keeps_the_section_and_the_drafts_in_it', async () => {
    const view = setup(mode, { allIgdb: true })
    await open('Empyrion: Galactic Survival')
    await field(mode, 'Name', 'IGDB')
    expect(input('Name').value).toBe('name-value')
    set('Name', 'A title the user is part-way through typing')
    const editor = document.querySelector('.metadata-editor')
    const name = input('Name')
    const reads = view.request.mock.calls.filter(([input]) => input.route === 'metadata.get').length
    // A second invocation of the same production open handler is idempotent, as the source command is.
    fireEvent.click(document.querySelector<HTMLButtonElement>('.avalon-details-more > button')!)
    fireEvent.click(document.querySelector<HTMLButtonElement>('button[title="Edit each field by hand"]')!)
    expect(document.querySelector('.metadata-editor')).toBe(editor)
    expect(input('Name')).toBe(name)
    expect(name.value).toBe('A title the user is part-way through typing')
    expect(view.request.mock.calls.filter(([input]) => input.route === 'metadata.get')).toHaveLength(reads)
    expect(writes(view)).toEqual([])
  })
  it('The_editor_writes_the_work_the_tile_resolves_to', async () => {
    const view = setup(mode)
    await open()
    await field(mode, 'Name')
    expect(document.querySelectorAll('[data-metadata-field]')).toHaveLength(6)
    expect(input('Name').value).toBe('Prey')
    set('Name', 'Prey (2006)')
    click('Save name')
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Saved.'))
    expect(writes(view)).toEqual([
      expect.objectContaining({
        route: 'metadata.put',
        params: { workId: 1 },
        body: { field: 'name', value: 'Prey (2006)', expectedRevision: 'metadata-1' },
      }),
    ])
    expect(view.saved().fields.find((field) => field.field === 'name')).toMatchObject({
      value: 'Prey (2006)',
      source: 'user',
    })
    expect(
      view
        .saved()
        .fields.filter((field) => field.field !== 'name')
        .map((field) => field.source),
    ).toEqual([null, null, null, null, null])
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('A_field_can_be_handed_back_to_automatic_from_the_modal', async () => {
    const view = setup(mode)
    await open()
    await field(mode, 'Publisher')
    set('Publisher', 'Human Head Studios')
    click('Save publisher')
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Saved.'))
    await field(mode, 'Publisher', 'YOU')
    click('Use automatic publisher')
    if (mode === 'fullscreen') {
      const confirm = await screen.findByRole('dialog', { name: 'Reset Publisher?' })
      fireEvent.click(within(confirm).getByRole('button', { name: 'Use automatic publisher' }))
    }
    await waitFor(() =>
      expect(screen.getByRole('status').textContent).toBe('Publisher returned to automatic.'),
    )
    await field(mode, 'Publisher')
    expect(input('Publisher').value).toBe('')
    expect(screen.queryByRole('button', { name: 'Use automatic publisher' })).toBeNull()
    expect(view.saved().fields.find((field) => field.field === 'publisher')).toEqual({
      field: 'publisher',
      value: null,
      source: null,
    })
    expect(writes(view).at(-1)).toMatchObject({
      route: 'metadata.reset',
      params: { workId: 1 },
      body: { field: 'publisher', expectedRevision: 'metadata-2' },
    })
  })
  it('A_saved_name_reaches_the_tile_and_the_headline_without_touching_the_drafts', async () => {
    const view = setup('desktop')
    await open()
    await screen.findByLabelText('Name')
    set('About', 'A half-written sentence.')
    set('Publisher', 'Human Head Studios')
    view.mode(mode)
    await field(mode, 'Name')
    const details = document.querySelector('.avalon-details')
    const editor = document.querySelector('.metadata-editor')
    const title = document.querySelector('.avalon-details h1')
    set('Name', 'Prey (2006)')
    click('Save name')
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Saved.'))
    expect(document.querySelector('.avalon-details')).toBe(details)
    expect(document.querySelector('.metadata-editor')).toBe(editor)
    expect(title?.textContent).toBe('Prey (2006)')
    expect(
      view.client.getQueryData<{ games: { title: string }[] }>(['api', 'library.get'])?.games[0].title,
    ).toBe('Prey (2006)')
    expect(input('About').value).toBe('A half-written sentence.')
    expect(input('Publisher').value).toBe('Human Head Studios')
    expect(within(row('About')).getByText('AUTO')).toBeTruthy()
    expect(within(row('Publisher')).getByText('AUTO')).toBeTruthy()
    expect(screen.getByRole('dialog', { name: 'Edit metadata · Prey (2006)' })).toBeTruthy()
  })
  it.each(['avalon', 'shared', 'editor'] as const)('No_service_means_no_editor: %s', async (surface) => {
    const view = setup(mode, { surface, available: false })
    await waitFor(() =>
      expect(view.client.getQueryData(['api', 'metadata.get', { workId: 1 }])).toMatchObject({
        available: false,
      }),
    )
    if (surface === 'avalon') {
      await screen.findByRole('button', { name: 'More' })
      click('More')
    }
    expect(screen.queryByRole('button', { name: 'Edit details' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Metadata' })).toBeNull()
    expect(document.querySelector('.metadata-editor')).toBeNull()
    expect(writes(view)).toEqual([])
  })
  it.each([true, undefined])('available=%s retains the metadata editor', async (available) => {
    setup(mode, { available })
    await open()
    await field(mode, 'Name')
    expect(input('Name').value).toBe('Prey')
  })
  it('removing the optional metadata service closes only its editor', async () => {
    const view = setup(mode, { available: true })
    await open()
    await view.available(false)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit metadata · Prey' })).toBeNull())
    expect(screen.getByRole('heading', { name: 'Prey', level: 1 })).toBeTruthy()
    expect(view.close).not.toHaveBeenCalled()
  })
  it('No_picker_means_the_url_route_only', async () => {
    const view = setup(mode)
    await open()
    for (const label of ['Cover art', 'Background art']) {
      if (mode === 'fullscreen') {
        click(`${label} · AUTO`)
        await screen.findByLabelText('Artwork image URL')
        expect(screen.queryByRole('button', { name: 'Choose file' })).toBeNull()
        expect(screen.getByRole('button', { name: 'Import URL' })).toBeTruthy()
        const browser = document.querySelector('.artwork-browser-dialog')!
        fireEvent.click(within(browser as HTMLElement).getByRole('button', { name: 'Back' }))
      } else {
        expect(input(label)).toBeTruthy()
        expect(screen.getByRole('button', { name: `Save ${label.toLowerCase()}` })).toBeTruthy()
        expect(screen.queryByLabelText(`Choose ${label.toLowerCase()} file`)).toBeNull()
      }
    }
    expect(document.querySelectorAll('input[type="file"]')).toHaveLength(0)
    expect(writes(view)).toEqual([])
  })
  it.each([false, true])('Reduced_motion_reaches_the_panel: saved=%s', async (reduced) => {
    document.documentElement.classList.toggle('reduced-motion', reduced)
    const motion = vi.spyOn(AvalonBackdropController.prototype, 'setReducedMotion')
    setup(mode)
    await screen.findByRole('heading', { name: 'Prey', level: 1 })
    // Desktop art is static in either setting; fullscreen follows the saved motion preference.
    await waitFor(() => expect(motion).toHaveBeenLastCalledWith(mode === 'desktop' || reduced))
    expect(document.querySelector('.avalon-detail-backdrop')).toBeTruthy()
  })
})
