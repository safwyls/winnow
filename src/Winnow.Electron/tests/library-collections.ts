import { fireEvent, screen } from '@testing-library/react'
import { libraryRole, returnToLibrary } from './library-controls'

export function selectCollection(id: number | 'all') {
  const picker = document.querySelector<HTMLSelectElement>('.avalon-list-picker select')
  if (picker) fireEvent.change(picker, { target: { value: String(id) } })
  else {
    returnToLibrary()
    if (
      id === 'all' &&
      document.querySelector<HTMLElement>('.avalon-library')?.dataset.listId !== 'all' &&
      screen.queryByRole('button', { name: 'More' })
    ) {
      fireEvent.click(libraryRole('button', { name: 'Close list' }))
      return
    }
    if (id !== 'all' && screen.queryByRole('button', { name: 'My lists' }))
      fireEvent.click(screen.getByRole('button', { name: 'My lists' }))
    const target =
      id === 'all'
        ? [...document.querySelectorAll<HTMLButtonElement>('.avalon-buckets button')].find((button) =>
            button.textContent?.startsWith('All games'),
          )
        : document.querySelector<HTMLButtonElement>(`[data-avalon-list="${id}"]`)
    if (!target) throw Error(`Collection ${id} is not rendered`)
    fireEvent.click(target)
  }
}
export function selectedCollection() {
  return (
    document.querySelector<HTMLElement>('.avalon-library')?.dataset.listId ??
    document.querySelector<HTMLSelectElement>('.avalon-list-picker select')?.value ??
    document.querySelector<HTMLElement>('[data-avalon-list][aria-pressed="true"]')?.dataset.avalonList ??
    'all'
  )
}
export function collectionNames() {
  const fullscreen = screen.queryByRole('button', { name: 'My lists' })
  if (fullscreen) {
    fireEvent.click(fullscreen)
    const names = [
      'All games',
      ...[...document.querySelectorAll('[data-avalon-list]')].map((button) =>
        button.textContent?.replace(/ · \d+ games/, '').replace(' · Live list', ' · Live'),
      ),
    ]
    returnToLibrary()
    return names
  }
  const picker = document.querySelector<HTMLSelectElement>('.avalon-list-picker select')
  return picker
    ? [...picker.options].map((option) => option.textContent)
    : [
        'All games',
        ...[...document.querySelectorAll('[data-avalon-list] > span')].map((span) => span.textContent),
      ]
}
