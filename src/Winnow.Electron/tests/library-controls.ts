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
export function libraryLabel(...args: Parameters<typeof screen.getByLabelText>) {
  if (fullscreenLibrary() && !screen.queryByLabelText(...args)) {
    fireEvent.click(libraryRole('button', { name: 'More' }))
    fireEvent.click(screen.getByText('Current search and sort'))
  }
  const field = screen.getByLabelText(...args)
  const details = field.closest('details')
  if (details && !details.open) fireEvent.click(details.querySelector('summary')!)
  return field
}
