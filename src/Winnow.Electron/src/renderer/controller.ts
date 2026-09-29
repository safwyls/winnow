import { useEffect, useRef } from 'react'

export type Direction = 'left' | 'right' | 'up' | 'down'
type Rect = { left: number; top: number; width: number; height: number }
/** Keep directional movement on the nearest row/column before crossing to another. */
export function spatialTarget(rectangles: Rect[], current: number, direction: Direction): number {
  if (!rectangles.length) return -1
  if (current < 0 || current >= rectangles.length) return 0
  const start = rectangles[current]
  const x = start.left + start.width / 2,
    y = start.top + start.height / 2
  let target = current,
    best = Infinity
  rectangles.forEach((rect, index) => {
    if (index === current) return
    const dx = rect.left + rect.width / 2 - x,
      dy = rect.top + rect.height / 2 - y
    const horizontal = direction === 'left' || direction === 'right'
    const primary = (horizontal ? dx : dy) * (direction === 'left' || direction === 'up' ? -1 : 1)
    if (primary <= 1) return
    const secondary = Math.abs(horizontal ? dy : dx)
    const overlap = horizontal
      ? Math.min(start.top + start.height, rect.top + rect.height) - Math.max(start.top, rect.top)
      : Math.min(start.left + start.width, rect.left + rect.width) - Math.max(start.left, rect.left)
    const score = primary + secondary * 3 + (overlap <= 0 ? 10000 : 0)
    if (score < best) {
      best = score
      target = index
    }
  })
  return target
}

/** Reconnecting or returning from another window requires every held input to release. */
export class ControllerEdges {
  private ready = false
  private identity: number | null = null
  private previous: boolean[] = []
  reset() {
    this.ready = false
    this.identity = null
    this.previous = []
  }
  sample(index: number, buttons: boolean[], axes: number[], active: boolean) {
    if (!active) {
      this.reset()
      return []
    }
    if (this.identity !== index) {
      this.reset()
      this.identity = index
    }
    if (!this.ready) {
      this.ready = buttons.every((value) => !value) && axes.every((value) => Math.abs(value) < 0.35)
      this.previous = buttons
      return []
    }
    const edges = buttons.flatMap((value, button) => (value && !this.previous[button] ? [button] : []))
    this.previous = buttons
    return edges
  }
  get armed() {
    return this.ready
  }
}

