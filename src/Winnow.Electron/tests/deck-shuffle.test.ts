// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DeckShuffle } from '../src/renderer/components/deck-shuffle'

let root: HTMLDivElement
const originalAnimate = Object.getOwnPropertyDescriptor(Element.prototype, 'animate')
let draws: { frames: Keyframe[]; animation: Animation }[]
const controller = new DeckShuffle()
function deck(selected: number) {
  root.innerHTML = [1, 2, 3]
    .map((id) => {
      const slot = id === selected ? 'selected' : id === (selected % 3) + 1 ? 'next' : 'previous'
      return `<button data-deck-key="${id}" data-deck-slot="${slot}" style="transform:translateX(${slot === 'selected' ? -50 : slot === 'next' ? -12 : -84}%);opacity:${slot === 'selected' ? 1 : 0.8};z-index:${slot === 'selected' ? 3 : 1}"></button>`
    })
    .join('')
}
beforeEach(() => {
  root = document.createElement('div')
  document.body.append(root)
  draws = []
  Object.defineProperty(Element.prototype, 'animate', {
    configurable: true,
    value: vi.fn((frames: Keyframe[]) => {
      const animation = { cancel: vi.fn(), onfinish: null } as unknown as Animation
      draws.push({ frames, animation })
      return animation
    }),
  })
  deck(1)
})
afterEach(() => {
  controller.stop()
  root.remove()
  if (originalAnimate) Object.defineProperty(Element.prototype, 'animate', originalAnimate)
  else Reflect.deleteProperty(Element.prototype, 'animate')
})

describe('card deck shuffling', () => {
  it.each([1, -1])(
    'shuffles in direction %s and releases motion state after the last card settles',
    (direction) => {
      const before = controller.capture(root)
      deck(direction > 0 ? 2 : 3)
      controller.play(root, before, direction)
      expect(root.dataset.shuffling).toBe('true')
      expect(draws).toHaveLength(3)
      expect(draws[0].frames[1].transform).toContain(direction > 0 ? '-115%' : '15%')
      draws[0].animation.onfinish?.call(draws[0].animation, {} as AnimationPlaybackEvent)
      expect(root.dataset.shuffling).toBe('true')
      for (const { animation } of draws.slice(1))
        animation.onfinish?.call(animation, {} as AnimationPlaybackEvent)
      expect(root.hasAttribute('data-shuffling')).toBe(false)
    },
  )

  it('retargets from the current pose and cancels the old motion without changing selection', () => {
    const before = controller.capture(root)
    deck(2)
    controller.play(root, before, 1)
    const prior = [...draws]
    root.querySelector<HTMLElement>('[data-deck-key="2"]')!.style.transform = 'translateX(-37%)'
    const interrupted = controller.capture(root)
    deck(3)
    controller.play(root, interrupted, 1)
    expect(prior.every(({ animation }) => vi.mocked(animation.cancel).mock.calls.length === 1)).toBe(true)
    expect(draws[4].frames[0].transform).toBe('translateX(-37%)')
    expect(root.querySelector('[data-deck-slot="selected"]')?.getAttribute('data-deck-key')).toBe('3')
    controller.stop()
    expect(root.hasAttribute('data-shuffling')).toBe(false)
    expect(draws.slice(3).every(({ animation }) => vi.mocked(animation.cancel).mock.calls.length === 1)).toBe(
      true,
    )
  })

  it('settles immediately for reduced motion, including when toggled mid-shuffle', () => {
    const before = controller.capture(root)
    deck(2)
    controller.play(root, before, 1)
    controller.play(root, before, 1, true)
    expect(draws).toHaveLength(3)
    expect(draws.every(({ animation }) => vi.mocked(animation.cancel).mock.calls.length === 1)).toBe(true)
    expect(root.hasAttribute('data-shuffling')).toBe(false)
  })

  it('does not animate a single-card deck', () => {
    root.innerHTML = '<button data-deck-key="1" data-deck-slot="selected"></button>'
    controller.play(root, controller.capture(root), 1)
    expect(draws).toHaveLength(0)
  })
})
