// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import type { Metadata } from '../src/renderer/api/types'
import { MetadataEditor } from '../src/renderer/features/parity-details'
import { clearViewState } from '../src/renderer/viewState'

vi.mock('../src/renderer/components/Artwork', () => ({
  Artwork: ({ hero }: { hero?: boolean }) => (
    <span data-testid={hero ? 'background-preview' : 'cover-preview'} />
  ),
}))
const fields = ['name', 'first_release_year', 'summary', 'cover_url', 'publisher', 'background_url']
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('draft:metadata-fields:42')
  clearViewState('metadata-fields:42:sending')
})
function mount(options: { source?: string | null; outcome?: string; pendingRead?: Promise<unknown> } = {}) {
  let saved: Metadata = {
    workId: 42,
    title: 'Original title',
    isPinned: false,
    revision: 'v1',
    fields: fields.map((field) => ({
      field,
      value: field === 'name' ? 'Original title' : field === 'first_release_year' ? '2020' : null,
      source: options.source === undefined ? 'igdb' : options.source,
    })),
  }
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'metadata.get')
      return options.pendingRead ?? { ok: true, status: 200, data: structuredClone(saved) }
    const body = input.body as { field: string; value?: string | null }
    if (options.outcome && options.outcome !== 'Applied')
      return { ok: true, status: 200, data: { outcome: options.outcome } }
    const reset = input.route === 'metadata.reset'
    saved = {
      ...saved,
      revision: `v${Number(saved.revision.slice(1)) + 1}`,
      fields: saved.fields.map((field) =>
        field.field === body.field
          ? {
              ...field,
              value: reset ? field.value : (body.value?.trim() ?? 'user:art'),
              source: reset ? null : 'user',
            }
          : field,
      ),
    }
    return { ok: true, status: 200, data: { outcome: 'Applied' } }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  })
  clients.push(client)
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const view = render(
    <QueryClientProvider client={client}>
      <MetadataEditor workId={42} />
    </QueryClientProvider>,
  )
  return { request, invalidate, client, ...view }
}
function row(label: string) {
  return screen.getByLabelText(label).closest('form')!
}
async function loaded() {
  await screen.findByLabelText('Name')
}

