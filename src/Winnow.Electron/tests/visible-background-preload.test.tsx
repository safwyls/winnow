// @vitest-environment jsdom
import { useRef } from 'react'
import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { useVisibleBackgroundPreload } from '../src/renderer/components/useVisibleBackgroundPreload'
import { preloadBackground } from '../src/renderer/components/background-preload'

vi.mock('../src/renderer/components/background-preload', () => ({ preloadBackground: vi.fn(async () => {}) }))
vi.mock('../src/renderer/components/artwork-images', () => ({ artworkImages: () => ({ pendingCount: 0 }) }))
let callback: IntersectionObserverCallback
let observer: IntersectionObserver
const clients: QueryClient[] = []
const games = [{ workId: 1 }, { workId: 2, headerWorkId: 12 }, { workId: 3 }]
function Harness({ hidden = false, ids = [1, 2, 3], composition = 'avalon', fullscreen = false }) {
  const root = useRef<HTMLDivElement>(null)
  useVisibleBackgroundPreload(root, games, composition, fullscreen)
  return (
    <section inert={hidden}>
      <input aria-label="Filter" />
      <div ref={root}>
        {ids.map((id) => (
          <button key={id} data-work-id={id}>
            Game {id}
          </button>
        ))}
      </div>
    </section>
  )
}
function intersect(ids: number[]) {
  act(() =>
    callback(
      [...document.querySelectorAll('[data-work-id]')].map(
        (target) =>
          ({
            target,
            isIntersecting: ids.includes(Number((target as HTMLElement).dataset.workId)),
            intersectionRatio: ids.includes(Number((target as HTMLElement).dataset.workId)) ? 1 : 0,
          }) as IntersectionObserverEntry,
      ),
      observer,
    ),
  )
}
const advance = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms))
beforeEach(() => {
  vi.useFakeTimers()
  vi.mocked(preloadBackground).mockClear()
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      constructor(cb: IntersectionObserverCallback) {
        callback = cb
        observer = this as unknown as IntersectionObserver
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.unstubAllGlobals()
  vi.useRealTimers()
})
function fixture(props: Parameters<typeof Harness>[0] = {}) {
  const client = new QueryClient()
  clients.push(client)
  return render(<Harness {...props} />, {
    wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
  })
}

it.each(['avalon', 'afterglow', 'rift', 'catalogue'])(
  'warms only clipped visible %s games with canonical and header IDs',
  async (composition) => {
    fixture({ composition })
    intersect([2])
    await advance(400)
    expect(preloadBackground).toHaveBeenCalledTimes(1)
    expect(preloadBackground).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        workId: 2,
        coverWorkId: 12,
        compositionId: composition,
        fullscreen: false,
      }),
      expect.any(AbortSignal),
    )
  },
)

it('debounces toolbar filtering and scroll events, then samples the replaced fullscreen rows', async () => {
  const view = fixture({ fullscreen: true })
  intersect([1, 2])
  await advance(300)
  fireEvent.input(view.getByLabelText('Filter'))
  await advance(300)
  fireEvent.scroll(view.container.querySelector('div')!)
  view.rerender(<Harness ids={[3]} fullscreen />)
  await advance(0)
  intersect([3])
  await advance(399)
  expect(preloadBackground).not.toHaveBeenCalled()
  await advance(1)
  expect(preloadBackground).toHaveBeenCalledTimes(1)
  expect(preloadBackground).toHaveBeenCalledWith(
    expect.anything(),
    expect.objectContaining({ workId: 3, fullscreen: true }),
    expect.any(AbortSignal),
  )
})

it('does not warm hidden retained panes and cancels active warming when hidden or unmounted', async () => {
  const view = fixture({ hidden: true })
  intersect([1])
  await advance(400)
  expect(preloadBackground).not.toHaveBeenCalled()
  vi.mocked(preloadBackground).mockImplementationOnce(() => new Promise(() => {}))
  view.rerender(<Harness />)
  await advance(401)
  const signal = vi.mocked(preloadBackground).mock.calls[0][2]
  expect(signal.aborted).toBe(false)
  view.rerender(<Harness hidden />)
  await advance(0)
  expect(signal.aborted).toBe(true)
  view.unmount()
  await advance(500)
  expect(preloadBackground).toHaveBeenCalledTimes(1)
})
