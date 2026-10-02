export const ControllerButton = {
  Accept: 0,
  Back: 1,
  Play: 2,
  Context: 3,
  Previous: 4,
  Next: 5,
  PagePrevious: 6,
  PageNext: 7,
  Search: 8,
  Menu: 9,
  Up: 12,
  Down: 13,
  Left: 14,
  Right: 15,
  ScrollUp: 18,
  ScrollDown: 19,
} as const

export interface ControllerButtonState {
  pressed: boolean
  value?: number
}
export interface ControllerPadState {
  index: number
  connected: boolean
  mapping: string
  buttons: readonly ControllerButtonState[]
  axes: readonly number[]
}

export const controllerDeadzone = 12000 / 32768
export const controllerTriggerThreshold = 30 / 255

/** Chromium normalizes platform mappings; its Y axis increases downward. */
export function controllerButtons(pad: Pick<ControllerPadState, 'buttons' | 'axes'>): boolean[] {
  const result = Array.from({ length: 20 }, (_, index) => {
    if (index > 15) return false
    const button = pad.buttons[index]
    if (!button) return false
    const threshold = index === 6 || index === 7 ? controllerTriggerThreshold : 0.5
    return button.pressed || (Number.isFinite(button.value) && button.value! > threshold)
  })
  const x = pad.axes[0] ?? 0,
    y = pad.axes[1] ?? 0
  if (Number.isFinite(x) && Number.isFinite(y) && Math.max(Math.abs(x), Math.abs(y)) >= controllerDeadzone) {
    const direction =
      Math.abs(x) > Math.abs(y)
        ? x < 0
          ? ControllerButton.Left
          : ControllerButton.Right
        : y < 0
          ? ControllerButton.Up
          : ControllerButton.Down
    result[direction] = true
  }
  const scroll = pad.axes[3] ?? 0
  if (Number.isFinite(scroll) && Math.abs(scroll) >= controllerDeadzone)
    result[scroll < 0 ? ControllerButton.ScrollUp : ControllerButton.ScrollDown] = true
  return result
}

const directions = [12, 13, 14, 15, 18, 19]
/** Suppress each reconnect-held input until it releases; repeat only navigation. */
export class ControllerInput {
  private index: number | null = null
  private suppressed: boolean[] = []
  private previous: boolean[] = []
  private repeatAt = 0

  reset() {
    this.index = null
    this.suppressed = []
    this.previous = []
    this.repeatAt = 0
  }

  sample(pad: ControllerPadState | null, active: boolean, now: number): number[] {
    if (!active || !pad?.connected || pad.mapping !== 'standard') {
      this.reset()
      return []
    }
    const raw = controllerButtons(pad)
    if (this.index !== pad.index) {
      this.reset()
      this.index = pad.index
      this.suppressed = [...raw]
    }
    this.suppressed = raw.map((held, index) => held && !!this.suppressed[index])
    const buttons = raw.map((held, index) => held && !this.suppressed[index])
    const pressed = buttons.flatMap((held, index) => (held && !this.previous[index] ? [index] : []))
    if (directions.some((index) => buttons[index] !== !!this.previous[index])) this.repeatAt = now + 400
    else if (directions.some((index) => buttons[index]) && now >= this.repeatAt) {
      for (const index of directions) if (buttons[index] && !pressed.includes(index)) pressed.push(index)
      // A delayed frame emits one navigation step rather than replaying elapsed intervals.
      this.repeatAt = now + 110
    }
    this.previous = buttons
    return pressed
  }
}
