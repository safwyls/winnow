// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ManualEditor } from '../src/renderer/features/ManualEditor'
import { ManualGames } from '../src/renderer/features/ManualGames'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { ManualGame } from '../src/renderer/api/types'

const original: ManualGame = {
  ownershipId: 77,
  releaseId: 70,
  workId: 7,
  title: 'Original',
  revision: 'revision-1',
  igdbMappingRevision: 1,
  igdbId: 333,
  steamAppId: '123',
}
afterEach(() => {
  cleanup()
  for (const key of [
    'draft:manual:new',
    'draft:manual:77',
    'desktop:manual:editing',
    'fullscreen:manual:editing',
  ])
    clearViewState(key)
})
function fixture(
  content: React.ReactNode,
  respond: (input: ApiRequest) => unknown = () => ({ ok: true, status: 200, data: [] }),
) {
  const request = vi.fn(async (input: ApiRequest) => respond(input))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(<QueryClientProvider client={client}>{content}</QueryClientProvider>)
  return { request, client }
}
const change = (name: string, value: string) =>
  fireEvent.change(screen.getByLabelText(name), { target: { value } })
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
describe.each(['desktop', 'fullscreen'] as const)('%s manual form source contracts', (mode) => {
  it('focuses the opened form and returns to its exact add or edit control after cancel', async () => {
    fixture(<div className={`mode-${mode}`}><ManualGames mode={mode} /></div>, (input) => ({ ok: true, status: 200, data: input.route === 'manual.detail' ? original : [original] }))
    const add = screen.getByRole('button', { name: 'Add a game' })
    fireEvent.click(add)
    expect(document.activeElement).toBe(screen.getByLabelText('Title'))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(document.activeElement).toBe(add)
    const edit = await screen.findByRole('button', { name: 'Edit game' })
    fireEvent.click(edit)
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Title')))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(document.activeElement).toBe(edit)
  })
  it.each([
    ['Title', ' ', 'A title is needed.'],
    ['Release year', '1899', 'A year from 1900 to 2200.'],
    ['Release year', '2201', 'A year from 1900 to 2200.'],
    ['Release year', 'not-a-year', 'A year from 1900 to 2200.'],
    ['IGDB ID', 'not-a-number', 'A positive numeric ID.'],
    ['IGDB ID', '0', 'A positive numeric ID.'],
    ['IGDB ID', '-1', 'A positive numeric ID.'],
    ['Steam app ID', '0', 'A positive numeric ID, up to 4294967295.'],
    ['Steam app ID', '4294967296', 'A positive numeric ID, up to 4294967295.'],
  ])('reports invalid %s=%s beside that field without writing', async (field, value, expected) => {
    const close = vi.fn()
    const { request } = fixture(
      <div className={`mode-${mode}`}>
        <ManualEditor initial={null} onClose={close} />
      </div>,
    )
    change('Title', 'Draft game')
    change(field, value)
    save()
    expect((await screen.findByRole('alert')).textContent).toContain(expected)
    expect(screen.getByLabelText(field).getAttribute('aria-invalid')).toBe('true')
    expect(
      document.getElementById(screen.getByLabelText(field).getAttribute('aria-describedby')!)?.textContent,
    ).toBe(expected)
    expect(request).not.toHaveBeenCalled()
    expect(close).not.toHaveBeenCalled()
    expect((screen.getByLabelText(field) as HTMLInputElement).value).toBe(value)
    for (const label of ['Title', 'Release year', 'IGDB ID', 'Steam app ID'].filter(
      (label) => label !== field,
    ))
      expect(screen.getByLabelText(label).getAttribute('aria-invalid')).toBe('false')
  })
  it.each(['SteamAppId', 'IgdbId'])(
    'places an already claimed %s conflict only on the identifier and allows correction',
    async (field) => {
      const close = vi.fn()
      let refused = true
      const { request } = fixture(
        <div className={`mode-${mode}`}>
          <ManualEditor initial={null} onClose={close} />
        </div>,
        () =>
          refused
            ? {
                ok: false,
                status: 409,
                message: 'Server detail',
                data: { field, reason: 'ClaimedByAnotherGame' },
              }
            : { ok: true, status: 200, data: original },
      )
      change('Title', 'Draft game')
      change('IGDB ID', '666')
      change('Steam app ID', '456')
      save()
      expect((await screen.findByRole('alert')).textContent).toContain(
        'Another game in your library already has this.',
      )
      expect(screen.queryByText('Server detail')).toBeNull()
      expect(close).not.toHaveBeenCalled()
      const label = field === 'SteamAppId' ? 'Steam app ID' : 'IGDB ID'
      expect(screen.getByLabelText(label).getAttribute('aria-invalid')).toBe('true')
      refused = false
      change(label, '789')
      save()
      await waitFor(() => expect(close).toHaveBeenCalledOnce())
      expect(request).toHaveBeenCalledTimes(2)
    },
  )
  it.each(['LegacyIdentifierHistory', 'StorefrontObservation'])(
    'retains corrected draft and explains %s on Steam field',
    async (reason) => {
      const close = vi.fn()
      fixture(
        <div className={`mode-${mode}`}>
          <ManualEditor initial={original} onClose={close} />
        </div>,
        () => ({ ok: false, status: 409, data: { field: 'SteamAppId', reason } }),
      )
      change('Title', 'Corrected title')
      change('Release year', '2020')
      change('IGDB ID', '666')
      change('Steam app ID', '456')
      save()
      expect((await screen.findByRole('alert')).textContent).toContain('Keep it to edit details')
      expect(screen.getByLabelText('Steam app ID').getAttribute('aria-invalid')).toBe('true')
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Corrected title')
      expect((screen.getByLabelText('Steam app ID') as HTMLInputElement).value).toBe('456')
      expect(close).not.toHaveBeenCalled()
    },
  )
  it('requires cancel and reopen when a concurrent IGDB mapping changed', async () => {
    const close = vi.fn()
    fixture(
      <div className={`mode-${mode}`}>
        <ManualEditor initial={original} onClose={close} />
      </div>,
      (input) =>
        input.route === 'manual.get'
          ? {
              ok: true,
              status: 200,
              data: [{ ...original, igdbId: 555, igdbMappingRevision: 2, revision: 'revision-2' }],
            }
          : { ok: false, status: 409 },
    )
    change('Title', 'Corrected title')
    change('IGDB ID', '666')
    change('Steam app ID', '456')
    save()
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Cancel and reopen'))
    expect(screen.getByLabelText('IGDB ID').getAttribute('aria-invalid')).toBe('true')
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Corrected title')
    expect((screen.getByLabelText('Steam app ID') as HTMLInputElement).value).toBe('456')
    expect(screen.queryByRole('button', { name: 'Keep my draft for the next save' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Save game' }) as HTMLButtonElement).disabled).toBe(true)
    expect(close).not.toHaveBeenCalled()
  })
  it('names the game in a cancel-first removal prompt and only removes after confirmation', async () => {
    let games = [original]
    const { request } = fixture(
      <div className={`mode-${mode}`}>
        <ManualGames mode={mode} />
      </div>,
      (input) => {
        if (input.route === 'manual.delete') games = []
        return { ok: true, status: 200, data: games }
      },
    )
    const remove = await screen.findByRole('button', { name: 'Remove…' })
    remove.focus()
    fireEvent.click(remove)
    const dialog = screen.getByRole('dialog', { name: 'Remove “Original”?' })
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Keep game' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Keep game' }))
    expect(request.mock.calls.some(([input]) => input.route === 'manual.delete')).toBe(false)
    expect(screen.getByRole('button', { name: 'Original' })).toBeTruthy()
    await waitFor(() => expect(document.activeElement).toBe(remove))
    fireEvent.click(remove)
    fireEvent.click(screen.getByRole('button', { name: 'Remove entry' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Original' })).toBeNull())
    expect(request.mock.calls.filter(([input]) => input.route === 'manual.delete')).toEqual([
      [{ route: 'manual.delete', params: { ownershipId: 77 }, body: undefined }],
    ])
  })
})
