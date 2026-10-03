import { ChoiceMenu, type ChoiceOption } from './ChoiceMenu'

export type SortOption = ChoiceOption

/** Library and Merges share the same compact desktop sort control. */
export function SortMenu(props: {
  value: string
  options: readonly SortOption[]
  onChange(value: string): void
}) {
  return <ChoiceMenu label="Sort order" prefix="Sort" {...props} />
}
