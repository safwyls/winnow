export interface BackdropKey {
  provider: string
  id: string
}
export interface BackdropCandidate {
  key: BackdropKey
  aspectRatio: number
  fitWholeHero: boolean
}
export interface BackdropSelection {
  candidates: BackdropCandidate[]
  coverKey: BackdropKey | null
}
export interface BackdropSize {
  width: number
  height: number
  scale: number
  fullscreen: boolean
  fitWholeHero?: boolean
}
export interface BackdropPixels {
  source: string
  width: number
  height: number
  dispose(): void
}
export interface BackdropLayer {
  candidate: BackdropCandidate
  pixels: BackdropPixels
  requestedWidth: number
  fallback: boolean
}
export interface BackdropFrame {
  current: BackdropLayer | null
  outgoing: BackdropLayer | null
  progress: number
  loading: boolean
}
export const BACKDROP_FADE_MS = 180
export const backdropKey = (key: BackdropKey) => `${key.provider}:${key.id}`
const isBackdropKey = (value: unknown): value is BackdropKey =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as BackdropKey).provider === 'string' &&
  typeof (value as BackdropKey).id === 'string'
const isBackdropCandidate = (value: unknown): value is BackdropCandidate =>
  typeof value === 'object' &&
  value !== null &&
  isBackdropKey((value as BackdropCandidate).key) &&
  // Saved images without stored dimensions report an aspect ratio of 0.
  Number.isFinite((value as BackdropCandidate).aspectRatio)
/** A missing, partial or malformed selection has no candidates; it must never fail a consumer. */
export function normalizeBackdropSelection(value: unknown): BackdropSelection {
  const selection =
    typeof value === 'object' && value !== null && !Array.isArray(value)
      ? (value as Partial<Record<keyof BackdropSelection, unknown>>)
      : undefined
  const candidates = Array.isArray(selection?.candidates)
    ? selection.candidates.filter(isBackdropCandidate).map((candidate) => ({
        key: candidate.key,
        aspectRatio: candidate.aspectRatio,
        fitWholeHero: candidate.fitWholeHero === true,
      }))
    : []
  return { candidates, coverKey: isBackdropKey(selection?.coverKey) ? selection.coverKey : null }
}
export function backdropCandidates(selection: BackdropSelection): BackdropCandidate[] {
  const { candidates, coverKey } = normalizeBackdropSelection(selection)
  if (coverKey && !candidates.some((candidate) => backdropKey(candidate.key) === backdropKey(coverKey)))
    candidates.push({ key: coverKey, aspectRatio: 2 / 3, fitWholeHero: false })
  return candidates
}
export function backdropGeometry(
  candidate: BackdropCandidate,
  size: BackdropSize,
  ratio = candidate.aspectRatio,
) {
  const fitted =
    size.fullscreen &&
    size.fitWholeHero !== false &&
    candidate.fitWholeHero &&
    size.width / size.height >= 21 / 9 - 0.0001
  const width = fitted ? Math.min(size.width, size.height * ratio) : size.width
  return { fitted, width, height: fitted ? width / ratio : size.height, left: (size.width - width) / 2 }
}
export function backdropWidth(candidate: BackdropCandidate, size: BackdropSize) {
  const geometry = backdropGeometry(candidate, size)
  const width =
    (geometry.fitted ? geometry.width : Math.max(size.width, size.height * candidate.aspectRatio)) *
    size.scale
  return [160, 240, 320, 480, 640, 1280, 1920, 2560, 3840].find((bucket) => width <= bucket) ?? 3840
}

/** Only decoded replacements may displace visible pixels. A pending blend owns both layers until it ends. */
export class BackdropController {
  private current: BackdropLayer | null = null
  private outgoing: BackdropLayer | null = null
  private ready: BackdropLayer | null | undefined
  private selection: AbortController | undefined
  private generation = 0
  private frameId: number | undefined
  private started: number | undefined
  private progress = 1
  private loading = false
  private reduced = false
  private disposed = false
  private target = ''
  constructor(
    private readonly dependencies: {
      resolve(workId: number, aspectRatio: number, signal: AbortSignal): Promise<BackdropSelection>
      image(key: BackdropKey, width: number, signal: AbortSignal): Promise<BackdropPixels | null>
      publish(frame: BackdropFrame): void
      requestFrame(callback: FrameRequestCallback): number
      cancelFrame(id: number): void
    },
  ) {}

