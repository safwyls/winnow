import { useLayoutEffect, useRef } from 'react'
import './cover-padding.css'

type Edges = { top: string; bottom: string; left: string; right: string }
const palettes = new WeakMap<object, Edges>()

function average(pixels: Uint8ClampedArray) {
  let red = 0,
    green = 0,
    blue = 0,
    weight = 0
  for (let index = 0; index < pixels.length; index += 4) {
    const alpha = pixels[index + 3] / 255
    red += pixels[index] * alpha
    green += pixels[index + 1] * alpha
    blue += pixels[index + 2] * alpha
    weight += alpha
  }
  return weight
    ? `rgb(${Math.round(red / weight)}, ${Math.round(green / weight)}, ${Math.round(blue / weight)})`
    : 'transparent'
}

function sample(image: HTMLImageElement, owner: object) {
  const cached = palettes.get(owner)
  if (cached) return cached
  const canvas = document.createElement('canvas'),
    context = canvas.getContext('2d', { willReadFrequently: true })
  if (!context) return null
  const width = image.naturalWidth,
    height = image.naturalHeight
  const edge = (x: number, y: number, w: number, h: number) => {
    canvas.width = w
    canvas.height = h
    context.drawImage(image, x, y, w, h, 0, 0, w, h)
    return average(context.getImageData(0, 0, w, h).data)
  }
  try {
    const colors = {
      top: edge(0, 0, width, 1),
      bottom: edge(0, height - 1, width, 1),
      left: edge(0, 0, 1, height),
      right: edge(width - 1, 0, 1, height),
    }
    palettes.set(owner, colors)
    return colors
  } catch {
    // A non-readable image keeps the theme surface as its fallback.
    return null
  }
}

export function CoverPadding({
  image,
  owner = image,
}: {
  image: HTMLImageElement | null
  owner?: object | null
}) {
  const canvas = useRef<HTMLCanvasElement>(null)
  useLayoutEffect(() => {
    const node = canvas.current!
    if (!image || !owner || !image.naturalWidth || !image.naturalHeight) {
      node.width = node.width
      return
    }
    const context = node.getContext('2d')
    if (!context) return
    const colors = sample(image, owner)
    const paint = () => {
      const width = node.clientWidth,
        height = node.clientHeight,
        ratio = window.devicePixelRatio || 1
      node.width = Math.round(width * ratio)
      node.height = Math.round(height * ratio)
      if (!image || !colors || !width || !height || getComputedStyle(image).objectFit !== 'contain') return
      const scale = Math.min(width / image.naturalWidth, height / image.naturalHeight)
      const horizontal = Math.max(0, (width - image.naturalWidth * scale) / 2)
      const vertical = Math.max(0, (height - image.naturalHeight * scale) / 2)
      context.scale(ratio, ratio)
      const fill = (color: string, x: number, y: number, w: number, h: number) => {
        context.fillStyle = color
        context.fillRect(x, y, w, h)
      }
      if (vertical > 0) {
        fill(colors.top, 0, 0, width, vertical)
        fill(colors.bottom, 0, height - vertical, width, vertical)
      }
      if (horizontal > 0) {
        fill(colors.left, 0, 0, horizontal, height)
        fill(colors.right, width - horizontal, 0, horizontal, height)
      }
    }
    paint()
    const resize = typeof ResizeObserver === 'function' ? new ResizeObserver(paint) : undefined
    resize?.observe(node)
    const preferences = new MutationObserver(paint)
    preferences.observe(document.documentElement, { attributes: true, attributeFilter: ['style', 'class'] })
    if (image) preferences.observe(image, { attributes: true, attributeFilter: ['style', 'class'] })
    window.addEventListener('resize', paint)
    return () => {
      resize?.disconnect()
      preferences.disconnect()
      window.removeEventListener('resize', paint)
      context.clearRect(0, 0, node.width, node.height)
    }
  }, [image, owner])
  return <canvas ref={canvas} className="cover-padding" aria-hidden="true" />
}
