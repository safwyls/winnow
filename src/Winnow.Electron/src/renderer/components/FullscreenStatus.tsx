import { useEffect, useState } from 'react'
import { controllerSample, selectedStandardController } from '../../shared/controller-status'

const selected = () => selectedStandardController(Array.from(navigator.getGamepads?.() ?? []))
const identity = (pad: Gamepad | null) => (pad ? `${pad.index}:${pad.id}` : '')

export function FullscreenStatus() {
  const [now, setNow] = useState(() => new Date())
  const [status, setStatus] = useState('Controller disconnected')
  useEffect(() => {
    let disposed = false,
      generation = 0,
      previous: string | null = null,
      pending = false,
      released = true
    const release = () => {
      if (released) return
      released = true
      void window.winnow.controllerBattery?.(null).catch(() => {})
    }
    const refresh = () => {
      const pad = selected(),
        key = identity(pad)
      if (key !== previous) {
        release()
        generation++
        previous = key
        setStatus(pad ? 'Controller connected' : 'Controller disconnected')
      }
      if (!pad || document.visibilityState === 'hidden') {
        release()
        return
      }
      if (pending || !window.winnow.controllerBattery) return
      pending = true
      released = false
      const version = generation
      void window.winnow
        .controllerBattery(controllerSample(pad))
        .then(
          (battery) => {
            if (!disposed && version === generation && identity(selected()) === key)
              setStatus(battery ?? 'Controller connected')
          },
          () => {
            if (!disposed && version === generation && identity(selected()) === key)
              setStatus('Controller connected')
          },
        )
        .finally(() => {
          pending = false
        })
    }
    const connectionChanged = () => {
      generation++
      previous = null
      release()
      refresh()
    }
    const resume = () => {
      setNow(new Date())
      connectionChanged()
    }
    const clock = window.setInterval(() => setNow(new Date()), 15000)
    const controller = window.setInterval(refresh, 1000)
    window.addEventListener('gamepadconnected', connectionChanged)
    window.addEventListener('gamepaddisconnected', connectionChanged)
    window.addEventListener('focus', resume)
    document.addEventListener('visibilitychange', resume)
    refresh()
    return () => {
      disposed = true
      generation++
      clearInterval(clock)
      clearInterval(controller)
      window.removeEventListener('gamepadconnected', connectionChanged)
      window.removeEventListener('gamepaddisconnected', connectionChanged)
      window.removeEventListener('focus', resume)
      document.removeEventListener('visibilitychange', resume)
      release()
    }
  }, [])
  const clock = now.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
  return (
    <div className="avalon-system-status">
      <span className="avalon-controller-status" role="status" title={status}>
        {status}
      </span>
      <time className="avalon-clock" dateTime={now.toISOString()} aria-label={`Local time: ${clock}`}>
        {clock}
      </time>
    </div>
  )
}
