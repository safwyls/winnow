import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { canNotifyJournal, deliverNotification } from '../src/main/notifications'
class NotificationFixture extends EventEmitter {
  show = vi.fn()
  close = vi.fn(() => {
    this.emit('close')
  })
}
afterEach(() => vi.useRealTimers())
describe('native journal notification delivery', () => {
  it('reports unavailable for missing and headless windows without reaching native delivery', () => {
    const supported = vi.fn(() => true)
    const detached = {
      isDestroyed: () => false,
      isFocused: () => false,
      getNativeWindowHandle: () => new Uint8Array(),
    }
    expect(canNotifyJournal(undefined, supported)).toBe(false)
    expect(canNotifyJournal(null, supported)).toBe(false)
    expect(canNotifyJournal(detached, supported)).toBe(false)
    expect(canNotifyJournal({ ...detached, getNativeWindowHandle: () => new Uint8Array(8) }, supported)).toBe(
      false,
    )
    expect(supported).not.toHaveBeenCalled()
  })
  it('keeps live background windows eligible while handling destruction races without an application error', () => {
    const supported = vi.fn(() => true)
    const background = {
      isDestroyed: () => false,
      isFocused: () => false,
      getNativeWindowHandle: () => new Uint8Array([1, 0, 0, 0]),
    }
    expect(canNotifyJournal(background, supported)).toBe(true)
    expect(canNotifyJournal({ ...background, isFocused: () => true }, supported)).toBe(false)
    expect(canNotifyJournal({ ...background, isDestroyed: () => true }, supported)).toBe(false)
    expect(
      canNotifyJournal(
        {
          ...background,
          getNativeWindowHandle: () => {
            throw new Error('Object has been destroyed')
          },
        },
        supported,
      ),
    ).toBe(false)
    expect(canNotifyJournal(background, () => false)).toBe(false)
    expect(supported).toHaveBeenCalledOnce()
  })
  it('waits for actual display before claiming native delivery', async () => {
    const notification = new NotificationFixture()
    const result = deliverNotification(notification)
    notification.emit('show')
    expect(await result).toBe(true)
    expect(notification.listenerCount('failed')).toBe(0)
    expect(notification.close).not.toHaveBeenCalled()
  })
  it('returns the in-window fallback when OS delivery fails', async () => {
    const notification = new NotificationFixture()
    const result = deliverNotification(notification)
    notification.emit('failed')
    expect(await result).toBe(false)
    expect(notification.close).toHaveBeenCalledOnce()
  })
  it('bounds an unresponsive notifier and removes its listeners', async () => {
    vi.useFakeTimers()
    const notification = new NotificationFixture()
    const result = deliverNotification(notification)
    await vi.advanceTimersByTimeAsync(5000)
    expect(await result).toBe(false)
    expect(notification.eventNames()).toEqual([])
  })
})
