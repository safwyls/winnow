import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { SettledBackgroundQueue } from '../src/renderer/components/settled-background-queue'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

it('waits for browsing to settle and samples the latest visible games', async () => {
  let visible = [1, 2]
  const load = vi.fn(async (_id: number, _signal: AbortSignal) => {})
  const queue = new SettledBackgroundQueue({ collect: () => visible, load, busy: () => false })
  queue.changed()
  await vi.advanceTimersByTimeAsync(350)
  visible = [3, 4]
  queue.changed()
  await vi.advanceTimersByTimeAsync(399)
  expect(load).not.toHaveBeenCalled()
  await vi.advanceTimersByTimeAsync(1)
  expect(load.mock.calls.map(([id]) => id)).toEqual([3, 4])
  queue.dispose()
})

it('keeps two jobs at most, even when obsolete jobs ignore cancellation', async () => {
  let visible = [1, 2, 3, 4]
  const jobs: { id: number; signal: AbortSignal; finish(): void }[] = []
  const queue = new SettledBackgroundQueue({
    collect: () => visible,
    busy: () => false,
    load: (id, signal) => new Promise<void>((finish) => jobs.push({ id, signal, finish })),
  })
  queue.changed()
  await vi.advanceTimersByTimeAsync(400)
  expect(jobs.map((job) => job.id)).toEqual([1, 2])
  visible = [8, 9, 10]
  queue.changed()
  expect(jobs.every((job) => job.signal.aborted)).toBe(true)
  await vi.advanceTimersByTimeAsync(400)
  expect(jobs).toHaveLength(2)
  jobs[0].finish()
  await vi.advanceTimersByTimeAsync(0)
  expect(jobs.map((job) => job.id)).toEqual([1, 2, 8])
  queue.dispose()
  jobs.forEach((job) => job.finish())
  await vi.advanceTimersByTimeAsync(1000)
  expect(jobs).toHaveLength(3)
  expect(jobs[2].signal.aborted).toBe(true)
})

it('waits behind foreground artwork and continues after a speculative failure', async () => {
  let busy = true
  const load = vi.fn(async (id: number) => {
    if (id === 1) throw Error('unavailable')
  })
  const queue = new SettledBackgroundQueue({ collect: () => [1, 2, 3], load, busy: () => busy })
  queue.changed()
  await vi.advanceTimersByTimeAsync(900)
  expect(load).not.toHaveBeenCalled()
  busy = false
  await vi.advanceTimersByTimeAsync(100)
  expect(load.mock.calls.map(([id]) => id)).toEqual([1, 2, 3])
  queue.dispose()
})

it('starts after a bounded foreground head start even when a cover remains pending, and resets on new browsing', async () => {
  let visible = [1, 2, 3]
  let running = 0,
    maximumRunning = 0
  const jobs: { id: number; signal: AbortSignal; finish(): void }[] = []
  const queue = new SettledBackgroundQueue({
    collect: () => visible,
    busy: () => true,
    load: (id, signal) =>
      new Promise<void>((resolve) => {
        running++
        maximumRunning = Math.max(maximumRunning, running)
        jobs.push({
          id,
          signal,
          finish() {
            running--
            resolve()
          },
        })
      }),
  })
  queue.changed()
  await vi.advanceTimersByTimeAsync(1399)
  expect(jobs).toHaveLength(0)
  await vi.advanceTimersByTimeAsync(1)
  expect(jobs.map((job) => job.id)).toEqual([1, 2])
  jobs[0].finish()
  await vi.advanceTimersByTimeAsync(0)
  expect(jobs.map((job) => job.id)).toEqual([1, 2, 3])

  visible = [8, 9, 10]
  queue.changed()
  expect(jobs[1].signal.aborted).toBe(true)
  expect(jobs[2].signal.aborted).toBe(true)
  await vi.advanceTimersByTimeAsync(399)
  jobs[1].finish()
  jobs[2].finish()
  await vi.advanceTimersByTimeAsync(1000)
  expect(jobs).toHaveLength(3)
  await vi.advanceTimersByTimeAsync(1)
  expect(jobs.map((job) => job.id)).toEqual([1, 2, 3, 8, 9])
  expect(maximumRunning).toBe(2)
  queue.dispose()
  jobs[3].finish()
  jobs[4].finish()
  await vi.advanceTimersByTimeAsync(2000)
  expect(jobs).toHaveLength(5)
})
