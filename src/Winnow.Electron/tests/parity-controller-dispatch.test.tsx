// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { useController } from '../src/renderer/controller'

let frames: Map<number, FrameRequestCallback>, nextFrame: number, time: number
let held: number[], axes: number[], focused: boolean
const callbacks = () => ({
  menu: vi.fn(),
  search: vi.fn(),
  switchPage: vi.fn(),
  keyboard: vi.fn(),
  play: vi.fn(),
})
function Harness({
  children,
  surface = 'desktop',
  actions,
}: {
  children: ReactNode
  surface?: 'desktop' | 'fullscreen'
  actions: ReturnType<typeof callbacks>
}) {
  useController({ enabled: true, surface, ...actions })
  return children
}
function frame() {
  act(() => {
    time += 16
    const pending = [...frames.values()]
    frames.clear()
    pending.forEach((callback) => callback(time))
  })
}
function tap(...buttons: number[]) {
  held = []
  frame()
  held = buttons
  frame()
  held = []
  frame()
}

it('releases cursor ownership on surface changes and unmount while mouse movement restores it', () => {
  const actions = callbacks()
  const view = render(
    <Harness actions={actions}>
      <button>First</button>
    </Harness>,
  )
  frame()
  tap(13)
  expect(document.documentElement.dataset.controller).toBe('true')
  act(() => window.dispatchEvent(new Event('pointermove')))
  expect(document.documentElement.dataset.controller).toBeUndefined()
  tap(13)
  view.rerender(
    <Harness actions={actions} surface="fullscreen">
      <button>First</button>
    </Harness>,
  )
  expect(document.documentElement.dataset.controller).toBeUndefined()
  tap(13)
  expect(document.documentElement.dataset.controller).toBe('true')
  view.rerender(
    <Harness actions={actions} surface="desktop">
      <button>First</button>
    </Harness>,
  )
  expect(document.documentElement.dataset.controller).toBeUndefined()
  tap(13)
  view.unmount()
  expect(document.documentElement.dataset.controller).toBeUndefined()
  expect(frames.size).toBe(0)
})
beforeEach(() => {
  frames = new Map()
  nextFrame = 0
  time = 0
  held = []
  axes = [0, 0, 0, 0]
  focused = true
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    frames.set(++nextFrame, callback)
    return nextFrame
  })
  vi.stubGlobal('cancelAnimationFrame', (id: number) => frames.delete(id))
  vi.spyOn(document, 'hasFocus').mockImplementation(() => focused)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const index = [...document.querySelectorAll('button,input,summary')].indexOf(this)
    return {
      left: 0,
      top: index * 80,
      width: 160,
      height: 40,
      right: 160,
      bottom: index * 80 + 40,
      x: 0,
      y: index * 80,
      toJSON() {},
    }
  })
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() })
  Object.defineProperty(navigator, 'getGamepads', {
    configurable: true,
    value: () => [
      {
        index: 0,
        connected: true,
        mapping: 'standard',
        axes,
        buttons: Array.from({ length: 17 }, (_, index) => ({
          pressed: held.includes(index),
          value: held.includes(index) ? 1 : 0,
        })),
      },
    ],
  })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  Reflect.deleteProperty(navigator, 'getGamepads')
})

it('reaches disclosure summaries while skipping closed content and pointer-only steppers', () => {
  render(
    <Harness actions={callbacks()}>
      <button>Start</button>
      <button tabIndex={-1}>Pointer stepper</button>
      <details>
        <summary>Filter groups</summary>
        <button>Hidden choice</button>
      </details>
      <button>End</button>
    </Harness>,
  )
  screen.getByRole('button', { name: 'Start' }).focus()
  tap(13)
  expect(document.activeElement).toBe(screen.getByText('Filter groups'))
  tap(13)
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'End' }))
  tap(12)
  tap(0)
  expect(document.querySelector('details')!.open).toBe(true)
  tap(13)
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Hidden choice' }))
})

