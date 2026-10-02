import { useEffect, useRef } from 'react'
import dragon from '../assets/dragon.svg'
import dragonSource from '../assets/dragon.svg?raw'
import { closedDragonContours } from './dragon-contours'
import type { DragonProgress } from './dragon-renderer'

export function LoadingDragon({
  tracing,
  onFrame,
  onFailure,
}: {
  tracing: boolean
  onFrame(elapsed: number): void
  onFailure(): void
}) {
  const host = useRef<HTMLSpanElement>(null),
    latest = useRef({ onFrame, onFailure })
  latest.current = { onFrame, onFailure }
  useEffect(() => {
    const element = host.current
    if (!element || !tracing) return
    const canvas = document.createElement('canvas')
    // A worker owns the transferred canvas for this presentation, including the reveal fade.
    if (!canvas.transferControlToOffscreen || typeof Worker === 'undefined') {
      latest.current.onFailure()
      return
    }
    let worker: Worker | undefined,
      live = true,
      stopObserving = () => {}
    try {
      const paths = [
        ...new DOMParser().parseFromString(dragonSource, 'image/svg+xml').querySelectorAll('path'),
      ].map((path) => path.getAttribute('d')!)
      const contours = closedDragonContours(paths).map((path) => {
        const measure = document.createElementNS('http://www.w3.org/2000/svg', 'path')
        measure.setAttribute('d', path)
        return { path, length: measure.getTotalLength() }
      })
      worker = new Worker(new URL('./dragon.worker.ts', import.meta.url), { type: 'module' })
      const offscreen = canvas.transferControlToOffscreen()
      canvas.setAttribute('aria-hidden', 'true')
      element.append(canvas)
      worker.onmessage = ({ data }: MessageEvent<DragonProgress>) => {
        if (!live) return
        element.dataset.frames = String(data.frames)
        element.dataset.elapsed = String(data.elapsed)
        element.dataset.phase = String(data.phase)
        element.dataset.worker = 'true'
        element.dataset.ink = data.ink
        element.dataset.glow = data.glow
        element.dataset.size = String(data.size)
        latest.current.onFrame(data.elapsed)
      }
      worker.onerror = (event) => {
        event.preventDefault()
        if (live) latest.current.onFailure()
      }
      worker.onmessageerror = () => {
        if (live) latest.current.onFailure()
      }
      const appearance = () => {
        const style = getComputedStyle(element)
        return {
          ink: style.color,
          glow: style.getPropertyValue('--accent').trim() || '#43e8cd',
          size: Math.ceil(element.getBoundingClientRect().width * devicePixelRatio),
        }
      }
      const initial = appearance()
      let previous = JSON.stringify(initial)
      worker.postMessage(
        {
          kind: 'start',
          canvas: offscreen,
          contours,
          mark: paths.join(' '),
          ...initial,
        },
        [offscreen],
      )
      const updateAppearance = () => {
        if (!live) return
        const value = appearance(),
          identity = JSON.stringify(value)
        if (identity !== previous) {
          previous = identity
          worker!.postMessage({ kind: 'appearance', ...value })
        }
      }
      const resized = new ResizeObserver(updateAppearance),
        themed = new MutationObserver(updateAppearance)
      resized.observe(element)
      for (let ancestor: HTMLElement | null = element; ancestor; ancestor = ancestor.parentElement)
        themed.observe(ancestor, {
          attributes: true,
          attributeFilter: ['style', 'class', 'data-theme', 'data-mode'],
        })
      window.addEventListener('resize', updateAppearance)
      stopObserving = () => {
        resized.disconnect()
        themed.disconnect()
        window.removeEventListener('resize', updateAppearance)
      }
    } catch {
      live = false
      stopObserving()
      worker?.terminate()
      canvas.remove()
      latest.current.onFailure()
      return
    }
    return () => {
      live = false
      stopObserving()
      worker?.terminate()
      canvas.remove()
      delete element.dataset.worker
      delete element.dataset.frames
      delete element.dataset.elapsed
      delete element.dataset.phase
      delete element.dataset.ink
      delete element.dataset.glow
      delete element.dataset.size
    }
  }, [tracing])
  return (
    <span ref={host} className="startup-dragon" data-tracing={tracing} aria-hidden="true">
      <span className="startup-dragon-still" style={{ maskImage: `url(${dragon})` }} />
    </span>
  )
}
