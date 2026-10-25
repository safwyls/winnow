import { describe, expect, it, vi } from 'vitest'
import {
  FetchProgressObserver,
  FetchStatus,
  reportFetchProgress,
  type FetchProgress,
} from '../src/renderer/features/fetch-status'

describe('original fetch status contracts', () => {
  it('a pass with nothing to do shows nothing', () => {
    const status = new FetchStatus()
    status.begin(0)
    expect(status.snapshot.active).toBe(false)
  })
  it('the field names what is left as a count', () => {
    const status = new FetchStatus()
    status.begin(1247)
    expect(status.snapshot.active).toBe(true)
    expect(status.snapshot.remainingText).toBe('1,247')
    expect(status.snapshot.automationName).toContain('1,247')
    expect(status.snapshot.label).not.toBe('')
    expect(status.snapshot.remainingNote).not.toBe('')
  })
  it('the count falls as the pass advances', () => {
    const status = new FetchStatus()
    status.begin(80)
    status.report(40)
    expect(status.snapshot.active).toBe(true)
    expect(status.snapshot.remainingText).toBe('40')
  })
  it('the field disappears on completion', () => {
    const status = new FetchStatus()
    status.begin(80)
    status.report(0)
    expect(status.snapshot.active).toBe(false)
  })
  it('a report that arrives after completion does not bring it back', () => {
    const status = new FetchStatus()
    status.begin(80)
    status.clear()
    status.report(40)
    expect(status.snapshot.active).toBe(false)
  })
  it('the singular and the plural are both written', () => {
    const status = new FetchStatus()
    status.begin(1)
    const one = status.snapshot.remainingNote
    status.report(2)
    expect(one).toBe('title left')
    expect(status.snapshot.remainingNote).toBe('titles left')
    expect(one).not.toBe(status.snapshot.remainingNote)
  })
})

describe('live progress observer', () => {
  it('starts a new reported pass after completion while plain slice reports remain ignored', () => {
    const status = new FetchStatus()
    reportFetchProgress(status, { total: 80, remaining: 40 })
    expect(status.snapshot.remaining).toBe(40)
    reportFetchProgress(status, { total: 80, remaining: 0 })
    status.report(40)
    expect(status.snapshot.active).toBe(false)
    reportFetchProgress(status, { total: 1247, remaining: 997 })
    expect(status.snapshot).toMatchObject({ active: true, remaining: 997 })
  })
  it('coalesces a burst, cancels obsolete reads and refuses a late count after observed completion', async () => {
    const reads: { signal: AbortSignal; resolve(value: FetchProgress): void }[] = []
    const publish = vi.fn()
    const observer = new FetchProgressObserver(
      (signal) => new Promise((resolve) => reads.push({ signal, resolve })),
      publish,
    )
    observer.refresh()
    await Promise.resolve()
    observer.refresh()
    observer.refresh()
    observer.refresh()
    expect(reads[0].signal.aborted).toBe(true)
    await Promise.resolve()
    expect(reads).toHaveLength(2)
    reads[1].resolve({ total: 80, remaining: 0 })
    await Promise.resolve()
    expect(publish).toHaveBeenLastCalledWith(expect.objectContaining({ active: false, remaining: 0 }))
    reads[0].resolve({ total: 80, remaining: 40 })
    await Promise.resolve()
    expect(publish).toHaveBeenCalledTimes(1)
    observer.dispose()
  })
  it('cancels pending reads and drops queued refreshes and completions on disposal', async () => {
    let resolve!: (progress: FetchProgress) => void
    const read = vi.fn(
      (signal: AbortSignal) =>
        new Promise<FetchProgress>((done) => {
          resolve = done
        }),
    )
    const publish = vi.fn()
    const observer = new FetchProgressObserver(read, publish)
    observer.refresh()
    await Promise.resolve()
    observer.refresh()
    observer.dispose()
    resolve({ total: 1247, remaining: 997 })
    await Promise.resolve()
    observer.refresh()
    await Promise.resolve()
    expect(read).toHaveBeenCalledTimes(1)
    expect(read.mock.calls[0][0].aborted).toBe(true)
    expect(publish).not.toHaveBeenCalled()
  })
  it('recovers from a failed transport on the next observation without inventing progress', async () => {
    const read = vi
      .fn()
      .mockRejectedValueOnce(new Error('Disconnected'))
      .mockResolvedValue({ total: 80, remaining: 40 })
    const publish = vi.fn()
    const observer = new FetchProgressObserver(read, publish)
    observer.refresh()
    await Promise.resolve()
    await Promise.resolve()
    expect(publish).not.toHaveBeenCalled()
    observer.refresh()
    await Promise.resolve()
    await Promise.resolve()
    expect(publish).toHaveBeenLastCalledWith(expect.objectContaining({ active: true, remaining: 40 }))
    observer.dispose()
  })
})
