// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { FullscreenStatus } from '../src/renderer/components/FullscreenStatus'
import type { WinnowBridge } from '../src/shared/bridge'

let pads: (Gamepad | null)[], battery: ReturnType<typeof vi.fn>
const pad = (index = 0, id = 'Controller') =>
  ({ index, id, connected: true, mapping: 'standard', buttons: [], axes: [] }) as unknown as Gamepad
beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date(2026, 8, 30, 13, 1, 55))
  pads = []
  battery = vi.fn().mockResolvedValue(null)
  Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => pads })
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
  window.winnow = { controllerBattery: battery } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
})
async function change(event = 'gamepadconnected') {
  await act(async () => {
    window.dispatchEvent(new Event(event))
  })
}

it('shows local short time, refreshes every 15 seconds and releases timers on detach', async () => {
  const view = render(<FullscreenStatus />)
  const time = () => new Date().toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
  expect(screen.getByLabelText(`Local time: ${time()}`).textContent).toBe(time())
  const initial = screen.getByText(time()).textContent
  await act(async () => {
    await vi.advanceTimersByTimeAsync(14999)
  })
  expect(document.querySelector('time')!.textContent).toBe(initial)
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1)
  })
  expect(document.querySelector('time')!.textContent).toBe(time())
  view.unmount()
  expect(vi.getTimerCount()).toBe(0)
})
it('uses the navigation controller selection, known labels and generic unknown/disconnected states', async () => {
  render(<FullscreenStatus />)
  expect(screen.getByRole('status').textContent).toBe('Controller disconnected')
  pads = [null, { ...pad(), mapping: '' }, pad(2)]
  await change()
  expect(screen.getByRole('status').textContent).toBe('Controller connected')
  expect(battery).toHaveBeenLastCalledWith(expect.objectContaining({ index: 2 }))
  for (const label of ['Wired controller', 'Controller battery low', 'Controller battery full']) {
    battery.mockResolvedValue(label)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000)
    })
    expect(screen.getByRole('status').textContent).toBe(label)
  }
  battery.mockRejectedValue(Error('unavailable'))
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1000)
  })
  expect(screen.getByRole('status').textContent).toBe('Controller connected')
  pads = []
  await change('gamepaddisconnected')
  expect(screen.getByRole('status').textContent).toBe('Controller disconnected')
  expect(battery).toHaveBeenLastCalledWith(null)
})
it('rejects stale replies when a device reconnects at the same browser index', async () => {
  let resolve!: (value: string | null) => void
  battery.mockImplementation((value) =>
    value === null
      ? Promise.resolve(null)
      : new Promise((done) => {
          resolve = done
        }),
  )
  pads = [pad()]
  const view = render(<FullscreenStatus />)
  await change('gamepaddisconnected')
  await change()
  await act(async () => {
    resolve('Controller battery full')
  })
  expect(screen.getByRole('status').textContent).toBe('Controller connected')
  battery.mockResolvedValue('Wired controller')
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1000)
  })
  expect(screen.getByRole('status').textContent).toBe('Wired controller')
  view.unmount()
  expect(battery).toHaveBeenLastCalledWith(null)
})
it('stops hidden native polling and resumes clock and status on visibility return', async () => {
  pads = [pad()]
  render(<FullscreenStatus />)
  await act(async () => {})
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'))
  })
  const calls = battery.mock.calls.length
  await act(async () => {
    await vi.advanceTimersByTimeAsync(30000)
  })
  expect(battery).toHaveBeenCalledTimes(calls)
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
  battery.mockResolvedValue('Controller battery medium')
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'))
  })
  expect(screen.getByRole('status').textContent).toBe('Controller battery medium')
})

it('switches to the next navigation device and ignores the outgoing device reply', async () => {
  let finish!: (value: string) => void
  pads = [pad(1, 'First'), pad(3, 'Second')]
  battery.mockImplementation((value) =>
    value === null
      ? Promise.resolve(null)
      : new Promise((done) => {
          finish = done
        }),
  )
  render(<FullscreenStatus />)
  pads = [null, pad(3, 'Second')]
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1000)
    finish('Controller battery full')
  })
  expect(screen.getByRole('status').textContent).toBe('Controller connected')
  battery.mockResolvedValue('Wired controller')
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1000)
  })
  expect(battery).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'Second', index: 3 }))
  expect(screen.getByRole('status').textContent).toBe('Wired controller')
})

it('supports an unavailable native bridge and refreshes time immediately on focus return', async () => {
  delete window.winnow.controllerBattery
  pads = [pad()]
  render(<FullscreenStatus />)
  expect(screen.getByRole('status').textContent).toBe('Controller connected')
  vi.setSystemTime(new Date(2026, 8, 30, 17, 39))
  await change('focus')
  const time = new Date().toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
  expect(screen.getByLabelText(`Local time: ${time}`)).toBeTruthy()
})

it('releases a pending probe on detach and ignores its eventual reply and later device events', async () => {
  let finish!: (value: string) => void
  pads = [pad()]
  battery.mockImplementation((value) =>
    value === null
      ? Promise.resolve(null)
      : new Promise((done) => {
          finish = done
        }),
  )
  const view = render(<FullscreenStatus />)
  view.unmount()
  expect(battery).toHaveBeenLastCalledWith(null)
  const calls = battery.mock.calls.length
  await act(async () => {
    finish('Controller battery full')
  })
  await change()
  expect(battery).toHaveBeenCalledTimes(calls)
  expect(vi.getTimerCount()).toBe(0)
})
