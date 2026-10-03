import { fireEvent } from '@testing-library/react'

/** Select through the same popup and click path used by the renderer. */
export function chooseOption(control: HTMLElement, value: string) {
  if (control.getAttribute('aria-expanded') !== 'true') fireEvent.click(control)
  const menu = document.getElementById(control.getAttribute('aria-controls') ?? '')
  const option = [...(menu?.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]') ?? [])].find(
    (item) => item.value === value,
  )
  if (!option) throw new Error(`No choice ${JSON.stringify(value)} in ${control.getAttribute('aria-label')}`)
  fireEvent.click(option)
}
