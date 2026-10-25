import { fireEvent, screen } from '@testing-library/react'
import { libraryRole, returnToLibrary } from './library-controls'
import { chooseOption } from './choice-test-utils'

const listPicker = () =>
  document.querySelector<HTMLButtonElement>('.avalon-list-picker button[aria-haspopup="menu"]')

export function selectCollection(id: number | 'all') {
  const picker = listPicker()
  if (picker) chooseOption(picker, String(id))
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
    listPicker()?.value ??
    document.querySelector<HTMLElement>('[data-avalon-list][aria-pressed="true"]')?.dataset.avalonList ??
    'all'
  )
}
export function collectionNames() {
  const picker = listPicker()
  if (picker) {
    if (picker.getAttribute('aria-expanded') !== 'true') fireEvent.click(picker)
    const menu = document.getElementById(picker.getAttribute('aria-controls') ?? '')
    if (!menu) throw Error('List picker did not open its menu')
    const names = [...menu.querySelectorAll('[role="menuitemradio"]')].map((option) => option.textContent)
    fireEvent.keyDown(menu, { key: 'Escape' })
    return names
  }
  const fullscreen = screen.queryByRole('button', { name: 'My lists' })
  if (fullscreen) {
    fireEvent.click(fullscreen)
    const names = [
      'All games',
      ...[...document.querySelectorAll('[data-avalon-list]')].map((button) =>
        button.textContent?.replace(/ · \d+ games?/, '').replace(' · Live list', ' · Live'),
      ),
    ]
    returnToLibrary()
    return names
  }
  return [
    'All games',
    ...[...document.querySelectorAll('[data-avalon-list] > span')].map((span) => span.textContent),
  ]
}
