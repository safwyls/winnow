import { useEffect, useRef } from 'react'
import { ControllerInput, ControllerButton as B, type ControllerPadState } from '../shared/controller-input'
import { selectedStandardController } from '../shared/controller-status'

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
function available(element: HTMLElement) {
  // Closed disclosure content still has DOM nodes, but only its summary can receive input.
  for (let parent = element.parentElement; parent; parent = parent.parentElement) {
    if (
      parent instanceof HTMLDetailsElement &&
      !parent.open &&
      !parent.querySelector(':scope > summary')?.contains(element)
    )
      return false
  }
  return (
    !element.closest(
      '[inert],[hidden],[aria-hidden="true"],[role="dialog"][data-state="closed"],[role="alertdialog"][data-state="closed"],[role="menu"][data-state="closed"],[data-controller-scope][data-state="closed"]',
    ) &&
    getComputedStyle(element).display !== 'none' &&
    getComputedStyle(element).visibility !== 'hidden'
  )
}
function controls(scope: ParentNode) {
  return [
    ...scope.querySelectorAll<HTMLElement>(
      'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], summary, [tabindex="0"]',
    ),
  ].filter(
    (element) =>
      available(element) &&
      !element.matches(':disabled') &&
      element.tabIndex >= 0 &&
      element.getBoundingClientRect().width > 0 &&
      element.getBoundingClientRect().height > 0,
  )
}
export function controllerScope(): ParentNode {
  const overlays = [
    ...document.querySelectorAll<HTMLElement>(
      '[role="dialog"], [role="alertdialog"], [role="menu"], [data-controller-scope]',
    ),
  ].filter(available)
  return overlays.at(-1) ?? document
}
export function controllerActivationTarget(): HTMLElement | null {
  const scope = controllerScope()
  const focused = document.activeElement as HTMLElement | null
  if (
    focused &&
    focused !== document.body &&
    available(focused) &&
    !focused.matches(':disabled') &&
    (scope === document || (scope as HTMLElement).contains(focused))
  )
    return focused
  // Passive prompts keep arrival focus; the first controller action enters their scope.
  return (
    [...scope.querySelectorAll<HTMLElement>('[data-controller-initial]:not(:disabled)')].find(available) ??
    controls(scope)[0] ??
    null
  )
}
export function moveControllerTab(previous: boolean) {
  const active = document.activeElement as HTMLElement | null
  const event = new KeyboardEvent('keydown', {
    key: 'Tab',
    shiftKey: previous,
    bubbles: true,
    cancelable: true,
  })
  active?.dispatchEvent(event)
  if (event.defaultPrevented) return
  const candidates = controls(controllerScope()).filter((element) => element.tabIndex >= 0)
  const index = candidates.indexOf(active!)
  const next =
    index < 0
      ? previous
        ? candidates.length - 1
        : 0
      : (index + (previous ? -1 : 1) + candidates.length) % candidates.length
  candidates[next]?.focus({ preventScroll: true })
  candidates[next]?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
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
  const candidates = controls(controllerScope())
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
  surface?: 'desktop' | 'fullscreen'
  menu(): void
  search(): void
  switchPage(delta: number): void
  keyboard(input: HTMLInputElement | HTMLTextAreaElement): void
  play(): void
}
export function scrollControllerRegion(direction: -1 | 1) {
  const scope = controllerScope()
  const scrollable = (element: HTMLElement) =>
    /^(auto|scroll|overlay)$/.test(getComputedStyle(element).overflowY) &&
    element.scrollHeight > element.clientHeight &&
    element.clientHeight > 0 &&
    !element.closest('[hidden],[inert],[aria-hidden="true"]')
  const active = document.activeElement as HTMLElement | null
  let target = scope === document || (scope as HTMLElement).contains(active) ? active : null
  while (target && !scrollable(target)) target = target === scope ? null : target.parentElement
  if (!target)
    target =
      [...scope.querySelectorAll<HTMLElement>('*')]
        .filter(scrollable)
        .sort(
          (left, right) => right.clientWidth * right.clientHeight - left.clientWidth * left.clientHeight,
        )[0] ?? null
  if (target) {
    const step = Number(target.dataset.controllerScrollStep)
    target.scrollBy({
      top: direction * (step > 0 && Number.isFinite(step) ? step : target.clientHeight * 0.25),
      behavior: 'instant',
    })
  }
}
export function useController(actions: Actions) {
  const latest = useRef(actions)
  latest.current = actions
  useEffect(() => {
    delete document.documentElement.dataset.controller
  }, [actions.surface])
  useEffect(() => {
    const input = new ControllerInput()
    let frame = 0
    const mouse = () => delete document.documentElement.dataset.controller
    const reset = () => {
      input.reset()
      mouse()
    }
    window.addEventListener('blur', reset)
    window.addEventListener('gamepaddisconnected', reset)
    window.addEventListener('pointermove', mouse)
    window.addEventListener('pointerdown', mouse)
    const tick = (time: number) => {
      const action = latest.current
      const pad = selectedStandardController(Array.from(navigator.getGamepads?.() ?? []))
      const active = document.visibilityState === 'visible' && document.hasFocus()
      const clicked = input.sample(pad as ControllerPadState | null, active, time)
      const dispatch = () => {
        if (!clicked.length) return
        document.documentElement.dataset.controller = 'true'
        if (clicked.includes(B.Menu)) {
          action.menu()
          return
        }
        if (!action.enabled) return
        const focused = controllerActivationTarget()
        if (focused && focused !== document.activeElement) focused.focus({ preventScroll: true })
        if (clicked.includes(B.Back)) {
          ;(document.activeElement ?? document.body).dispatchEvent(
            new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
          )
          return
        }
        if (clicked.includes(B.Context) || (clicked.includes(B.Accept) && editable(focused))) {
          if (editable(focused) && !focused.readOnly && !focused.disabled) action.keyboard(focused)
          else if (action.surface !== 'desktop')
            controllerScope()
              .querySelector<HTMLButtonElement>('[data-controller-context]:not(:disabled)')
              ?.click()
          return
        }
        if (clicked.includes(B.Previous) || clicked.includes(B.Next)) {
          if (action.surface === 'desktop') moveControllerTab(clicked.includes(B.Previous))
          else if (controllerScope() === document) action.switchPage(clicked.includes(B.Previous) ? -1 : 1)
          return
        }
        if (clicked.includes(B.Accept)) {
          focused?.click()
          return
        }
        const scope = controllerScope()
        // Keyboard editing shortcuts apply on both presentation surfaces.
        if (clicked.includes(B.Play)) {
          const backspace = scope.querySelector<HTMLButtonElement>('[data-keyboard-backspace]')
          const pageAction = scope.querySelector<HTMLButtonElement>('[data-controller-play]:not(:disabled)')
          if (backspace) backspace.click()
          else if (action.surface !== 'desktop') {
            if (pageAction) pageAction.click()
            else action.play()
          }
          return
        }
        const enter = scope.querySelector<HTMLButtonElement>('[data-keyboard-enter]')
        if (enter && clicked.includes(B.PageNext)) {
          enter.click()
          return
        }
        if (action.surface !== 'desktop') {
          if (scope === document && clicked.includes(B.Search)) {
            action.search()
            return
          }
          for (const [button, delta] of [
            [B.PagePrevious, -1],
            [B.PageNext, 1],
          ]) {
            if (!clicked.includes(button)) continue
            const pager = scope.querySelector<HTMLElement>('[data-controller-page]')
            if (pager) {
              pager.dispatchEvent(
                new KeyboardEvent('keydown', {
                  key: delta < 0 ? 'PageUp' : 'PageDown',
                  bubbles: true,
                  cancelable: true,
                }),
              )
              return
            }
            const explicit = scope.querySelectorAll<HTMLButtonElement>('[data-controller-tab]')
            const tabs = [
              ...(explicit.length ? explicit : scope.querySelectorAll<HTMLButtonElement>('.tabs button')),
            ].filter((tab) => !tab.disabled && available(tab) && tab.getBoundingClientRect().height > 0)
            const index = tabs.findIndex((tab) =>
              ['aria-pressed', 'aria-selected', 'aria-current'].some(
                (attribute) => tab.getAttribute(attribute) === 'true',
              ),
            )
            const target = tabs[(index + delta + tabs.length) % tabs.length]
            target?.focus({ preventScroll: true })
            target?.click()
            return
          }
        }
        if (clicked.includes(B.ScrollUp)) {
          scrollControllerRegion(-1)
          return
        }
        if (clicked.includes(B.ScrollDown)) {
          scrollControllerRegion(1)
          return
        }
        const direction = clicked.includes(B.Up)
          ? 'up'
          : clicked.includes(B.Down)
            ? 'down'
            : clicked.includes(B.Left)
              ? 'left'
              : clicked.includes(B.Right)
                ? 'right'
                : null
        if (direction) moveControllerFocus(direction)
      }
      if (pad && active) dispatch()
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
