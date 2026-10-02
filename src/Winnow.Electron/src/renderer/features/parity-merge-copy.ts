import { mergeMemberLabels, mergeTitle, type MergeCard, type MergeSection } from './parity-merge-model'

export const mergeScreenCopy = {
  possibleMatches: 'Possible matches',
  loading: 'Loading possible matches…',
  sortPrefix: 'Sort · ',
  kindPrefix: 'Kind · ',
  platformPrefix: 'Preferred platform · ',
  preferredMainPlatform: 'Preferred main platform',
  proposalKinds: 'Proposal kinds',
  filteredProposals: 'Filtered proposals',
  countArrow: ' → ',
  separator: ' · ',
  checkSaved: 'Check saved review',
  selectGroup: 'Select group',
  entries: 'entries',
  nested: 'nested, nothing deleted',
  headerStore: 'Header store',
  details: 'Details',
  reviewUndo: 'Review undo',
  undo: 'Undo review decisions',
  dismissUndo: 'Dismiss review undo',
  dismiss: 'Dismiss',
  reviewNotice: 'Review notice',
  refused: "Couldn't link those.",
  refusedNote: 'That proposal was out of date · nothing changed.',
  dismissNotice: 'Dismiss review notice',
} as const

export const mergeSectionNames: Record<MergeSection, string> = {
  stores: 'Across stores',
  editions: 'Editions',
  expansions: 'Expansions',
  parts: 'Parts',
  tests: 'Test builds',
}
export const mergeSortOptions = [
  { value: 'strength', label: 'Strongest match' },
  { value: 'playtime', label: 'Playtime at stake' },
  { value: 'title', label: 'Title' },
] as const
export const mergePlatformOptions = [
  { value: '', label: 'None' },
  { value: 'steam', label: 'Steam' },
  { value: 'epic', label: 'Epic Games' },
  { value: 'gog', label: 'GOG' },
] as const

export const mergeActionCopy = {
  same: 'Same game',
  different: 'Different games',
  separate: 'Separate again',
  sameTip: 'Nest the other rows under the header (S)',
  differentTip: 'Leave them separate, not asked again (D)',
  separateTip: 'Undo this roll-up. Nothing was deleted.',
  undoTip: 'Put it back the way it was',
  dismissTip: 'Dismiss',
  unreadTip: 'Patched since you played',
} as const

export function mergeActionNames(card: MergeCard) {
  const labels = mergeMemberLabels(card)
  const included = labels.filter((_, index) => card.included.includes(card.rows[index]!.workId))
  return {
    same: `${mergeActionCopy.same}: ${included.join(', ')}, under ${mergeTitle(card)}`,
    different: `${mergeActionCopy.different}: ${labels.join(', ')}`,
    separate: `${mergeActionCopy.separate}: ${mergeTitle(card)}`,
  }
}
