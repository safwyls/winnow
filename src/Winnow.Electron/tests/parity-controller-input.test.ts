import { describe, expect, it } from 'vitest'
import {
  ControllerButton as B,
  ControllerInput,
  controllerButtons,
  type ControllerPadState,
} from '../src/shared/controller-input'

function pad(held: number[] = [], axes = [0, 0, 0, 0], index = 0): ControllerPadState {
  return {
    index,
    connected: true,
    mapping: 'standard',
    axes,
    buttons: Array.from({ length: 17 }, (_, id) => ({
      pressed: held.includes(id),
      value: held.includes(id) ? 1 : 0,
    })),
  }
}
function active(pad: ControllerPadState) {
  return controllerButtons(pad).flatMap((held, id) => (held ? [id] : []))
}

describe('original controller mapping contracts', () => {
  it.each([
    [0, []],
    [11999, []],
    [-11999, []],
    [12000, [B.ScrollUp]],
    [-32768, [B.ScrollDown]],
  ] as const)('maps original right-stick Y %s through the preserved deadzone', (y, expected) => {
    expect(active(pad([], [0, 0, 0, -y / 32768]))).toEqual(expected)
  })
  it.each([
    [0, 0, []],
    [11999, -11999, []],
    [12000, 0, [B.Right]],
    [-32768, 0, [B.Left]],
    [0, -32768, [B.Down]],
    [20000, 25000, [B.Up]],
  ] as const)('chooses the original dominant stick direction for %s,%s', (x, y, expected) => {
    expect(active(pad([], [x / 32768, -y / 32768, 0, 0]))).toEqual(expected)
  })
  it.each([
    B.Accept,
    B.Back,
    B.Play,
    B.Context,
    B.Previous,
    B.Next,
    B.Search,
    B.Menu,
    B.Up,
    B.Down,
    B.Left,
    B.Right,
  ])('preserves the standard face, shoulder or navigation action %s', (button) => {
    expect(active(pad([button]))).toEqual([button])
  })
  it.each([
    [0, 0, []],
    [30, 30, []],
    [31, 0, [B.PagePrevious]],
    [0, 255, [B.PageNext]],
    [255, 255, [B.PagePrevious, B.PageNext]],
  ] as const)(
    'maps independent trigger values %s and %s at the original threshold',
    (left, right, expected) => {
      const state = pad()
      ;(state.buttons as { pressed: boolean; value: number }[])[6] = { pressed: false, value: left / 255 }
      ;(state.buttons as { pressed: boolean; value: number }[])[7] = { pressed: false, value: right / 255 }
      expect(active(state)).toEqual(expected)
    },
  )
  it('does not turn unavailable or non-finite axes into navigation', () => {
    expect(active(pad([], []))).toEqual([])
    expect(active(pad([], [NaN, Infinity, 0, -Infinity]))).toEqual([])
  })
})

describe('original controller filtering contracts', () => {
  it('repeats held scroll after 400ms and every 110ms without repeating Accept', () => {
    const input = new ControllerInput()
    input.sample(pad(), true, 0)
    const held = pad([B.Accept], [0, 0, 0, 1])
    expect(input.sample(held, true, 1)).toEqual([B.Accept, B.ScrollDown])
    expect(input.sample(held, true, 400)).toEqual([])
    expect(input.sample(held, true, 401)).toEqual([B.ScrollDown])
    expect(input.sample(held, true, 511)).toEqual([B.ScrollDown])
  })
  it('does not repeat Accept or fire it on focus return until it releases', () => {
    const input = new ControllerInput(),
      held = pad([B.Accept])
    expect(input.sample(pad(), true, 0)).toEqual([])
    expect(input.sample(held, true, 1)).toEqual([B.Accept])
    expect(input.sample(held, true, 1000)).toEqual([])
    expect(input.sample(held, false, 1001)).toEqual([])
    expect(input.sample(held, true, 1002)).toEqual([])
    expect(input.sample(held, true, 2000)).toEqual([])
    input.sample(pad(), true, 2001)
    expect(input.sample(held, true, 2002)).toEqual([B.Accept])
  })
  it('repeats directions at exact boundaries, resets on reversal and never replays stalled frames', () => {
    const input = new ControllerInput(),
      right = pad([B.Right]),
      left = pad([B.Left])
    input.sample(pad(), true, 0)
    expect(input.sample(right, true, 1)).toEqual([B.Right])
    expect(input.sample(right, true, 400)).toEqual([])
    expect(input.sample(right, true, 401)).toEqual([B.Right])
    expect(input.sample(right, true, 510)).toEqual([])
    expect(input.sample(right, true, 511)).toEqual([B.Right])
    expect(input.sample(right, true, 10000)).toEqual([B.Right])
    expect(input.sample(right, true, 10001)).toEqual([])
    expect(input.sample(left, true, 10002)).toEqual([B.Left])
    expect(input.sample(left, true, 10112)).toEqual([])
  })
  it('releases reconnect suppression independently so a fresh direction works while Accept stays held', () => {
    const input = new ControllerInput()
    input.sample(null, true, 0)
    expect(input.sample(pad([B.Accept]), true, 0)).toEqual([])
    expect(input.sample(pad([B.Accept, B.Down]), true, 1000)).toEqual([B.Down])
    input.sample(pad(), true, 2000)
    expect(input.sample(pad([B.Accept]), true, 3000)).toEqual([B.Accept])
  })
  it('suppresses launch, search and paging on reconnect and never repeats held actions', () => {
    const input = new ControllerInput(),
      held = pad([B.Play, B.Search, B.PageNext])
    expect(input.sample(held, true, 0)).toEqual([])
    input.sample(pad(), true, 1000)
    expect(input.sample(held, true, 2000)).toEqual([B.Play, B.PageNext, B.Search])
    expect(input.sample(held, true, 3000)).toEqual([])
    input.sample(null, true, 4000)
    expect(input.sample(held, true, 5000)).toEqual([])
  })
  it('suppresses sticks held on connection individually while fresh actions remain available', () => {
    const input = new ControllerInput()
    expect(input.sample(pad([], [1, 0, 0, -1]), true, 0)).toEqual([])
    expect(input.sample(pad([B.Back], [1, 0, 0, -1]), true, 1000)).toEqual([B.Back])
    expect(input.sample(pad([], [0, 0, 0, -1]), true, 2000)).toEqual([])
    expect(input.sample(pad([], [-1, 0, 0, -1]), true, 2001)).toEqual([B.Left])
    expect(input.sample(pad([], [-1, 0, 0, -1]), true, 2401)).toEqual([B.Left])
  })
  it('resets the held state for a different controller and ignores disconnected or unmapped devices', () => {
    const input = new ControllerInput()
    input.sample(pad(), true, 0)
    expect(input.sample(pad([B.Accept], [], 1), true, 1)).toEqual([])
    expect(input.sample({ ...pad([B.Accept]), connected: false }, true, 2)).toEqual([])
    expect(input.sample({ ...pad([B.Accept]), mapping: '' }, true, 3)).toEqual([])
    input.sample(pad(), true, 4)
    expect(input.sample(pad([B.Accept]), true, 5)).toEqual([B.Accept])
  })
})
