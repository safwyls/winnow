export interface AvalonGridPosition {
  columns: number
  firstRow: number
  selectedIndex: number
  selectedId: number | null
}

export const initialGridPosition = (): AvalonGridPosition => ({
  columns: 6,
  firstRow: 0,
  selectedIndex: 0,
  selectedId: null,
})
const bounded = (index: number, count: number) => Math.max(0, Math.min(Math.max(0, count - 1), index))
const rowCount = (count: number, columns: number) => Math.ceil(count / columns)

function reveal(position: AvalonGridPosition, count: number): AvalonGridPosition {
  const row = Math.floor(position.selectedIndex / position.columns)
  let firstRow = position.firstRow
  if (row < firstRow) firstRow = row
  else if (row > firstRow + 1) firstRow = row - 1
  return {
    ...position,
    firstRow: Math.max(0, Math.min(Math.max(0, rowCount(count, position.columns) - 2), firstRow)),
  }
}

/** Keep stable game identity and only move the two-row window when selection leaves it. */
export function reconcileGrid(
  position: AvalonGridPosition,
  columns: number,
  ids: number[],
): AvalonGridPosition {
  const next = { ...position, columns: Math.max(1, Math.floor(columns)) }
  if (!ids.length) return { ...next, selectedId: null, selectedIndex: 0, firstRow: 0 }
  const current = next.selectedId === null ? -1 : ids.indexOf(next.selectedId)
  next.selectedIndex = current >= 0 ? current : bounded(next.selectedIndex, ids.length)
  next.selectedId = ids[next.selectedIndex]
  return reveal(next, ids.length)
}

export function selectGrid(position: AvalonGridPosition, index: number, ids: number[]): AvalonGridPosition {
  if (!ids.length) return reconcileGrid(position, position.columns, ids)
  const selectedIndex = bounded(index, ids.length)
  return reveal({ ...position, selectedIndex, selectedId: ids[selectedIndex] }, ids.length)
}

export function moveGridRows(position: AvalonGridPosition, delta: number, ids: number[]): AvalonGridPosition {
  const next = reconcileGrid(position, position.columns, ids)
  if (!ids.length) return next
  const row = bounded(
    Math.floor(next.selectedIndex / next.columns) + delta,
    rowCount(ids.length, next.columns),
  )
  return selectGrid(
    next,
    Math.min(row * next.columns + (next.selectedIndex % next.columns), ids.length - 1),
    ids,
  )
}

export const gridPageCount = (count: number, columns: number) =>
  Math.max(1, Math.ceil(rowCount(count, Math.max(1, columns)) / 2))
export const homePageStart = (position: number, capacity: number, count: number) =>
  Math.floor(bounded(position, count) / Math.max(1, capacity)) * Math.max(1, capacity)

/** Keep the destination shelf's saved page, but carry the current visible column. */
export function homeShelfPosition(current: number, saved: number, capacity: number, count: number): number {
  const columns = Math.max(1, capacity)
  return bounded(homePageStart(saved, columns, count) + (current % columns), count)
}
