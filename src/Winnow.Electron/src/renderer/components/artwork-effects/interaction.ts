import type { ArtworkEffectOptions } from '../../../shared/artworkEffects'
import type { ArtworkRenderer } from './renderer'

interface Registration {
  surface: HTMLElement
  target: HTMLElement
  options: ArtworkEffectOptions
  reducedMotion: boolean
}
interface Active {
  registration: Registration
  image: HTMLImageElement
  imageSource: string
  width: number
  height: number
  input: 'pointer' | 'keyboard'
  still: boolean
  x: number
  y: number
  targetX: number
  targetY: number
  reveal: number
}

const clamp = (value: number) => Math.min(1, Math.max(0, value))
const lighting = (options: ArtworkEffectOptions) =>
  (options.finish !== 'off' && options.intensity > 0) || (options.highlightFoil && options.foilStrength > 0)
const loadRenderer = async (lost: () => void) => (await import('./renderer')).createArtworkRenderer(lost)

/** One coordinator per renderer document, regardless of card or theme count. */
export class ArtworkEffectCoordinator {
  private registrations = new Set<Registration>()
  private active?: Active
  private runtime?: ArtworkRenderer
  private initialization?: Promise<ArtworkRenderer | undefined>
  private retiring?: Promise<unknown>
  private lifetime = 0
  private failed = false
  private frame = 0
  private restoreFrame = 0
  private previousTime = 0
  private globalEvents?: AbortController
  private changes?: MutationObserver
  private resize?: ResizeObserver

  constructor(private factory = loadRenderer) {}

  register(registration: Registration) {
    this.registrations.add(registration)
    if (this.registrations.size === 1) this.listen()
    const { surface, target } = registration
    const events = new AbortController()
    const eventOptions = { signal: events.signal }
    let pointer: { x: number; y: number } | undefined
    const move = (event: PointerEvent) => {
      if (event.pointerType === 'touch') return
      pointer = { x: event.clientX, y: event.clientY }
      this.activate(registration, 'pointer', pointer)
    }
    target.addEventListener('pointerenter', move, eventOptions)
    target.addEventListener('pointermove', move, eventOptions)
    target.addEventListener(
      'pointerleave',
      () => {
        pointer = undefined
        if (this.active?.registration === registration && this.active.input === 'pointer') this.clear()
      },
      eventOptions,
    )
    target.addEventListener(
      'focusin',
      () => {
        if (target.matches(':focus-visible')) this.activate(registration, 'keyboard')
      },
      eventOptions,
    )
    target.addEventListener(
      'focusout',
      () => {
        if (this.active?.registration === registration) this.clear()
      },
      eventOptions,
    )
    surface.addEventListener(
      'load',
      () => {
        // React's image load handler clears its placeholder during this event.
        // Observe the committed image state after the capture/bubble handlers finish.
        queueMicrotask(() => {
          if (!this.registrations.has(registration)) return
          if (target === document.activeElement && target.matches(':focus-visible'))
            this.activate(registration, 'keyboard')
          else if (pointer) this.activate(registration, 'pointer', pointer)
        })
      },
      { ...eventOptions, capture: true },
    )
    if (target === document.activeElement && target.matches(':focus-visible'))
      this.activate(registration, 'keyboard')
    return () => {
      events.abort()
      if (this.active?.registration === registration) this.clear()
      this.registrations.delete(registration)
      if (!this.registrations.size) this.dispose()
    }
  }

