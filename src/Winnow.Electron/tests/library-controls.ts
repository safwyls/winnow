import { fireEvent, screen } from '@testing-library/react'
import { chooseOption } from './choice-test-utils'

// Shared behavior tests reach fullscreen actions through the same explicit panels
// as a user; desktop controls keep their direct path. Native tests measure the panels.
function fullscreenLibrary() {
  return (
    Boolean(document.querySelector('.avalon-library')) &&
    Boolean(screen.queryByRole('button', { name: 'More', hidden: true }))
  )
}
export function returnToLibrary() {
  const back = screen.queryByRole('button', { name: 'Back to library' })
  if (back) fireEvent.click(back)
}
export function libraryRole(...args: Parameters<typeof screen.getByRole>) {
  let [role, options] = args
  if (role === 'textbox' && ['From this year', 'Up to this year'].includes(String(options?.name))) {
    // Fullscreen's controller keyboard edits this retained model input, as the source TextBox fixture does.
    const input = document.querySelector<HTMLInputElement>(
      `.avalon-filter-panel.fullscreen input[aria-label="${options?.name}"]`,
    )
    if (input) return input
  }
  if (fullscreenLibrary() && options?.name === 'Filters') options = { ...options, name: 'Filter & sort' }
  let control = screen.queryByRole(role, options)
  if (control) return control
  if (!fullscreenLibrary()) return screen.getByRole(role, options)
  returnToLibrary()
  control = screen.queryByRole(role, options)
  if (control) return control
  fireEvent.click(screen.getByRole('button', { name: 'More' }))
  if (role === 'textbox' || (role === 'button' && options?.name === 'Sort'))
    fireEvent.click(screen.getByText('Current search and sort'))
  return screen.getByRole(role, options)
}

export function chooseFilterSelect(field: 'Sort' | 'Collection' | 'Installation', value: string) {
  const panel = document.querySelector('.avalon-filter-panel.fullscreen')
  if (!panel) {
    chooseOption(screen.getByLabelText(field), value)
    return
  }
  const back = screen.queryByRole('button', { name: 'Back to filters' })
  if (back) fireEvent.click(back)
  fireEvent.click(
    screen.getByRole('button', { name: field === 'Installation' ? /^ON DISK ·/ : new RegExp(`^${field} ·`) }),
  )
  const choice = [...panel.querySelectorAll<HTMLButtonElement>('[data-filter-choice]')].find(
    (button) => button.dataset.filterChoice === value,
  )
  if (!choice) throw Error(`Missing ${field} choice ${value}`)
  fireEvent.click(choice)
}

export function openFilterGroup(header: string) {
  const back = screen.queryByRole('button', { name: 'Back to filters' })
  if (back) fireEvent.click(back)
  fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${header} ·`) }))
}
export function libraryLabel(...args: Parameters<typeof screen.getByLabelText>) {
  if (args[0] === 'Sort' && !fullscreenLibrary()) return screen.getByRole('button', { name: /^Sort ·/ })
  if (fullscreenLibrary() && !screen.queryByLabelText(...args)) {
    fireEvent.click(libraryRole('button', { name: 'More' }))
    fireEvent.click(screen.getByText('Current search and sort'))
  }
  const field = screen.getByLabelText(...args)
  const details = field.closest('details')
  if (details && !details.open) fireEvent.click(details.querySelector('summary')!)
  return field
}
export function setLibrarySort(value: string) {
  chooseOption(libraryLabel('Sort'), value)
}
