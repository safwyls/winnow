import { expect, it } from 'vitest'
import {
  gridPageCount,
  homePageStart,
  homeShelfPosition,
  initialGridPosition,
  moveGridRows,
  reconcileGrid,
  selectGrid,
} from '../src/renderer/themes/avalon-navigation'

const ids = (count: number) => Array.from({ length: count }, (_, index) => index + 1)
it('moves vertically within the overlap and scrolls only at the two-row edge', () => {
  const games = ids(40)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 2, games)
  for (const [direction, index, first] of [
    [1, 8, 0],
    [1, 14, 1],
    [-1, 8, 1],
    [-1, 2, 0],
    [-1, 2, 0],
  ]) {
    state = moveGridRows(state, direction, games)
    expect(state.selectedIndex).toBe(index)
    expect(state.firstRow).toBe(first)
  }
})
it('clamps a partial final row while retaining both visible rows', () => {
  const games = ids(27)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 23, games)
  state = moveGridRows(state, 1, games)
  expect(state).toMatchObject({ selectedId: 27, selectedIndex: 26, firstRow: 3 })
  expect(state.selectedIndex - state.firstRow * state.columns).toBe(8)
  expect(moveGridRows(state, 1, games)).toEqual(state)
  state = moveGridRows(state, -1, games)
  expect(state).toMatchObject({ selectedIndex: 20, firstRow: 3 })
})
it('moves two rows per page and clamps both ends', () => {
  const games = ids(27)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 5, games)
  for (const [direction, index, first] of [
    [2, 17, 1],
    [2, 26, 3],
    [2, 26, 3],
    [-2, 14, 2],
    [-2, 2, 0],
    [-2, 2, 0],
  ]) {
    state = moveGridRows(state, direction, games)
    expect(state.selectedIndex).toBe(index)
    expect(state.firstRow).toBe(first)
  }
})
it('follows selected identity through reorder and filtering', () => {
  const games = ids(40)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 34, games)
  state = reconcileGrid(state, 6, [...games].reverse())
  expect(state).toMatchObject({ selectedId: 35, selectedIndex: 5, firstRow: 0 })
  state = reconcileGrid(state, 6, [2, 7, 35, 40])
  expect(state).toMatchObject({ selectedId: 35, selectedIndex: 2, firstRow: 0 })
})
it('uses the nearest remaining index when the selected game is removed', () => {
  const games = ids(40)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 34, games)
  state = reconcileGrid(state, 6, ids(15))
  expect(state).toMatchObject({ selectedId: 15, selectedIndex: 14, firstRow: 1 })
  state = reconcileGrid(state, 6, [70, 80, 90])
  expect(state).toMatchObject({ selectedId: 90, selectedIndex: 2, firstRow: 0 })
})
it('preserves identity and retains first row when a resize keeps selection visible', () => {
  const games = ids(71)
  let state = selectGrid(reconcileGrid(initialGridPosition(), 6, games), 27, games)
  expect(state.firstRow).toBe(3)
  for (const [columns, first, position] of [
    [7, 3, 6],
    [12, 2, 3],
    [3, 8, 3],
  ]) {
    state = reconcileGrid(state, columns, games)
    expect(state.selectedId).toBe(28)
    expect(state.firstRow).toBe(first)
    expect(state.selectedIndex - first * columns).toBe(position)
  }
})
it('clears an empty collection and can repopulate with a normalized column count', () => {
  const games = ids(40)
  const state = reconcileGrid(selectGrid(reconcileGrid(initialGridPosition(), 6, games), 34, games), 6, [])
  expect(state).toMatchObject({ selectedId: null, selectedIndex: 0, firstRow: 0 })
  for (const delta of [-2, -1, 1, 2]) expect(moveGridRows(state, delta, [])).toEqual(state)
  expect(reconcileGrid(state, 0, [90])).toEqual({ columns: 1, selectedId: 90, selectedIndex: 0, firstRow: 0 })
})
it.each([
  [0, 1],
  [1, 1],
  [12, 1],
  [13, 2],
  [24, 2],
  [27, 3],
])('counts two-row pages including a partial row (%s games)', (count, expected) => {
  expect(gridPageCount(count, 6)).toBe(expected)
})
it('carries the current visible column to the destination saved overflow page', () => {
  expect(homeShelfPosition(8, 0, 6, 20)).toBe(2)
  expect(homeShelfPosition(4, 8, 6, 20)).toBe(10)
  expect(homeShelfPosition(11, 10, 6, 20)).toBe(11)
  expect(homeShelfPosition(5, 12, 6, 14)).toBe(13)
  expect(homeShelfPosition(4, 19, 6, 2)).toBe(1)
})
it('clamps saved pages after shrink and recomputes them after capacity changes', () => {
  expect(homePageStart(19, 6, 20)).toBe(18)
  expect(homePageStart(19, 6, 10)).toBe(6)
  expect(homePageStart(19, 8, 20)).toBe(16)
  expect(homePageStart(19, 8, 0)).toBe(0)
})