describe('original per-field metadata contracts', () => {
  it('loads all six fields with attributed sources and draws only two art pickers', async () => {
    mount()
    await loaded()
    expect(screen.getAllByText('IGDB', { exact: true })).toHaveLength(6)
    expect(screen.queryByRole('button', { name: /Use automatic/ })).toBeNull()
    expect(screen.getAllByTestId('cover-preview')).toHaveLength(1)
    expect(screen.getAllByTestId('background-preview')).toHaveLength(1)
    expect(document.querySelectorAll('input[type="file"]')).toHaveLength(2)
  })
  it('describes an unclaimed field as automatic with a nonempty explanation', async () => {
    mount({ source: null })
    await loaded()
    for (const badge of screen.getAllByText('AUTO', { exact: true })) expect(badge.title.trim()).not.toBe('')
    expect(screen.queryByRole('button', { name: /Use automatic/ })).toBeNull()
  })
  it('saves only one field, adopts its stored value and preserves other sources and drafts', async () => {
    const { request, invalidate } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Publisher'), { target: { value: '  Eleon  ' } })
    fireEvent.change(screen.getByLabelText('About'), { target: { value: 'half-typed' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save publisher' }))
    await screen.findByText('Saved.')
    await waitFor(() => expect((screen.getByLabelText('Publisher') as HTMLInputElement).value).toBe('Eleon'))
    expect((screen.getByLabelText('About') as HTMLTextAreaElement).value).toBe('half-typed')
    expect(within(row('Publisher')).getByText('YOU')).toBeTruthy()
    expect(screen.getAllByText('IGDB', { exact: true })).toHaveLength(5)
    expect(
      request.mock.calls.filter(([input]) => input.route === 'metadata.put').map(([input]) => input.body),
    ).toEqual([{ field: 'publisher', value: '  Eleon  ', expectedRevision: 'v1' }])
    expect(invalidate).toHaveBeenCalledTimes(1)
  })
  it('returns only the chosen user field to automatic and removes its reset control', async () => {
    const { request } = mount({ source: 'user' })
    await loaded()
    fireEvent.click(screen.getByRole('button', { name: 'Use automatic publisher' }))
    await screen.findByText('Publisher returned to automatic.')
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Use automatic publisher' })).toBeNull())
    expect(within(row('Publisher')).getByText('AUTO')).toBeTruthy()
    expect(within(row('Name')).getByText('YOU')).toBeTruthy()
    expect(request.mock.calls.find(([input]) => input.route === 'metadata.reset')![0].body).toEqual({
      field: 'publisher',
      expectedRevision: 'v1',
    })
  })
  it('keeps a refused reset user-owned and announces the refusal without a library refresh', async () => {
    const { invalidate } = mount({ source: 'user', outcome: 'WorkNotFound' })
    await loaded()
    fireEvent.click(screen.getByRole('button', { name: 'Use automatic publisher' }))
    expect((await screen.findByRole('alert')).textContent).toContain('no longer in your library')
    expect(within(row('Publisher')).getByText('YOU')).toBeTruthy()
    expect(invalidate).not.toHaveBeenCalled()
  })
  it.each(['1899', '2201', '2.5'])(
    'refuses invalid release year %s before writing and keeps its draft',
    async (value) => {
      const { request } = mount()
      await loaded()
      fireEvent.change(screen.getByLabelText('Release year'), { target: { value } })
      fireEvent.click(screen.getByRole('button', { name: 'Save release year' }))
      expect((await screen.findByRole('alert')).textContent).toContain('between 1900 and 2200')
      expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
      expect((screen.getByLabelText('Release year') as HTMLInputElement).value).toBe(value)
    },
  )
  it('saves a four-digit release year as a user field', async () => {
    const { request } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Release year'), { target: { value: '2017' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save release year' }))
    await screen.findByText('Saved.')
    await waitFor(() => expect(within(row('Release year')).getByText('YOU')).toBeTruthy())
    expect(request.mock.calls.find(([input]) => input.route === 'metadata.put')![0].body).toEqual({
      field: 'first_release_year',
      value: '2017',
      expectedRevision: 'v1',
    })
  })
  it('does not save or download an empty art URL', async () => {
    const { request } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Cover art'), { target: { value: '   ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save cover art' }))
    expect((await screen.findByRole('alert')).textContent).toContain('valid image URL')
    expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
  })
  it('downloads an art URL through the backend and refreshes artwork only after it is applied', async () => {
    const { request, invalidate } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Background art'), {
      target: { value: 'https://example.test/wide.png' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save background art' }))
    await screen.findByText('Saved.')
    await waitFor(() => expect(invalidate).toHaveBeenCalledTimes(2))
    expect(request.mock.calls.find(([input]) => input.route === 'metadata.art-download')![0].body).toEqual({
      field: 'background_url',
      url: 'https://example.test/wide.png',
      expectedRevision: 'v1',
    })
    expect(within(row('Background art')).getByText('YOU')).toBeTruthy()
  })
  it('writes nothing when a file picker is dismissed', async () => {
    const { request } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Choose cover art file'), { target: { files: [] } })
    expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
  })
  it('uploads the chosen local image content and adopts its user source', async () => {
    const { request } = mount()
    await loaded()
    fireEvent.change(screen.getByLabelText('Choose cover art file'), {
      target: { files: [new File(['image bytes'], 'cover.png', { type: 'image/png' })] },
    })
    await screen.findByText('Saved.')
    await waitFor(() => expect(within(row('Cover art')).getByText('YOU')).toBeTruthy())
    expect(request.mock.calls.find(([input]) => input.route === 'metadata.art-upload')![0].body).toEqual({
      field: 'cover_url',
      content: btoa('image bytes'),
      expectedRevision: 'v1',
    })
  })
  it('does not refresh library state or adopt a text draft after a refused write', async () => {
    const { request, invalidate } = mount({ outcome: 'WorkNotFound' })
    await loaded()
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Refused rename' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    await screen.findByRole('alert')
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Refused rename')
    expect(within(row('Name')).getByText('IGDB')).toBeTruthy()
    expect(invalidate).not.toHaveBeenCalled()
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.get')).toHaveLength(1)
  })
  it.each([
    ['UnknownField', 'This field is no longer editable.'],
    ['WorkNotFound', 'This game is no longer in your library.'],
    ['FileNotFound', 'The image file could not be found.'],
    ['Unreadable', 'The image could not be read.'],
    ['TooLarge', 'Choose an image no larger than 16 MiB.'],
    ['NotAnImage', 'Choose a supported image file.'],
    ['BadUrl', 'Enter a valid image URL.'],
    ['DownloadFailed', 'The image could not be downloaded.'],
    ['Failed', 'The change could not be saved.'],
    ['Conflict', 'The metadata changed elsewhere. Refresh before saving again.'],
  ])(
    'announces art refusal %s distinctly, retains the draft and does not report a save',
    async (outcome, message) => {
      const { request, invalidate } = mount({ outcome })
      await loaded()
      fireEvent.change(screen.getByLabelText('Cover art'), {
        target: { value: 'https://example.test/cover.png' },
      })
      fireEvent.click(screen.getByRole('button', { name: 'Save cover art' }))
      expect((await screen.findByRole('alert')).textContent).toBe(message)
      expect((screen.getByLabelText('Cover art') as HTMLInputElement).value).toBe(
        'https://example.test/cover.png',
      )
      expect(screen.queryByText('YOU', { exact: true })).toBeNull()
      expect(invalidate).not.toHaveBeenCalled()
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.get')).toHaveLength(1)
    },
  )
  it('shows words while reading fields, then clears the loading state', async () => {
    let finish!: (value: unknown) => void
    mount({
      pendingRead: new Promise((done) => {
        finish = done
      }),
    })
    expect(screen.getByRole('status').textContent).toBe('Loading fields…')
    expect(screen.queryByLabelText('Name')).toBeNull()
    await act(async () =>
      finish({
        ok: true,
        status: 200,
        data: {
          workId: 42,
          title: 'Read',
          isPinned: false,
          revision: 'v1',
          fields: [{ field: 'name', value: 'Read', source: 'igdb' }],
        },
      }),
    )
    await loaded()
    expect(screen.queryByRole('status')).toBeNull()
  })
  it('draws no field rows when the work cannot be read', async () => {
    mount({
      pendingRead: Promise.resolve({ ok: false, status: 404, message: "Couldn't load this game's fields." }),
    })
    expect((await screen.findByRole('alert')).textContent).toContain("Couldn't load this game's fields.")
    expect(screen.queryByLabelText('Name')).toBeNull()
  })
})
