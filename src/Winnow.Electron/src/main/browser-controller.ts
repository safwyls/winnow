export interface BrowserPad {
  index: number
  buttons: boolean[]
  axes: number[]
}
export interface BrowserKey {
  key: string
  shift: boolean
}
/** Browser reading scrolls; account forms use up/down to move between fields. */
export function browserControllerKey(button: number, reading: boolean): BrowserKey | null {
  const keys: Record<number, BrowserKey> = {
    0: { key: 'Enter', shift: false },
    2: { key: 'Space', shift: false },
    4: { key: 'Tab', shift: true },
    5: { key: 'Tab', shift: false },
    6: { key: 'PageUp', shift: false },
    7: { key: 'PageDown', shift: false },
    12: { key: reading ? 'Up' : 'Tab', shift: !reading },
    13: { key: reading ? 'Down' : 'Tab', shift: false },
    14: { key: 'Left', shift: false },
    15: { key: 'Right', shift: false },
  }
  if (!reading && button === 8) return { key: 'Backspace', shift: false }
  return keys[button] ?? null
}
export function readBrowserPad(value: unknown): BrowserPad | null {
  if (!value || typeof value !== 'object') return null
  const pad = value as BrowserPad
  if (
    !Number.isSafeInteger(pad.index) ||
    pad.index < 0 ||
    pad.index > 16 ||
    !Array.isArray(pad.buttons) ||
    pad.buttons.length > 18 ||
    !pad.buttons.every((value) => typeof value === 'boolean') ||
    !Array.isArray(pad.axes) ||
    pad.axes.length > 4 ||
    !pad.axes.every((value) => typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= 1)
  )
    return null
  return pad
}
export class BrowserController {
  private index = -1
  private armed = false
  private previous: boolean[] = []
  private held = -1
  private repeatAt = 0
  reset() {
    this.index = -1
    this.armed = false
    this.previous = []
    this.held = -1
  }
  sample(pad: BrowserPad | null, active: boolean, now: number): number[] {
    if (!active || !pad) {
      this.reset()
      return []
    }
    if (pad.index !== this.index) {
      this.reset()
      this.index = pad.index
    }
    const buttons = [...pad.buttons]
    if (Math.abs(pad.axes[0] ?? 0) > 0.6) buttons[(pad.axes[0] ?? 0) < 0 ? 14 : 15] = true
    if (Math.abs(pad.axes[1] ?? 0) > 0.6) buttons[(pad.axes[1] ?? 0) < 0 ? 12 : 13] = true
    if (!this.armed) {
      this.armed = buttons.every((value) => !value) && pad.axes.every((value) => Math.abs(value) < 0.35)
      this.previous = buttons
      return []
    }
    const edges = buttons.flatMap((pressed, index) => (pressed && !this.previous[index] ? [index] : []))
    const held = [12, 13, 14, 15, 6, 7].find((button) => buttons[button]) ?? -1
    if (held !== this.held) {
      this.held = held
      this.repeatAt = now + 400
    } else if (held >= 0 && now >= this.repeatAt) {
      if (!edges.includes(held)) edges.push(held)
      this.repeatAt = now + 100
    }
    this.previous = buttons
    return edges
  }
}
// Read only hardware state from the trusted application document, never from the website DOM.
export const browserPadScript = `(()=>{const pad=Array.from(navigator.getGamepads?.()??[]).find(p=>p&&p.connected&&p.mapping==='standard');return pad?{index:pad.index,buttons:Array.from(pad.buttons).slice(0,18).map(b=>b.pressed||b.value>.5),axes:Array.from(pad.axes).slice(0,4)}:null})()`
