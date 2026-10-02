import { describe, it, expect, vi } from 'vitest'
import {
  SurfacePreparation,
  type PreparationClock,
  type PreparationState,
} from '../src/renderer/startup/preparation'
import { closedDragonContours } from '../src/renderer/startup/dragon-contours'

const deferred = () => {
  let resolve!: () => void, reject!: (value: unknown) => void
  const promise = new Promise<void>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}
async function flush() {
  for (let i = 0; i < 12; i++) await Promise.resolve()
}
class Frames implements PreparationClock {
  invisible = false
  time = 0
  callbacks: ((time: number) => void)[] = []
  hidden = () => this.invisible
  frame = (signal: AbortSignal) =>
    new Promise<number>((resolve, reject) => {
      const abort = () => reject(signal.reason)
      signal.addEventListener('abort', abort, { once: true })
      const finish = (time: number) => {
        signal.removeEventListener('abort', abort)
        signal.aborted ? abort() : resolve(time)
      }
      if (signal.aborted) abort()
      else if (this.invisible) finish(this.time)
      else this.callbacks.push(finish)
    })
  async tick() {
    this.time += 60
    const list = this.callbacks.splice(0)
    list.forEach((callback) => callback(this.time))
    await flush()
  }
}
function fixture(reduced: boolean | undefined = false) {
  const frames = new Frames(),
    states: PreparationState[] = []
  const owner = new SurfacePreparation(frames, (state) => states.push(state))
  owner.configureMotion(reduced)
  return { owner, frames, states }
}
describe.each(['desktop', 'fullscreen'])('%s preparation contract', () => {
  it.each([false, true])(
    'paints before loading and keeps two ready layout frames at reduced motion %s',
    async (reduced) => {
      const { owner, frames } = fixture(reduced),
        ready = deferred(),
        load = vi.fn(() => ready.promise)
      const work = owner.start(load)
      expect(load).not.toHaveBeenCalled()
      await frames.tick()
      expect(load).toHaveBeenCalledOnce()
      for (let i = 0; i < 35; i++) await frames.tick()
      expect(owner.state.phase).toBe('loading')
      owner.renderedTrace(1800)
      ready.resolve()
      await flush()
      expect(owner.state.phase).toBe('layout')
      await frames.tick()
      expect(owner.state.phase).toBe('layout')
      await frames.tick()
      if (reduced) expect(owner.state.phase).toBe('ready')
      else {
        expect(owner.state.phase).toBe('circuit')
        for (let i = 0; i < 5; i++) await frames.tick()
      }
      await work
      expect(owner.state).toMatchObject({ phase: 'ready', tracing: false, opacity: 0 })
    },
  )
  it('counts the full circuit from rendered worker frames and keeps tracing throughout the fade', async () => {
    const { owner, frames, states } = fixture()
    const work = owner.start(async () => {})
    for (let i = 0; i < 40; i++) await frames.tick()
    expect(owner.state.phase).toBe('circuit')
    owner.renderedTrace(1799)
    await frames.tick()
    expect(owner.state.phase).toBe('circuit')
    owner.renderedTrace(1800)
    await frames.tick()
    await frames.tick()
    await frames.tick()
    expect(owner.state).toMatchObject({ phase: 'fading', tracing: true })
    expect(owner.state.opacity).toBeGreaterThan(0)
    expect(owner.state.opacity).toBeLessThan(1)
    for (let i = 0; i < 4; i++) await frames.tick()
    await work
    expect(states.filter((state) => state.phase === 'fading').every((state) => state.tracing)).toBe(true)
  })
  it('does not trace before the saved preference arrives and reduced motion skips hold and fade', async () => {
    const { owner, frames, states } = fixture(undefined)
    owner.configureMotion(undefined)
    const work = owner.start(async () => {})
    for (let i = 0; i < 35; i++) await frames.tick()
    expect(owner.state).toMatchObject({ phase: 'circuit', tracing: false })
    owner.configureMotion(true)
    await frames.tick()
    await work
    expect(states.some((state) => state.phase === 'fading')).toBe(false)
    expect(owner.state.phase).toBe('ready')
  })
  it('failure stays covered and retry runs a fresh load', async () => {
    const { owner, frames } = fixture(true),
      load = vi.fn().mockRejectedValueOnce(Error('fixture')).mockResolvedValue(undefined)
    const first = owner.start(load)
    await frames.tick()
    await first
    expect(owner.state).toMatchObject({ phase: 'failed', opacity: 1, tracing: false })
    const retry = owner.start(load)
    for (let i = 0; i < 4; i++) await frames.tick()
    await retry
    expect(load).toHaveBeenCalledTimes(2)
    expect(owner.state.phase).toBe('ready')
  })
  it('leaving before attachment cancels without starting a read', async () => {
    const { owner, frames } = fixture(),
      load = vi.fn(async () => {})
    const work = owner.start(load)
    owner.cancel()
    await work
    await frames.tick()
    expect(load).not.toHaveBeenCalled()
    expect(owner.state.phase).not.toBe('ready')
  })
  it('a rendering failure cancels the presentation and late data cannot reveal it', async () => {
    const { owner, frames, states } = fixture(),
      ready = deferred()
    const work = owner.start(() => ready.promise)
    await frames.tick()
    owner.fail()
    await work
    const count = states.length
    ready.resolve()
    await frames.tick()
    expect(states).toHaveLength(count)
    expect(owner.state).toMatchObject({ phase: 'failed', opacity: 1, tracing: false })
  })
  it('closing cancels pending preparation immediately and a late read cannot reveal', async () => {
    const { owner, frames, states } = fixture(),
      ready = deferred()
    const work = owner.start(() => ready.promise)
    await frames.tick()
    owner.cancel()
    await work
    const count = states.length
    ready.resolve()
    await frames.tick()
    expect(states).toHaveLength(count)
    expect(owner.state.phase).not.toBe('ready')
  })
  it.each([false, true])(
    'reentry shares an unfinished read but refreshes a finished one: %s',
    async (completed) => {
      const { owner, frames } = fixture(true),
        ready = deferred(),
        load = vi.fn(() => ready.promise)
      const first = owner.start(load)
      await frames.tick()
      owner.cancel()
      await first
      if (completed) {
        ready.resolve()
        await flush()
      }
      const second = owner.start(load)
      await frames.tick()
      ready.resolve()
      await flush()
      for (let i = 0; i < 4; i++) await frames.tick()
      await second
      expect(load).toHaveBeenCalledTimes(completed ? 2 : 1)
      expect(owner.state.phase).toBe('ready')
    },
  )
  it('hidden preparation never waits for invisible window frames', async () => {
    const { owner, frames, states } = fixture()
    frames.invisible = true
    await owner.start(async () => {})
    expect(owner.state.phase).toBe('ready')
    expect(frames.callbacks).toHaveLength(0)
    expect(states.some((state) => state.phase === 'fading')).toBe(false)
  })
  it('restoring during a hidden read keeps one trace clock', async () => {
    const { owner, frames } = fixture(),
      ready = deferred()
    frames.invisible = true
    const work = owner.start(() => ready.promise)
    await flush()
    frames.invisible = false
    owner.renderedTrace(1800)
    ready.resolve()
    await flush()
    for (let i = 0; i < 8; i++) await frames.tick()
    await work
    expect(owner.state.phase).toBe('ready')
  })
})
it('separates inner and detached closed contours without losing relative move origins', () => {
  expect(closedDragonContours(['M10,20l4,4Z m5,6l1,1z', 'M0,0L5,5Z'])).toEqual([
    'M10,20l4,4Z ',
    'M15,26l1,1z',
    'M0,0L5,5Z',
  ])
  expect(() => closedDragonContours(['M1,2L5,5'])).toThrow('closed contours')
})
