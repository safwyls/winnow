import React, { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { LoadingDragon } from '../../src/renderer/startup/LoadingDragon'
import { closedDragonContours } from '../../src/renderer/startup/dragon-contours'
import { dragonDash, dragonShapes, drawDragon } from '../../src/renderer/startup/dragon-renderer'
import dragonSource from '../../src/renderer/assets/dragon.svg?raw'
import '../../src/renderer/startup/startup.css'

const paths = [
    ...new DOMParser().parseFromString(dragonSource, 'image/svg+xml').querySelectorAll('path'),
  ].map((path) => path.getAttribute('d')!),
  contours = closedDragonContours(paths).map((path) => {
    const measure = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    measure.setAttribute('d', path)
    return { path, length: measure.getTotalLength(), measure }
  }),
  shapes = dragonShapes({ mark: paths.join(' '), contours })
const probe = {
  frames: [] as number[],
  failures: 0,
  contours: () =>
    contours.map(({ path, length, measure }) => ({
      path,
      length,
      phases: Array.from({ length: 101 }, (_, index) => {
        const phase = index / 100,
          dash = dragonDash(length, phase),
          segments: [number, number][] = []
        for (let repeat = -2; repeat <= 2; repeat++) {
          const start = repeat * (dash.pattern[0] + dash.pattern[1]) - dash.offset,
            a = Math.max(0, start),
            b = Math.min(length, start + dash.pattern[0])
          if (b > a) segments.push([a, b])
        }
        const head = phase === 0 ? length : length * phase,
          actual = measure.getPointAtLength(
            segments.reduce(
              (best, [, b]) => (Math.abs(b - head) < Math.abs(best - head) ? b : best),
              -length,
            ),
          ),
          expected = measure.getPointAtLength(head)
        return {
          trail: segments.reduce((sum, [a, b]) => sum + b - a, 0),
          headError: Math.hypot(actual.x - expected.x, actual.y - expected.y),
        }
      }),
    })),
  appearance(ink: string, glow: string, size: number) {
    document.documentElement.style.color = ink
    document.documentElement.style.setProperty('--accent', glow)
    const element = document.querySelector<HTMLElement>('.startup-dragon')!
    element.style.width = element.style.height = `${size}px`
  },
  sample(ink: string, glow: string, size: number, phase: number) {
    const canvas = document.querySelector<HTMLCanvasElement>('#sample')!,
      // Keep readback comparisons on one drawing backend instead of triggering Chromium's GPU fallback.
      context = canvas.getContext('2d', { willReadFrequently: true })!
    canvas.width = canvas.height = size
    drawDragon(context, shapes, { ink, glow, size }, phase)
    const pixels = context.getImageData(0, 0, size, size).data,
      target = [1, 3, 5].map((start) => parseInt(ink.slice(start, start + 2), 16))
    let inkPixels = 0,
      visible = 0,
      glowPixels = 0,
      hash = 2166136261
    for (let i = 0; i < pixels.length; i += 4) {
      hash = Math.imul(
        hash ^ pixels[i] ^ (pixels[i + 1] << 8) ^ (pixels[i + 2] << 16) ^ (pixels[i + 3] << 24),
        16777619,
      )
      if (pixels[i + 3] < 20) continue
      visible++
      if (target.every((value, channel) => Math.abs(value - pixels[i + channel]) < 5)) inkPixels++
      else if (pixels[i + 1] > pixels[i] * 1.1 && pixels[i + 2] > pixels[i] * 1.1) glowPixels++
    }
    return { inkPixels, visible, glowPixels, hash }
  },
}
Object.assign(window, { dragonProbe: probe })
function Probe() {
  const [tracing, setTracing] = useState(false),
    [attached, setAttached] = useState(true)
  return (
    <main>
      <div style={{ height: 160 }}>
        {attached && (
          <LoadingDragon
            tracing={tracing}
            onFrame={(elapsed) => probe.frames.push(elapsed)}
            onFailure={() => probe.failures++}
          />
        )}
      </div>
      <button onClick={() => setTracing(!tracing)}>{tracing ? 'Stop trace' : 'Start trace'}</button>
      <button onClick={() => setAttached(!attached)}>{attached ? 'Detach dragon' : 'Attach dragon'}</button>
      <canvas id="sample" />
    </main>
  )
}
document.documentElement.style.color = '#faebd7'
document.documentElement.style.setProperty('--accent', '#40e0d0')
document.body.style.background = '#0f1c1e'
createRoot(document.getElementById('root')!).render(<Probe />)
