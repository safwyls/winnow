import { useEffect, useRef } from 'react'

/** A seeded, static sky: preference changes only adjust opacity, never start a render loop. */
export function Starfield({ brightness }: { brightness: number }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const element = canvas.current
    if (!element) return
    const draw = () => {
      const { width, height } = element.getBoundingClientRect()
      const scale = Math.min(window.devicePixelRatio || 1, 2)
      element.width = Math.round(width * scale)
      element.height = Math.round(height * scale)
      const context = element.getContext('2d')
      if (!context) return
      context.scale(scale, scale)
      let seed = 71923
      const random = () => {
        seed = (seed * 16807) % 2147483647
        return (seed - 1) / 2147483646
      }
      for (let i = 0; i < Math.min(520, (width * height) / 3600); i++) {
        const x = random() * width,
          y = random() * height,
          radius = random() > 0.96 ? 1.3 : 0.35 + random() * 0.55
        context.fillStyle = `rgba(${random() > 0.5 ? '171,225,224' : '204,192,247'},${0.22 + random() * 0.65})`
        context.beginPath()
        context.arc(x, y, radius, 0, Math.PI * 2)
        context.fill()
      }
    }
    const observer = new ResizeObserver(draw)
    observer.observe(element)
    draw()
    return () => observer.disconnect()
  }, [])
  return (
    <canvas
      className="rift-starfield"
      ref={canvas}
      style={{ opacity: brightness / 100 }}
      aria-hidden="true"
    />
  )
}
