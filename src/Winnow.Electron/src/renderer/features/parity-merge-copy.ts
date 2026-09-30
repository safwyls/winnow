import { mergeMemberLabels, mergeTitle, type MergeCard } from './parity-merge-model'

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