export function editable(element: Element | null): element is HTMLInputElement | HTMLTextAreaElement {
  return (
    element instanceof HTMLTextAreaElement ||
    (element instanceof HTMLInputElement &&
      ['text', 'search', 'email', 'password', 'url', 'number', 'tel'].includes(element.type))
  )
}
export function setTextValue(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const proto =
    input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(proto, 'value')?.set?.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new Event('change', { bubbles: true }))
}
export function controllerScope(): ParentNode {
  const dialogs = document.querySelectorAll('[role="dialog"], [role="alertdialog"]')
  return dialogs[dialogs.length - 1] ?? document
}
export function moveControllerFocus(direction: Direction) {
  const active = document.activeElement as HTMLElement | null
  const key = new KeyboardEvent('keydown', {
    key: `Arrow${direction[0].toUpperCase()}${direction.slice(1)}`,
    bubbles: true,
    cancelable: true,
  })
  active?.dispatchEvent(key)
  if (key.defaultPrevented) return
  if (
    (active instanceof HTMLSelectElement ||
      (active instanceof HTMLInputElement && active.type === 'range')) &&
    (direction === 'left' || direction === 'right')
  ) {
    if (active instanceof HTMLSelectElement) {
      active.selectedIndex = Math.max(
        0,
        Math.min(active.options.length - 1, active.selectedIndex + (direction === 'left' ? -1 : 1)),
      )
      active.dispatchEvent(new Event('change', { bubbles: true }))
    } else {
      const value = Number(active.value) + Number(active.step || 1) * (direction === 'left' ? -1 : 1)
      setTextValue(
        active,
        String(Math.max(Number(active.min || 0), Math.min(Number(active.max || 100), value))),
      )
    }
    return
  }
  const candidates = [
    ...controllerScope().querySelectorAll<HTMLElement>(
      'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]',
    ),
  ].filter(
    (element) =>
      !element.closest('[inert],[hidden],[aria-hidden="true"]') &&
      element.getBoundingClientRect().width > 0 &&
      element.getBoundingClientRect().height > 0,
  )
  const selected = spatialTarget(
    candidates.map((element) => element.getBoundingClientRect()),
    candidates.indexOf(active!),
    direction,
  )
  candidates[selected]?.focus({ preventScroll: true })
  candidates[selected]?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
}
interface Actions {
  enabled: boolean
  menu(): void
  search(): void
  switchPage(delta: number): void
  keyboard(input: HTMLInputElement | HTMLTextAreaElement): void
  play(): void
}
export function useController(actions: Actions) {
  const latest = useRef(actions)
  latest.current = actions
  useEffect(() => {
    const edges = new ControllerEdges()
    let frame = 0,
      lastMove = 0
    const mouse = () => delete document.documentElement.dataset.controller
    const reset = () => {
      edges.reset()
      mouse()
    }
    window.addEventListener('blur', reset)
    window.addEventListener('gamepaddisconnected', reset)
    window.addEventListener('pointermove', mouse)
    window.addEventListener('pointerdown', mouse)
    const tick = (time: number) => {
      const action = latest.current
      const pad = Array.from(navigator.getGamepads?.() ?? []).find(Boolean)
      const active = document.visibilityState === 'visible' && document.hasFocus()
      if (!pad) edges.reset()
      else {
        const pressed = pad.buttons.map((button) => button.pressed)
        const clicked = edges.sample(pad.index, pressed, [...pad.axes], active)
        if (active && edges.armed) {
          if (clicked.length || pad.axes.some((axis) => Math.abs(axis) > 0.5))
            document.documentElement.dataset.controller = 'true'
          if (clicked.includes(9)) action.menu()
          if (action.enabled) {
            if (clicked.includes(0)) {
              const focused = document.activeElement
              if (editable(focused) && !focused.readOnly && !focused.disabled) action.keyboard(focused)
              else (focused as HTMLElement)?.click()
            }
            if (clicked.includes(1))
              (document.activeElement ?? document.body).dispatchEvent(
                new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
              )
            if (clicked.includes(2)) {
              const backspace =
                controllerScope().querySelector<HTMLButtonElement>('[data-keyboard-backspace]')
              const pageAction = controllerScope().querySelector<HTMLButtonElement>(
                '[data-controller-play]:not(:disabled)',
              )
              if (backspace) backspace.click()
              else if (pageAction) pageAction.click()
              else action.play()
            }
            if (clicked.includes(3))
              controllerScope().querySelector<HTMLButtonElement>('[data-controller-context]')?.click()
            const modal = controllerScope() !== document
            if (!modal && clicked.includes(8)) action.search()
            if (!modal && clicked.includes(4)) action.switchPage(-1)
            if (!modal && clicked.includes(5)) action.switchPage(1)
            for (const [button, delta] of [
              [6, -1],
              [7, 1],
            ]) {
              if (!clicked.includes(button)) continue
              const enter = controllerScope().querySelector<HTMLButtonElement>('[data-keyboard-enter]')
              if (enter && button === 7) {
                enter.click()
                continue
              }
              const scope = controllerScope()
              const explicit = scope.querySelectorAll<HTMLButtonElement>('[data-controller-tab]')
              const tabs = [
                ...(explicit.length ? explicit : scope.querySelectorAll<HTMLButtonElement>('.tabs button')),
              ].filter((tab) => !tab.disabled && tab.getBoundingClientRect().height > 0)
              const index = tabs.findIndex(
                (tab) =>
                  tab.getAttribute('aria-pressed') === 'true' ||
                  tab.getAttribute('aria-selected') === 'true' ||
                  tab.getAttribute('aria-current') === 'true',
              )
              tabs[(index + delta + tabs.length) % tabs.length]?.click()
            }
            if (time - lastMove > 180) {
              const direction =
                pressed[12] || pad.axes[1] < -0.5
                  ? 'up'
                  : pressed[13] || pad.axes[1] > 0.5
                    ? 'down'
                    : pressed[14] || pad.axes[0] < -0.5
                      ? 'left'
                      : pressed[15] || pad.axes[0] > 0.5
                        ? 'right'
                        : null
              if (direction) {
                moveControllerFocus(direction)
                lastMove = time
              }
            }
            const scroll = pad.axes[3] ?? 0
            if (Math.abs(scroll) > 0.25) {
              let target = document.activeElement as HTMLElement | null
              while (target && target.scrollHeight <= target.clientHeight) target = target.parentElement
              target?.scrollBy({ top: scroll * 18, behavior: 'instant' })
            }
          }
        }
      }
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => {
      cancelAnimationFrame(frame)
      reset()
      window.removeEventListener('blur', reset)
      window.removeEventListener('gamepaddisconnected', reset)
      window.removeEventListener('pointermove', mouse)
      window.removeEventListener('pointerdown', mouse)
    }
  }, [])
}