  private listen() {
    this.globalEvents = new AbortController()
    const options = { signal: this.globalEvents.signal }
    document.addEventListener('scroll', this.scrolled, { ...options, capture: true, passive: true })
    document.addEventListener(
      'keydown',
      (event) => {
        if (
          event.altKey ||
          event.ctrlKey ||
          event.metaKey ||
          ![
            'Tab',
            'ArrowUp',
            'ArrowDown',
            'ArrowLeft',
            'ArrowRight',
            'Home',
            'End',
            'PageUp',
            'PageDown',
          ].includes(event.key)
        )
          return
        // A navigation key can change input modality without changing focus. Wait for
        // native scrolling and theme keyboard navigation before selecting the card.
        if (this.active?.input === 'pointer') this.clear()
        this.restoreKeyboardAfterLayout()
      },
      { ...options, capture: true },
    )
    document.addEventListener('pointerdown', this.clear, { ...options, capture: true })
    document.addEventListener('visibilitychange', this.clear, options)
    window.addEventListener('blur', this.clear, options)
    window.addEventListener('resize', this.clear, options)
    window.addEventListener('pagehide', this.clear, options)
    window.addEventListener(
      'focus',
      () => {
        for (const registration of this.registrations) {
          if (
            registration.target === document.activeElement &&
            registration.target.matches(':focus-visible')
          ) {
            this.activate(registration, 'keyboard')
            break
          }
        }
      },
      options,
    )
    this.changes = new MutationObserver(() => {
      const active = this.active
      if (
        active &&
        (!this.available(active.registration) ||
          active.registration.surface.querySelector('img') !== active.image ||
          this.source(active.image) !== active.imageSource)
      )
        this.clear()
    })
    this.changes.observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['src', 'srcset', 'hidden', 'aria-hidden', 'inert', 'data-loading'],
    })
    if (typeof ResizeObserver !== 'undefined')
      this.resize = new ResizeObserver(() => {
        if (!this.active) return
        const { surface } = this.active.registration
        if (surface.offsetWidth !== this.active.width || surface.offsetHeight !== this.active.height)
          this.clear()
      })
  }

  private source(image: HTMLImageElement) {
    return image.currentSrc || image.src
  }

  private focused() {
    return [...this.registrations].find((registration) => registration.target === document.activeElement)
  }

  private visible(target: HTMLElement) {
    const bounds = target.getBoundingClientRect()
    let left = Math.max(0, bounds.left),
      top = Math.max(0, bounds.top)
    let right = Math.min(window.innerWidth, bounds.right),
      bottom = Math.min(window.innerHeight, bounds.bottom)
    // Virtualized libraries clip within their own scroller, which can be much
    // smaller than the viewport. A focused but clipped card must not keep its GPU work.
    for (let parent = target.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent)
      const clipX = /auto|scroll|hidden|clip/.test(style.overflowX || style.overflow)
      const clipY = /auto|scroll|hidden|clip/.test(style.overflowY || style.overflow)
      if (!clipX && !clipY) continue
      const clip = parent.getBoundingClientRect()
      if (clipX) {
        left = Math.max(left, clip.left)
        right = Math.min(right, clip.right)
      }
      if (clipY) {
        top = Math.max(top, clip.top)
        bottom = Math.min(bottom, clip.bottom)
      }
    }
    return right > left && bottom > top
  }

  private restoreKeyboardAfterLayout() {
    if (this.restoreFrame) cancelAnimationFrame(this.restoreFrame)
    this.restoreFrame = requestAnimationFrame(() => {
      this.restoreFrame = 0
      const registration = this.focused()
      if (registration && this.visible(registration.target)) this.activate(registration, 'keyboard')
    })
  }

  private scrolled = () => {
    const pointer = this.active?.input === 'pointer'
    const keyboard = this.active?.input === 'keyboard' || this.focused()?.target.matches(':focus-visible')
    this.clear()
    if (!pointer && keyboard) this.restoreKeyboardAfterLayout()
  }

  private available({ surface, target }: Registration) {
    return (
      surface.isConnected &&
      !document.hidden &&
      !target.closest('[hidden], [inert], [aria-hidden="true"]') &&
      !document.querySelector('dialog[open], [role="dialog"][aria-modal="true"]')
    )
  }

  private activate(registration: Registration, input: Active['input'], point?: { x: number; y: number }) {
    const { surface, target, options } = registration
    if (!this.available(registration) || (!options.floating && !lighting(options))) return
    if (input === 'keyboard' && !this.visible(target)) return
    const image = surface.querySelector('img')
    if (
      !image?.complete ||
      !image.naturalWidth ||
      !image.naturalHeight ||
      image.closest('[data-loading="true"]')
    )
      return
    const bounds = target.getBoundingClientRect()
    // Read layout dimensions from the untransformed frame, not its raised child.
    const width = surface.offsetWidth,
      height = surface.offsetHeight
    if (!bounds.width || !bounds.height || !width || !height) return
    const still =
      registration.reducedMotion ||
      !options.followPointer ||
      input === 'keyboard' ||
      (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches)
    const nx = still || !point ? 0.5 : clamp((point.x - bounds.left) / bounds.width)
    const ny = still || !point ? 0.36 : clamp((point.y - bounds.top) / bounds.height)
    let active = this.active
    if (
      !active ||
      active.registration !== registration ||
      active.image !== image ||
      active.imageSource !== this.source(image)
    ) {
      this.clear()
      active = {
        registration,
        image,
        imageSource: this.source(image),
        width,
        height,
        input,
        still,
        x: nx * width,
        y: ny * height,
        targetX: nx * width,
        targetY: ny * height,
        reveal: still ? 1 : 0,
      }
      this.active = active
      this.resize?.observe(surface)
      surface.dataset.artworkActive = 'true'
      if (lighting(options) && !this.failed) void this.attach(active)
    }
    active.input = input
    active.still = still
    active.targetX = nx * width
    active.targetY = ny * height
    surface.dataset.artworkInput = input
    surface.dataset.artworkMotion = still ? 'still' : 'follow'
    if (options.floating) surface.dataset.artworkFloating = 'true'
    surface.style.setProperty('--artwork-tilt-x', `${still ? 0 : -(ny * 2 - 1) * options.tilt}deg`)
    surface.style.setProperty('--artwork-tilt-y', `${still ? 0 : (nx * 2 - 1) * options.tilt}deg`)
    surface.style.setProperty('--artwork-shadow-x', `${still ? 0 : -(nx * 2 - 1) * 9}px`)
    surface.style.setProperty('--artwork-shadow-y', `${still ? 18 : 18 - (ny * 2 - 1) * 6}px`)
    if (this.runtime?.attached && lighting(options)) this.schedule()
  }

  private async initialize() {
    if (this.runtime) return this.runtime
    if (this.failed) return
    if (!this.initialization) {
      const lifetime = this.lifetime
      // Theme replacement can unmount every card while WebGL is still initializing.
      // Finish disposing that context before creating its replacement.
      this.initialization = (this.retiring ?? Promise.resolve())
        .then(() =>
          this.factory(() => {
            if (lifetime === this.lifetime) this.unavailable()
          }),
        )
        .then((runtime) => {
          if (lifetime !== this.lifetime) {
            runtime.destroy()
            return
          }
          this.runtime = runtime
          return runtime
        })
        .catch(() => {
          if (lifetime === this.lifetime) this.unavailable()
          return undefined
        })
    }
    return this.initialization
  }

  private async attach(active: Active) {
    const runtime = await this.initialize()
    if (
      !runtime ||
      this.active !== active ||
      !this.available(active.registration) ||
      this.source(active.image) !== active.imageSource
    )
      return
    try {
      runtime.attach(
        active.registration.surface.querySelector('.winnow-artwork-surface')!,
        active.image,
        active.width,
        active.height,
        active.registration.options,
      )
      this.schedule()
    } catch {
      this.unavailable()
    }
  }

  private schedule() {
    if (!this.frame) this.frame = requestAnimationFrame(this.tick)
  }

  private tick = (time: number) => {
    this.frame = 0
    const active = this.active,
      runtime = this.runtime
    if (!active || !runtime?.attached) return
    if (!this.available(active.registration)) {
      this.clear()
      return
    }
    const ease = active.still
      ? 1
      : 1 - Math.exp(-Math.min(this.previousTime ? time - this.previousTime : 16, 50) * 0.016)
    this.previousTime = time
    active.x += (active.targetX - active.x) * ease
    active.y += (active.targetY - active.y) * ease
    active.reveal += (1 - active.reveal) * ease
    const settled =
      Math.abs(active.x - active.targetX) + Math.abs(active.y - active.targetY) < 0.15 &&
      active.reveal > 0.995
    if (settled) {
      active.x = active.targetX
      active.y = active.targetY
      active.reveal = 1
    }
    try {
      runtime.draw(
        active.x,
        active.y,
        active.reveal,
        active.still ? 'still' : settled ? 'settled' : 'following',
      )
    } catch {
      this.unavailable()
      return
    }
    if (!settled) this.schedule()
  }

  private unavailable = () => {
    this.failed = true
    if (this.active) this.active.registration.surface.dataset.artworkLighting = 'unavailable'
    if (this.frame) cancelAnimationFrame(this.frame)
    this.frame = 0
    this.runtime?.destroy()
    this.runtime = undefined
    this.initialization = undefined
    // CSS depth is independent of WebGL. Original artwork remains the fallback.
  }

  private clear = () => {
    if (this.frame) cancelAnimationFrame(this.frame)
    if (this.restoreFrame) cancelAnimationFrame(this.restoreFrame)
    this.frame = 0
    this.restoreFrame = 0
    this.previousTime = 0
    const surface = this.active?.registration.surface
    this.active = undefined
    if (surface) {
      delete surface.dataset.artworkActive
      delete surface.dataset.artworkFloating
      delete surface.dataset.artworkInput
      delete surface.dataset.artworkLighting
      for (const name of ['tilt-x', 'tilt-y', 'shadow-x', 'shadow-y'])
        surface.style.removeProperty(`--artwork-${name}`)
    }
    this.resize?.disconnect()
    this.runtime?.detach()
  }

  private dispose() {
    this.clear()
    this.lifetime++
    this.retiring = this.initialization
    this.globalEvents?.abort()
    this.changes?.disconnect()
    this.resize?.disconnect()
    this.runtime?.destroy()
    this.runtime = undefined
    this.initialization = undefined
    this.failed = false
  }
}

export const artworkEffects = new ArtworkEffectCoordinator()
