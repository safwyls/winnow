// @vitest-environment jsdom
import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PortalSurface } from '../src/renderer/components/portal-effects'
import type { PortalRenderer } from '../src/renderer/components/portal-effects/renderer'

const factory = vi.hoisted(() => vi.fn())
vi.mock('../src/renderer/components/portal-effects/renderer', () => ({ createPortalRenderer: factory }))

function runtime() {
  const canvas = document.createElement('canvas')
  return {
    attach: vi.fn<PortalRenderer['attach']>((element) => element.append(canvas)),
    resize: vi.fn<PortalRenderer['resize']>(),
    draw: vi.fn<PortalRenderer['draw']>(),
    destroy: vi.fn(() => canvas.remove()),
  }
}
async function settle() {
  await act(async () => {
    await Promise.resolve()
    await Promise.resolve()
    await Promise.resolve()
  })
}
let renderer: ReturnType<typeof runtime>
let intersect: (entries: { isIntersecting: boolean }[]) => void
const intersectionDisconnect = vi.fn()
const resizeDisconnect = vi.fn()

beforeEach(() => {
  vi.useFakeTimers()
  renderer = runtime()
  factory.mockReset().mockResolvedValue(renderer)
  intersectionDisconnect.mockReset()
  resizeDisconnect.mockReset()
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(400)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(540)
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect = resizeDisconnect
    },
  )
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      constructor(callback: typeof intersect) {
        intersect = callback
      }
      observe() {}
      disconnect = intersectionDisconnect
    },
  )
})
afterEach(() => {
  cleanup()
  document.documentElement.removeAttribute('style')
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('reusable portals', () => {
  it('updates artwork and reading content without recreating the GPU renderer', async () => {
    const view = render(<PortalSurface artwork={<img src="first-cover" alt="" />}>First game</PortalSurface>)
    await settle()
    act(() => vi.advanceTimersByTime(500))
    const canvas = view.container.querySelector('canvas')
    view.rerender(<PortalSurface artwork={<img src="second-cover" alt="" />}>Second game</PortalSurface>)
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    expect(renderer.destroy).not.toHaveBeenCalled()
    expect(view.container.querySelector('canvas')).toBe(canvas)
    expect(view.container.querySelector('img')?.getAttribute('src')).toBe('second-cover')
    expect(view.container.querySelector('.winnow-portal-content')?.textContent).toBe('Second game')
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-surface')?.dataset.portalOpening).toBe(
      'false',
    )
  })

  it('reveals fixed content, clears every expansion mask and releases GPU work exactly once', async () => {
    const expanded = vi.fn()
    const view = render(
      <PortalSurface
        artwork={<img src="data:image/png,cached" alt="" />}
        expansion={{ x: 40, y: 30, width: 160, height: 240 }}
        onExpanded={expanded}
      >
        <button>View game</button>
      </PortalSurface>,
    )
    await settle()
    const plane = view.container.querySelector<HTMLElement>('.winnow-portal-content')!
    const reveal = view.container.querySelector<HTMLElement>('.winnow-portal-reveal')!
    const root = view.container.querySelector<HTMLElement>('.winnow-portal-surface')!
    act(() => vi.advanceTimersByTime(160))
    expect(reveal.style.clipPath).toContain('polygon(')
    expect(plane.style.clipPath).toBe('none')
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-art')!.style.clipPath).toBe('none')
    expect(reveal.contains(plane)).toBe(true)
    expect(reveal.contains(view.container.querySelector('.winnow-portal-art'))).toBe(true)
    expect(root.dataset.portalOpening).toBe('true')
    expect(plane.style.width).toBe('')
    expect(plane.style.height).toBe('')
    expect(plane.style.transform).toBe('')
    expect(plane.inert).toBe(true)
    act(() => vi.advanceTimersByTime(600))
    expect(plane.inert).toBe(false)
    expect(plane.style.clipPath).toBe('none')
    expect(reveal.style.clipPath).toBe('none')
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-art')!.style.clipPath).toBe('none')
    expect(root.dataset.portalExpanded).toBe('true')
    expect(root.dataset.portalRunning).toBe('false')
    expect(expanded).toHaveBeenCalledTimes(1)
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
    const renders = renderer.draw.mock.calls.length
    act(() => vi.advanceTimersByTime(10000))
    expect(renderer.draw).toHaveBeenCalledTimes(renders)
    expect(view.container.querySelector('canvas')).toBeNull()
    view.unmount()
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
    expect(intersectionDisconnect).toHaveBeenCalledTimes(1)
    expect(resizeDisconnect).toHaveBeenCalledTimes(1)
  })

  it.each(['resize', 'blur', 'deactivate', 'unmount', 'reduced-motion'] as const)(
    'releases the shared mask and temporary layer hint on %s during expansion',
    async (action) => {
      const expanded = vi.fn()
      const properties = { expansion: { x: 40, y: 30, width: 160, height: 240 }, onExpanded: expanded }
      const view = render(
        <PortalSurface {...properties}>
          <button>Back</button>
        </PortalSurface>,
      )
      await settle()
      act(() => vi.advanceTimersByTime(80))
      const root = view.container.querySelector<HTMLElement>('.winnow-portal-surface')!
      const reveal = view.container.querySelector<HTMLElement>('.winnow-portal-reveal')!
      const plane = view.container.querySelector<HTMLElement>('.winnow-portal-content')!
      expect(root.dataset.portalOpening).toBe('true')
      act(() => {
        if (action === 'resize') {
          vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(700)
          fireEvent.resize(window)
        }
        if (action === 'blur') fireEvent.blur(window)
        if (action === 'deactivate')
          view.rerender(
            <PortalSurface {...properties} active={false}>
              Back
            </PortalSurface>,
          )
        if (action === 'unmount') view.unmount()
        if (action === 'reduced-motion')
          view.rerender(
            <PortalSurface {...properties} reducedMotion>
              Back
            </PortalSurface>,
          )
      })
      expect(root.dataset.portalOpening).toBe('false')
      expect(root.dataset.portalRunning).toBe('false')
      expect(reveal.style.clipPath).toBe('none')
      expect(plane.inert).toBe(false)
      expect(renderer.destroy).toHaveBeenCalledTimes(1)
      expect(expanded).toHaveBeenCalledTimes(action === 'unmount' || action === 'deactivate' ? 0 : 1)
      const renders = renderer.draw.mock.calls.length
      act(() => vi.advanceTimersByTime(1000))
      expect(renderer.draw).toHaveBeenCalledTimes(renders)
    },
  )

  it('releases the large renderer once its rim has left the pane, before the overscan animation ends', async () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(3440)
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(1440)
    const expanded = vi.fn()
    const view = render(
      <PortalSurface expansion={{ x: 50, y: 220, width: 400, height: 540 }} onExpanded={expanded}>
        Details
      </PortalSurface>,
    )
    await settle()
    act(() => vi.advanceTimersByTime(550))
    expect(expanded).toHaveBeenCalledTimes(1)
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-surface')!.dataset.portalRunning).toBe(
      'false',
    )
  })

  it('stops ambient frames at zero and resumes changed options without rebuilding the renderer', async () => {
    const view = render(
      <PortalSurface options={{ activity: 0 }}>
        <p>Readable content</p>
      </PortalSurface>,
    )
    await settle()
    act(() => vi.advanceTimersByTime(500))
    const paused = renderer.draw.mock.calls.length
    act(() => vi.advanceTimersByTime(1000))
    expect(renderer.draw).toHaveBeenCalledTimes(paused)
    view.rerender(
      <PortalSurface options={{ activity: 100 }}>
        <p>Readable content</p>
      </PortalSurface>,
    )
    act(() => vi.advanceTimersByTime(1000))
    expect(renderer.draw.mock.calls.length - paused).toBeLessThanOrEqual(32)
    expect(renderer.draw.mock.calls.length).toBeGreaterThan(paused + 20)
    expect(factory).toHaveBeenCalledTimes(1)
    view.rerender(
      <PortalSurface options={{ activity: 0 }}>
        <p>Readable content</p>
      </PortalSurface>,
    )
    const stopped = renderer.draw.mock.calls.length
    act(() => vi.advanceTimersByTime(1000))
    expect(renderer.draw).toHaveBeenCalledTimes(stopped)
  })

  it('recolors an idle portal when accent variables change without restarting entrance or motion', async () => {
    const view = render(
      <PortalSurface options={{ activity: 0 }}>
        <p>Preview</p>
      </PortalSurface>,
    )
    await settle()
    act(() => vi.advanceTimersByTime(500))
    const previous = renderer.draw.mock.calls.at(-1)![0]
    const count = renderer.draw.mock.calls.length
    document.documentElement.style.setProperty('--portal-rim-a', '1 .25 .5')
    document.documentElement.style.setProperty('--portal-rim-b', '.5 .75 1')
    await settle()
    expect(renderer.draw).toHaveBeenCalledTimes(count + 1)
    const current = renderer.draw.mock.calls.at(-1)!
    expect(current[0]).toEqual(previous)
    expect(Array.from(current[1])).toEqual([1, 0.25, 0.5])
    expect(Array.from(current[2])).toEqual([0.5, 0.75, 1])
    expect(factory).toHaveBeenCalledTimes(1)
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-surface')!.dataset.portalOpening).toBe(
      'false',
    )
    document.documentElement.style.setProperty('--unrelated-preference', '99')
    await settle()
    expect(renderer.draw).toHaveBeenCalledTimes(count + 1)
  })

  it.each(['blur', 'offscreen', 'hidden'] as const)(
    'pauses %s portals and resumes only when visible and focused',
    async (mode) => {
      const view = render(
        <PortalSurface>
          <p>Readable content</p>
        </PortalSurface>,
      )
      await settle()
      act(() => vi.advanceTimersByTime(500))
      act(() => {
        if (mode === 'blur') fireEvent.blur(window)
        if (mode === 'offscreen') intersect([{ isIntersecting: false }])
        if (mode === 'hidden') {
          vi.spyOn(document, 'hidden', 'get').mockReturnValue(true)
          fireEvent(document, new Event('visibilitychange'))
        }
      })
      const renders = renderer.draw.mock.calls.length
      act(() => vi.advanceTimersByTime(1000))
      expect(renderer.draw).toHaveBeenCalledTimes(renders)
      act(() => {
        if (mode === 'blur') fireEvent.focus(window)
        if (mode === 'offscreen') intersect([{ isIntersecting: true }])
        if (mode === 'hidden') {
          vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
          fireEvent(document, new Event('visibilitychange'))
        }
        vi.advanceTimersByTime(100)
      })
      expect(renderer.draw.mock.calls.length).toBeGreaterThan(renders)
      view.unmount()
      expect(renderer.destroy).toHaveBeenCalledTimes(1)
    },
  )

  it('shows reduced-motion details immediately without allocating a renderer', async () => {
    const expanded = vi.fn()
    const view = render(
      <PortalSurface
        reducedMotion
        expansion={{ x: 20, y: 20, width: 100, height: 150 }}
        onExpanded={expanded}
      >
        <button>Back</button>
      </PortalSurface>,
    )
    await settle()
    expect(expanded).toHaveBeenCalledTimes(1)
    expect(factory).not.toHaveBeenCalled()
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-content')!.inert).toBe(false)
  })

  it('keeps cached artwork and readable content when WebGL fails, with no ambient fallback loop', async () => {
    factory.mockRejectedValueOnce(new Error('No WebGL'))
    const view = render(
      <PortalSurface artwork={<img src="data:image/png,cached" alt="" />}>
        <p>Game description</p>
      </PortalSurface>,
    )
    await settle()
    act(() => vi.advanceTimersByTime(500))
    expect(view.container.querySelector('img')!.getAttribute('src')).toBe('data:image/png,cached')
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-content')!.style.clipPath).toBe('none')
    expect(view.container.querySelector<HTMLElement>('.winnow-portal-surface')!.dataset.portalRunning).toBe(
      'false',
    )
    expect(view.getByText('Game description')).toBeTruthy()
  })

  it('destroys a renderer that finishes initialization after unmount without attaching it', async () => {
    let resolve!: (renderer: PortalRenderer) => void
    factory.mockReturnValueOnce(
      new Promise<PortalRenderer>((done) => {
        resolve = done
      }),
    )
    const view = render(
      <PortalSurface>
        <p>Game description</p>
      </PortalSurface>,
    )
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    view.unmount()
    await act(async () => {
      resolve(renderer)
    })
    expect(renderer.attach).not.toHaveBeenCalled()
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
  })

  it('keeps an inactive portal dormant until activated, then disposes on deactivation', async () => {
    const view = render(
      <PortalSurface active={false}>
        <p>Preview</p>
      </PortalSurface>,
    )
    await settle()
    expect(factory).not.toHaveBeenCalled()
    view.rerender(
      <PortalSurface active>
        <p>Preview</p>
      </PortalSurface>,
    )
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    view.rerender(
      <PortalSurface active={false}>
        <p>Preview</p>
      </PortalSurface>,
    )
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
  })
})