it('desktop direction and Accept operate checkboxes inside the active flyout and Back closes only that scope', () => {
  const actions = callbacks(),
    close = vi.fn()
  render(
    <Harness actions={actions}>
      <button>Background</button>
      <div
        role="menu"
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault()
            close()
          }
        }}
      >
        <input aria-label="First" type="checkbox" />
        <input aria-label="Second" type="checkbox" />
      </div>
    </Harness>,
  )
  screen.getByLabelText('First').focus()
  frame()
  tap(13)
  expect(document.activeElement).toBe(screen.getByLabelText('Second'))
  tap(0)
  expect((screen.getByLabelText('Second') as HTMLInputElement).checked).toBe(true)
  tap(1)
  expect(close).toHaveBeenCalledOnce()
  expect(actions.menu).not.toHaveBeenCalled()
})
it('desktop shoulders wrap inside a passive prompt in both directions without activating background actions', () => {
  const actions = callbacks(),
    background = vi.fn()
  const view = render(
    <Harness actions={actions}>
      <button onClick={background}>Background</button>
      <div role="dialog">
        <button>One</button>
        <button>Two</button>
      </div>
    </Harness>,
  )
  screen.getByText('Background').focus()
  frame()
  expect(document.activeElement).toBe(screen.getByText('Background'))
  for (const button of [5, 4])
    for (let step = 0; step < 15; step++) {
      tap(button)
      expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true)
    }
  expect(background).not.toHaveBeenCalled()
  expect(actions.switchPage).not.toHaveBeenCalled()
  view.rerender(
    <Harness actions={actions}>
      <button onClick={background}>Background</button>
    </Harness>,
  )
  screen.getByText('Background').focus()
  tap(0)
  expect(background).toHaveBeenCalledOnce()
})
it.each([5, 13])(
  'desktop movement %s skips disabled controls and Accept raises exactly one click',
  (button) => {
    const actions = callbacks(),
      click = vi.fn()
    render(
      <Harness actions={actions}>
        <button>First</button>
        <button disabled>Disabled</button>
        <button onClick={click}>Last</button>
      </Harness>,
    )
    screen.getByText('First').focus()
    frame()
    tap(button)
    expect(document.activeElement).toBe(screen.getByText('Last'))
    tap(0)
    expect(click).toHaveBeenCalledOnce()
    tap(4)
    expect(document.activeElement).toBe(screen.getByText('First'))
  },
)
it.each(['desktop', 'fullscreen'] as const)(
  '%s right-stick scrolling moves a quarter viewport and keeps focus',
  (surface) => {
    const actions = callbacks(),
      scroll = vi.fn()
    render(
      <Harness actions={actions} surface={surface}>
        <div data-testid="scroll" style={{ overflowY: 'auto' }}>
          <button>Anchor</button>
        </div>
      </Harness>,
    )
    const region = screen.getByTestId('scroll')
    Object.defineProperties(region, {
      clientHeight: { value: 300 },
      scrollHeight: { value: 2000 },
      scrollBy: { value: scroll },
    })
    const anchor = screen.getByText('Anchor')
    anchor.focus()
    frame()
    axes = [0, 0, 0, 1]
    frame()
    expect(scroll).toHaveBeenLastCalledWith({ top: 75, behavior: 'instant' })
    expect(document.activeElement).toBe(anchor)
    axes = [0, 0, 0, 0]
    frame()
    axes = [0, 0, 0, -1]
    frame()
    expect(scroll).toHaveBeenLastCalledWith({ top: -75, behavior: 'instant' })
    expect(document.activeElement).toBe(anchor)
  },
)
it('desktop Accept and Y open the existing text field without submitting or changing its value', () => {
  const actions = callbacks()
  render(
    <Harness actions={actions}>
      <input aria-label="List name" defaultValue="Weekend" />
    </Harness>,
  )
  const input = screen.getByLabelText('List name') as HTMLInputElement
  input.focus()
  frame()
  tap(0)
  expect(actions.keyboard).toHaveBeenLastCalledWith(input)
  tap(3)
  expect(actions.keyboard).toHaveBeenCalledTimes(2)
  expect(input.value).toBe('Weekend')
})
it.each(['desktop', 'fullscreen'] as const)(
  '%s Menu and Back take priority over a simultaneous Accept',
  (surface) => {
    const actions = callbacks(),
      click = vi.fn(),
      back = vi.fn()
    render(
      <Harness actions={actions} surface={surface}>
        <button
          onClick={click}
          onKeyDown={(event) => {
            if (event.key === 'Escape') back()
          }}
        >
          Action
        </button>
      </Harness>,
    )
    screen.getByText('Action').focus()
    frame()
    tap(9, 0)
    expect(actions.menu).toHaveBeenCalledOnce()
    expect(click).not.toHaveBeenCalled()
    tap(1, 0)
    expect(back).toHaveBeenCalledOnce()
    expect(click).not.toHaveBeenCalled()
  },
)
it('fullscreen triggers activate and focus the selected section while desktop triggers leave the page alone', () => {
  const actions = callbacks(),
    select = vi.fn()
  const children = (
    <>
      <button data-controller-tab aria-selected="true">
        Overview
      </button>
      <button data-controller-tab onClick={select}>
        Activity
      </button>
    </>
  )
  const view = render(
    <Harness actions={actions} surface="fullscreen">
      {children}
    </Harness>,
  )
  screen.getByText('Overview').focus()
  frame()
  tap(7)
  expect(select).toHaveBeenCalledOnce()
  expect(document.activeElement).toBe(screen.getByText('Activity'))
  view.rerender(
    <Harness actions={actions} surface="desktop">
      {children}
    </Harness>,
  )
  screen.getByText('Overview').focus()
  tap(7)
  expect(select).toHaveBeenCalledOnce()
  expect(document.activeElement).toBe(screen.getByText('Overview'))
})
it('hidden and closed overlays cannot capture desktop controller activation', () => {
  const actions = callbacks(),
    click = vi.fn()
  render(
    <Harness actions={actions}>
      <button onClick={click}>Visible action</button>
      <div role="dialog" hidden>
        <button>Hidden</button>
      </div>
      <div role="menu" data-state="closed">
        <button>Closed</button>
      </div>
    </Harness>,
  )
  screen.getByText('Visible action').focus()
  frame()
  tap(0)
  expect(click).toHaveBeenCalledOnce()
})

it('a closed Radix trigger remains a visible controller target', () => {
  const actions = callbacks(),
    click = vi.fn()
  render(
    <Harness actions={actions}>
      <button>Start</button>
      <button data-state="closed" aria-haspopup="dialog" onClick={click}>
        Open list
      </button>
    </Harness>,
  )
  screen.getByText('Start').focus()
  frame()
  tap(5)
  expect(document.activeElement).toBe(screen.getByText('Open list'))
  tap(0)
  expect(click).toHaveBeenCalledOnce()
})
