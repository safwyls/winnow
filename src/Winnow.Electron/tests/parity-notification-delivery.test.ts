import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { deliverNotification } from '../src/main/notifications'
class NotificationFixture extends EventEmitter {
  show = vi.fn()
  close = vi.fn(() => {
    this.emit('close')
  })
}
afterEach(() => vi.useRealTimers())
describe('native journal notification delivery', () => {
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
