// @vitest-environment jsdom
import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LibraryTools } from '../src/renderer/features/LibraryTools'
import { clearViewState } from '../src/renderer/viewState'
import { deriveExecutableFacts } from '../src/main/executable-facts'
import type { ApiRequest } from '../src/shared/bridge'
import type { ExecutableFacts } from '../src/shared/executable-facts'
import type { Mode } from '../src/renderer/api/types'

const candidate = (igdbId: number, name: string, firstReleaseYear: number) => ({
  igdbId,
  name,
  firstReleaseYear,
  platforms: [] as string[],
})
type Candidate = ReturnType<typeof candidate>
const facts = (title: string) => deriveExecutableFacts(`D:\\Games\\${title}\\${title}.exe`, title)
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}
const clients: QueryClient[] = []
function setup(mode: Mode, picked: ExecutableFacts | null, candidates: Candidate[] = [], strict = false) {
  const search = vi.fn<(title: string) => Promise<Candidate[]>>().mockResolvedValue(candidates)
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [], lists: [] }
        : input.route === 'metadata.search'
          ? await search(String(input.params?.title))
          : input.route === 'manual.get'
            ? []
            : {},
  }))
  const picker = vi.fn<() => Promise<ExecutableFacts | null>>().mockResolvedValue(picked)
  const cancelRequest = vi.fn(async (_requestId: string) => {})
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, cancelRequest, chooseManualExecutableFacts: picker },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const mount = () => {
    const content = (
      <QueryClientProvider client={client}>
        <LibraryTools mode={mode} />
      </QueryClientProvider>
    )
    return render(strict ? <StrictMode>{content}</StrictMode> : content)
  }
  const view = mount()
  fireEvent.click(screen.getByRole('button', { name: 'Manual games' }))
  const writes = () =>
    request.mock.calls
      .map(([input]) => input)
      .filter(
        (input) =>
          input.route.startsWith('manual.') && !['manual.get', 'manual.detail'].includes(input.route),
      )
  return { request, picker, search, cancelRequest, view, mount, writes }
}
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('draft:manual:new')
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:manual:editing`)
    clearViewState(`${mode}:library-tools:tab`)
  }
})
const value = (name: string) => (screen.getByLabelText(name) as HTMLInputElement).value
const change = (name: string, value: string) =>
  fireEvent.change(screen.getByLabelText(name), { target: { value } })
const click = (name: string) => fireEvent.click(screen.getByRole('button', { name }))
const disabled = (name: string) => (screen.getByRole('button', { name }) as HTMLButtonElement).disabled
async function browse() {
  click('Choose executable')
  await waitFor(() => expect(disabled('Choose executable')).toBe(false))
}
async function saved(view: ReturnType<typeof setup>) {
  await waitFor(() => expect(view.writes()).toHaveLength(1))
  expect(view.writes()[0].route).toBe('manual.create')
  return view.writes()[0].body
}

// Frozen source cf45d9f1127243a987d3cf6e664a32fc767ecb67 uses real derivation over
// a fake inspector's description. Bridge assertions here complement real database evidence.
describe.each(['desktop', 'fullscreen'] as const)('%s manual executable source contracts', (mode) => {
  it('Browsing_fills_the_executable_proposes_the_title_and_searches', async () => {
    const view = setup(mode, facts('Celeste'), [candidate(1, 'Celeste', 2018)])
    click('Add from executable…')
    await screen.findByRole('button', { name: 'Use these details' })
    expect(screen.getByRole('heading', { name: 'Add a game' })).toBeTruthy()
    expect(view.picker).toHaveBeenCalledTimes(1)
    expect(value('Executable path')).toBe('D:\\Games\\Celeste\\Celeste.exe')
    expect(value('Installation folder')).toBe('D:\\Games\\Celeste')
    expect(value('Title')).toBe('Celeste')
    expect(screen.getByText('The file identifies itself as Celeste.')).toBeTruthy()
    expect(view.search.mock.calls).toEqual([['Celeste']])
    expect(screen.getAllByRole('button', { name: 'Use these details' })).toHaveLength(1)
    expect(screen.getByText('2018')).toBeTruthy()
    expect(view.writes()).toEqual([])
  })
  it('A_cancelled_dialog_changes_nothing', async () => {
    const view = setup(mode, null)
    click('Add a game')
    await browse()
    expect(value('Title')).toBe('')
    expect(value('Executable path')).toBe('')
    expect(value('Installation folder')).toBe('')
    expect(view.search).not.toHaveBeenCalled()
    expect(screen.queryByText(/The file identifies|Guessed|No title found/)).toBeNull()
    expect(view.writes()).toEqual([])
  })
  it('Choosing_a_candidate_fills_the_form_and_writes_nothing', async () => {
    const view = setup(mode, deriveExecutableFacts('D:\\Games\\CP\\bin\\game.exe', null), [
      candidate(1877, 'Cyberpunk 2077', 2020),
    ])
    click('Add from executable…')
    await screen.findByRole('button', { name: 'Use these details' })
    expect(value('Title')).toBe('CP')
    change('Title', 'Cyberpunk')
    click('Find IGDB matches')
    fireEvent.click(await screen.findByRole('button', { name: 'Use these details' }))
    expect(value('Title')).toBe('Cyberpunk 2077')
    expect(value('Release year')).toBe('2020')
    expect(value('IGDB ID')).toBe('1877')
    expect(
      screen.getByText('Using details from Cyberpunk 2077. Nothing is saved until you choose Save game.'),
    ).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Use these details' })).toBeNull()
    expect(view.writes()).toEqual([])
    click('Save game')
    expect(await saved(view)).toMatchObject({
      title: 'Cyberpunk 2077',
      firstReleaseYear: 2020,
      igdbId: 1877,
      executablePath: 'D:\\Games\\CP\\bin\\game.exe',
      installPath: 'D:\\Games\\CP\\bin',
    })
  })
  it('Correcting_the_title_and_searching_again_overrides_the_proposal', async () => {
    const view = setup(mode, deriveExecutableFacts('D:\\Games\\Prey\\prey.exe', 'Prey'), [
      candidate(1, 'Prey', 2006),
    ])
    click('Add from executable…')
    await screen.findByRole('button', { name: 'Use these details' })
    expect(view.search.mock.calls).toEqual([['Prey']])
    change('Title', 'Prey 2017')
    click('Find IGDB matches')
    await waitFor(() => expect(view.search.mock.calls).toEqual([['Prey'], ['Prey 2017']]))
    expect(value('Title')).toBe('Prey 2017')
    expect(view.writes()).toEqual([])
  })
  it('Dismissing_the_proposal_leaves_the_form_as_typed', async () => {
    const view = setup(mode, deriveExecutableFacts('D:\\Games\\Iconoclasts\\game.exe', null), [
      candidate(9, 'Something Else', 1999),
    ])
    click('Add from executable…')
    await screen.findByRole('button', { name: 'Use these details' })
    change('Title', 'Iconoclasts')
    click('Find IGDB matches')
    await screen.findByRole('button', { name: 'Keep my own details' })
    click('Keep my own details')
    expect(value('Title')).toBe('Iconoclasts')
    expect(value('Executable path')).toBe('D:\\Games\\Iconoclasts\\game.exe')
    expect(value('IGDB ID')).toBe('')
    expect(screen.queryByRole('button', { name: 'Use these details' })).toBeNull()
    expect(screen.queryByText('No matching games. You can still fill the form by hand.')).toBeNull()
    expect(view.writes()).toEqual([])
    click('Save game')
    expect(await saved(view)).toMatchObject({ title: 'Iconoclasts', igdbId: null })
  })
  it('An_executable_that_yields_nothing_still_saves_by_hand', async () => {
    const view = setup(mode, deriveExecutableFacts('C:\\Games\\game.exe'))
    click('Add from executable…')
    await screen.findByText('No title found in the file. Type one above.')
    expect(value('Title')).toBe('')
    expect(value('Executable path')).toBe('C:\\Games\\game.exe')
    expect(disabled('Find IGDB matches')).toBe(true)
    expect(view.search).not.toHaveBeenCalled()
    change('Title', 'A Disc I Own')
    change('Platform', 'PlayStation 2')
    click('Save game')
    expect(await saved(view)).toMatchObject({
      title: 'A Disc I Own',
      platformLabel: 'PlayStation 2',
      executablePath: 'C:\\Games\\game.exe',
      installPath: 'C:\\Games',
    })
    expect(view.search).not.toHaveBeenCalled()
  })
  it('The_chosen_executable_is_the_one_stored', async () => {
    const view = setup(mode, facts('Tunic'))
    click('Add from executable…')
    await screen.findByText('The file identifies itself as Tunic.')
    click('Save game')
    expect(await saved(view)).toMatchObject({
      title: 'Tunic',
      executablePath: 'D:\\Games\\Tunic\\Tunic.exe',
      installPath: 'D:\\Games\\Tunic',
    })
  })
  it('A_second_browse_replaces_its_own_guess_but_not_the_users', async () => {
    const view = setup(mode, facts('Celeste'))
    click('Add from executable…')
    await screen.findByText('The file identifies itself as Celeste.')
    view.picker.mockResolvedValue(facts('Tunic'))
    await browse()
    expect(value('Title')).toBe('Tunic')
    change('Title', 'My own title')
    view.picker.mockResolvedValue(facts('Braid'))
    await browse()
    expect(value('Title')).toBe('My own title')
    expect(value('Executable path')).toBe('D:\\Games\\Braid\\Braid.exe')
    expect(value('Installation folder')).toBe('D:\\Games\\Braid')
    expect(view.search.mock.calls).toEqual([['Celeste'], ['Tunic'], ['My own title']])
    expect(view.writes()).toEqual([])
  })
})

describe.each(['desktop', 'fullscreen'] as const)('%s executable draft lifetime', (mode) => {
  it('explains a publisher from version info and a replacement title guessed from its folder', async () => {
    const view = setup(
      mode,
      deriveExecutableFacts('D:\\Games\\Celeste\\Celeste.exe', 'Celeste', null, 'Extremely OK Games'),
    )
    click('Add from executable…')
    await screen.findByText('The file identifies itself as Celeste. Published by Extremely OK Games.')
    view.picker.mockResolvedValue(deriveExecutableFacts('D:\\Games\\Tunic\\game.exe'))
    await browse()
    expect(value('Title')).toBe('Tunic')
    expect(screen.getByText('Guessed Tunic from the path.')).toBeTruthy()
    expect(view.writes()).toEqual([])
  })

  it('preserves a title typed while inspection is pending without saving from the picker alone', async () => {
    const view = setup(mode, null),
      pending = deferred<ExecutableFacts | null>()
    view.picker.mockReturnValueOnce(pending.promise)
    click('Add from executable…')
    change('Title', 'My choice')
    await act(async () => pending.resolve(facts('Tunic')))
    expect(value('Title')).toBe('My choice')
    expect(value('Executable path')).toBe('D:\\Games\\Tunic\\Tunic.exe')
    expect(view.search.mock.calls).toEqual([['My choice']])
    expect(view.writes()).toEqual([])
  })
  it('does not reclaim a user title when a later executable happens to have the same name', async () => {
    const view = setup(mode, facts('Celeste'))
    click('Add from executable…')
    await screen.findByText('The file identifies itself as Celeste.')
    change('Title', 'Tunic')
    view.picker.mockResolvedValue(facts('Tunic'))
    await browse()
    view.picker.mockResolvedValue(facts('Braid'))
    await browse()
    expect(value('Title')).toBe('Tunic')
    expect(value('Executable path')).toBe('D:\\Games\\Braid\\Braid.exe')
    expect(view.search.mock.calls).toEqual([['Celeste'], ['Tunic'], ['Tunic']])
  })
  it.each(['success', 'failure'] as const)(
    'ignores an old picker %s after cancel and reopen',
    async (outcome) => {
      const view = setup(mode, null),
        pending = deferred<ExecutableFacts | null>()
      view.picker.mockReturnValueOnce(pending.promise)
      click('Add from executable…')
      change('Title', 'Canceled draft')
      click('Cancel')
      click('Add a game')
      change('Title', 'New draft')
      change('Executable path', 'D:\\Games\\New\\new.exe')
      await act(async () =>
        outcome === 'success' ? pending.resolve(facts('Old')) : pending.reject(Error('Old picker failed')),
      )
      expect(value('Title')).toBe('New draft')
      expect(value('Executable path')).toBe('D:\\Games\\New\\new.exe')
      expect(value('Installation folder')).toBe('')
      expect(screen.queryByText(/The file identifies|could not be selected/)).toBeNull()
      expect(view.search).not.toHaveBeenCalled()
      expect(view.writes()).toEqual([])
    },
  )
  it('preserves navigation drafts but drops an inspection that resolves after unmount', async () => {
    const view = setup(mode, null),
      pending = deferred<ExecutableFacts | null>()
    view.picker.mockReturnValueOnce(pending.promise)
    click('Add from executable…')
    change('Title', 'Unfinished draft')
    change('Platform', 'Handheld')
    view.view.unmount()
    await act(async () => pending.resolve(facts('Old')))
    view.mount()
    expect(value('Title')).toBe('Unfinished draft')
    expect(value('Platform')).toBe('Handheld')
    expect(value('Executable path')).toBe('')
    expect(screen.queryByText('The file identifies itself as Old.')).toBeNull()
    expect(view.search).not.toHaveBeenCalled()
    expect(view.writes()).toEqual([])
  })
  it('blocks both button and submit writes until inspection completes', async () => {
    const view = setup(mode, null),
      pending = deferred<ExecutableFacts | null>()
    view.picker.mockReturnValueOnce(pending.promise)
    click('Add from executable…')
    change('Title', 'My game')
    expect(disabled('Save game')).toBe(true)
    click('Save game')
    fireEvent.submit(screen.getByLabelText('Title').closest('form')!)
    await act(async () => {})
    expect(view.writes()).toEqual([])
    await act(async () => pending.resolve(facts('Tunic')))
    expect(disabled('Save game')).toBe(false)
    click('Save game')
    expect(await saved(view)).toMatchObject({
      title: 'My game',
      executablePath: 'D:\\Games\\Tunic\\Tunic.exe',
      installPath: 'D:\\Games\\Tunic',
    })
  })
  it.each(['success', 'failure'] as const)(
    'ignores an old search %s after correcting and searching the title again',
    async (outcome) => {
      const view = setup(mode, null),
        old = deferred<Candidate[]>()
      view.search.mockReturnValueOnce(old.promise).mockResolvedValueOnce([candidate(99, 'Prey 2017', 2017)])
      click('Add a game')
      change('Title', 'Prey')
      click('Find IGDB matches')
      await waitFor(() => expect(view.search).toHaveBeenCalledWith('Prey'))
      change('Title', '  Prey 2017  ')
      expect(disabled('Find IGDB matches')).toBe(false)
      click('Find IGDB matches')
      await screen.findByText('Prey 2017')
      await act(async () =>
        outcome === 'success'
          ? old.resolve([candidate(1, 'Prey', 2006)])
          : old.reject(Error('Old search failed')),
      )
      expect(view.search.mock.calls).toEqual([['Prey'], ['Prey 2017']])
      expect(view.cancelRequest).toHaveBeenCalledTimes(1)
      expect(screen.queryByText('Prey', { exact: true })).toBeNull()
      expect(screen.queryByText('Old search failed')).toBeNull()
      expect(screen.getAllByRole('button', { name: 'Use these details' })).toHaveLength(1)
      click('Use these details')
      expect(value('Title')).toBe('Prey 2017')
      expect(value('Release year')).toBe('2017')
      expect(value('IGDB ID')).toBe('99')
      expect(view.writes()).toEqual([])
    },
  )
  it('retains typed fields after picker failure and permits retry', async () => {
    const view = setup(mode, null)
    view.picker.mockRejectedValueOnce(Error('File unavailable')).mockResolvedValueOnce(facts('Tunic'))
    click('Add a game')
    change('Title', 'My own title')
    change('Release year', '2001')
    change('Platform', 'Handheld')
    await browse()
    expect(screen.getByText('The executable could not be selected. Enter its path instead.')).toBeTruthy()
    expect(value('Title')).toBe('My own title')
    expect(value('Release year')).toBe('2001')
    expect(value('Platform')).toBe('Handheld')
    expect(view.writes()).toEqual([])
    await browse()
    expect(screen.queryByText('The executable could not be selected. Enter its path instead.')).toBeNull()
    expect(value('Title')).toBe('My own title')
    expect(value('Executable path')).toBe('D:\\Games\\Tunic\\Tunic.exe')
    expect(view.search.mock.calls).toEqual([['My own title']])
  })
  it('keeps all draft and candidate evidence when a second picker is canceled', async () => {
    const view = setup(mode, facts('Celeste'), [candidate(1, 'Celeste', 2018)])
    click('Add from executable…')
    await screen.findByRole('button', { name: 'Use these details' })
    change('Release year', '2017')
    change('Platform', 'My platform')
    view.picker.mockResolvedValue(null)
    await browse()
    expect(value('Title')).toBe('Celeste')
    expect(value('Release year')).toBe('2017')
    expect(value('Platform')).toBe('My platform')
    expect(value('Executable path')).toBe('D:\\Games\\Celeste\\Celeste.exe')
    expect(screen.getByText('The file identifies itself as Celeste.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Use these details' })).toBeTruthy()
    expect(view.search.mock.calls).toEqual([['Celeste']])
    expect(view.writes()).toEqual([])
  })
  it('saves inspected facts while metadata is pending and ignores the late result after reopen', async () => {
    const view = setup(mode, facts('Tunic')),
      pending = deferred<Candidate[]>()
    view.search.mockReturnValueOnce(pending.promise)
    click('Add from executable…')
    await screen.findByText('The file identifies itself as Tunic.')
    await screen.findByRole('button', { name: 'Searching IGDB…' })
    expect(disabled('Save game')).toBe(false)
    click('Save game')
    expect(await saved(view)).toMatchObject({ title: 'Tunic', executablePath: 'D:\\Games\\Tunic\\Tunic.exe' })
    await waitFor(() => expect(screen.queryByLabelText('Title')).toBeNull())
    click('Add a game')
    change('Title', 'Next game')
    await act(async () => pending.resolve([candidate(44, 'Old Tunic match', 2022)]))
    expect(value('Title')).toBe('Next game')
    expect(value('IGDB ID')).toBe('')
    expect(value('Executable path')).toBe('')
    expect(screen.queryByText('Old Tunic match')).toBeNull()
    expect(view.writes()).toHaveLength(1)
  })
  it('opens one picker and accepts its result during StrictMode effect replay', async () => {
    const view = setup(mode, null, [], true),
      pending = deferred<ExecutableFacts | null>()
    view.picker.mockReturnValueOnce(pending.promise)
    click('Add from executable…')
    expect(view.picker).toHaveBeenCalledTimes(1)
    await act(async () => pending.resolve(facts('Celeste')))
    expect(value('Title')).toBe('Celeste')
    expect(value('Executable path')).toBe('D:\\Games\\Celeste\\Celeste.exe')
    expect(view.search.mock.calls).toEqual([['Celeste']])
    expect(view.writes()).toEqual([])
  })
})
