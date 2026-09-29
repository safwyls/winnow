// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LibraryTools } from '../src/renderer/features/LibraryTools'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { ExecutableFacts } from '../src/shared/executable-facts'
import type { Mode } from '../src/renderer/api/types'

const facts = (title: string | null, extra: Partial<ExecutableFacts> = {}): ExecutableFacts => ({
  executablePath: `D:\\Games\\${title ?? 'game'}\\game.exe`,
  installPath: `D:\\Games\\${title ?? 'game'}`,
  title,
  titleSource: title ? 'file-description' : 'none',
  publisher: null,
  ...extra,
})
const candidate = { igdbId: 1877, name: 'Cyberpunk 2077', firstReleaseYear: 2020, platforms: ['PC'] }
function setup(mode: Mode, picked: ExecutableFacts | null, candidates = [candidate]) {
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [], lists: [] }
        : input.route === 'metadata.search'
          ? candidates
          : input.route === 'manual.get'
            ? []
            : {},
  }))
  const picker = vi.fn<() => Promise<ExecutableFacts | null>>().mockResolvedValue(picked)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, chooseManualExecutableFacts: picker },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(
    <QueryClientProvider client={client}>
      <LibraryTools mode={mode} />
    </QueryClientProvider>,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Manual games' }))
  return { request, picker, view, client }
}
afterEach(() => {
  cleanup()
  clearViewState('draft:manual:new')
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:manual:editing`)
    clearViewState(`${mode}:library-tools:tab`)
  }
})
const value = (name: string) => (screen.getByLabelText(name) as HTMLInputElement).value
async function browse() {
  fireEvent.click(screen.getByRole('button', { name: 'Choose executable' }))
  await screen.findByRole('button', { name: 'Choose executable' })
}

describe.each(['desktop', 'fullscreen'] as const)('manual executable flow in %s', (mode) => {
  it('opens the form and file picker in one gesture, fills inspected facts and searches the proposed title', async () => {
    const view = setup(mode, facts('Celeste', { publisher: 'Extremely OK Games' }), [
      { ...candidate, name: 'Celeste', firstReleaseYear: 2018 },
    ])
    fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
    await screen.findByRole('button', { name: 'Use these details' })
    expect(view.picker).toHaveBeenCalledTimes(1)
    expect(value('Executable path')).toBe('D:\\Games\\Celeste\\game.exe')
    expect(value('Installation folder')).toBe('D:\\Games\\Celeste')
    expect(value('Title')).toBe('Celeste')
    expect(
      screen.getByText('The file identifies itself as Celeste. Published by Extremely OK Games.'),
    ).toBeTruthy()
    expect(
      view.request.mock.calls
        .filter(([input]) => input.route === 'metadata.search')
        .map(([input]) => input.params),
    ).toEqual([{ title: 'Celeste' }])
    expect(
      view.request.mock.calls.some(
        ([input]) => input.route.startsWith('manual.') && input.route !== 'manual.get',
      ),
    ).toBe(false)
  })
  it('changes no fields or match evidence when the native picker is canceled', async () => {
    const view = setup(mode, null)
    fireEvent.click(screen.getByRole('button', { name: 'Add a game' }))
    await browse()
    expect(value('Title')).toBe('')
    expect(value('Executable path')).toBe('')
    expect(value('Installation folder')).toBe('')
    expect(view.request.mock.calls.some(([input]) => input.route === 'metadata.search')).toBe(false)
    expect(screen.queryByText(/The file identifies|Guessed|No title found/)).toBeNull()
  })
  it('selects candidate metadata without writing until Save and records the chosen executable and its folder', async () => {
    const view = setup(mode, facts(null))
    fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
    await screen.findByText('No title found in the file. Type one above.')
    expect(view.request.mock.calls.some(([input]) => input.route === 'metadata.search')).toBe(false)
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Cyberpunk' } })
    fireEvent.click(screen.getByRole('button', { name: 'Find IGDB matches' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Use these details' }))
    expect(value('Title')).toBe('Cyberpunk 2077')
    expect(value('Release year')).toBe('2020')
    expect(value('IGDB ID')).toBe('1877')
    expect(screen.queryByRole('button', { name: 'Use these details' })).toBeNull()
    expect(view.request.mock.calls.some(([input]) => input.route === 'manual.create')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() =>
      expect(view.request.mock.calls.some(([input]) => input.route === 'manual.create')).toBe(true),
    )
    expect(view.request.mock.calls.find(([input]) => input.route === 'manual.create')![0].body).toMatchObject(
      {
        title: 'Cyberpunk 2077',
        firstReleaseYear: 2020,
        igdbId: 1877,
        executablePath: 'D:\\Games\\game\\game.exe',
        installPath: 'D:\\Games\\game',
      },
    )
  })
  it('searches a corrected title and can dismiss candidates without changing the form or preventing a manual save', async () => {
    const view = setup(mode, facts('Prey'))
    fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
    await screen.findByRole('button', { name: 'Use these details' })
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Prey 2017' } })
    fireEvent.click(screen.getByRole('button', { name: 'Find IGDB matches' }))
    await waitFor(() =>
      expect(view.request.mock.calls.filter(([input]) => input.route === 'metadata.search')).toHaveLength(2),
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Keep my own details' }))
    expect(value('Title')).toBe('Prey 2017')
    expect(value('Executable path')).toBe('D:\\Games\\Prey\\game.exe')
    expect(value('IGDB ID')).toBe('')
    expect(screen.queryByRole('button', { name: 'Use these details' })).toBeNull()
    expect(screen.queryByText('No matching games. You can still fill the form by hand.')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() =>
      expect(
        view.request.mock.calls.find(([input]) => input.route === 'manual.create')![0].body,
      ).toMatchObject({ title: 'Prey 2017', igdbId: null }),
    )
    expect(
      view.request.mock.calls
        .filter(([input]) => input.route === 'metadata.search')
        .map(([input]) => input.params),
    ).toEqual([{ title: 'Prey' }, { title: 'Prey 2017' }])
  })
  it('leaves an uninformative executable editable without searching and still saves a manually entered game', async () => {
    const view = setup(mode, facts(null))
    fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
    await screen.findByText('No title found in the file. Type one above.')
    expect(value('Title')).toBe('')
    expect((screen.getByRole('button', { name: 'Find IGDB matches' }) as HTMLButtonElement).disabled).toBe(
      true,
    )
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'A Disc I Own' } })
    fireEvent.change(screen.getByLabelText('Platform'), { target: { value: 'PlayStation 2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() =>
      expect(
        view.request.mock.calls.find(([input]) => input.route === 'manual.create')![0].body,
      ).toMatchObject({ title: 'A Disc I Own', platformLabel: 'PlayStation 2' }),
    )
    expect(view.request.mock.calls.some(([input]) => input.route === 'metadata.search')).toBe(false)
  })
  it('replaces its previous proposed title on a second browse but preserves a user override on the third', async () => {
    const view = setup(mode, facts('Celeste'), [])
    fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
    await screen.findByText('The file identifies itself as Celeste.')
    await screen.findByRole('button', { name: 'Choose executable' })
    view.picker.mockResolvedValue(facts('Tunic', { titleSource: 'folder-name' }))
    await browse()
    expect(value('Title')).toBe('Tunic')
    expect(screen.getByText('Guessed Tunic from the path.')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'My own title' } })
    view.picker.mockResolvedValue(facts('Braid'))
    await browse()
    expect(value('Title')).toBe('My own title')
    expect(value('Executable path')).toBe('D:\\Games\\Braid\\game.exe')
    expect(value('Installation folder')).toBe('D:\\Games\\Braid')
    expect(
      view.request.mock.calls
        .filter(([input]) => input.route === 'metadata.search')
        .map(([input]) => input.params),
    ).toEqual([{ title: 'Celeste' }, { title: 'Tunic' }, { title: 'My own title' }])
  })
})

it('preserves a title typed while the inspector is pending and never stores anything from the picker alone', async () => {
  const view = setup('desktop', facts('Tunic'), [])
  let finish!: (value: ExecutableFacts) => void
  view.picker.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Add from executable…' }))
  fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'My choice' } })
  await act(async () => finish(facts('Tunic')))
  expect(value('Title')).toBe('My choice')
  expect(value('Executable path')).toBe('D:\\Games\\Tunic\\game.exe')
  expect(view.request.mock.calls.some(([input]) => input.route === 'manual.create')).toBe(false)
})