  setReducedMotion(value: boolean) {
    this.reduced = value
    if (value && this.outgoing) this.finish(true)
  }

  select(workId: number, size: BackdropSize, force = false) {
    if (this.disposed || size.width <= 0 || size.height <= 0) return
    const target = `${workId}:${size.width}:${size.height}:${size.scale}:${size.fullscreen}:${size.fitWholeHero}`
    if (!force && target === this.target) return
    this.target = target
    const generation = ++this.generation
    this.selection?.abort()
    this.selection = new AbortController()
    this.clearReady()
    this.loading = true
    this.emit()
    void this.load(workId, size, generation, this.selection.signal, force)
  }

  private async load(
    workId: number,
    size: BackdropSize,
    generation: number,
    signal: AbortSignal,
    force: boolean,
  ) {
    const stale = () => this.disposed || signal.aborted || generation !== this.generation
    let selection: BackdropSelection
    try {
      selection = normalizeBackdropSelection(
        await this.dependencies.resolve(workId, size.width / size.height, signal),
      )
    } catch {
      if (!stale()) this.settle(null)
      return
    }
    if (stale()) return
    const candidates = backdropCandidates(selection)
    for (let index = 0; index < candidates.length; index++) {
      const candidate = candidates[index]
      const width = backdropWidth(candidate, size)
      const held =
        this.current && backdropKey(this.current.candidate.key) === backdropKey(candidate.key)
          ? this.current
          : null
      if (held && held.requestedWidth >= width && !force) {
        this.loading = false
        this.emit()
        return
      }
      let pixels: BackdropPixels | null = null
      try {
        pixels = await this.dependencies.image(candidate.key, width, signal)
      } catch {
        /* Missing or undecodable images advance to the next local candidate. */
      }
      if (stale()) {
        pixels?.dispose()
        return
      }
      if (pixels) {
        this.settle({
          candidate,
          pixels,
          requestedWidth: width,
          fallback: index >= selection.candidates.length,
        })
        return
      }
      // A failed higher-resolution copy must not discard a usable displayed image.
      if (held) {
        this.loading = false
        this.emit()
        return
      }
    }
    this.settle(null)
  }

  private settle(layer: BackdropLayer | null) {
    this.loading = false
    if (this.outgoing && !this.reduced) {
      this.clearReady()
      this.ready = layer
      this.emit()
    } else this.present(layer)
  }

  private present(layer: BackdropLayer | null) {
    this.finish(false)
    const previous = this.current
    this.current = layer
    if (
      layer &&
      previous &&
      !this.reduced &&
      !layer.fallback &&
      backdropKey(layer.candidate.key) !== backdropKey(previous.candidate.key)
    ) {
      this.outgoing = previous
      this.progress = 0
      this.started = undefined
      this.frameId = this.dependencies.requestFrame(this.tick)
    } else previous?.pixels.dispose()
    this.emit()
  }

  private tick = (now: number) => {
    this.frameId = undefined
    if (this.disposed || !this.outgoing) return
    this.started ??= now
    this.progress = this.reduced ? 1 : Math.min(1, (now - this.started) / BACKDROP_FADE_MS)
    if (this.progress === 1) this.finish(true)
    else {
      this.emit()
      this.frameId = this.dependencies.requestFrame(this.tick)
    }
  }

  private finish(presentReady: boolean) {
    if (this.frameId !== undefined) this.dependencies.cancelFrame(this.frameId)
    this.frameId = undefined
    this.started = undefined
    this.progress = 1
    this.outgoing?.pixels.dispose()
    this.outgoing = null
    if (presentReady && this.ready !== undefined) {
      const ready = this.ready
      this.ready = undefined
      this.present(ready)
    } else this.emit()
  }
  private clearReady() {
    this.ready?.pixels.dispose()
    this.ready = undefined
  }
  private emit() {
    if (!this.disposed)
      this.dependencies.publish({
        current: this.current,
        outgoing: this.outgoing,
        progress: this.progress,
        loading: this.loading,
      })
  }
  dispose() {
    if (this.disposed) return
    this.disposed = true
    this.generation++
    this.selection?.abort()
    this.finish(false)
    this.clearReady()
    this.current?.pixels.dispose()
    this.current = null
    this.dependencies.publish({ current: null, outgoing: null, progress: 1, loading: false })
  }
}
