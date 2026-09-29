// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import type { Metadata } from '../src/renderer/api/types'
import { MetadataEditor } from '../src/renderer/features/parity-details'
import { MetadataDialog } from '../src/renderer/features/metadata-dialog'
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
function mount(
  options: {
    source?: string | null
    outcome?: string
    pendingRead?: Promise<unknown>
    pendingWrite?: Promise<void>
    fullscreen?: boolean
    dialog?: boolean
  } = {},
) {
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
    await options.pendingWrite
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
  const close = vi.fn(),
    editText = vi.fn()
  const view = render(
    <QueryClientProvider client={client}>
      {options.fullscreen || options.dialog ? (
        <MetadataDialog
          workId={42}
          title="Original title"
          mode={options.fullscreen ? 'fullscreen' : 'desktop'}
          onClose={close}
          editText={editText}
        />
      ) : (
        <MetadataEditor workId={42} />
      )}
    </QueryClientProvider>,
  )
  return { request, invalidate, client, close, editText, ...view }
}
function row(label: string) {
  return screen.getByLabelText(label).closest('form')!
}
async function loaded() {
  await screen.findByLabelText('Name')
}

describe('fullscreen metadata field navigation', () => {
  it.each([false, true])(
    'disables Back throughout a pending save and restores it after publication, fullscreen=%s',
    async (fullscreen) => {
      let resolveWrite!: () => void
      const pendingWrite = new Promise<void>((resolve) => {
        resolveWrite = resolve
      })
      const { close } = mount({ dialog: true, fullscreen, pendingWrite })
      if (fullscreen) fireEvent.click(await screen.findByRole('button', { name: 'Name · IGDB' }))
      else await loaded()
      fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Saved name' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
      const back = screen.getByRole('button', { name: 'Back' }) as HTMLButtonElement
      await waitFor(() => expect(back.disabled).toBe(true))
      fireEvent.click(back)
      fireEvent.keyDown(screen.getByLabelText('Name'), { key: 'Escape' })
      expect(close).not.toHaveBeenCalled()
      await act(async () => resolveWrite())
      await waitFor(() => expect(back.disabled).toBe(false))
      expect(screen.getByRole('status').textContent).toBe('Saved.')
      fireEvent.click(back)
      expect(close).toHaveBeenCalledTimes(1)
    },
  )
  it('orders attributed menu fields and restores the original draft on Back after a validation refusal', async () => {
    const { request, close } = mount({ fullscreen: true })
    const year = await screen.findByRole('button', { name: 'Release year · IGDB' })
    expect(
      [...document.querySelectorAll('.metadata-field-menu > button')].map((node) => node.textContent),
    ).toEqual([
      'Name · IGDB',
      'Release year · IGDB',
      'About · IGDB',
      'Cover art · IGDB',
      'Publisher · IGDB',
      'Background art · IGDB',
    ])
    fireEvent.click(year)
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit value' })),
    )
    fireEvent.change(screen.getByLabelText('Release year'), { target: { value: '2201' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save release year' }))
    expect((await screen.findByRole('alert')).textContent).toContain('1900 and 2200')
    fireEvent.keyDown(screen.getByLabelText('Release year'), { key: 'Escape' })
    await waitFor(() => expect(document.activeElement).toBe(year))
    expect(close).not.toHaveBeenCalled()
    fireEvent.click(year)
    expect((screen.getByLabelText('Release year') as HTMLInputElement).value).toBe('2020')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
  })
  it('opens the shared keyboard for the active value and cancels without saving', async () => {
    const { request, editText } = mount({ fullscreen: true })
    const origin = await screen.findByRole('button', { name: 'About · IGDB' })
    fireEvent.click(origin)
    fireEvent.click(screen.getByRole('button', { name: 'Edit value' }))
    expect(editText).toHaveBeenCalledWith(screen.getByLabelText('About'))
    fireEvent.change(screen.getByLabelText('About'), { target: { value: 'Temporary' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(document.activeElement).toBe(origin))
    fireEvent.click(origin)
    expect((screen.getByLabelText('About') as HTMLInputElement).value).toBe('')
    expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
  })
  it('returns a committed field to its updated source row with confirmation and focus', async () => {
    const { request } = mount({ fullscreen: true })
    fireEvent.click(await screen.findByRole('button', { name: 'Name · IGDB' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'New title' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    const origin = await screen.findByRole('button', { name: 'Name · YOU' })
    await waitFor(() => expect(document.activeElement).toBe(origin))
    expect(screen.getByRole('status').textContent).toBe('Saved.')
    fireEvent.click(origin)
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('New title')
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.put')).toHaveLength(1)
  })
  it('blocks navigation and duplicate writes until the active save and refresh finish', async () => {
    let finish!: () => void
    const { request, close } = mount({
      fullscreen: true,
      pendingWrite: new Promise<void>((resolve) => {
        finish = resolve
      }),
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Name · IGDB' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    fireEvent.keyDown(screen.getByLabelText('Name'), { key: 'Escape' })
    expect(close).not.toHaveBeenCalled()
    expect(screen.getByRole('textbox', { name: 'Name' })).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true)
    await act(async () => finish())
    await screen.findByRole('button', { name: 'Name · YOU' })
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.put')).toHaveLength(1)
  })
  it.each(['Applied', 'WorkNotFound'])(
    'confirms reset and retains the field when the result is %s',
    async (outcome) => {
      const { request } = mount({ fullscreen: true, source: 'user', outcome })
      fireEvent.click(await screen.findByRole('button', { name: 'Publisher · YOU' }))
      fireEvent.click(screen.getByRole('button', { name: 'Use automatic publisher' }))
      let confirmation = screen.getByRole('dialog', { name: 'Reset Publisher?' })
      expect(document.activeElement).toBe(within(confirmation).getByRole('button', { name: 'Cancel' }))
      fireEvent.click(within(confirmation).getByRole('button', { name: 'Cancel' }))
      expect(request.mock.calls.every(([input]) => input.route === 'metadata.get')).toBe(true)
      fireEvent.click(screen.getByRole('button', { name: 'Use automatic publisher' }))
      confirmation = screen.getByRole('dialog', { name: 'Reset Publisher?' })
      fireEvent.click(within(confirmation).getByRole('button', { name: 'Use automatic publisher' }))
      if (outcome === 'Applied') {
        const origin = await screen.findByRole('button', { name: 'Publisher · AUTO' })
        await waitFor(() => expect(document.activeElement).toBe(origin))
        expect(screen.getByRole('status').textContent).toBe('Publisher returned to automatic.')
      } else {
        expect((await screen.findByRole('alert')).textContent).toContain('no longer in your library')
        expect(screen.getByRole('button', { name: 'Use automatic publisher' })).toBeTruthy()
      }
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.reset')).toHaveLength(1)
    },
  )
})

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
