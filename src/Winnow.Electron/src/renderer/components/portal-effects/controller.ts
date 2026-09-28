import {
  normalizePortalOptions,
  portalActivityRate,
  portalGeometry,
  portalPolygon,
  PORTAL_AMBIENT_MS,
  PORTAL_EXPANSION_MS,
  PORTAL_OPENING_MS,
  type PortalOptions,
  type PortalPoint,
  type PortalRect,
} from './geometry'
import type { PortalRenderer } from './renderer'

interface PortalElements {
  root: HTMLElement
  artwork: HTMLElement
  scene: HTMLElement
  content: HTMLElement
}
interface PortalConfiguration {
  options?: Partial<PortalOptions>
  reducedMotion: boolean
  origin?: PortalPoint
  expansion?: PortalRect
  onExpanded?: () => void
}

/** Own one aperture's lifetime; neither its artwork nor its content changes size. */
export class PortalController {
  private options: PortalOptions
  private renderer?: PortalRenderer
  private disposed = false
  private failed = false
  private initializing = false
  private expanded = false
  private frame = 0
  private width = 0
  private height = 0
  private sceneX = -24
  private sceneY = -24
  private sceneWidth = 0
  private sceneHeight = 0
  private started = performance.now()
  private lastPhase = this.started
  private lastFrame = this.started
  private time = 0
  private progress: number
  private focused = document.hasFocus()
  private visible = !document.hidden
  private intersecting = true
  private colorA: Float32Array = new Float32Array([0.46, 0.81, 0.74])
  private colorB: Float32Array = new Float32Array([0.7, 0.64, 0.95])
  private resizeObserver?: ResizeObserver
  private intersectionObserver?: IntersectionObserver
  private themeObserver?: MutationObserver

