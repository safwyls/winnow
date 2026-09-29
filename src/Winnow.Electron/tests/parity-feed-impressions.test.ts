// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { FeedImpressions } from '../src/renderer/components/feed-impressions'
import { createElement } from 'react'
import { cleanup, render } from '@testing-library/react'
import { Impression } from '../src/renderer/components/primitives'

class Intersections {
  static all: Intersections[] = []
  observe = vi.fn()
  unobserve = vi.fn()
  disconnect = vi.fn()
  constructor(private callback: IntersectionObserverCallback) {
    Intersections.all.push(this)
  }
  expose(target: HTMLElement, fraction = 1, rectangle = new DOMRect(0, 0, 100, 150)) {
    this.callback(
      [
        {
          target,
          isIntersecting: fraction > 0,
          intersectionRatio: fraction,
          intersectionRect: rectangle,
          boundingClientRect: rectangle,
          rootBounds: null,
          time: performance.now(),
        },
      ],
      this as unknown as IntersectionObserver,
    )
  }
}
const removals: (() => void)[] = []
function setup(write = vi.fn(async (_releaseId: number, _shelfId: string) => {})) {
  const observations = new FeedImpressions(document, write)
  const card = (id: number, shelf = 'recommended') => {
    const element = document.createElement('div')
    element.innerHTML = `<button>Game ${id}</button>`
    document.body.append(element)
    const stop = observations.observe(element, id, shelf)
    removals.push(stop)
    return { element, stop }
  }
  return { observations, write, card }
}
async function frame() {
  await Promise.resolve()
  await vi.advanceTimersByTimeAsync(20)
}
beforeEach(() => {
  vi.useFakeTimers({ now: new Date('2026-09-29T12:00:00Z') })
  Intersections.all = []
  vi.stubGlobal('IntersectionObserver', Intersections)
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
})
afterEach(() => {
  cleanup()
  for (const stop of removals.splice(0)) stop()
  document.body.replaceChildren()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

it('registers the shared impression component only while a shelf supports feedback', () => {
  const request = vi.fn(async () => ({ ok: true, status: 200 }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const tree = (enabled: boolean) =>
    createElement(Impression, {
      releaseId: 321,
      shelfId: 'plugin:unscored',
      enabled,
      children: createElement('button', {}, 'A game'),
    })
  const view = render(tree(false))
  expect(Intersections.all).toHaveLength(0)
  view.rerender(tree(true))
  const observer = Intersections.all[0],
    card = view.container.firstElementChild as HTMLElement
  observer.expose(card)
  expect(request).toHaveBeenCalledWith({
    route: 'feedImpression',
    body: { releaseId: 321, shelfId: 'plugin:unscored' },
  })
  view.rerender(tree(false))
  expect(observer.disconnect).toHaveBeenCalledOnce()
  observer.expose(card)
  expect(request).toHaveBeenCalledOnce()
})

it('records only live intersecting cards, including partially clipped cards, and shares one observer', () => {
  const { card, write } = setup(),
    a = card(1),
    b = card(2),
    c = card(3)
  expect(Intersections.all).toHaveLength(1)
  const observer = Intersections.all[0]
  expect(write).not.toHaveBeenCalled()
  observer.expose(a.element, 0)
  observer.expose(b.element, 0.02)
  expect(write.mock.calls).toEqual([[2, 'recommended']])
  observer.expose(c.element)
  observer.expose(b.element)
  expect(write.mock.calls).toEqual([
    [2, 'recommended'],
    [3, 'recommended'],
  ])
})

it('deduplicates the same release across shelves, reloads, and screen remounts for the UTC day', () => {
  const { card, write } = setup(),
    a = card(1)
  Intersections.all[0].expose(a.element)
  const duplicate = card(1, 'another-shelf')
  Intersections.all[0].expose(duplicate.element)
  a.stop()
  duplicate.stop()
  const reload = card(1)
  Intersections.all[1].expose(reload.element)
  expect(write).toHaveBeenCalledTimes(1)
})

it('ignores callbacks queued by a removed card or disconnected observation generation', () => {
  const { card, write } = setup(),
    a = card(1),
    b = card(2)
  const old = Intersections.all[0]
  a.stop()
  old.expose(a.element)
  expect(write).not.toHaveBeenCalled()
  b.stop()
  const current = card(3)
  old.expose(current.element)
  expect(write).not.toHaveBeenCalled()
  Intersections.all[1].expose(current.element)
  expect(write).toHaveBeenCalledWith(3, 'recommended')
})

it.each(['hidden', 'inactive'] as const)(
  'suppresses %s windows and records still-visible cards on return',
  async (state) => {
    const { card, write } = setup(),
      a = card(1)
    if (state === 'hidden') vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    else vi.mocked(document.hasFocus).mockReturnValue(false)
    Intersections.all[0].expose(a.element)
    expect(write).not.toHaveBeenCalled()
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    vi.mocked(document.hasFocus).mockReturnValue(true)
    document.dispatchEvent(new Event('visibilitychange'))
    window.dispatchEvent(new Event('focus'))
    await frame()
    expect(write).toHaveBeenCalledOnce()
  },
)

it.each(['hidden', 'aria-hidden', 'display', 'opacity'] as const)(
  'waits while an ancestor hides a card through %s',
  async (attribute) => {
    const { card, write } = setup(),
      a = card(1)
    const parent = document.createElement('section')
    document.body.append(parent)
    parent.append(a.element)
    if (attribute === 'hidden') parent.hidden = true
    else if (attribute === 'aria-hidden') parent.setAttribute('aria-hidden', 'true')
    else parent.style[attribute] = attribute === 'opacity' ? '0' : 'none'
    Intersections.all[0].expose(a.element)
    expect(write).not.toHaveBeenCalled()
    parent.removeAttribute('hidden')
    parent.removeAttribute('aria-hidden')
    parent.removeAttribute('style')
    await frame()
    expect(write).toHaveBeenCalledOnce()
  },
)

it('waits for a modal to close and records a new UTC day only when the feed is exposed again', async () => {
  const { card, write } = setup(),
    a = card(1)
  Intersections.all[0].expose(a.element)
  expect(write).toHaveBeenCalledOnce()
  const modal = document.createElement('div')
  modal.setAttribute('role', 'dialog')
  document.body.append(modal)
  vi.setSystemTime(new Date('2026-09-30T12:00:00Z'))
  window.dispatchEvent(new Event('focus'))
  await frame()
  expect(write).toHaveBeenCalledOnce()
  modal.remove()
  await frame()
  expect(write).toHaveBeenCalledTimes(2)
})

it('records a continuously visible card at the next UTC day without polling every card', async () => {
  vi.setSystemTime(new Date('2026-09-29T23:59:59.990Z'))
  const { card, write } = setup(),
    a = card(1)
  Intersections.all[0].expose(a.element)
  await vi.advanceTimersByTimeAsync(12)
  expect(write).toHaveBeenCalledTimes(2)
  expect(Intersections.all).toHaveLength(1)
})

it('loads a new pass and scrolls to unseen cards while hidden without recording until the window returns', async () => {
  const { card, write } = setup(),
    original = card(1)
  Intersections.all[0].expose(original.element)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  original.stop()
  const reloaded = card(1),
    later = card(2)
  Intersections.all[1].expose(reloaded.element)
  Intersections.all[1].expose(later.element, 0.05)
  await frame()
  expect(write.mock.calls).toEqual([[1, 'recommended']])
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
  document.dispatchEvent(new Event('visibilitychange'))
  await frame()
  expect(write.mock.calls).toEqual([
    [1, 'recommended'],
    [2, 'recommended'],
  ])
})

it('uses the clipped intersection to reject an opaque covering element until it leaves', async () => {
  const { card, write } = setup(),
    a = card(1),
    overlay = document.createElement('div')
  document.body.append(overlay)
  const hit = vi.fn((): Element | null => overlay)
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: hit })
  try {
    Intersections.all[0].expose(a.element, 0.2, new DOMRect(20, 40, 50, 60))
    expect(hit).toHaveBeenCalledWith(45, 70)
    expect(write).not.toHaveBeenCalled()
    hit.mockReturnValue(a.element.firstElementChild)
    overlay.remove()
    await frame()
    expect(write).toHaveBeenCalledOnce()
  } finally {
    delete (document as unknown as Record<string, unknown>).elementFromPoint
  }
})

it('a failed recording can retry on the next exposure without an unhandled task', async () => {
  const report = vi
    .fn(async (_releaseId: number, _shelfId: string) => {})
    .mockRejectedValueOnce(Error('Backend unavailable'))
  const { card } = setup(report),
    a = card(1)
  Intersections.all[0].expose(a.element)
  await Promise.resolve()
  await Promise.resolve()
  window.dispatchEvent(new Event('focus'))
  await frame()
  expect(report).toHaveBeenCalledTimes(2)
})

it('never records an unscored recent-play shelf or an invalid release', () => {
  const { card, write } = setup()
  card(1, 'recently_played')
  card(2, 'recently-played')
  card(0)
  card(-1)
  expect(Intersections.all).toHaveLength(0)
  expect(write).not.toHaveBeenCalled()
})

it('stops observing, frame work, and midnight work when the last card leaves', async () => {
  const { card, write } = setup(),
    a = card(1)
  const observer = Intersections.all[0]
  observer.expose(a.element, 0)
  window.dispatchEvent(new Event('focus'))
  a.stop()
  a.element.remove()
  expect(observer.disconnect).toHaveBeenCalledOnce()
  expect(observer.unobserve).toHaveBeenCalledWith(a.element)
  await vi.advanceTimersByTimeAsync(86400001)
  expect(write).not.toHaveBeenCalled()
})
