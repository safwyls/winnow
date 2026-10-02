// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { restoreFocusWhenReady } from '../src/renderer/features/restore-focus'

afterEach(() => {
  document.body.replaceChildren()
  vi.restoreAllMocks()
})
const frame = () => new Promise<void>((done) => requestAnimationFrame(() => done()))
function fixture() {
  const origin = document.createElement('button')
  const next = document.createElement('button')
  const background = document.createElement('div')
  background.append(origin)
  document.body.append(background, next)
  next.focus()
  return { origin, next, background }
}

it('restores the retained Details opener after an unchanged review finishes refreshing', async () => {
  const { origin, next } = fixture()
  origin.disabled = true
  restoreFocusWhenReady(origin)
  await frame()
  expect(document.activeElement).toBe(next)
  origin.disabled = false
  await Promise.resolve()
  expect(document.activeElement).toBe(origin)
})

it('waits for the modal background to become accessible before restoring focus', async () => {
  const { origin, next, background } = fixture()
  background.setAttribute('aria-hidden', 'true')
  restoreFocusWhenReady(origin)
  await frame()
  expect(document.activeElement).toBe(next)
  background.removeAttribute('aria-hidden')
  await Promise.resolve()
  expect(document.activeElement).toBe(origin)
})

for (const event of ['pointerdown', 'keydown'])
  it(`does not steal focus after a subsequent ${event} while the opener refreshes`, async () => {
    const { origin, next } = fixture()
    origin.disabled = true
    restoreFocusWhenReady(origin)
    await frame()
    next.dispatchEvent(new Event(event, { bubbles: true }))
    origin.disabled = false
    await Promise.resolve()
    expect(document.activeElement).toBe(next)
  })

it('abandons a detached opener even if that node is later reattached', async () => {
  const { origin, next, background } = fixture()
  origin.disabled = true
  const focus = vi.spyOn(origin, 'focus')
  restoreFocusWhenReady(origin)
  await frame()
  origin.remove()
  await Promise.resolve()
  background.append(origin)
  origin.disabled = false
  await frame()
  expect(focus).not.toHaveBeenCalled()
  expect(document.activeElement).toBe(next)
})
