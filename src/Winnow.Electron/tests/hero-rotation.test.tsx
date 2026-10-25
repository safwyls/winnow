// @vitest-environment jsdom
import React, { useState } from 'react'
import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useHeroRotation, type HeroRotation } from '../src/renderer/useHeroRotation'

let hasFocus = true
let visibility: DocumentVisibilityState = 'visible'
const observers: FakeIntersectionObserver[] = []
class FakeIntersectionObserver {
  target: Element | undefined
  observe = vi.fn((target: Element) => {
    this.target = target
  })
  disconnect = vi.fn()
  constructor(private callback: IntersectionObserverCallback) {
    observers.push(this)
  }
  emit(intersectionRatio: number) {
    this.callback(
      [
        {
          target: this.target,
          isIntersecting: intersectionRatio > 0,
          intersectionRatio,
        } as IntersectionObserverEntry,
      ],
      this as unknown as IntersectionObserver,
    )
  }
}

beforeEach(() => {
  vi.useFakeTimers()
  hasFocus = true
  visibility = 'visible'
  observers.length = 0
  vi.spyOn(document, 'hasFocus').mockImplementation(() => hasFocus)
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
  vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver)
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

interface HarnessOptions {
  count: number
  enabled: boolean
  reducedMotion: boolean
  forcedIndex?: number
  startIndex?: number
}
function mount(options: Partial<HarnessOptions> = {}) {
  let props: HarnessOptions = { count: 3, enabled: true, reducedMotion: false, ...options }
  let state: HeroRotation
  const onSelect = vi.fn()
  function Harness({ count, enabled, reducedMotion, forcedIndex, startIndex = 0 }: HarnessOptions) {
    const [index, setIndex] = useState(startIndex)
    state = useHeroRotation({
      count,
      index: forcedIndex ?? index,
      enabled,
      reducedMotion,
      onSelect(next) {
        onSelect(next)
        setIndex(next)
      },
    })
    return (
      <>
        <section
          ref={state.ref}
          onMouseEnter={state.onMouseEnter}
          onMouseLeave={state.onMouseLeave}
          onFocusCapture={state.onFocusCapture}
          onBlurCapture={state.onBlurCapture}
          aria-label="Recommendations"
        >
          <button onClick={() => state.select(index + 1)}>Next</button>
          <button onClick={state.togglePaused}>{state.paused ? 'Resume' : 'Pause'}</button>
        </section>
        <button>Outside</button>
      </>
    )
  }
  const view = render(<Harness {...props} />)
  return {
    ...view,
    onSelect,
    get state() {
      return state!
    },
    get observer() {
      return observers.at(-1)!
    },
    get section() {
      return view.getByRole('region', { name: 'Recommendations' })
    },
    update(patch: Partial<HarnessOptions>) {
      props = { ...props, ...patch }
      view.rerender(<Harness {...props} />)
    },
  }
}
function advance(milliseconds = 9_000) {
  act(() => vi.advanceTimersByTime(milliseconds))
}
function visible(view: ReturnType<typeof mount>, ratio = 1) {
  act(() => view.observer.emit(ratio))
}

describe('Discover hero rotation', () => {
  it('starts only when visible and advances once every nine seconds, wrapping the last item', () => {
    const view = mount({ startIndex: 2 })
    expect(view.state.paused).toBe(false)
    advance(90_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    visible(view)
    expect(view.state.rotating).toBe(true)
    advance(8_999)
    expect(view.onSelect).not.toHaveBeenCalled()
    advance(1)
    expect(view.onSelect).toHaveBeenLastCalledWith(0)
    advance()
    expect(view.onSelect).toHaveBeenLastCalledWith(1)
    expect(view.onSelect).toHaveBeenCalledTimes(2)
  })

  it('gives a full reading interval after the pointer leaves', () => {
    const view = mount()
    visible(view)
    advance(8_000)
    fireEvent.mouseEnter(view.section)
    expect(view.state.rotating).toBe(false)
    expect(view.state.paused).toBe(false)
    advance(20_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    fireEvent.mouseLeave(view.section)
    advance(8_999)
    expect(view.onSelect).not.toHaveBeenCalled()
    advance(1)
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('stays still while focus moves between carousel controls and resumes after focus leaves', () => {
    const view = mount()
    visible(view)
    act(() => view.getByRole('button', { name: 'Next' }).focus())
    advance(20_000)
    act(() => view.getByRole('button', { name: 'Pause' }).focus())
    advance(20_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    expect(view.state.rotating).toBe(false)
    act(() => view.getByRole('button', { name: 'Outside' }).focus())
    advance()
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('does not rotate in another window or while the document is hidden', () => {
    const view = mount()
    visible(view)
    advance(3_000)
    act(() => {
      hasFocus = false
      window.dispatchEvent(new Event('blur'))
    })
    advance(30_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    act(() => {
      hasFocus = true
      window.dispatchEvent(new Event('focus'))
    })
    advance(3_000)
    act(() => {
      visibility = 'hidden'
      document.dispatchEvent(new Event('visibilitychange'))
    })
    advance(30_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    act(() => {
      visibility = 'visible'
      document.dispatchEvent(new Event('visibilitychange'))
    })
    advance(8_999)
    expect(view.onSelect).not.toHaveBeenCalled()
    advance(1)
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('pauses an offscreen hero and starts a new interval when at least half is visible again', () => {
    const view = mount()
    visible(view)
    advance(8_000)
    visible(view, 0.49)
    advance(20_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    visible(view, 0.5)
    advance()
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('respects reduced motion even after Resume, but permits manual selection', () => {
    const view = mount({ reducedMotion: true })
    visible(view)
    act(() => {
      view.state.togglePaused()
    })
    act(() => {
      view.state.togglePaused()
    })
    expect(view.state.paused).toBe(false)
    expect(view.state.rotating).toBe(false)
    advance(90_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    act(() => view.state.select(2))
    expect(view.onSelect).toHaveBeenLastCalledWith(2)
    expect(view.state.paused).toBe(true)
    view.update({ reducedMotion: false })
    advance(90_000)
    expect(view.onSelect).toHaveBeenCalledOnce()
    act(() => view.state.togglePaused())
    advance()
    expect(view.onSelect).toHaveBeenLastCalledWith(0)
  })

  it('manual navigation wraps safely and holds its selection until explicitly resumed', () => {
    const view = mount()
    visible(view)
    act(() => view.state.select(-1))
    expect(view.onSelect).toHaveBeenLastCalledWith(2)
    expect(view.state.paused).toBe(true)
    advance(90_000)
    expect(view.onSelect).toHaveBeenCalledOnce()
    act(() => view.state.togglePaused())
    advance()
    expect(view.onSelect).toHaveBeenLastCalledWith(0)
  })

  it('stops for pending feedback without losing the explicit pause preference', () => {
    const view = mount()
    visible(view)
    advance(8_000)
    view.update({ enabled: false })
    advance(30_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    act(() => view.state.pause())
    view.update({ enabled: true })
    advance(30_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    act(() => view.state.togglePaused())
    advance()
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('uses the current count after a feed change and never selects an empty feed', () => {
    const view = mount({ startIndex: 2 })
    visible(view)
    advance(8_000)
    view.update({ count: 2 })
    advance(8_999)
    expect(view.onSelect).not.toHaveBeenCalled()
    advance(1)
    expect(view.onSelect).toHaveBeenLastCalledWith(0)
    view.update({ count: 1 })
    advance(30_000)
    expect(view.onSelect).toHaveBeenCalledOnce()
    view.update({ count: 0 })
    act(() => view.state.select(1))
    advance(30_000)
    expect(view.onSelect).toHaveBeenCalledOnce()
  })

  it('cancels its timer and observer on unmount and ignores late intersection notifications', () => {
    const view = mount()
    visible(view)
    const observer = view.observer
    expect(vi.getTimerCount()).toBe(1)
    view.unmount()
    expect(observer.disconnect).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
    act(() => {
      observer.emit(1)
      window.dispatchEvent(new Event('focus'))
      document.dispatchEvent(new Event('visibilitychange'))
    })
    advance(30_000)
    expect(view.onSelect).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })
})