  constructor(
    private elements: PortalElements,
    private configuration: PortalConfiguration,
  ) {
    this.options = normalizePortalOptions(configuration.options)
    this.progress = configuration.reducedMotion ? 1 : 0
    elements.root.dataset.portalExpanded = 'false'
    elements.root.dataset.portalRenderer = 'fallback'
    window.addEventListener('blur', this.blur)
    window.addEventListener('focus', this.focus)
    document.addEventListener('visibilitychange', this.visibility)
    window.addEventListener('resize', this.resize)
    this.themeObserver = new MutationObserver(() => {
      if (this.readColors()) this.draw()
    })
    this.themeObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['style', 'class'],
    })
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(this.resize)
      this.resizeObserver.observe(elements.root)
    }
    if (typeof IntersectionObserver !== 'undefined') {
      this.intersectionObserver = new IntersectionObserver((entries) => {
        this.intersecting = entries[0]?.isIntersecting ?? false
        this.synchronizeActivity()
      })
      this.intersectionObserver.observe(elements.root)
    }
    this.resize()
    this.synchronizeActivity()
  }

  setOptions(options: Partial<PortalOptions> = {}) {
    this.advancePhase(performance.now())
    if (!this.frame) this.lastFrame = performance.now()
    this.options = normalizePortalOptions(options)
    this.readColors()
    this.draw()
    this.schedule()
  }

  private async initialize() {
    if (this.initializing || this.disposed || this.expanded || this.failed) return
    this.initializing = true
    try {
      const { createPortalRenderer } = await import('./renderer')
      if (this.disposed || this.expanded) return
      const renderer = await createPortalRenderer(this.fallback)
      if (this.disposed || this.expanded || this.failed) {
        renderer.destroy()
        return
      }
      this.renderer = renderer
      renderer.attach(this.elements.scene)
      renderer.resize(this.sceneWidth, this.sceneHeight)
      this.elements.root.dataset.portalRenderer = 'webgl'
      this.draw()
      this.schedule()
    } catch {
      this.fallback()
    }
  }

  private get moving() {
    return !this.configuration.reducedMotion && this.focused && this.visible && this.intersecting
  }

  private readColors() {
    const style = getComputedStyle(this.elements.root)
    const read = (name: string, fallback: Float32Array) => {
      const values = style.getPropertyValue(name).trim().split(/[ ,]+/).map(Number)
      return values.length === 3 &&
        values.every((value) => Number.isFinite(value) && value >= 0 && value <= 1)
        ? new Float32Array(values)
        : fallback
    }
    const colorA = read('--portal-rim-a', new Float32Array([0.46, 0.81, 0.74]))
    const colorB = read('--portal-rim-b', new Float32Array([0.7, 0.64, 0.95]))
    const changed =
      colorA.some((value, index) => value !== this.colorA[index]) ||
      colorB.some((value, index) => value !== this.colorB[index])
    this.colorA = colorA
    this.colorB = colorB
    return changed
  }

  private resize = () => {
    if (this.disposed) return
    const width = this.elements.root.clientWidth,
      height = this.elements.root.clientHeight
    if (width <= 0 || height <= 0) return
    if (this.configuration.expansion && this.width && (width !== this.width || height !== this.height))
      this.progress = 1
    this.width = width
    this.height = height
    const origin = this.configuration.origin ?? { x: width / 2, y: height / 2 }
    this.sceneX = Math.min(-24, origin.x - 24)
    this.sceneY = Math.min(-24, origin.y - 24)
    this.sceneWidth = Math.ceil(Math.max(width + 24, origin.x + 24) - this.sceneX)
    this.sceneHeight = Math.ceil(Math.max(height + 24, origin.y + 24) - this.sceneY)
    Object.assign(this.elements.scene.style, {
      left: `${this.sceneX}px`,
      top: `${this.sceneY}px`,
      width: `${this.sceneWidth}px`,
      height: `${this.sceneHeight}px`,
    })
    this.readColors()
    this.renderer?.resize(this.sceneWidth, this.sceneHeight)
    this.draw()
    if (this.progress === 1) this.complete()
    if (!this.expanded) void this.initialize()
  }

  private advancePhase(now: number) {
    if (this.moving)
      this.time += (Math.max(0, now - this.lastPhase) / 1000) * portalActivityRate(this.options.activity)
    this.lastPhase = now
  }

  private draw() {
    if (this.disposed || !this.width || this.expanded) return
    const geometry = portalGeometry(
      this.width,
      this.height,
      this.progress,
      this.options,
      this.configuration.reducedMotion ? 0 : this.time,
      this.configuration.origin,
      this.configuration.expansion,
    )
    const clip = portalPolygon(geometry)
    const opening = this.progress < 1
    this.elements.artwork.style.clipPath = clip
    // Only the image mask moves at rest. The text plane is never repeatedly composited.
    this.elements.content.style.clipPath = opening ? clip : 'none'
    this.elements.content.inert = opening
    this.elements.root.dataset.portalOpening = String(opening)
    this.elements.root.dataset.portalReady = 'true'
    try {
      this.renderer?.draw(
        {
          ...geometry,
          center: {
            x: geometry.center.x - this.sceneX,
            y: geometry.center.y - this.sceneY,
          },
        },
        this.colorA,
        this.colorB,
      )
    } catch {
      this.fallback()
    }
  }

  private tick = (now: number) => {
    this.frame = 0
    if (this.disposed || this.expanded || !this.moving) return
    const elapsed = now - this.lastFrame
    if (this.progress < 1 || elapsed >= PORTAL_AMBIENT_MS - 0.1) {
      const duration = this.configuration.expansion ? PORTAL_EXPANSION_MS : PORTAL_OPENING_MS
      const opening = this.progress < 1
      this.progress = Math.min(1, Math.max(0, now - this.started) / duration)
      this.lastFrame = opening
        ? now
        : this.lastFrame + Math.max(1, Math.floor((elapsed + 0.1) / PORTAL_AMBIENT_MS)) * PORTAL_AMBIENT_MS
      this.advancePhase(now)
      this.draw()
      if (this.progress === 1) this.complete()
    }
    this.schedule()
  }

  private schedule() {
    const needsFrame =
      !this.disposed &&
      !this.expanded &&
      this.moving &&
      this.width > 0 &&
      (this.progress < 1 || (!!this.renderer && !this.failed && this.options.activity > 0))
    if (needsFrame && !this.frame) this.frame = requestAnimationFrame(this.tick)
    if (!needsFrame && this.frame) {
      cancelAnimationFrame(this.frame)
      this.frame = 0
    }
    this.elements.root.dataset.portalRunning = String(needsFrame)
  }

  private complete() {
    if (!this.configuration.expansion || this.expanded || this.disposed) return
    this.expanded = true
    this.elements.artwork.style.clipPath = 'none'
    this.elements.content.style.clipPath = 'none'
    this.elements.content.inert = false
    this.elements.root.dataset.portalOpening = 'false'
    this.elements.root.dataset.portalExpanded = 'true'
    this.renderer?.destroy()
    this.renderer = undefined
    this.schedule()
    this.configuration.onExpanded?.()
  }

  private synchronizeActivity() {
    this.lastPhase = performance.now()
    this.lastFrame = this.lastPhase
    if (!this.moving && this.progress < 1) {
      this.progress = 1
      this.draw()
      this.complete()
    }
    this.schedule()
  }
  private blur = () => {
    this.focused = false
    this.synchronizeActivity()
  }
  private focus = () => {
    this.focused = true
    this.synchronizeActivity()
  }
  private visibility = () => {
    this.visible = !document.hidden
    this.synchronizeActivity()
  }
  private fallback = () => {
    if (this.disposed) return
    this.failed = true
    this.renderer?.destroy()
    this.renderer = undefined
    this.elements.root.dataset.portalRenderer = 'fallback'
    this.schedule()
  }

  dispose() {
    if (this.disposed) return
    this.disposed = true
    cancelAnimationFrame(this.frame)
    this.frame = 0
    this.resizeObserver?.disconnect()
    this.intersectionObserver?.disconnect()
    this.themeObserver?.disconnect()
    window.removeEventListener('blur', this.blur)
    window.removeEventListener('focus', this.focus)
    window.removeEventListener('resize', this.resize)
    document.removeEventListener('visibilitychange', this.visibility)
    this.renderer?.destroy()
    this.renderer = undefined
    this.elements.content.inert = false
    this.elements.root.dataset.portalRunning = 'false'
  }
}
