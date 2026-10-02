export interface OwnedArtwork {
  source: string
  width: number
  height: number
  dispose(): void
}
export interface ArtworkLease<T> {
  readonly ready: Promise<T | null>
  readonly current: T | null
  readonly failed: boolean
  release(): void
}
type Loader<T> = (signal: AbortSignal) => Promise<T | null>
interface Slot<T> {
  key: string
  refs: number
  abort: AbortController
  load: Loader<T>
  ready: Promise<T | null>
  complete(value: T | null): void
  value: T | null
  finished: boolean
  failed: boolean
  cached: boolean
  loadedAt: number
}

export const artworkWidths = [160, 240, 320, 480, 640, 1280, 1920, 2560, 3840] as const
export function artworkWidth(pixels: number) {
  return artworkWidths.find((width) => pixels <= width) ?? artworkWidths[artworkWidths.length - 1]
}

/** The LRU owns scrollback; visible leases keep evicted pixels alive until their last release. */
export class ArtworkCache<T extends OwnedArtwork> {
  private slots = new Map<string, Slot<T>>()
  private retained = new Set<Slot<T>>()
  private pending = new Set<Slot<T>>()
  private lru = new Map<string, Slot<T>>()
  private queue: Slot<T>[] = []
  private running = 0
  private bytes = 0
  private closed = false
  private shutdown?: Promise<void>
  private readonly maxPending: number
  private readonly concurrent: number
  private readonly budget: number
  private readonly freshness: number
  private readonly now: () => number

  constructor(
    options: {
      maxPending?: number
      concurrent?: number
      maxBytes?: number
      freshness?: number
      now?: () => number
    } = {},
  ) {
    this.maxPending = Math.max(1, options.maxPending ?? 128)
    this.concurrent = Math.max(1, options.concurrent ?? 6)
    this.budget = Math.max(0, options.maxBytes ?? 32 * 1024 * 1024)
    this.freshness = options.freshness ?? 120_000
    this.now = options.now ?? Date.now
  }

  get pendingCount() {
    return this.pending.size
  }
  get decodedCount() {
    return this.lru.size
  }
  get decodedBytes() {
    return this.bytes
  }
  get liveSlots() {
    return [...this.retained].filter((slot) => slot.refs > 0).length
  }

  acquire(key: string, load: Loader<T>): ArtworkLease<T> {
    if (this.closed) return this.empty()
    let slot = this.slots.get(key)
    if (slot?.value && this.now() - slot.loadedAt >= this.freshness) {
      this.evict(slot, true)
      slot = undefined
    }
    // A canceled source may still be unwinding. Never join it or race a second factory.
    if (slot?.abort.signal.aborted) return this.afterRetirement(slot, key, load)
    if (!slot) {
      if (this.pending.size >= this.maxPending) return this.empty()
      let complete!: (value: T | null) => void
      const ready = new Promise<T | null>((done) => {
        complete = done
      })
      slot = {
        key,
        load,
        ready,
        complete,
        refs: 0,
        abort: new AbortController(),
        value: null,
        finished: false,
        failed: false,
        cached: false,
        loadedAt: 0,
      }
      this.slots.set(key, slot)
      this.pending.add(slot)
      this.retained.add(slot)
      this.queue.push(slot)
    } else if (slot.cached) {
      this.lru.delete(key)
      this.lru.set(key, slot)
    }
    const owned = slot
    owned.refs++
    let released = false
    const lease: ArtworkLease<T> = {
      ready: owned.ready.then((value) => (released ? null : value)),
      get current() {
        return released ? null : owned.value
      },
      get failed() {
        return owned.failed
      },
      release: () => {
        if (released) return
        released = true
        if (--owned.refs === 0) {
          if (!owned.finished) {
            owned.abort.abort()
            this.drain()
          } else this.releasePixels(owned)
        }
      },
    }
    this.drain()
    return lease
  }

  async get(key: string, load: Loader<T>): Promise<T | null> {
    const lease = this.acquire(key, load)
    try {
      return await lease.ready
    } finally {
      lease.release()
    }
  }

  invalidate(key?: string) {
    for (const slot of [...this.slots.values()]) {
      if (key !== undefined && slot.key !== key) continue
      if (slot.finished) this.evict(slot, true)
      else slot.abort.abort()
    }
    this.drain()
  }

  close(): Promise<void> {
    if (this.shutdown) return this.shutdown
    this.closed = true
    for (const slot of [...this.slots.values()]) if (slot.finished) this.evict(slot, true)
    for (const slot of this.pending) slot.abort.abort()
    this.drain()
    this.shutdown = Promise.all([...this.pending].map((slot) => slot.ready)).then(() => undefined)
    return this.shutdown
  }

  private empty(): ArtworkLease<T> {
    return { ready: Promise.resolve(null), current: null, failed: false, release() {} }
  }

  private afterRetirement(retiring: Slot<T>, key: string, load: Loader<T>): ArtworkLease<T> {
    let released = false
    let replacement: ArtworkLease<T> | undefined
    return {
      ready: retiring.ready.then(() => {
        if (released || this.closed) return null
        replacement = this.acquire(key, load)
        return replacement.ready
      }),
      get current() {
        return replacement?.current ?? null
      },
      get failed() {
        return replacement?.failed ?? false
      },
      release() {
        released = true
        replacement?.release()
      },
    }
  }

  private drain() {
    // Canceled queued work must retire even when every running source ignores cancellation.
    for (const slot of this.queue.filter((entry) => entry.abort.signal.aborted)) this.finish(slot, null)
    this.queue = this.queue.filter((slot) => !slot.finished)
    while (!this.closed && this.running < this.concurrent && this.queue.length) {
      const slot = this.queue.shift()!
      this.running++
      void this.run(slot)
    }
  }

  private async run(slot: Slot<T>) {
    let value: T | null = null
    try {
      value = await slot.load(slot.abort.signal)
    } catch {
      slot.failed = !slot.abort.signal.aborted
    }
    if (value && (this.closed || slot.abort.signal.aborted)) {
      value.dispose()
      value = null
    }
    this.finish(slot, value)
    this.running--
    this.drain()
  }

  private finish(slot: Slot<T>, value: T | null) {
    if (slot.finished) return
    slot.finished = true
    slot.value = value
    this.pending.delete(slot)
    if (value) {
      slot.loadedAt = this.now()
      slot.cached = true
      this.lru.set(slot.key, slot)
      this.bytes += this.cost(value)
      while (this.bytes > this.budget && this.lru.size) this.evict(this.lru.values().next().value!)
    } else {
      if (this.slots.get(slot.key) === slot) this.slots.delete(slot.key)
      this.retained.delete(slot)
    }
    slot.complete(value)
    this.releasePixels(slot)
  }

  private evict(slot: Slot<T>, retire = false) {
    if (slot.cached) {
      this.lru.delete(slot.key)
      slot.cached = false
      if (slot.value) this.bytes -= this.cost(slot.value)
    }
    if ((retire || !slot.refs) && this.slots.get(slot.key) === slot) this.slots.delete(slot.key)
    this.releasePixels(slot)
  }

  private releasePixels(slot: Slot<T>) {
    if (slot.refs || slot.cached || !slot.finished) return
    slot.value?.dispose()
    slot.value = null
    if (this.slots.get(slot.key) === slot) this.slots.delete(slot.key)
    this.retained.delete(slot)
  }

  private cost(value: T) {
    return value.width * value.height * 4
  }
}
