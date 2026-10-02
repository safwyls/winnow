import { fireEvent, screen } from '@testing-library/react'

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
  if (['textbox', 'combobox'].includes(role)) fireEvent.click(screen.getByText('Current search and sort'))
  return screen.getByRole(role, options)
}

export function chooseFilterSelect(field: 'Sort' | 'Collection' | 'Installation', value: string) {
  const panel = document.querySelector('.avalon-filter-panel.fullscreen')
  if (!panel) {
    fireEvent.change(screen.getByLabelText(field), { target: { value } })
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
  if (fullscreenLibrary()) fireEvent.change(libraryLabel('Sort'), { target: { value } })
  else {
    fireEvent.click(screen.getByRole('button', { name: /^Sort ·/ }))
    const option = screen
      .getAllByRole('menuitemradio')
      .find((item) => (item as HTMLButtonElement).value === value)
    if (!option) throw new Error(`Missing library sort option: ${value}`)
    fireEvent.click(option)
  }
}
