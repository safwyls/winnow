import { fireEvent } from '@testing-library/react'

export function selectCollection(id: number | 'all') {
  const picker = document.querySelector<HTMLSelectElement>('.avalon-list-picker select')
  if (picker) fireEvent.change(picker, { target: { value: String(id) } })
  else {
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
    document.querySelector<HTMLSelectElement>('.avalon-list-picker select')?.value ??
    document.querySelector<HTMLElement>('[data-avalon-list][aria-pressed="true"]')?.dataset.avalonList ??
    'all'
  )
}
export function collectionNames() {
  const picker = document.querySelector<HTMLSelectElement>('.avalon-list-picker select')
  return picker
    ? [...picker.options].map((option) => option.textContent)
    : [
        'All games',
        ...[...document.querySelectorAll('[data-avalon-list] > span')].map((span) => span.textContent),
      ]
}
