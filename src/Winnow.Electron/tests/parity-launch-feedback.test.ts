import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LaunchFeedback } from '../src/renderer/features/launch-feedback'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())
const observed = (ownership: string) => ({ kind: 'launch.observed', resource: ownership })

describe('original launch-strip contracts', () => {
  it('resolves the matching watcher observation and dismisses confirmed running after three seconds', () => {
    const status = new LaunchFeedback()
    status.waiting(7, 'Portal 2')
    expect(status.getSnapshot()).toEqual({
      open: true,
      waiting: true,
      problem: false,
      message: 'Starting Portal 2…',
    })
    vi.advanceTimersByTime(20_000)
    status.observe(observed('7'))
    expect(status.getSnapshot()).toEqual({
      open: true,
      waiting: false,
      problem: false,
      message: 'Portal 2 is running.',
    })
    vi.advanceTimersByTime(2_999)
    expect(status.getSnapshot().open).toBe(true)
    vi.advanceTimersByTime(1)
    expect(status.getSnapshot().open).toBe(false)
  })
  it('silently clears an unobserved launch at the original ninety-second deadline', () => {
    const status = new LaunchFeedback()
    status.waiting(7, 'Portal 2')
    vi.advanceTimersByTime(89_999)
    expect(status.getSnapshot().waiting).toBe(true)
    vi.advanceTimersByTime(1)
    expect(status.getSnapshot()).toEqual({ open: false, waiting: false, problem: false, message: '' })
  })
  it('uses the source refusal copy and expires the problem after seven seconds', () => {
    const status = new LaunchFeedback()
    status.refused('Portal 2', 'Steam')
    expect(status.getSnapshot()).toEqual({
      open: true,
      waiting: false,
      problem: true,
      message: "Couldn't reach Steam to start Portal 2.",
    })
    vi.advanceTimersByTime(6_999)
    expect(status.getSnapshot().open).toBe(true)
    vi.advanceTimersByTime(1)
    expect(status.getSnapshot().open).toBe(false)
  })
  it('does not resolve the current strip for another ownership or an unconfirmed change event', () => {
    const status = new LaunchFeedback()
    status.waiting(7, 'Portal 2')
    status.observe(observed('9'))
    status.observe({ kind: 'launch.changed', resource: '7' })
    expect(status.getSnapshot().message).toBe('Starting Portal 2…')
    expect(status.getSnapshot().waiting).toBe(true)
  })
  it('disposal stops observations and timers from changing the retained strip', () => {
    const status = new LaunchFeedback()
    status.waiting(7, 'Portal 2')
    status.dispose()
    status.observe(observed('7'))
    vi.advanceTimersByTime(100_000)
    expect(status.getSnapshot().message).toBe('Starting Portal 2…')
  })
})

describe('production dispatch coordination', () => {
  it('attributes the collapsed title to the actual selected ownership', async () => {
    const status = new LaunchFeedback()
    await status.track(2, 'Two copies', 'Steam', 'Play', async () => 0)
    status.observe(observed('1'))
    expect(status.getSnapshot().waiting).toBe(true)
    status.observe(observed('2'))
    expect(status.getSnapshot().message).toBe('Two copies is running.')
  })
  it('retains an observation arriving before the HTTP handoff response', async () => {
    const status = new LaunchFeedback()
    await status.track(7, 'Portal 2', 'Steam', 'Play', async () => {
      status.observe(observed('7'))
      return 0
    })
    expect(status.getSnapshot().message).toBe('Portal 2 is running.')
    expect(status.getSnapshot().waiting).toBe(false)
  })
  it.each(['Install', 'Uninstall', 'Manage', 'OpenStore'])(
    'does not create a launch strip for %s',
    async (action) => {
      const status = new LaunchFeedback(),
        send = vi.fn(async () => 0)
      await status.track(7, 'Portal 2', 'Steam', action, send)
      expect(send).toHaveBeenCalledOnce()
      expect(status.getSnapshot().open).toBe(false)
    },
  )
  it('does not replace a first launch or extend its deadline when the backend reports AlreadyRunning', async () => {
    const status = new LaunchFeedback()
    await status.track(7, 'Portal 2', 'Steam', 'Play', async () => 0)
    vi.advanceTimersByTime(20_000)
    await status.track(7, 'Portal 2', 'Steam', 'Play', async () => 1)
    vi.advanceTimersByTime(70_000)
    expect(status.getSnapshot().open).toBe(false)
  })
  it('keeps concurrent duplicate operation responses from rearming the same strip', async () => {
    const status = new LaunchFeedback()
    let finishFirst!: (value: number) => void, finishSecond!: (value: number) => void
    const first = status.track(
      7,
      'Portal 2',
      'Steam',
      'Play',
      () =>
        new Promise((resolve) => {
          finishFirst = resolve
        }),
    )
    const second = status.track(
      7,
      'Portal 2',
      'Steam',
      'Play',
      () =>
        new Promise((resolve) => {
          finishSecond = resolve
        }),
    )
    finishFirst(0)
    await first
    vi.advanceTimersByTime(20_000)
    finishSecond(0)
    await second
    vi.advanceTimersByTime(70_000)
    expect(status.getSnapshot().open).toBe(false)
  })
  it('keeps a late response from replacing the most recently requested game', async () => {
    const status = new LaunchFeedback()
    let finish!: (value: number) => void
    const old = status.track(
      7,
      'Portal 2',
      'Steam',
      'Play',
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    await status.track(9, 'Celeste', 'Steam', 'Play', async () => 0)
    finish(2)
    await old
    expect(status.getSnapshot().message).toBe('Starting Celeste…')
  })
  it('leaves interrupted transport outcomes to the retained retry UI instead of claiming a refusal', async () => {
    const status = new LaunchFeedback()
    await expect(
      status.track(7, 'Portal 2', 'Steam', 'Play', async () => {
        throw Error('Interrupted')
      }),
    ).rejects.toThrow('Interrupted')
    expect(status.getSnapshot().open).toBe(false)
  })
  it('ignores completion after disposal and refuses a detached callback before dispatch', async () => {
    const status = new LaunchFeedback()
    let finish!: (value: number) => void
    const pending = status.track(
      7,
      'Portal 2',
      'Steam',
      'Play',
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    status.dispose()
    finish(0)
    await pending
    expect(status.getSnapshot().open).toBe(false)
    const send = vi.fn(async () => 0)
    await status.track(7, 'Portal 2', 'Steam', 'Play', send)
    expect(send).not.toHaveBeenCalled()
  })
  it('ignores a retired request after reattachment while accepting a fresh request', async () => {
    const status = new LaunchFeedback()
    let finish!: (value: number) => void
    const old = status.track(
      7,
      'Portal 2',
      'Steam',
      'Play',
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    status.dispose()
    status.start()
    finish(0)
    await old
    expect(status.getSnapshot().open).toBe(false)
    await status.track(9, 'Celeste', 'Steam', 'Play', async () => 0)
    expect(status.getSnapshot().message).toBe('Starting Celeste…')
  })
})
